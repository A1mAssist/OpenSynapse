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
}
