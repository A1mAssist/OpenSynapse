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
    public Task<string?> TryApplyMacroBindingAsync(
        string macroId,
        MacroInput input,
        MacroTriggerMode triggerMode,
        MacroPlaybackMode playbackMode,
        int repeatCount,
        bool hyperShiftLayer,
        MacroSoftwareBinding? softwareBinding,
        CancellationToken cancellationToken = default) =>
        TryApplyMacroBindingCoreAsync(macroId, input, triggerMode, playbackMode, repeatCount,
            hyperShiftLayer, softwareBinding, cancellationToken, deferSave: false);

    private async Task<string?> TryApplyMacroBindingCoreAsync(
        string macroId,
        MacroInput input,
        MacroTriggerMode triggerMode,
        MacroPlaybackMode playbackMode,
        int repeatCount,
        bool hyperShiftLayer,
        MacroSoftwareBinding? softwareBinding,
        CancellationToken cancellationToken,
        bool deferSave)
    {
        var row = Items.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, macroId));
        if (row is null)
        {
            if (SoftwareShortcutCatalog.ActionOf(macroId) is { } shortcut)
            {
                return await TryApplyBuiltInBindingAsync(
                        shortcut.CreateMacro(), input, hyperShiftLayer, softwareBinding, cancellationToken, deferSave)
                    .ConfigureAwait(true);
            }

            return NativeMouseButtonCatalog.ActionOf(macroId) is { } native
                ? await TryApplyBuiltInBindingAsync(
                        native.CreateMacro(), input, hyperShiftLayer, softwareBinding, cancellationToken, deferSave)
                    .ConfigureAwait(true)
                : AppStrings.Text("MacroTriggerMacroMissing");
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
        var previousAdditional = CloneTriggers(row.Definition.AdditionalTriggers);
        var previousBinding = row.Definition.SoftwareBinding?.Clone();
        var existing = row.Definition.FindTrigger(input, MacroModifierKeys.None, hyperShiftLayer);
        if (existing is null && row.Definition.Trigger is null)
        {
            existing = trigger;
            row.SetTrigger(trigger);
            row.Definition.SoftwareBinding = softwareBinding?.Clone();
        }
        else if (existing is null)
        {
            trigger.SoftwareBinding = softwareBinding?.Clone();
            row.Definition.AdditionalTriggers.Add(trigger);
            existing = trigger;
        }
        else
        {
            existing.Mode = triggerMode;
            // Two mapping rows can report the same physical key. The first
            // binding owns the assignment; a later alias must not take it over.
            if (softwareBinding is not null &&
                (existing.SoftwareBinding is null ||
                 existing.SoftwareBinding.ButtonId == softwareBinding.ButtonId))
            {
                existing.SoftwareBinding = softwareBinding.Clone();
                if (ReferenceEquals(existing, row.Definition.Trigger))
                {
                    row.Definition.SoftwareBinding = softwareBinding.Clone();
                }
            }
            row.SetTrigger(row.Definition.Trigger);
        }
        // Playback belongs to the key that was bound. Writing it to the
        // definition would let a second key change the first key's behavior.
        existing.PlaybackMode = playbackMode;
        existing.RepeatCount = playbackMode == MacroPlaybackMode.Repeat
            ? Math.Clamp(repeatCount, 1, MacroDefinition.MaxRepeatCount)
            : null;
        row.Refresh();
        MarkMacroDirty(row);
        if (deferSave) return null;
        if (!await TrySaveMacroProfileAsync(cancellationToken).ConfigureAwait(true))
        {
            row.SetTrigger(previousTrigger);
            row.Definition.AdditionalTriggers = previousAdditional;
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
    /// Removes one trigger from a macro without changing the editor selection.
    /// Returns null on success, or the user-facing error when the profile was not changed.
    /// </summary>
    public Task<string?> TryClearMacroTriggerAsync(
        string macroId,
        MacroInput input,
        bool hyperShiftLayer,
        CancellationToken cancellationToken = default) =>
        TryClearMacroTriggerCoreAsync(macroId, input, hyperShiftLayer, cancellationToken, deferSave: false);

    private async Task<string?> TryClearMacroTriggerCoreAsync(
        string macroId,
        MacroInput input,
        bool hyperShiftLayer,
        CancellationToken cancellationToken,
        bool deferSave)
    {
        var row = Items.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, macroId));
        if (row is null)
        {
            return !IsBuiltInMacro(macroId)
                ? null
                : await TryClearShortcutTriggerAsync(
                        macroId, input, hyperShiftLayer, cancellationToken, deferSave)
                    .ConfigureAwait(true);
        }

        var trigger = row?.Definition.FindTrigger(input, MacroModifierKeys.None, hyperShiftLayer);
        return row is null || trigger is null
            ? null
            : await ClearMacroTriggersAsync(row, [trigger], cancellationToken, deferSave).ConfigureAwait(true);
    }

    public Task<string?> TryClearMacroTriggerAsync(
        string macroId,
        CancellationToken cancellationToken = default)
    {
        var row = Items.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, macroId));
        if (row is null && IsBuiltInMacro(macroId))
        {
            return TryClearShortcutTriggersAsync(macroId, cancellationToken);
        }

        var triggers = row?.Definition.AllTriggers.ToArray() ?? [];
        return triggers.Length == 0
            ? Task.FromResult<string?>(null)
            : ClearMacroTriggersAsync(row!, triggers, cancellationToken);
    }

    /// <summary>
    /// A built-in action ignores the trigger/playback choice: the mapping page
    /// only needs the button and the original assignment it has to restore.
    /// </summary>
    private async Task<string?> TryApplyBuiltInBindingAsync(
        MacroDefinition prototype,
        MacroInput input,
        bool hyperShiftLayer,
        MacroSoftwareBinding? softwareBinding,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        if (IsMacroRecording || IsMacroCountdown)
        {
            return AppStrings.Text("MacroEditBusy");
        }

        var profile = _getActiveProfile();
        var stored = profile.SystemMacros.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, prototype.Id));
        var added = stored is null;
        stored ??= prototype;
        var previousTrigger = stored.Trigger;
        var previousAdditional = CloneTriggers(stored.AdditionalTriggers);
        var previousBinding = stored.SoftwareBinding?.Clone();
        var trigger = stored.FindTrigger(input, MacroModifierKeys.None, hyperShiftLayer);
        if (trigger is null)
        {
            trigger = new MacroTrigger
            {
                Input = input.Clone(),
                Mode = MacroTriggerMode.Press,
                HyperShiftLayer = hyperShiftLayer,
            };
            stored.AdditionalTriggers.Add(trigger);
        }

        trigger.Mode = MacroTriggerMode.Press;
        trigger.SoftwareBinding = softwareBinding?.Clone();
        stored.PlaybackMode = MacroPlaybackMode.Once;
        stored.RepeatCount = 1;
        if (added)
        {
            profile.SystemMacros.Add(stored);
        }

        if (deferSave) return null;
        if (!await TrySaveMacroProfileAsync(cancellationToken, prepareDirtyMacros: false)
                .ConfigureAwait(true))
        {
            stored.Trigger = previousTrigger;
            stored.AdditionalTriggers = previousAdditional;
            stored.SoftwareBinding = previousBinding;
            if (added)
            {
                profile.SystemMacros.Remove(stored);
            }

            return AppStrings.Text("MacroSaveFailed");
        }

        RefreshMacroRuntime();
        return null;
    }

    private async Task<string?> TryClearShortcutTriggerAsync(
        string macroId,
        MacroInput input,
        bool hyperShiftLayer,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        var stored = FindSystemMacro(macroId);
        var trigger = stored?.FindTrigger(input, MacroModifierKeys.None, hyperShiftLayer);
        return stored is null || trigger is null
            ? null
            : await RemoveSystemMacroTriggersAsync(stored, [trigger], cancellationToken, deferSave)
                .ConfigureAwait(true);
    }

    private Task<string?> TryClearShortcutTriggersAsync(
        string macroId,
        CancellationToken cancellationToken)
    {
        var stored = FindSystemMacro(macroId);
        return stored is null || !stored.AllTriggers.Any()
            ? Task.FromResult<string?>(null)
            : RemoveSystemMacroTriggersAsync(stored, stored.AllTriggers.ToArray(), cancellationToken);
    }

    private MacroDefinition? FindSystemMacro(string macroId) =>
        _getActiveProfile().SystemMacros.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, macroId));

    private static bool IsBuiltInMacro(string macroId) =>
        SoftwareShortcutCatalog.ActionOf(macroId) is not null ||
        NativeMouseButtonCatalog.ActionOf(macroId) is not null;

    private async Task<string?> RemoveSystemMacroTriggersAsync(
        MacroDefinition stored,
        IReadOnlyList<MacroTrigger> triggers,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        if (IsMacroRecording || IsMacroCountdown)
        {
            return AppStrings.Text("MacroEditBusy");
        }

        MacroSoftwareBinding[] keypadBindings = deferSave ? [] : KeypadSoftwareBindings(stored, triggers);
        await using var restoration = await BeginKeypadBindingRestoreAsync(keypadBindings, cancellationToken)
            .ConfigureAwait(true);
        if (keypadBindings.Length > 0 && restoration is null)
            return AppStrings.Text("MacroTriggerRestoreFailed");

        var previousTrigger = stored.Trigger;
        var previousAdditional = CloneTriggers(stored.AdditionalTriggers);
        var previousBinding = stored.SoftwareBinding?.Clone();
        var removed = triggers.ToHashSet();
        stored.AdditionalTriggers.RemoveAll(removed.Contains);
        if (stored.Trigger is { } primary && removed.Contains(primary))
        {
            stored.Trigger = stored.AdditionalTriggers.FirstOrDefault();
            if (stored.Trigger is not null)
            {
                stored.AdditionalTriggers.Remove(stored.Trigger);
            }

            stored.SoftwareBinding = stored.Trigger?.SoftwareBinding?.Clone();
        }

        var profile = _getActiveProfile();
        var dropping = !stored.AllTriggers.Any();
        if (dropping)
        {
            profile.SystemMacros.Remove(stored);
        }

        if (deferSave) return null;
        if (!await TrySaveMacroProfileAsync(cancellationToken, prepareDirtyMacros: false)
                .ConfigureAwait(true))
        {
            if (dropping)
            {
                profile.SystemMacros.Add(stored);
            }

            stored.Trigger = previousTrigger;
            stored.AdditionalTriggers = previousAdditional;
            stored.SoftwareBinding = previousBinding;
            return AppStrings.Text("MacroSaveFailed");
        }

        restoration?.Commit();
        RefreshMacroRuntime();
        return null;
    }

    private async Task<string?> ClearMacroTriggersAsync(
        MacroRowViewModel row,
        IReadOnlyList<MacroTrigger> triggers,
        CancellationToken cancellationToken,
        bool deferSave = false)
    {
        if (triggers.Count == 0)
        {
            return null;
        }

        if (IsMacroRecording || IsMacroCountdown)
        {
            return AppStrings.Text("MacroEditBusy");
        }

        MacroSoftwareBinding[] keypadBindings = deferSave ? [] : KeypadSoftwareBindings(row.Definition, triggers);
        await using var restoration = await BeginKeypadBindingRestoreAsync(keypadBindings, cancellationToken)
            .ConfigureAwait(true);
        if (keypadBindings.Length > 0 && restoration is null)
            return AppStrings.Text("MacroTriggerRestoreFailed");

        var wasDirty = _dirtyMacroIds.Contains(row.Id);
        var previousTrigger = row.Definition.Trigger?.Clone();
        var previousAdditional = CloneTriggers(row.Definition.AdditionalTriggers);
        var previousBinding = row.Definition.SoftwareBinding?.Clone();
        var removed = triggers.ToHashSet();
        row.Definition.AdditionalTriggers.RemoveAll(removed.Contains);
        if (row.Definition.Trigger is { } primary && removed.Contains(primary))
        {
            row.Definition.Trigger = row.Definition.AdditionalTriggers.FirstOrDefault();
            if (row.Definition.Trigger is not null)
            {
                row.Definition.AdditionalTriggers.Remove(row.Definition.Trigger);
            }
            row.Definition.SoftwareBinding = row.Definition.Trigger?.SoftwareBinding?.Clone();
            row.SetTrigger(row.Definition.Trigger);
        }
        else
        {
            row.Refresh();
        }
        MarkMacroDirty(row);
        if (deferSave) return null;
        if (!await TrySaveMacroProfileAsync(cancellationToken).ConfigureAwait(true))
        {
            row.SetTrigger(previousTrigger);
            row.Definition.AdditionalTriggers = previousAdditional;
            row.Definition.SoftwareBinding = previousBinding;
            RestoreMacroDirtyState(row.Id, wasDirty);
            return AppStrings.Text("MacroSaveFailed");
        }

        restoration?.Commit();
        RefreshMacroRuntime();
        NotifySelectedMacroChanged();
        return null;
    }

    private static List<MacroTrigger> CloneTriggers(IReadOnlyList<MacroTrigger> triggers) =>
        triggers.Select(trigger => trigger.Clone()).ToList();
}
