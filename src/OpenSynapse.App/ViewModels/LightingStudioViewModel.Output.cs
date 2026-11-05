using System.Collections.ObjectModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>One device that rejected a frame or failed to restore its own lighting.</summary>
public sealed record StudioOutputFailure(string DeviceName, string Error, bool Restore);

/// <summary>Per-device layer snapshots and the HID frame pump.</summary>
public sealed partial class LightingStudioViewModel
{
    /// <summary>Latest failure per device and operation, observable without hiding the global status.</summary>
    public ObservableCollection<StudioOutputFailure> OutputFailures { get; } = new();

    public bool CanApply =>
        !_isBusy && (_argbRouter is not null ||
            ((_lighting is not null || _hidRouter is not null) &&
             Devices.Any(device => device.Descriptor is not null || device.HasMatrix)));

    private StudioLayerRender[] BuildLayers()
    {
        var device = SelectedDevice;
        return device is null ? [] : BuildLayersFor(device);
    }

    private StudioLayerRender[] BuildLayersFor(StudioDeviceOption device)
    {
        var (rows, columns) = FrameSize(device);
        return BuildLayersFor(device.EffectiveProductId, device.EffectiveInstanceId, rows, columns);
    }

    private StudioLayerRender[] BuildLayersFor(
        ushort productId, string? instanceId, int rows, int columns)
    {
        var profile = _project.SelectedProfile;
        if (profile is null || rows <= 0 || columns <= 0) return [];

        var layers = new List<StudioLayerRender>();
        foreach (var layer in profile.EnumerateLayers())
        {
            if (layer.IsGroup) continue;
            if (layer.Regions.Count == 0)
            {
                layers.Add(new StudioLayerRender(
                    layer.Params, null, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
                continue;
            }

            foreach (var region in layer.Regions)
            {
                var mask = StudioMaskBuilder.BuildRegion(region, productId, instanceId, rows, columns);
                if (!HasMask(mask)) continue;
                layers.Add(new StudioLayerRender(
                    region.Params, mask, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
            }
        }

        return layers.ToArray();
    }

    private static bool HasMask(byte[] mask)
    {
        foreach (var value in mask)
        {
            if (value != 0) return true;
        }

        return false;
    }

    private IReadOnlyList<StudioDeviceMatch> ConnectedMatches()
    {
        var matches = Devices
            .Where(device => device.HasMatrix && device.EffectiveProductId != 0 &&
                !string.IsNullOrWhiteSpace(device.EffectiveInstanceId))
            .Select(device => new StudioDeviceMatch(
                device.EffectiveProductId, device.EffectiveInstanceId!, LayoutFor(device)))
            .ToList();
        var products = matches.Select(match => match.ProductId).ToHashSet();
        foreach (var descriptor in _devices().Where(device =>
            device.Access == DeviceAccessState.Available &&
            device.ProductId != StudioArgbDevice.ProductId &&
            !products.Contains(device.ProductId)))
        {
            matches.Add(new StudioDeviceMatch(descriptor.ProductId, descriptor.Id, LayoutFor(descriptor.Id)));
        }

        return matches;
    }

    private StudioLedLayout? LayoutFor(StudioDeviceOption device) =>
        LayoutFor(device.EffectiveInstanceId) ?? device.Layout;

    private StudioLedLayout? LayoutFor(string? instanceId) =>
        string.IsNullOrWhiteSpace(instanceId)
            ? null
            : DeviceVisuals.FirstOrDefault(visual =>
                string.Equals(visual.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))?.Layout;

    /// <summary>Renders one frame per matrix device for the canvas preview.</summary>
    public IReadOnlyDictionary<string, StudioDeviceFrame> RenderDeviceFrames(TimeSpan elapsed)
    {
        var frames = new Dictionary<string, StudioDeviceFrame>(StringComparer.OrdinalIgnoreCase);
        var now = Math.Max(0, elapsed.TotalSeconds);
        foreach (var device in Devices)
        {
            if (!device.HasMatrix) continue;
            var (rows, columns) = FrameSize(device);
            var layers = BuildLayersFor(device);
            if (layers.Length == 0)
            {
                // The hardware pump writes black for an empty stack; the preview must match.
                frames[device.EffectiveInstanceId ?? string.Empty] = new StudioDeviceFrame(
                    rows, columns, new StudioRgb[rows * columns]);
                continue;
            }
            var timed = new (StudioLayerRender Layer, double Seconds)[layers.Length];
            for (var index = 0; index < layers.Length; index++)
            {
                var layer = layers[index];
                var state = GetClock(layer.Id).Sample(now, layer.Params, rows, columns);
                timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
            }

            var input = _inputSampler?.Sample(rows, columns) ?? default;
            frames[device.EffectiveInstanceId ?? string.Empty] = new StudioDeviceFrame(
                rows,
                columns,
                StudioCompositor.RenderTimedLayers(timed, rows, columns, input));
        }

        return frames;
    }

    private void RefreshHidSnapshots(IReadOnlyList<RazerHidDeviceConnection>? targets = null)
    {
        if (_hidRouter is null) return;
        var live = targets ?? _hidRouter.GetTargets();
        var snapshots = new Dictionary<string, StudioLayerRender[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in live)
        {
            var (rows, columns) = target.Definition.MatrixDimensions is { } dimensions &&
                target.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite)
                ? MatrixSize(FindLayout(target.InstanceId), dimensions.Rows, dimensions.Columns)
                : (1, 1);
            snapshots[target.InstanceId] = BuildLayersFor(
                target.Definition.ProductId, target.InstanceId, rows, columns);
        }

        _hidRouter.Update(connection => snapshots.TryGetValue(connection.InstanceId, out var layers) ? layers : []);
    }

    private void RefreshArgbSnapshots(IReadOnlyList<StudioArgbPort> ports)
    {
        if (_argbRouter is null) return;
        var snapshots = new Dictionary<byte, StudioLayerRender[]>();
        foreach (var port in ports)
        {
            var device = Devices.FirstOrDefault(item => item.IsArgb &&
                StudioLedSelection.SameInstance(item.EffectiveInstanceId, port.InstanceId));
            var (rows, columns) = device is null ? (1, port.LedCount) : MatrixSize(device);
            var layers = BuildLayersFor(port.ProductId, port.InstanceId, rows, columns);
            snapshots[port.Channel] = rows > 1
                ? StudioArgbDevice.SliceChannel(layers, rows, columns, port.Channel, port.LedCount)
                : layers;
        }

        _argbRouter.Update(port => snapshots.TryGetValue(port.Channel, out var layers) ? layers : []);
    }

    private async Task ApplyArgbAsync(List<string> applied, bool outputPumpStarted)
    {
        if (_argbRouter is null || !await _argbRouter.RefreshAsync()) return;
        var ports = _argbRouter.GetTargets();
        if (ports.Count == 0) return;
        RefreshArgbSnapshots(ports);
        if (!outputPumpStarted) StartOutputPump();
        applied.AddRange(ports.Select(port => port.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void StartOutputPump()
    {
        if (_hidRouter is null && _argbRouter is null) return;
        _outputCancellation?.Cancel();
        _outputCancellation?.Dispose();
        _outputCancellation = new CancellationTokenSource();
        var token = _outputCancellation.Token;
        _outputClock.Restart();
        _outputTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000d / 60d));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    if (_hidRouter is not null)
                        await _hidRouter.ApplyAsync(_outputClock.Elapsed, token).ConfigureAwait(false);
                    if (_argbRouter is not null)
                        await _argbRouter.ApplyAsync(_outputClock.Elapsed, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                // The next Apply reports a fresh failure; a background pump must not crash the app.
            }
        }, token);
    }

    private async Task StopOutputPumpAsync()
    {
        var cancellation = _outputCancellation;
        _outputCancellation = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        if (_outputTask is not null)
        {
            try
            {
                await _outputTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _outputTask = null;
        cancellation.Dispose();
    }

    public async Task StopAsync()
    {
        if (_lighting is null && _hidRouter is null && _argbRouter is null) return;
        IsPlaying = false;
        IsBusy = true;
        var failures = new List<string>();
        async Task StopPartAsync(Func<Task> stop)
        {
            try
            {
                await stop().ConfigureAwait(true);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                failures.Add(exception.Message);
            }
        }

        try
        {
            await StopPartAsync(StopOutputPumpAsync);
            if (_hidRouter is not null) await StopPartAsync(() =>
                _hidRouter.StopAsync(applyFallback: _restoreAfterExternal is null));
            if (_argbRouter is not null) await StopPartAsync(() => _argbRouter.StopAsync());
            // Set the neutral status first; a restore-failure event that follows must win.
            StatusText = AppStrings.Text("LightingStudioStatusStopped");
            if (_lighting is not null) await StopPartAsync(() => _lighting.StopAsync());
            if (_inputSampler is not null) await StopPartAsync(() => _inputSampler.StopAsync().AsTask());
            if (_needsExternalRestore && _restoreAfterExternal is { } restore)
            {
                try
                {
                    await restore().ConfigureAwait(true);
                    _needsExternalRestore = false;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(exception.Message);
                }
            }
            _source = null;
            _appliedBlade = null;
            IsApplied = false;
            if (failures.Count > 0)
            {
                StatusText = AppStrings.FormatText(
                    "LightingStudioStatusStopFailed",
                    string.Join("; ", failures));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnOutputWriteFailed(string deviceName, Exception exception)
    {
        var error = DescribeFailure(exception);
        RecordOutputFailure(deviceName, error, restore: false);
        void Report() => StatusText =
            $"{AppStrings.FormatText("LightingStudioStatusDeviceWriteFailed", deviceName)}: {error}";
        if (_dispatcher is { } dispatcher && !dispatcher.HasThreadAccess) dispatcher.TryEnqueue(Report);
        else Report();
    }

    private void OnHidRestoreFailed(string deviceName, Exception exception)
    {
        // A Blade without readable effect state uses a documented exit effect rather
        // than restoring a frame; show that as a plain state, not a raw English error.
        if (exception is NotSupportedException)
        {
            RecordOutputFailure(deviceName, exception.Message, restore: true);
            void ReportFallback() => StatusText =
                AppStrings.FormatText("LightingStudioStatusRestoreFallback", deviceName);
            if (_dispatcher is { } fallbackDispatcher && !fallbackDispatcher.HasThreadAccess)
                fallbackDispatcher.TryEnqueue(ReportFallback);
            else ReportFallback();
            return;
        }

        var error = DescribeFailure(exception);
        RecordOutputFailure(deviceName, error, restore: true);
        void Report() => StatusText =
            $"{AppStrings.FormatText("LightingStudioStatusRestoreFailed", deviceName)}: {error}";
        if (_dispatcher is { } dispatcher && !dispatcher.HasThreadAccess) dispatcher.TryEnqueue(Report);
        else Report();
    }

    private static string DescribeFailure(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

    private void RecordOutputFailure(string deviceName, string error, bool restore)
    {
        void Report()
        {
            var failure = new StudioOutputFailure(deviceName, error, restore);
            for (var index = 0; index < OutputFailures.Count; index++)
            {
                var existing = OutputFailures[index];
                if (existing.Restore == restore &&
                    string.Equals(existing.DeviceName, deviceName, StringComparison.Ordinal))
                {
                    OutputFailures[index] = failure;
                    return;
                }
            }

            OutputFailures.Add(failure);
        }

        if (_dispatcher is { } dispatcher && !dispatcher.HasThreadAccess) dispatcher.TryEnqueue(Report);
        else Report();
    }
}
