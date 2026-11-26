using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    private readonly Func<string, MacroInput, bool, CancellationToken, Task<string?>>?
        _softwareTriggerClearer;

    private sealed record ClearedSoftwareTrigger(
        string MacroId,
        MacroTrigger Trigger,
        MacroPlaybackMode PlaybackMode,
        int RepeatCount,
        MacroSoftwareBinding? SoftwareBinding);

    private int FindSoftwareMacro(MacroInput input, bool hyperShiftLayer, out MacroTriggerMode mode) =>
        RazerSoftwareMacroTriggerMatcher.FindIndex(_localMacros, input, hyperShiftLayer, out mode);

    private MacroDefinition? FindShortcutMacro(MacroInput input, bool hyperShiftLayer) =>
        _systemMacros.FirstOrDefault(macro =>
            SoftwareShortcutCatalog.ActionOf(macro.Id) is not null &&
            macro.AllTriggers.Any(trigger =>
                trigger.HyperShiftLayer == hyperShiftLayer &&
                trigger.Input.Matches(input)));

    private MacroDefinition? FindNativeMouseMacro(MacroInput input, bool hyperShiftLayer) =>
        _systemMacros.FirstOrDefault(macro =>
            NativeMouseButtonCatalog.ActionOf(macro.Id) is not null &&
            macro.AllTriggers.Any(trigger =>
                trigger.HyperShiftLayer == hyperShiftLayer &&
                trigger.Input.Matches(input)));

    /// <summary>The macro that would intercept this key's press on the host.</summary>
    private MacroDefinition? FindHostOwner(MacroInput input, bool hyperShiftLayer) =>
        _localMacros.Concat(_systemMacros).FirstOrDefault(macro =>
            macro.Enabled && macro.FindTrigger(input, hyperShiftLayer) is not null);

    private static MacroSoftwareBinding? SoftwareBindingFor(
        MacroDefinition macro,
        MacroInput input,
        bool hyperShiftLayer)
    {
        var trigger = macro.FindTrigger(input, hyperShiftLayer);
        var binding = trigger?.SoftwareBinding ??
            (trigger is not null && ReferenceEquals(trigger, macro.Trigger)
                ? macro.SoftwareBinding
                : null);
        return binding?.Clone();
    }

    private MacroDefinition? ResolveSoftwareMacro(RazerHidMappingRowViewModel row)
    {
        if (row.OptionIndex < 0)
        {
            return row.SoftwareMacroId is { } loadedId
                ? _localMacros.FirstOrDefault(macro =>
                    StringComparer.Ordinal.Equals(macro.Id, loadedId))
                : null;
        }

        if (row.OptionIndex != row.LoadedOptionIndex)
        {
            // The user picked from the dropdown after the page was read.
            return row.OptionIndex >= 0 && row.OptionIndex < _localMacros.Count
                ? _localMacros[row.OptionIndex]
                : null;
        }

        if (row.SoftwareMacroId is { } boundId)
        {
            return _localMacros.FirstOrDefault(macro =>
                StringComparer.Ordinal.Equals(macro.Id, boundId));
        }

        return row.OptionIndex >= 0 && row.OptionIndex < _localMacros.Count
            ? _localMacros[row.OptionIndex]
            : null;
    }

    private MacroSoftwareBinding? CreateSoftwareBinding(
        RazerHidMappingRowViewModel row,
        IReadOnlyList<AssignmentBackup> backups,
        IReadOnlyList<ClearedSoftwareTrigger> clears)
    {
        if (row.IsHostOnly)
        {
            return null;
        }

        var backup = backups.FirstOrDefault(item => ReferenceEquals(item.Row, row));
        var previous = clears
            .Select(item => item.SoftwareBinding)
            .FirstOrDefault(binding => binding is not null &&
                binding.Layer == Mode &&
                binding.ButtonId == row.ButtonId);
        if (previous is not null &&
            (backup is null || IsAssignmentWrittenByBinding(row, backup.Assignment)))
        {
            // The old macro left this key Off; its binding holds the true original.
            return new MacroSoftwareBinding
            {
                ProductId = _productId,
                InstanceId = previous.InstanceId ?? DeviceInstanceId,
                Layer = Mode,
                ButtonId = row.ButtonId,
                OriginalFunctionId = previous.OriginalFunctionId,
                OriginalData = previous.OriginalData.ToList(),
            };
        }

        return backup is null
            ? null
            : new MacroSoftwareBinding
            {
                ProductId = _productId,
                InstanceId = DeviceInstanceId,
                Layer = Mode,
                ButtonId = row.ButtonId,
                OriginalFunctionId = backup.Assignment.FunctionId,
                OriginalData = backup.Assignment.Data.ToList(),
            };
    }

    // A stale mapping page must not rewrite a macro-editor change. New
    // bindings and rows whose mode/repeat values changed are written;
    // everything else is left alone.
    private HashSet<RazerHidMappingRowViewModel> SoftwareModeRows(
        IReadOnlyList<RazerHidMappingRowViewModel> softwareRows) =>
        softwareRows.Where(row =>
        {
            var macro = ResolveSoftwareMacro(row);
            var input = ResolveSoftwareTrigger(row.ButtonId);
            // A row that still shows the macro but lost its trigger must be rebound.
            return row.OptionIndex != row.LoadedOptionIndex ||
                row.HasSoftwareModeChanges ||
                !StringComparer.Ordinal.Equals(row.SoftwareMacroId, macro?.Id) ||
                (macro is not null && input is not null &&
                 macro.FindTrigger(input, LayerIndex == 1) is null);
        }).ToHashSet();

    private async Task WriteSoftwareModeRowAsync(
        RazerHidMappingRowViewModel row,
        IReadOnlyList<AssignmentBackup> backups,
        IReadOnlyList<ClearedSoftwareTrigger> clears,
        CancellationToken cancellationToken)
    {
        var macro = ResolveSoftwareMacro(row)!;
        var input = ResolveSoftwareTrigger(row.ButtonId)!;
        var error = await _softwareTriggerBinder!(
                macro.Id,
                input,
                row.TriggerMode,
                row.ExecutionMode,
                row.EffectiveExecutionRepeatCount,
                LayerIndex == 1,
                CreateSoftwareBinding(row, backups, clears),
                cancellationToken)
            .ConfigureAwait(true);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        row.MarkSoftwareModeLoaded();
    }

    /// <summary>
    /// A Windows shortcut is never written to the device. The button keeps
    /// reporting (or is turned off for private keys) and the runtime plays the
    /// sequence, exactly like a software macro.
    /// </summary>
    private async Task WriteShortcutRowAsync(
        RazerHidMappingRowViewModel row,
        IReadOnlyList<AssignmentBackup> backups,
        IReadOnlyList<ClearedSoftwareTrigger> clears,
        CancellationToken cancellationToken)
    {
        var action = SoftwareShortcutCatalog.Actions[row.OptionIndex];
        var input = _softwareTriggerResolver!(row.ButtonId)!;
        try
        {
            if (RazerSoftwareMacroAssignmentPolicy.NeedsDeviceAssignment(input))
            {
                var (functionId, data) = BindingAssignment(input, row.ButtonId);
                await _transport.SetAssignmentAsync(
                        row.ButtonId, Mode, functionId, data, cancellationToken)
                    .ConfigureAwait(true);
            }
            else if (backups.FirstOrDefault(item =>
                         ReferenceEquals(item.Row, row))?.Assignment is { } current &&
                     NeedsNativeKeyboardAssignment(current) &&
                     NativeKeyboardAssignment(row.ButtonId) is { } native)
            {
                // The device still plays the firmware shortcut, so the physical
                // key never reaches the hook. Give the key back to the keyboard.
                await _transport.SetAssignmentAsync(
                        row.ButtonId, Mode, native.FunctionId, native.Data, cancellationToken)
                    .ConfigureAwait(true);
            }
            else if (_deviceKind == RazerMappingDeviceKind.Mouse &&
                     input.Kind == MacroInputKind.MouseButton &&
                     backups.FirstOrDefault(item =>
                         ReferenceEquals(item.Row, row))?.Assignment is { } mouseCurrent &&
                     (mouseCurrent.FunctionId != RazerHidMappingFunctions.ButtonCode ||
                      mouseCurrent.Data.Count == 0 ||
                      mouseCurrent.Data[0] != row.ButtonId))
            {
                // Same for a mouse button left on a firmware action: the hook
                // only sees the button when the device reports the button.
                await _transport.SetAssignmentAsync(
                        row.ButtonId, Mode, RazerHidMappingFunctions.ButtonCode,
                        [row.ButtonId], cancellationToken)
                    .ConfigureAwait(true);
            }

            var error = await _softwareTriggerBinder!(
                    action.MacroId,
                    input,
                    MacroTriggerMode.Press,
                    MacroPlaybackMode.Once,
                    1,
                    LayerIndex == 1,
                    CreateSoftwareBinding(row, backups, clears),
                    cancellationToken)
                .ConfigureAwait(true);
            if (error is not null)
            {
                throw new InvalidOperationException(error);
            }
        }
        catch
        {
            await RestoreAsync(clears, backups).ConfigureAwait(true);
            throw;
        }
    }

    /// <summary>
    /// Applies a trigger/playback choice made on an already-bound row without
    /// waiting for the mapping page's Save. The key assignment is untouched.
    /// </summary>
    internal async Task PersistSoftwareModeAsync(RazerHidMappingRowViewModel row)
    {
        if (!_loaded || IsBusy || _softwareTriggerBinder is null ||
            _softwareTriggerResolver is null || !row.HasSoftwareModeChanges ||
            row.Group != RazerHidMappingGroup.Macro ||
            row.MacroTarget != MacroExecutionTarget.Software)
        {
            return;
        }

        if (row.SoftwareMacroId is not { } boundId)
        {
            return;
        }

        var macro = ResolveSoftwareMacro(row);
        if (macro is null ||
            !StringComparer.Ordinal.Equals(macro.Id, boundId) ||
            ResolveSoftwareTrigger(row.ButtonId) is not { } input)
        {
            return;
        }

        try
        {
            var error = await _softwareTriggerBinder(
                    macro.Id,
                    input,
                    row.TriggerMode,
                    row.ExecutionMode,
                    row.EffectiveExecutionRepeatCount,
                    LayerIndex == 1,
                    SoftwareBindingFor(macro, input, LayerIndex == 1),
                    CancellationToken.None)
                .ConfigureAwait(true);
            if (error is not null)
            {
                ErrorText = error;
                return;
            }

            row.MarkSoftwareModeLoaded();
            StatusText = AppStrings.Text("MacroSaved");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            ErrorText = exception.Message;
            _log?.Invoke($"software mode apply failed: {exception}");
        }
    }

    private static int SoftwareTriggerModeIndex(MacroTriggerMode mode) =>
        RazerHidMappingRowViewModel.IndexOf(mode);

    private bool NeedsNativeKeyboardAssignment(RazerMappingAssignment assignment) =>
        _deviceKind == RazerMappingDeviceKind.Keyboard &&
        _catalog is not RazerViperMappingActionCatalog &&
        (assignment.FunctionId == RazerHidMappingFunctions.Off ||
         // A raw Win+Tab chord keeps Win held on these firmwares.
         assignment.FunctionId == RazerHidMappingFunctions.KeyCode &&
         assignment.Data is [0x08, 0x2B] ||
         _catalog.Decode(assignment.FunctionId, assignment.Data)?.Group ==
             RazerHidMappingGroup.WindowsShortcut);

    private (byte FunctionId, byte[] Data)? NativeKeyboardAssignment(byte buttonId) =>
        _deviceKind == RazerMappingDeviceKind.Keyboard
            // Keypad rows are firmware ids; writing one as a key code lands on another key.
            && _productId is not (0x022B or 0x0244 or 0x02EB)
            ? RazerHidMappingActions.NativeKeyboardAssignment(buttonId, _productId)
            : null;

    /// <summary>
    /// The triggers a pending key is about to abandon: the row had a software macro
    /// and its next action is not that same macro.
    /// </summary>
    private ClearedSoftwareTrigger[] SoftwareTriggersToClear(
        IReadOnlyList<RazerHidMappingRowViewModel> pending)
    {
        // A private key can be reached from more than one mapping row. Keep the
        // trigger claimed by input, not by row, or moving the alias row clears
        // the binding the real row still needs.
        var claims = new List<RazerSoftwareMacroTriggerMatcher.Claim>();
        // A row that is still a software macro row keeps its trigger even when
        // it is not part of this save. Otherwise saving a different key could
        // clear the macro that the other key still owns.
        foreach (var row in Rows.Where(row =>
                     row.Group == RazerHidMappingGroup.Macro &&
                     row.MacroTarget == MacroExecutionTarget.Software))
        {
            if (ResolveSoftwareMacro(row) is { } macro &&
                ResolveSoftwareTrigger(row.ButtonId) is { } input)
            {
                claims.Add(new RazerSoftwareMacroTriggerMatcher.Claim(
                    macro.Id, input, LayerIndex == 1));
            }
        }
        foreach (var row in pending.Where(row =>
                     row.Group == RazerHidMappingGroup.WindowsShortcut &&
                     row.OptionIndex >= 0 &&
                     row.OptionIndex < SoftwareShortcutCatalog.Actions.Count))
        {
            if (ResolveSoftwareTrigger(row.ButtonId) is { } input)
            {
                claims.Add(new RazerSoftwareMacroTriggerMatcher.Claim(
                    SoftwareShortcutCatalog.Actions[row.OptionIndex].MacroId,
                    input,
                    LayerIndex == 1));
            }
        }
        // A host remap owns a native-button macro for the source key. Without this
        // claim the next save would treat that binding as orphaned and clear it.
        foreach (var row in pending.Where(row => row.Group == RazerHidMappingGroup.MouseButton))
        {
            if (PreviewHostRemap(row) is { Outcome: HostRemapOutcome.HostInject } plan &&
                ResolveSoftwareTrigger(row.ButtonId) is { } input)
            {
                claims.Add(new RazerSoftwareMacroTriggerMatcher.Claim(
                    NativeMouseButtonCatalog.MacroId(plan.TargetButtonId),
                    input,
                    LayerIndex == 1));
            }
        }

        var clears = new List<ClearedSoftwareTrigger>();
        // "Keep" means leave the key alone. A page reset can put a loaded macro
        // row back on Keep; treating that as an intentional move would delete a
        // trigger another key still uses.
        foreach (var row in pending)
        {
            if (row.SoftwareMacroId is not { } oldId)
            {
                continue;
            }

            var input = ResolveSoftwareTrigger(row.ButtonId);
            var macro = FindMacroById(oldId);
            if (input is null || macro is null)
            {
                continue;
            }

            var trigger = macro.FindTrigger(input, LayerIndex == 1);
            if (trigger is null)
            {
                continue;
            }

            if (RazerSoftwareMacroTriggerMatcher.IsClaimed(oldId, trigger, claims))
            {
                continue;
            }

            if (clears.Any(item =>
                    StringComparer.Ordinal.Equals(item.MacroId, oldId) &&
                    item.Trigger.HyperShiftLayer == trigger.HyperShiftLayer &&
                    item.Trigger.Input.Matches(trigger.Input)))
            {
                continue;
            }

            var binding = trigger.SoftwareBinding ??
                (ReferenceEquals(trigger, macro.Trigger) ? macro.SoftwareBinding : null);
            clears.Add(new ClearedSoftwareTrigger(
                oldId,
                trigger.Clone(),
                macro.EffectivePlaybackMode(trigger),
                macro.EffectiveRepeatCount(trigger),
                binding?.Clone()));
        }

        return clears.ToArray();
    }

    private MacroDefinition? FindMacroById(string macroId) =>
        _localMacros.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, macroId)) ??
        _systemMacros.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id, macroId));

    private async Task ClearSoftwareTriggersAsync(
        IReadOnlyList<ClearedSoftwareTrigger> clears,
        CancellationToken cancellationToken)
    {
        if (_softwareTriggerClearer is null)
        {
            return;
        }

        foreach (var clear in clears)
        {
            _log?.Invoke(
                $"software trigger cleared macro={clear.MacroId} " +
                $"code=0x{clear.Trigger.Input.RazerKeyCode:X2} button={clear.SoftwareBinding?.ButtonId}");
            var error = await _softwareTriggerClearer(
                    clear.MacroId,
                    clear.Trigger.Input,
                    clear.Trigger.HyperShiftLayer,
                    cancellationToken)
                .ConfigureAwait(true);
            if (error is not null)
            {
                throw new InvalidOperationException(error);
            }
        }
    }

    private async Task RestoreSoftwareTriggersAsync(IReadOnlyList<ClearedSoftwareTrigger> clears)
    {
        // Only a cleared trigger needs a binder; without a clearer nothing was removed.
        if (_softwareTriggerBinder is null || _softwareTriggerClearer is null)
        {
            return;
        }

        foreach (var clear in clears)
        {
            try
            {
                var error = await _softwareTriggerBinder(
                        clear.MacroId,
                        clear.Trigger.Input,
                        clear.Trigger.Mode,
                        clear.PlaybackMode,
                        clear.RepeatCount,
                        clear.Trigger.HyperShiftLayer,
                        clear.SoftwareBinding,
                        CancellationToken.None)
                    .ConfigureAwait(true);
                if (error is not null)
                {
                    _log?.Invoke($"software trigger restore failed: {error}");
                }
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _log?.Invoke($"software trigger restore failed: {exception.Message}");
            }
        }
    }

    private async Task RestoreAsync(
        IReadOnlyList<ClearedSoftwareTrigger> clears,
        IReadOnlyList<AssignmentBackup> backups)
    {
        await RestoreSoftwareTriggersAsync(clears).ConfigureAwait(true);
        await RestoreAssignmentsAsync(backups).ConfigureAwait(true);
    }
}
