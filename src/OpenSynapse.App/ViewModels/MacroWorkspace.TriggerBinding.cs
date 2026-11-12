using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    /// <summary>
    /// Binds a trigger to a named macro without selecting it in the macro editor.
    /// Returns null on success, or the user-facing error when the profile was not changed.
    /// </summary>
    public async Task<string?> TryBindMacroTriggerAsync(
        string macroId,
        MacroInput input,
        MacroTriggerMode mode,
        bool hyperShiftLayer = false,
        CancellationToken cancellationToken = default)
    {
        var row = Items.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, macroId));
        if (row is null)
        {
            return AppStrings.Text("MacroTriggerMacroMissing");
        }

        if (IsMacroRecording || IsMacroCountdown)
        {
            return AppStrings.Text("MacroEditBusy");
        }

        var trigger = new MacroTrigger
        {
            Input = input.Clone(),
            Mode = mode,
            HyperShiftLayer = hyperShiftLayer,
        };
        if (FindTriggerConflict(row.Definition, trigger, row) is { } conflict)
        {
            var message = AppStrings.FormatText("MacroTriggerConflict", conflict.Name);
            StatusText = message;
            return message;
        }

        var wasDirty = _dirtyMacroIds.Contains(row.Id);
        var previous = row.Definition.Trigger?.Clone();
        row.SetTrigger(trigger);
        MarkMacroDirty(row);
        if (!await TrySaveMacroProfileAsync(cancellationToken).ConfigureAwait(true))
        {
            row.SetTrigger(previous);
            RestoreMacroDirtyState(row.Id, wasDirty);
            return AppStrings.Text("MacroSaveFailed");
        }

        RefreshMacroRuntime();
        NotifySelectedMacroChanged();
        StatusText = AppStrings.FormatText(
            "MacroTriggerCaptured",
            MacroRowViewModel.DescribeTrigger(trigger));
        return null;
    }

    /// <summary>
    /// Applies the trigger and execution choices from the mapping page in one save.
    /// Returns null on success, or the user-facing error when the profile was not changed.
    /// </summary>
    public async Task<string?> TryApplyMacroBindingAsync(
        string macroId,
        MacroInput input,
        MacroTriggerMode triggerMode,
        MacroPlaybackMode playbackMode,
        int repeatCount,
        bool hyperShiftLayer,
        MacroSoftwareBinding? softwareBinding,
        CancellationToken cancellationToken = default)
    {
        var row = Items.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, macroId));
        if (row is null)
        {
            return AppStrings.Text("MacroTriggerMacroMissing");
        }

        if (IsMacroRecording || IsMacroCountdown)
        {
            return AppStrings.Text("MacroEditBusy");
        }

        var trigger = new MacroTrigger
        {
            Input = input.Clone(),
            Mode = triggerMode,
            HyperShiftLayer = hyperShiftLayer,
        };
        if (FindTriggerConflict(row.Definition, trigger, row) is { } conflict)
        {
            var message = AppStrings.FormatText("MacroTriggerConflict", conflict.Name);
            StatusText = message;
            return message;
        }

        if (!Enum.IsDefined(playbackMode))
        {
            playbackMode = MacroPlaybackMode.Once;
        }

        var wasDirty = _dirtyMacroIds.Contains(row.Id);
        var previousTrigger = row.Definition.Trigger?.Clone();
        var previousPlayback = row.Definition.PlaybackMode;
        var previousRepeat = row.Definition.RepeatCount;
        var previousBinding = row.Definition.SoftwareBinding?.Clone();
        row.SetTrigger(trigger);
        row.Definition.PlaybackMode = playbackMode;
        row.Definition.RepeatCount = Math.Clamp(repeatCount, 1, MacroDefinition.MaxRepeatCount);
        row.Definition.SoftwareBinding = softwareBinding?.Clone();
        row.Refresh();
        MarkMacroDirty(row);
        if (!await TrySaveMacroProfileAsync(cancellationToken).ConfigureAwait(true))
        {
            row.SetTrigger(previousTrigger);
            row.Definition.PlaybackMode = previousPlayback;
            row.Definition.RepeatCount = previousRepeat;
            row.Definition.SoftwareBinding = previousBinding;
            row.Refresh();
            RestoreMacroDirtyState(row.Id, wasDirty);
            return AppStrings.Text("MacroSaveFailed");
        }

        RefreshMacroRuntime();
        NotifySelectedMacroChanged();
        StatusText = AppStrings.FormatText(
            "MacroTriggerCaptured",
            MacroRowViewModel.DescribeTrigger(trigger));
        return null;
    }

    /// <summary>
    /// Removes one macro's trigger by id without changing the editor selection.
    /// Returns null on success, or the user-facing error when the profile was not changed.
    /// </summary>
    public async Task<string?> TryClearMacroTriggerAsync(
        string macroId,
        CancellationToken cancellationToken = default)
    {
        var row = Items.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, macroId));
        if (row?.Definition.Trigger is not { } trigger)
        {
            return null;
        }

        if (IsMacroRecording || IsMacroCountdown)
        {
            return AppStrings.Text("MacroEditBusy");
        }

        var wasDirty = _dirtyMacroIds.Contains(row.Id);
        var previous = trigger.Clone();
        var previousBinding = row.Definition.SoftwareBinding?.Clone();
        row.SetTrigger(null);
        row.Definition.SoftwareBinding = null;
        MarkMacroDirty(row);
        if (!await TrySaveMacroProfileAsync(cancellationToken).ConfigureAwait(true))
        {
            row.SetTrigger(previous);
            row.Definition.SoftwareBinding = previousBinding;
            RestoreMacroDirtyState(row.Id, wasDirty);
            return AppStrings.Text("MacroSaveFailed");
        }

        RefreshMacroRuntime();
        NotifySelectedMacroChanged();
        return null;
    }
}
