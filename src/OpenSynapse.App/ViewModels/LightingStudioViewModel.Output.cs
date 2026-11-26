using System.Collections.ObjectModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>One device that rejected a frame or failed to restore its own lighting.</summary>
public sealed record StudioOutputFailure(string DeviceName, string Error, bool Restore);

/// <summary>Per-device layer snapshots and the HID frame pump.</summary>
public sealed partial class LightingStudioViewModel
{
    private readonly Dictionary<(ushort ProductId, string InstanceId, int Rows, int Columns, StudioSceneRole Role),
        StudioLayerRender[]>
        _layerCache = new();
    private readonly StudioChromaStandardOutputRouter? _chromaRouter;

    /// <summary>Latest failure per device and operation, observable without hiding the global status.</summary>
    public ObservableCollection<StudioOutputFailure> OutputFailures { get; } = new();

    public bool CanApply =>
        !_isBusy && (_argbRouter is not null || _headsetRouter is not null || _chromaRouter is not null ||
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
        return BuildLayersFor(
            device.EffectiveProductId, device.EffectiveInstanceId, rows, columns, RoleFor(device));
    }

    private static StudioSceneRole RoleFor(StudioDeviceOption device)
    {
        if (device.IsBlade) return StudioSceneRole.Keyboard;
        if (device.IsArgb) return StudioSceneRole.Argb;
        if (device.IsSingleZone || (device.Rows == 1 && device.Columns == 1)) return StudioSceneRole.SingleZone;
        if (device.Rows > 1) return StudioSceneRole.Keyboard;
        return device.Columns >= 20 ? StudioSceneRole.Mousepad : StudioSceneRole.Mouse;
    }

