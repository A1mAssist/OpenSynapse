using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidMappingViewModel : INotifyPropertyChanged
{
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return;
        await _transport.RunInMappingModeAsync(ApplyCoreAsync, cancellationToken, forWrite: true)
            .ConfigureAwait(true);
    }

    private async Task ApplyCoreAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return;
        IsBusy = true;
        ErrorText = string.Empty;
        var applied = 0;
        ClearedSoftwareTrigger[] clears = [];
        try
        {
            // The macro library can change while the mapping page stays open;
            // resolve rows against the list that exists right now.
            await RefreshMacroContextAsync(cancellationToken).ConfigureAwait(true);
            var invalidMacro = Rows.FirstOrDefault(row =>
                row.Group == RazerHidMappingGroup.Macro && row.HasMacroError);
            if (invalidMacro is not null)
            {
                throw new InvalidOperationException(invalidMacro.MacroErrorText);
            }

            var pending = Rows.Where(row =>
                row.CanApply &&
                (_canWrite ||
                 row.Group == RazerHidMappingGroup.Macro &&
                 row.MacroTarget == MacroExecutionTarget.Software ||
                 row.Group == RazerHidMappingGroup.WindowsShortcut)).ToArray();
            var onboardRows = pending
                .Where(row => row.Group == RazerHidMappingGroup.Macro &&
                              row.MacroTarget == MacroExecutionTarget.Onboard)
                .ToArray();
            var softwareRows = pending
                .Where(row => row.Group == RazerHidMappingGroup.Macro &&
                              row.MacroTarget == MacroExecutionTarget.Software)
                .ToArray();
            var softwareModeRows = SoftwareModeRows(softwareRows);
            var shortcutRows = pending
                .Where(row => row.Group == RazerHidMappingGroup.WindowsShortcut)
                .ToArray();
            var assignmentRows = pending
                .Where(row => (row.Group != RazerHidMappingGroup.Macro ||
                               row.MacroTarget == MacroExecutionTarget.Onboard) &&
                              row.Group != RazerHidMappingGroup.WindowsShortcut)
                .ToArray();
            // A row the user configured but cannot write must say so; otherwise the
            // saved count silently comes up short and looks like a partial save.
            var skipped = Rows
                .Where(row => row.Group != RazerHidMappingGroup.Keep && !pending.Contains(row))
                .Select(row => $"{row.ButtonName}: {SkipReason(row)}")
                .ToList();

            var duplicate = onboardRows
                .GroupBy(row => row.SelectedMacroSlotId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
            {
                throw new InvalidOperationException(AppStrings.FormatText(
                    "RazerMappingMacroDuplicateSlot", duplicate.Key));
            }

            foreach (var row in onboardRows)
            {
                if (!_onboardWritable)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("RazerMappingMacroOnboardUnavailable"));
                }

                var slot = row.SelectedMacroSlotId;
                if (!_freeSlots.Contains(slot) && slot != row.AssignedMacroSlot)
                {
                    throw new InvalidOperationException(AppStrings.FormatText(
                        "RazerMappingMacroSlotInUse", slot));
                }

                if (row.OptionIndex < 0 || row.OptionIndex >= _localMacros.Count)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("RazerMappingMacroSelectionMissing"));
                }

                if (!UpdateMacroValidation(row))
                {
                    throw new InvalidOperationException(row.MacroErrorText);
                }
            }

            foreach (var row in softwareRows)
            {
                if (_softwareTriggerBinder is null ||
                    _softwareTriggerResolver?.Invoke(row.ButtonId) is null)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("RazerMappingMacroSoftwareUnavailable"));
                }

                if (ResolveSoftwareMacro(row) is null)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("RazerMappingMacroDeleted"));
                }
            }

            clears = SoftwareTriggersToClear(pending, softwareRows);

            foreach (var row in shortcutRows)
            {
                if (_softwareTriggerBinder is null ||
                    _softwareTriggerResolver?.Invoke(row.ButtonId) is null)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("RazerMappingMacroSoftwareUnavailable"));
                }

                if (row.OptionIndex < 0 ||
                    row.OptionIndex >= SoftwareShortcutCatalog.Actions.Count)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("RazerMappingMacroSelectionMissing"));
                }
            }

            var softwareAssignmentRows = softwareRows
                .Where(row => _softwareTriggerResolver?.Invoke(row.ButtonId) is { } input &&
                    (RazerSoftwareMacroAssignmentPolicy.NeedsDeviceAssignment(input) ||
                     softwareModeRows.Contains(row) &&
                     NativeKeyboardAssignment(row.ButtonId) is not null))
                .ToArray();
            // A shortcut row may still carry the firmware action a previous build
            // wrote; its current assignment is what tells us to take the key back.
            var shortcutAssignmentRows = shortcutRows;

            var backups = new List<AssignmentBackup>(
                assignmentRows.Length + softwareAssignmentRows.Length + shortcutAssignmentRows.Length);
            foreach (var row in assignmentRows
                         .Concat(softwareAssignmentRows)
                         .Concat(shortcutAssignmentRows))
            {
                backups.Add(new AssignmentBackup(
                    row,
                    await _transport.GetAssignmentAsync(row.ButtonId, Mode, cancellationToken)
                        .ConfigureAwait(true)));
            }

            try
            {
                await ClearSoftwareTriggersAsync(clears, cancellationToken).ConfigureAwait(true);
            }
            catch
            {
                await RestoreSoftwareTriggersAsync(clears).ConfigureAwait(true);
                throw;
            }

            foreach (var row in onboardRows)
            {
                await WriteMacroToSlotAsync(
                    row.SelectedMacroSlotId,
                    _localMacros[row.OptionIndex],
                    cancellationToken).ConfigureAwait(true);
            }

            foreach (var row in assignmentRows)
            {
                var action = row.CreateAction();
                if (action is null)
                {
                    skipped.Add($"{row.ButtonName}: " +
                        AppStrings.Text("RazerHidMappingUnsupportedAction"));
                    continue;
                }

                try
                {
                    await _transport.SetAssignmentAsync(
                        row.ButtonId, Mode, action.FunctionId, action.Data, cancellationToken)
                        .ConfigureAwait(true);
                }
                catch
                {
                    await RestoreAsync(clears, backups).ConfigureAwait(true);
                    throw;
                }

                applied++;
            }

            foreach (var row in softwareAssignmentRows)
            {
                var input = _softwareTriggerResolver!(row.ButtonId)!;
                try
                {
                    if (RazerSoftwareMacroAssignmentPolicy.NeedsDeviceAssignment(input))
                    {
                        var (functionId, data) = BindingAssignment(input, row.ButtonId);
                        await _transport.SetAssignmentAsync(
                            row.ButtonId, Mode, functionId, data,
                            cancellationToken).ConfigureAwait(true);
                    }
                    else if (backups.FirstOrDefault(item =>
                                 ReferenceEquals(item.Row, row))?.Assignment is { } current &&
                             NeedsNativeKeyboardAssignment(current) &&
                             NativeKeyboardAssignment(row.ButtonId) is { } native)
                    {
                        await _transport.SetAssignmentAsync(
                            row.ButtonId, Mode, native.FunctionId, native.Data,
                            cancellationToken).ConfigureAwait(true);
                    }
                }
                catch
                {
                    await RestoreAsync(clears, backups).ConfigureAwait(true);
                    throw;
                }
            }

            foreach (var row in softwareRows)
            {
                if (!softwareModeRows.Contains(row)) continue;

                try
                {
                    await WriteSoftwareModeRowAsync(row, backups, clears, cancellationToken)
                        .ConfigureAwait(true);
                }
                catch
                {
                    await RestoreAsync(clears, backups).ConfigureAwait(true);
                    throw;
                }

                applied++;
            }

            foreach (var row in shortcutRows)
            {
                await WriteShortcutRowAsync(row, backups, clears, cancellationToken)
                    .ConfigureAwait(true);
                applied++;
                row.CurrentActionText = SoftwareShortcutCatalog.Actions[row.OptionIndex].Name;
                row.SoftwareMacroId = SoftwareShortcutCatalog.Actions[row.OptionIndex].MacroId;
                row.LoadedOptionIndex = row.OptionIndex;
            }

            foreach (var row in softwareRows)
            {
                var macro = ResolveSoftwareMacro(row)!;
                row.CurrentActionText = AppStrings.FormatText(
                    "RazerMappingMacroSoftwareApplied",
                    macro.Name);
                row.SoftwareMacroId = macro.Id;
                row.LoadedOptionIndex = row.OptionIndex;
            }

            foreach (var row in assignmentRows)
            {
                var action = row.CreateAction();
                if (action is null) continue;
                row.CurrentActionText = DescribeAssignment(action.FunctionId, action.Data);
                row.SoftwareMacroId = null;
            }

            if (assignmentRows.Length > 0 && _mappingProfileSaver is not null)
            {
                try
                {
                    await PersistMappingProfileAsync(cancellationToken).ConfigureAwait(true);
                }
                catch
                {
                    await RestoreAsync(clears, backups).ConfigureAwait(true);
                    throw;
                }
            }

            if (applied > 0 && _afterApplyAsync is not null)
            {
                await _afterApplyAsync(cancellationToken).ConfigureAwait(true);
            }

            StatusText = applied == 0
                ? AppStrings.Text("RazerHidMappingNothingChanged")
                : AppStrings.FormatText("RazerHidMappingSavedCount", applied);
            if (skipped.Count > 0)
            {
                ErrorText = AppStrings.FormatText(
                    "RazerHidMappingSkippedRows",
                    skipped.Count,
                    string.Join("; ", skipped));
            }
        }
        catch (OnboardMacroVerificationException exception)
        {
            await RestoreSoftwareTriggersAsync(clears).ConfigureAwait(true);
            DisableOnboardForSession();
            ErrorText = exception.Message;
            _log?.Invoke($"mapping apply disabled onboard writes: {exception}");
        }
        catch (Exception exception) when (IsExpected(exception) || exception is ArgumentException)
        {
            ErrorText = exception.Message;
            _log?.Invoke($"mapping apply failed: {exception}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    private static string SkipReason(RazerHidMappingRowViewModel row) =>
        !row.IsAssignmentReadable
            ? AppStrings.Text("RazerHidMappingUnreadable")
            : row.Group == RazerHidMappingGroup.Macro &&
              row.MacroTarget == MacroExecutionTarget.Software &&
              !row.CanUseSoftwareMacro
                ? AppStrings.Text("RazerMappingButtonMacroUnavailable")
                : AppStrings.Text("RazerHidMappingUnsupportedAction");

    private void DisableOnboardForSession()
    {
        _onboardWritable = false;
        foreach (var row in Rows)
        {
            row.SetOnboardAvailability(false);
        }

        OnPropertyChanged(nameof(CanApply));
    }

    internal void SetLoadError(string message)
    {
        _loaded = false;
        ClearRows();
        SelectedRow = null;
        StatusText = string.Empty;
        ErrorText = string.IsNullOrWhiteSpace(message)
            ? AppStrings.Text("RazerHidMappingReadFailed")
            : message;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(CanApply));
    }
}
