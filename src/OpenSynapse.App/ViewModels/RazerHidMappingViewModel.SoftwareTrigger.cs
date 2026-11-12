using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    private readonly Func<string, CancellationToken, Task<string?>>? _softwareTriggerClearer;

    private sealed record ClearedSoftwareTrigger(
        string MacroId,
        MacroTrigger Trigger,
        MacroPlaybackMode PlaybackMode,
        int RepeatCount,
        MacroSoftwareBinding? SoftwareBinding);

    private int FindSoftwareMacro(MacroInput input, bool hyperShiftLayer, out MacroTriggerMode mode) =>
        RazerSoftwareMacroTriggerMatcher.FindIndex(_localMacros, input, hyperShiftLayer, out mode);

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
        IReadOnlyList<AssignmentBackup> backups)
    {
        var backup = backups.FirstOrDefault(item => ReferenceEquals(item.Row, row));
        return backup is null
            ? null
            : new MacroSoftwareBinding
            {
                ProductId = _productId,
                Layer = Mode,
                ButtonId = row.ButtonId,
                OriginalFunctionId = backup.Assignment.FunctionId,
                OriginalData = backup.Assignment.Data.ToList(),
            };
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
            if (row.SoftwareMacroId is not { } oldId ||
                (keptMacroIds.TryGetValue(row.ButtonId, out var nextId) &&
                 StringComparer.Ordinal.Equals(nextId, oldId)) ||
                clears.Any(item => StringComparer.Ordinal.Equals(item.MacroId, oldId)))
            {
                continue;
            }

            if (_localMacros.FirstOrDefault(
                    macro => StringComparer.Ordinal.Equals(macro.Id, oldId)) is { Trigger: { } trigger } macro)
            {
                clears.Add(new ClearedSoftwareTrigger(
                    oldId,
                    trigger.Clone(),
                    macro.PlaybackMode,
                    macro.RepeatCount,
                    macro.SoftwareBinding?.Clone()));
            }
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
            var error = await _softwareTriggerClearer(clear.MacroId, cancellationToken)
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
