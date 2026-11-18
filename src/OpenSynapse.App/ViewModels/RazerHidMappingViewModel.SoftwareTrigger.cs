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
            row.OptionIndex != row.LoadedOptionIndex ||
            row.HasSoftwareModeChanges ||
            !StringComparer.Ordinal.Equals(
                row.SoftwareMacroId,
                ResolveSoftwareMacro(row)?.Id)).ToHashSet();

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

    private static int SoftwareTriggerModeIndex(MacroTriggerMode mode) => mode switch
    {
        MacroTriggerMode.Toggle => 1,
        MacroTriggerMode.HoldRepeat => 2,
        _ => 0,
    };

    private bool NeedsNativeKeyboardAssignment(RazerMappingAssignment assignment) =>
        _deviceKind == RazerMappingDeviceKind.Keyboard &&
        _catalog is not RazerViperMappingActionCatalog &&
        (assignment.FunctionId == RazerHidMappingFunctions.Off ||
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
        IReadOnlyList<RazerHidMappingRowViewModel> pending,
        IReadOnlyList<RazerHidMappingRowViewModel> softwareRows)
    {
        var keptMacroIds = new Dictionary<byte, string>();
        foreach (var row in softwareRows.Where(row =>
                     row.Group == RazerHidMappingGroup.Macro &&
                     row.OptionIndex >= 0 &&
                     row.OptionIndex < _localMacros.Count))
        {
            keptMacroIds[row.ButtonId] = _localMacros[row.OptionIndex].Id;
        }
        foreach (var row in pending.Where(row =>
                     row.Group == RazerHidMappingGroup.WindowsShortcut &&
                     row.OptionIndex >= 0 &&
                     row.OptionIndex < SoftwareShortcutCatalog.Actions.Count))
        {
            keptMacroIds[row.ButtonId] = SoftwareShortcutCatalog.Actions[row.OptionIndex].MacroId;
        }

        var clears = new List<ClearedSoftwareTrigger>();
        // "Keep" never reaches the pending set, but a row that used to run a
        // software action must still give that action up.
        foreach (var row in pending.Concat(Rows.Where(row =>
                     row.Group == RazerHidMappingGroup.Keep && row.SoftwareMacroId is not null)))
        {
            if (row.SoftwareMacroId is not { } oldId)
            {
                continue;
            }

            if (keptMacroIds.TryGetValue(row.ButtonId, out var nextId) &&
                StringComparer.Ordinal.Equals(nextId, oldId))
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
                macro.PlaybackMode,
                macro.RepeatCount,
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