    private StudioLayerRender[] BuildLayersFor(
        ushort productId, string? instanceId, int rows, int columns, StudioSceneRole role)
    {
        if (UsesWorld(instanceId)) return _world.Canvas?.Layers ?? [];

        if (_activeScene is { } scene) return StudioSceneRenderer.Render(scene, role);

        var key = (productId, instanceId ?? string.Empty, rows, columns, role);
        if (_layerCache.TryGetValue(key, out var cached)) return cached;

        var profile = _project.SelectedProfile;
        if (profile is null || rows <= 0 || columns <= 0) return [];

        var layers = new List<StudioLayerRender>();
        foreach (var layer in profile.EnumerateLayers())
        {
            if (layer.IsGroup) continue;
            // .chroma is a keyboard matrix animation; a 1-row target has no
            // meaningful frame to show, so mice and single-LED devices stay dark.
            if (layer.Params.Effect == StudioEffectKind.ChromaFile && rows <= 1) continue;
            if (layer.Regions.Count == 0)
            {
                layers.Add(new StudioLayerRender(
                    layer.Params, null, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
                continue;
            }

            foreach (var region in layer.Regions)
            {
                // Import leaves an empty region when the device it named is not
                // connected. It has no target; painting it full-canvas would wash
                // the other devices with that region's color.
                if (region.Devices.Count == 0) continue;
                if (region.Params.Effect == StudioEffectKind.ChromaFile && rows <= 1) continue;

                var targetsDevice = false;
                foreach (var target in region.Devices)
                {
                    if (target.ProductId != productId) continue;
                    if (!string.IsNullOrWhiteSpace(target.InstanceId) &&
                        !string.Equals(target.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    targetsDevice = true;
                    break;
                }

                if (!targetsDevice) continue;

                var mask = StudioMaskBuilder.BuildRegion(region, productId, instanceId, rows, columns);
                if (!HasMask(mask)) continue;
                layers.Add(new StudioLayerRender(
                    region.Params, mask, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
            }
        }

        var result = layers.ToArray();
        _layerCache[key] = result;
        return result;
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
        EnsureWorld();
        var frames = new Dictionary<string, StudioDeviceFrame>(StringComparer.OrdinalIgnoreCase);
        if (_world.Active)
        {
            foreach (var pair in _world.RenderDeviceFrames(elapsed)) frames[pair.Key] = pair.Value;
        }
        var now = Math.Max(0, elapsed.TotalSeconds);
        _previewSeconds = now;
        foreach (var device in Devices)
        {
            if (!device.HasMatrix) continue;
            if (frames.ContainsKey(device.EffectiveInstanceId ?? string.Empty)) continue;
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
                var state = GetPreviewClock(device.EffectiveInstanceId, layer.Id)
                    .Sample(now, layer.Params, rows, columns);
                timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
            }

            var input = _inputSampler?.Sample(rows, columns) ?? default;
            input = input with { Keys = PreviewKeysFor(device, rows, columns).Snapshot() };
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
        EnsureWorld();
        var live = targets ?? _hidRouter.GetTargets();
        var snapshots = new Dictionary<string, StudioLayerRender[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in live)
        {
            var (rows, columns) = target.Definition.MatrixDimensions is { } dimensions &&
                target.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite)
                ? MatrixSize(FindLayout(target.InstanceId), dimensions.Rows, dimensions.Columns)
                : (1, 1);
            var device = Devices.FirstOrDefault(item =>
                StudioLedSelection.SameInstance(item.EffectiveInstanceId, target.InstanceId));
            var role = device is not null
                ? RoleFor(device)
                : rows > 1 ? StudioSceneRole.Keyboard : StudioSceneRole.Mouse;
            snapshots[target.InstanceId] = BuildLayersFor(
                target.Definition.ProductId, target.InstanceId, rows, columns, role);
        }

        _hidRouter.Update(connection => snapshots.TryGetValue(connection.InstanceId, out var layers) ? layers : []);
    }

    private void RefreshArgbSnapshots(IReadOnlyList<StudioArgbPort> ports)
    {
        if (_argbRouter is null) return;
        EnsureWorld();
        var snapshots = new Dictionary<byte, StudioLayerRender[]>();
        foreach (var port in ports)
        {
            if (UsesWorld(port.InstanceId))
            {
                snapshots[port.Channel] = _world.Canvas?.Layers ?? [];
                continue;
            }

            var device = Devices.FirstOrDefault(item => item.IsArgb &&
                StudioLedSelection.SameInstance(item.EffectiveInstanceId, port.InstanceId));
            var (rows, columns) = device is null ? (1, port.LedCount) : MatrixSize(device);
            var layers = BuildLayersFor(
                port.ProductId, port.InstanceId, rows, columns, StudioSceneRole.Argb);
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

    [NotProfileScoped]
    private bool ApplyChroma(List<string> applied, ref bool outputPumpStarted)
    {
        if (_chromaRouter is null) return false;
        var targets = _chromaRouter.GetTargets();
        if (targets.Count == 0) return false;
        RefreshChromaSnapshots(targets);
        if (!outputPumpStarted)
        {
            StartOutputPump();
            outputPumpStarted = true;
        }

        applied.AddRange(targets.Select(target => target.Name));
        return true;
    }

    private void RefreshChromaSnapshots(IReadOnlyList<RazerChromaDeviceConnection> targets)
    {
        if (_chromaRouter is null) return;
        EnsureWorld();
        var snapshots = new Dictionary<string, StudioLayerRender[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            snapshots[target.DevicePath] = BuildLayersFor(
                target.ProductId, target.DevicePath, 1, 1, StudioSceneRole.ChromaLink);
        }

        _chromaRouter.Update(connection =>
            snapshots.TryGetValue(connection.DevicePath, out var layers) ? layers : []);
    }

    [NotProfileScoped]
    private async Task<bool> ApplyHeadsetAsync(List<string> applied)
    {
        if (_headsetRouter is null || !await _headsetRouter.RefreshAsync().ConfigureAwait(true))
        {
            return false;
        }

        _headsetRouter.Update(BuildHeadsetLayers);
        applied.Add(_headsetRouter.DisplayName ?? "Razer headset");
        return true;
    }

    private IReadOnlyList<StudioLayerRender> BuildHeadsetLayers()
    {
        var device = Devices.FirstOrDefault(item => item.HasMatrix &&
            StudioLedSelection.SameInstance(item.EffectiveInstanceId, _headsetRouter!.InstanceId));
        return device is null
            ? BuildLayersFor(
                _headsetRouter!.ProductId,
                _headsetRouter.InstanceId,
                1,
                _headsetRouter.LedCount,
                StudioSceneRole.Headset)
            : BuildLayersFor(device);
    }

    private void StartOutputPump()
    {
        if (_hidRouter is null && _argbRouter is null && _headsetRouter is null && _chromaRouter is null) return;
        // Re-applying must not reset the effect clock or spawn a second pump.
        if (_outputTask is { IsCompleted: false }) return;
        _outputCancellation?.Cancel();
        _outputCancellation?.Dispose();
        _outputCancellation = new CancellationTokenSource();
        var token = _outputCancellation.Token;
        _outputClock.Restart();
        _outputTask = Task.Run(async () =>
        {
            // Razer's Studio engine and its native effects tick at 25 fps. The
            // matrix HID endpoints cannot sustain the old 60 fps pump.
            using var timer = new PeriodicTimer(
                TimeSpan.FromSeconds(1d / StudioEffectRenderer.NativeFramesPerSecond));
            // A fullscreen game can hold a HID endpoint long enough to fail a write. One
            // failed route must skip that frame only: ending the pump leaves every device
            // dark until the user re-applies.
            (string Name, Func<TimeSpan, CancellationToken, Task>? Apply)[] routes =
            [
                ("hid", _hidRouter is null ? null : (elapsed, ct) => _hidRouter.ApplyAsync(elapsed, ct)),
                ("argb", _argbRouter is null ? null : (elapsed, ct) => _argbRouter.ApplyAsync(elapsed, ct)),
                ("headset", _headsetRouter is null
                    ? null
                    : (elapsed, ct) => _headsetRouter.ApplyAsync(elapsed, ct)),
                ("chroma", _chromaRouter is null
                    ? null
                    : (elapsed, ct) => _chromaRouter.ApplyAsync(elapsed, ct)),
            ];
            static void LogFrame(long frameStart, long[] routeTicks)
            {
                var total = System.Diagnostics.Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
                if (total <= 80) return;
                var spread = routeTicks.Length == 0
                    ? 0
                    : (routeTicks.Max() - routeTicks.Min()) * 1000d /
                        System.Diagnostics.Stopwatch.Frequency;
                System.Diagnostics.Debug.WriteLine(
                    $"studio output frame {total:F0} ms, route spread {spread:F0} ms");
            }

            try
            {
                // Every route starts at the same logical instant; otherwise each
                // clock's first _startedAt differs by the time the earlier writes took.
                foreach (var route in routes)
                {
                    await ApplyRouteAsync(route, TimeSpan.Zero, token).ConfigureAwait(false);
                }

                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var elapsed = _outputClock.Elapsed;
                    var frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    var routeTicks = new long[routes.Length];
                    for (var index = 0; index < routes.Length; index++)
                    {
                        if (await ApplyRouteAsync(routes[index], elapsed, token).ConfigureAwait(false))
                        {
                            routeTicks[index] = System.Diagnostics.Stopwatch.GetTimestamp();
                        }
                    }
                    LogFrame(frameStart, routeTicks.Where(tick => tick != 0).ToArray());
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private static async Task<bool> ApplyRouteAsync(
        (string Name, Func<TimeSpan, CancellationToken, Task>? Apply) route,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        if (route.Apply is null) return false;
        try
        {
            await route.Apply(elapsed, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            System.Diagnostics.Debug.WriteLine(
                $"studio {route.Name} route failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
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

    /// <summary>
    /// Hands the devices to another lighting surface by fully stopping the studio
    /// session. StopAsync restores the captured baseline (or the documented exit
    /// effect), so a keyboard leaves custom-frame mode before the device page writes.
    /// </summary>
    internal async Task SuspendOutputAsync()
    {
        if (!IsApplied && !HasSuspendedOutput) return;
        await StopAsync();
    }

    private bool _suspendedOutput;
    private bool _suspendedWasPlaying;
    private readonly SemaphoreSlim _outputLifecycleGate = new(1, 1);
    internal bool HasSuspendedOutput => _suspendedOutput;

    internal void DiscardSuspendedOutput()
    {
        if (!HasSuspendedOutput) return;
        _suspendedOutput = false;
        ClearAppliedProject();
    }

    internal async Task SuspendForExternalAsync()
    {
        await _outputLifecycleGate.WaitAsync();
        try
        {
            if (!IsApplied) return;
            _suspendedOutput = true;
            _suspendedWasPlaying = IsPlaying;
            await StopOutputCoreAsync(preserveAppliedState: true);
        }
        finally { _outputLifecycleGate.Release(); }
    }

    internal async Task ResumeAfterExternalAsync()
    {
        await _outputLifecycleGate.WaitAsync();
        try
        {
            if (!HasSuspendedOutput) return;
            await ApplyOutputCoreAsync();
            if (IsApplied)
            {
                _suspendedOutput = false;
                IsPlaying = _suspendedWasPlaying;
            }
        }
        finally { _outputLifecycleGate.Release(); }
    }

    public async Task StopAsync()
    {
        await _outputLifecycleGate.WaitAsync();
        try
        {
            _suspendedOutput = false;
            await StopOutputCoreAsync(preserveAppliedState: false);
        }
        finally { _outputLifecycleGate.Release(); }
    }

    private async Task StopOutputCoreAsync(bool preserveAppliedState)
    {
        if (_isBusy) return;
        if (_lighting is null && _hidRouter is null && _argbRouter is null &&
            _headsetRouter is null && _chromaRouter is null)
        {
            return;
        }
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
            if (_headsetRouter is not null) await StopPartAsync(() =>
                _headsetRouter.StopAsync(applyFallback: _restoreAfterExternal is null));
            if (_chromaRouter is not null) await StopPartAsync(() => _chromaRouter.StopAsync());
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
            if (!preserveAppliedState)
            {
                _activeScene = null;
                UpdateSceneSelection();
                _sceneProfile = null;
                OnPropertyChanged(nameof(SelectedSceneCard));
                ClearAppliedProject();
            }
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
