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

    private CancellationTokenSource? _executionModeSaveCancellation;

    public IReadOnlyList<string> MacroTriggerModeOptions => AppStrings.Texts(
        "MacroTriggerModePress",
        "RazerMappingMacroTriggerHold",
        "RazerMappingMacroPlaybackToggle",
        "RazerMappingMacroPlaybackHoldRepeat");

    public MacroTriggerMode MacroTriggerMode =>
        SelectedMacro?.Definition.Trigger?.Mode ??
        SelectedMacro?.Definition.DefaultTriggerMode ?? MacroTriggerMode.Press;

    public int MacroTriggerModeIndex
    {
        get
        {
            var index = Array.IndexOf(EditableTriggerModes, MacroTriggerMode);
            return index < 0 ? 0 : index;
        }
        set
        {
            if (SelectedMacro is not { } selected ||
                value < 0 || value >= EditableTriggerModes.Length)
            {
                return;
            }

            var mode = EditableTriggerModes[value];
            if (MacroTriggerMode == mode)
            {
                return;
            }

            var previousMode = MacroTriggerMode;
            selected.Definition.DefaultTriggerMode = mode;
            if (selected.Definition.Trigger is { } trigger)
            {
                trigger.Mode = mode;
            }

            ApplyPlaybackChoice(selected, previousMode, mode);
            MarkMacroDirty(selected);
            RequestExecutionModeSave();

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
            if (DisplayPlaybackMode == mode)
            {
                return;
            }

            SetPlayback(selected.Definition, mode);

            MarkMacroDirty(selected);
            NotifyExecutionModeChanged();
            RequestExecutionModeSave();
        }
    }

    public double MacroExecutionRepeatCount
    {
        get => SelectedMacro is { } selected
            ? selected.Definition.EffectiveRepeatCount(selected.Definition.Trigger)
            : 1;
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
            var trigger = selected.Definition.Trigger;
            if (selected.Definition.RepeatCount == count &&
                (trigger?.PlaybackMode != MacroPlaybackMode.Repeat ||
                 trigger.RepeatCount == count))
            {
                return;
            }

            selected.Definition.RepeatCount = count;
            if (trigger is { PlaybackMode: MacroPlaybackMode.Repeat })
            {
                trigger.RepeatCount = count;
            }
            MarkMacroDirty(selected);
            RequestExecutionModeSave();
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
                        SelectedMacro.Definition.EffectiveRepeatCount(
                            SelectedMacro.Definition.Trigger),
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
            // Keep the current choice if the user subsequently clears the trigger.
            selected.Definition.DefaultTriggerMode = trigger.Mode;
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
                SelectedMacro?.Definition.EffectivePlaybackMode(
                    SelectedMacro.Definition.Trigger) ?? MacroPlaybackMode.Once);

    private static void SetPlayback(MacroDefinition definition, MacroPlaybackMode mode)
    {
        definition.PlaybackMode = mode;
        if (mode != MacroPlaybackMode.Repeat)
        {
            definition.RepeatCount = 1;
        }

        // The editor edits the definition's first binding; other keys keep
        // the playback each was bound with.
        if (definition.Trigger is { } trigger)
        {
            trigger.PlaybackMode = mode;
            trigger.RepeatCount = mode == MacroPlaybackMode.Repeat
                ? Math.Clamp(definition.RepeatCount, 1, MacroDefinition.MaxRepeatCount)
                : null;
        }
    }

    private static (MacroPlaybackPlan Plan, MacroPlaybackMode Playback) BuildDurationPlan(
        MacroDefinition definition,
        IReadOnlyDictionary<string, MacroDefinition> macros)
    {
        var playback = definition.EffectivePlaybackMode(definition.Trigger);
        return (
            MacroPlaybackPlan.Build(
                definition,
                macros,
                playback,
                definition.EffectiveRepeatCount(definition.Trigger)),
            playback);
    }

    private static void ApplyPlaybackChoice(
        MacroRowViewModel selected,
        MacroTriggerMode previousMode,
        MacroTriggerMode mode)
    {
        var playback = selected.Definition.EffectivePlaybackMode(selected.Definition.Trigger);
        if (previousMode == MacroTriggerMode.HoldRepeat &&
            mode != MacroTriggerMode.HoldRepeat &&
            playback == MacroPlaybackMode.RepeatUntilStopped)
        {
            playback = MacroPlaybackMode.Once;
        }

        playback = mode == MacroTriggerMode.HoldRepeat
            ? MacroPlaybackMode.RepeatUntilStopped
            : MacroPlaybackChoices.Normalize(mode, playback);
        if (selected.Definition.PlaybackMode == playback &&
            selected.Definition.EffectivePlaybackMode(selected.Definition.Trigger) == playback)
        {
            return;
        }

        SetPlayback(selected.Definition, playback);
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

    private void RequestExecutionModeSave()
    {
        _executionModeSaveCancellation?.Cancel();
        _executionModeSaveCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _executionModeSaveCancellation = cancellation;
        _ = SaveExecutionModeAfterDelayAsync(cancellation);
    }

    private async Task SaveExecutionModeAfterDelayAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(250, cancellation.Token).ConfigureAwait(false);
            if (_macroDispatcherQueue is not { } dispatcher)
            {
                return;
            }

            if (!dispatcher.HasThreadAccess)
            {
                dispatcher.TryEnqueue(() => _ = SaveExecutionModeNowAsync());
                return;
            }

            await SaveExecutionModeNowAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite(
                "macro",
                $"execution mode auto-save failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_executionModeSaveCancellation, cancellation))
            {
                _executionModeSaveCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task SaveExecutionModeNowAsync()
    {
        if (IsMacroRecording || IsMacroCountdown)
        {
            return;
        }

        var dirtyRows = Items
            .Where(row => _dirtyMacroIds.Contains(row.Id))
            .ToArray();
        if (dirtyRows.Length == 0 ||
            dirtyRows.Any(row =>
                !ValidateMacroTree(row.Definition) ||
                !ValidateMacroReferences(row.Definition)))
        {
            // An incomplete step field stays dirty for the explicit Save.
            return;
        }

        if (await SaveMacroChangesCoreAsync(
                forceSelected: false,
                CancellationToken.None).ConfigureAwait(true))
        {
            StatusText = AppStrings.Text("MacroSaved");
        }
    }
}
