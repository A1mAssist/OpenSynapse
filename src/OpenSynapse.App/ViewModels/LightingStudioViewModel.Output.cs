using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

/// <summary>Per-device layer snapshots and the HID frame pump.</summary>
public sealed partial class LightingStudioViewModel
{
    private StudioLayerRender[] BuildLayers()
    {
        var device = SelectedDevice;
        return device is null
            ? []
            : BuildLayersFor(device.EffectiveProductId, device.EffectiveInstanceId, device.Rows, device.Columns);
    }

    private StudioLayerRender[] BuildLayersFor(StudioDeviceOption device) =>
        BuildLayersFor(device.EffectiveProductId, device.EffectiveInstanceId, device.Rows, device.Columns);

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

    /// <summary>Renders one frame per matrix device for the canvas preview.</summary>
    public IReadOnlyDictionary<string, StudioDeviceFrame> RenderDeviceFrames(TimeSpan elapsed)
    {
        var frames = new Dictionary<string, StudioDeviceFrame>(StringComparer.OrdinalIgnoreCase);
        var now = Math.Max(0, elapsed.TotalSeconds);
        foreach (var device in Devices)
        {
            if (!device.HasMatrix) continue;
            var layers = BuildLayersFor(device);
            if (layers.Length == 0) continue;
            var timed = new (StudioLayerRender Layer, double Seconds)[layers.Length];
            for (var index = 0; index < layers.Length; index++)
            {
                var layer = layers[index];
                var state = GetClock(layer.Id).Sample(now, layer.Params);
                timed[index] = (layer with { Opacity = state.Running ? layer.Opacity : 0 }, state.LocalSeconds);
            }

            var input = _inputSampler?.Sample(device.Rows, device.Columns) ?? default;
            frames[device.EffectiveInstanceId ?? string.Empty] = new StudioDeviceFrame(
                device.Rows,
                device.Columns,
                StudioCompositor.RenderTimedLayers(timed, device.Rows, device.Columns, input));
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
            if (target.Definition.MatrixDimensions is not { } dimensions) continue;
            snapshots[target.InstanceId] = BuildLayersFor(
                target.Definition.ProductId, target.InstanceId, dimensions.Rows, dimensions.Columns);
        }

        _hidRouter.Update(connection => snapshots.TryGetValue(connection.InstanceId, out var layers) ? layers : []);
    }

    private void StartHidPump()
    {
        if (_hidRouter is null) return;
        _outputCancellation?.Cancel();
        _outputCancellation?.Dispose();
        _outputCancellation = new CancellationTokenSource();
        var token = _outputCancellation.Token;
        _outputClock.Restart();
        _outputTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    await _hidRouter.ApplyAsync(_outputClock.Elapsed, token).ConfigureAwait(false);
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

    private async Task StopHidPumpAsync()
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
}
