using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel
{
    private readonly Func<string, CancellationToken, Task<string?>>? _softwareTriggerClearer;

    private sealed record ClearedSoftwareTrigger(string MacroId, MacroTrigger Trigger);

    private int FindSoftwareMacro(MacroInput input, bool hyperShiftLayer, out MacroTriggerMode mode) =>
        RazerSoftwareMacroTriggerMatcher.FindIndex(_localMacros, input, hyperShiftLayer, out mode);

    private static int SoftwareTriggerModeIndex(MacroTriggerMode mode) => mode switch
    {
        MacroTriggerMode.Toggle => 1,
        MacroTriggerMode.Hold => 2,
        MacroTriggerMode.Sequence => 3,
        MacroTriggerMode.HoldRepeat => 4,
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
                    macro => StringComparer.Ordinal.Equals(macro.Id, oldId))?.Trigger is { } trigger)
            {
                clears.Add(new ClearedSoftwareTrigger(oldId, trigger.Clone()));
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
                        clear.Trigger.HyperShiftLayer,
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
