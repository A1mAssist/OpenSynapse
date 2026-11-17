using OpenSynapse.Core.Macros;
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

    /// <summary>
    /// The triggers a pending key is about to abandon: the row had a software macro
    /// and its next action is not that same macro.
    /// </summary>
    private ClearedSoftwareTrigger[] SoftwareTriggersToClear(
        IReadOnlyList<RazerHidMappingRowViewModel> pending,
        IReadOnlyList<RazerHidMappingRowViewModel> softwareRows)
    {
        var keptMacroIds = softwareRows.ToDictionary(
            row => row.ButtonId,
            row => _localMacros[row.OptionIndex].Id);
        var clears = new List<ClearedSoftwareTrigger>();
        foreach (var row in pending)
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
            var macro = _localMacros.FirstOrDefault(
                candidate => StringComparer.Ordinal.Equals(candidate.Id, oldId));
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
