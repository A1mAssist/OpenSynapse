using Microsoft.UI.Xaml;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    private static readonly MacroTriggerMode[] EditableTriggerModes =
    [
        MacroTriggerMode.Press,
        MacroTriggerMode.Hold,
        MacroTriggerMode.Toggle,
        MacroTriggerMode.HoldRepeat,
    ];

    private static readonly MacroPlaybackMode[] OnceOrRepeat =
    [
        MacroPlaybackMode.Once,
        MacroPlaybackMode.Repeat,
    ];

    private static readonly MacroPlaybackMode[] OnceRepeatOrContinuous =
    [
        MacroPlaybackMode.Once,
        MacroPlaybackMode.Repeat,
        MacroPlaybackMode.RepeatUntilStopped,
    ];

    private static readonly MacroPlaybackMode[] ContinuousOnly =
    [
        MacroPlaybackMode.RepeatUntilStopped,
    ];

    private string? _pendingTriggerMacroId;
    private MacroTriggerMode _pendingTriggerMode = MacroTriggerMode.Press;

    public IReadOnlyList<string> MacroTriggerModeOptions => AppStrings.Texts(
        "MacroTriggerModePress",
        "RazerMappingMacroTriggerHold",
        "RazerMappingMacroPlaybackToggle",
        "RazerMappingMacroPlaybackHoldRepeat");

    public MacroTriggerMode MacroTriggerMode
    {
        get
        {
            if (SelectedMacro?.Definition.Trigger is { } trigger)
            {
                return trigger.Mode;
            }

            return SelectedMacro is { } selected &&
                StringComparer.Ordinal.Equals(selected.Id, _pendingTriggerMacroId)
                    ? _pendingTriggerMode
                    : MacroTriggerMode.Press;
        }
    }

    public int MacroTriggerModeIndex
    {
        get
        {
            var index = Array.IndexOf(EditableTriggerModes, MacroTriggerMode);
            return index < 0 ? 0 : index;
        }
        set
        {
            if (value < 0 || value >= EditableTriggerModes.Length)
            {
                return;
            }

            var mode = EditableTriggerModes[value];
            if (MacroTriggerMode == mode)
            {
                return;
            }

            var previousMode = MacroTriggerMode;
            _pendingTriggerMacroId = SelectedMacro?.Id;
            _pendingTriggerMode = mode;
            var changed = false;
            if (SelectedMacro is { } selected)
            {
                if (selected.Definition.Trigger is { } trigger)
                {
                    trigger.Mode = mode;
                    changed = true;
                }

                changed |= ApplyPlaybackChoice(selected, previousMode, mode);
                if (changed) MarkMacroDirty(selected);
            }

            OnPropertyChanged(nameof(MacroTriggerMode));
            OnPropertyChanged(nameof(MacroTriggerModeIndex));
            NotifyExecutionModeChanged();
        }
    }

    public IReadOnlyList<string> MacroExecutionOptions => MacroTriggerMode switch
    {
        MacroTriggerMode.Toggle => AppStrings.Texts(
            "RazerMappingMacroPlaybackOnce",
            "RazerMappingMacroPlaybackRepeat",
            "RazerMappingMacroPlaybackContinuous"),
        MacroTriggerMode.HoldRepeat => AppStrings.Texts(
            "RazerMappingMacroPlaybackContinuous"),
        _ => AppStrings.Texts(
            "RazerMappingMacroPlaybackOnce",
            "RazerMappingMacroPlaybackRepeat"),
    };

    public int MacroExecutionModeIndex
    {
        get
        {
            var index = Array.IndexOf(PlaybackModes, DisplayPlaybackMode);
            return index < 0 ? 0 : index;
        }
        set
        {
            if (SelectedMacro is not { } selected ||
                value < 0 ||
                value >= PlaybackModes.Length)
            {
                return;
            }

            var mode = PlaybackModes[value];
            if (selected.Definition.PlaybackMode == mode)
            {
                return;
            }

            selected.Definition.PlaybackMode = mode;
            if (mode != MacroPlaybackMode.Repeat)
            {
                selected.Definition.RepeatCount = 1;
            }

            MarkMacroDirty(selected);
            NotifyExecutionModeChanged();
        }
    }

    public double MacroExecutionRepeatCount
    {
        get => SelectedMacro?.Definition.RepeatCount ?? 1;
        set
        {
            if (SelectedMacro is not { } selected || !double.IsFinite(value))
            {
                return;
            }

            var count = Math.Clamp(
                (int)Math.Round(value),
                1,
                MacroDefinition.MaxRepeatCount);
            if (selected.Definition.RepeatCount == count)
            {
                return;
            }

            selected.Definition.RepeatCount = count;
            MarkMacroDirty(selected);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacroExecutionSummary));
            OnPropertyChanged(nameof(MacroTotalDurationText));
        }
    }

    public Visibility MacroExecutionModeVisibility =>
        MacroTriggerMode == MacroTriggerMode.HoldRepeat
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility MacroExecutionRepeatVisibility =>
        DisplayPlaybackMode == MacroPlaybackMode.Repeat
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string MacroExecutionSummary
    {
        get
        {
            if (SelectedMacro is null)
            {
                return string.Empty;
            }

            var execution = DisplayPlaybackMode switch
            {
                MacroPlaybackMode.Repeat => AppStrings.FormatText(
                    "MacroExecutionRepeatValue",
                    Math.Clamp(
                        SelectedMacro.Definition.RepeatCount,
                        1,
                        MacroDefinition.MaxRepeatCount)),
                MacroPlaybackMode.RepeatUntilStopped => AppStrings.Text(
                    "RazerMappingMacroPlaybackContinuous"),
                _ => AppStrings.Text("RazerMappingMacroPlaybackOnce"),
            };
            return AppStrings.FormatText("MacroExecutionSummary", execution);
        }
    }

    internal void SyncExecutionModeFromSelection()
    {
        if (SelectedMacro is { } selected && selected.Definition.Trigger is { } trigger)
        {
            _pendingTriggerMacroId = selected.Id;
            _pendingTriggerMode = trigger.Mode;
        }

        OnPropertyChanged(nameof(MacroTriggerMode));
        OnPropertyChanged(nameof(MacroTriggerModeIndex));
        NotifyExecutionModeChanged();
    }

    private MacroPlaybackMode[] PlaybackModes => MacroTriggerMode switch
    {
        MacroTriggerMode.Toggle => OnceRepeatOrContinuous,
        MacroTriggerMode.HoldRepeat => ContinuousOnly,
        _ => OnceOrRepeat,
    };

    private MacroPlaybackMode DisplayPlaybackMode =>
        MacroTriggerMode == MacroTriggerMode.HoldRepeat
            ? MacroPlaybackMode.RepeatUntilStopped
            : MacroPlaybackChoices.Normalize(
                MacroTriggerMode,
                SelectedMacro?.Definition.PlaybackMode ?? MacroPlaybackMode.Once);

    private static bool ApplyPlaybackChoice(
        MacroRowViewModel selected,
        MacroTriggerMode previousMode,
        MacroTriggerMode mode)
    {
        var playback = selected.Definition.PlaybackMode;
        if (previousMode == MacroTriggerMode.HoldRepeat &&
            mode != MacroTriggerMode.HoldRepeat &&
            playback == MacroPlaybackMode.RepeatUntilStopped)
        {
            playback = MacroPlaybackMode.Once;
        }

        playback = mode == MacroTriggerMode.HoldRepeat
            ? MacroPlaybackMode.RepeatUntilStopped
            : MacroPlaybackChoices.Normalize(mode, playback);
        if (selected.Definition.PlaybackMode == playback)
        {
            return false;
        }

        selected.Definition.PlaybackMode = playback;
        if (playback != MacroPlaybackMode.Repeat)
        {
            selected.Definition.RepeatCount = 1;
        }

        return true;
    }

    private void NotifyExecutionModeChanged()
    {
        OnPropertyChanged(nameof(MacroExecutionOptions));
        OnPropertyChanged(nameof(MacroExecutionModeIndex));
        OnPropertyChanged(nameof(MacroExecutionModeVisibility));
        OnPropertyChanged(nameof(MacroExecutionRepeatCount));
        OnPropertyChanged(nameof(MacroExecutionRepeatVisibility));
        OnPropertyChanged(nameof(MacroExecutionSummary));
        OnPropertyChanged(nameof(MacroTotalDurationText));
    }
}
