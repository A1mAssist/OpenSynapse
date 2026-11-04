using System.Diagnostics;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>Maps decoded Blade keyboard input onto studio playback clocks.</summary>
public sealed partial class LightingStudioViewModel
{
    private StudioPlaybackTrigger? _previewTrigger;
    private readonly Stopwatch _inputClock = new();

    internal int HandleMappingInput(BladeMappingInputEvent input, TimeSpan previewElapsed)
    {
        if (!input.IsDown || input.Kind != BladeMappingInputKind.Keyboard) return 0;

        var blade = _appliedBlade ?? Devices.FirstOrDefault(device => device.IsBlade);
        if (blade is null) return 0;

        var layout = FindLayout(blade.EffectiveInstanceId);
        if (layout is null)
        {
            StatusText = AppStrings.FormatText(
                "LightingStudioStatusInputLayoutMissing", blade.Name);
            return 0;
        }

        if (layout.FindInput("kbd", input.Code) is not { } key) return 0;

        var layers = BuildLayersFor(blade);
        var (rows, columns) = FrameSize(blade);
        if (rows < 1 || columns < 1) return 0;

        _previewTrigger ??= new StudioPlaybackTrigger(GetClock);
        _previewTrigger.Update(layers, rows, columns);
        var point = new StudioTriggerInput(key.MatrixRow, key.MatrixCol);
        var affected = _previewTrigger.Handle(
            point, Math.Max(0, previewElapsed.TotalSeconds));
        if (affected > 0 && _source is not null)
        {
            _source.HandleInput(point, Math.Max(0, _inputClock.Elapsed.TotalSeconds));
        }

        return affected;
    }
}
