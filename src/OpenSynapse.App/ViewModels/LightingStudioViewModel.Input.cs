using System.Diagnostics;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>Shares physical key transitions with preview and device playback.</summary>
public sealed partial class LightingStudioViewModel
{
    private readonly Dictionary<(string Device, Guid Layer), StudioPlaybackClock> _previewClocks = new();
    private readonly Stopwatch _inputClock = new();
    private readonly Stopwatch _previewKeyClock = Stopwatch.StartNew();
    private readonly Dictionary<string, (StudioKeyHeat Heat, double At)> _previewKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private double _previewSeconds;

    public void TogglePlayback()
    {
        IsPlaying = !IsPlaying;
        if (IsPlaying) _ = EnsureInputAsync();
    }

    private StudioPlaybackClock GetPreviewClock(string? instanceId, Guid id)
    {
        var key = (instanceId ?? string.Empty, id);
        if (_previewClocks.TryGetValue(key, out var clock)) return clock;
        var phase = id == Guid.Empty ? 0 : (id.GetHashCode() & 0x7FFF) / 32768d;
        _previewClocks[key] = clock = new StudioPlaybackClock(phase);
        return clock;
    }

    private void RestoreBladeHeldKeys(StudioDeviceOption blade, int rows, int columns) =>
        _source?.RestoreHeldKeys(PreviewKeysFor(blade, rows, columns).Snapshot().Presses
            .Where(press => press.Held).Select(press => (press.Row, press.Column)));

    private StudioInputSources RequiredInputSources()
    {
        var sources = StudioInputSources.None;
        if (_project.SelectedProfile is not { } profile) return sources;
        foreach (var layer in profile.EnumerateLayers().Where(layer => !layer.IsGroup))
        {
            sources |= StudioInputSampler.RequiredSources(layer.Params);
            foreach (var region in layer.Regions)
                sources |= StudioInputSampler.RequiredSources(region.Params);
        }
        return sources;
    }

    internal int HandleMappingInput(BladeMappingInputEvent input, TimeSpan previewElapsed)
    {
        if (input.Kind != BladeMappingInputKind.Keyboard) return 0;

        var blade = _appliedBlade ?? Devices.FirstOrDefault(device => device.IsBlade);
        if (blade is null) return 0;

        var layout = FindLayout(blade.EffectiveInstanceId);
        if (layout is null)
        {
            StatusText = AppStrings.FormatText(
                "LightingStudioStatusInputLayoutMissing", blade.Name);
            return 0;
        }

        var matches = layout.Inputs.Where(key =>
            key.InputType == "kbd" && key.InputCode == input.Code &&
            key.InputModifier == (input.Extended ? 2 : 0)).ToArray();
        if (matches.Length == 0 || matches.Any(key =>
            key.MatrixRow != matches[0].MatrixRow || key.MatrixCol != matches[0].MatrixCol)) return 0;
        var key = matches[0];
        var point = new StudioTriggerInput(key.MatrixRow, key.MatrixCol, input.IsDown);
        var affected = HandlePreviewInput(blade, point, previewElapsed.TotalSeconds);
        _source?.HandleInput(point, Math.Max(0, _inputClock.Elapsed.TotalSeconds));
        return affected;
    }

    private void OnHidInputObserved(string instanceId, StudioTriggerInput input)
    {
        void Apply()
        {
            if (_disposed != 0) return;
            var device = Devices.FirstOrDefault(device => string.Equals(
                device.EffectiveInstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
            if (device is not null) HandlePreviewInput(device, input, _previewSeconds);
        }

        if (_dispatcher is { } dispatcher) dispatcher.TryEnqueue(Apply);
        else Apply();
    }

    private int HandlePreviewInput(StudioDeviceOption device, StudioTriggerInput input, double seconds)
    {
        var (rows, columns) = FrameSize(device);
        if ((uint)input.Row >= (uint)rows || (uint)input.Column >= (uint)columns) return 0;
        var keys = PreviewKeysFor(device, rows, columns);
        if (input.IsDown) keys.Press(input.Row, input.Column);
        else keys.Release(input.Row, input.Column);

        var trigger = new StudioPlaybackTrigger(id => GetPreviewClock(device.EffectiveInstanceId, id));
        trigger.Update(BuildLayersFor(device), rows, columns);
        return trigger.Handle(input, Math.Max(0, seconds));
    }

    private StudioKeyHeat PreviewKeysFor(StudioDeviceOption device, int rows, int columns)
    {
        var id = device.EffectiveInstanceId ?? string.Empty;
        var now = _previewKeyClock.Elapsed.TotalSeconds;
        if (!_previewKeys.TryGetValue(id, out var entry) ||
            entry.Heat.Rows != rows || entry.Heat.Columns != columns)
        {
            entry = (new StudioKeyHeat(rows, columns), now);
        }

        entry.Heat.Decay(now - entry.At);
        _previewKeys[id] = (entry.Heat, now);
        return entry.Heat;
    }
}
