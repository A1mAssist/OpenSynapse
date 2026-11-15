using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Devices;
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
    private void ClearRows()
    {
        Rows.Clear();
        LeftRows.Clear();
        RightRows.Clear();
        SelectedRow = null;
        OnPropertyChanged(nameof(TableVisibility));
        OnPropertyChanged(nameof(CanApply));
    }

    private void PopulateStageRows()
    {
        LeftRows.Clear();
        RightRows.Clear();
        if (_layout is not null)
        {
            var ordered = Rows
                .Where(row => _layout.Hotspots.ContainsKey(row.ButtonId))
                .Select(row => (Row: row, Hotspot: _layout.Hotspots[row.ButtonId]))
                .OrderBy(item => item.Hotspot.Y)
                .ToArray();
            foreach (var (row, hotspot) in ordered)
            {
                if (hotspot.IsLeft)
                {
                    LeftRows.Add(row);
                }
                else
                {
                    RightRows.Add(row);
                }
            }

            // Keys the product bundle does not place on the photo (the 009C DPI
            // buttons) still get a callout so they can be edited, just without
            // a connector line.
            foreach (var row in Rows.Where(row => !_layout.Hotspots.ContainsKey(row.ButtonId)))
            {
                LeftRows.Add(row);
            }
        }

        OnPropertyChanged(nameof(TableVisibility));
    }

    private void ReplaceRows(
        IReadOnlyList<RazerHidMappingRowViewModel> rows,
        byte? selectedButtonId)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        PopulateStageRows();
        SelectedRow = selectedButtonId is { } id
            ? rows.FirstOrDefault(row => row.ButtonId == id)
            : null;
        OnPropertyChanged(nameof(CanApply));
    }

    private bool _readQueued;
    private bool _hasReadAnyLayer;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        IsLoaded || IsBusy ? Task.CompletedTask : ReadAsync(cancellationToken);

    /// <summary>
    /// Reads the current layer. A layer change while a read is running queues one
    /// more read so the rows always end up on the layer the user selected.
    /// </summary>
    public async Task ReadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            _readQueued = true;
            return;
        }

        do
        {
            _readQueued = false;
            await ReadCoreAsync(cancellationToken).ConfigureAwait(true);
        } while (_readQueued && !cancellationToken.IsCancellationRequested);
    }

    private async Task ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        var mode = Mode;
        var selectedButtonId = _selectedRow?.ButtonId;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            await RefreshMacroContextAsync(cancellationToken).ConfigureAwait(true);
            var ids = (await _transport.GetButtonIdsAsync(cancellationToken).ConfigureAwait(true)).ToArray();
            if (ids.Length > 0 && ids.Distinct().Count() != ids.Length)
            {
                _log?.Invoke(
                    $"mapping pid=0x{_productId:X4} returned duplicate key ids: " +
                    string.Join(",", ids.Select(id => id.ToString("X2", CultureInfo.InvariantCulture))));
                ids = [];
            }
            if (ids.Length == 0)
            {
                // Razer's UI keeps these ids in the product bundle when the firmware returns no button list.
                var visual = await _assetService.GetAsync(_productId, _deviceKind, cancellationToken)
                    .ConfigureAwait(true);
                var keyboardIds = visual.Keyboard is { } keyboard
                    ? keyboard.ButtonIdsByKeyName.Values
                        .Concat(RazerKeypadKeyCatalog.ExtraButtonIds(_productId))
                        .Distinct()
                        .Order()
                        .ToArray()
                    : null;
                var mouseIds = visual.Mouse?.ButtonNames?.Keys.Order().ToArray();
                ids = _deviceKind == RazerMappingDeviceKind.Keyboard ? keyboardIds ?? mouseIds ?? [] : mouseIds ?? keyboardIds ?? [];
            }
            _log?.Invoke(ids.Length == 0
                ? $"mapping pid=0x{_productId:X4}: the device returned no button list"
                : $"mapping pid=0x{_productId:X4} buttons: {string.Join(",", ids.Select(id => id.ToString("X2", CultureInfo.InvariantCulture)))}");
            var rows = new List<RazerHidMappingRowViewModel>(ids.Length);
            var defaults = _freeSlots.ToList();
            var unreadable = 0;
            foreach (var id in ids.Where(id => id != 0))
            {
                var softwareTrigger = _softwareTriggerResolver?.Invoke(id);
                if (softwareTrigger is null)
                {
                    _log?.Invoke($"mapping pid=0x{_productId:X4} button 0x{id:X2} has no software trigger");
                }
                var row = new RazerHidMappingRowViewModel(
                    id,
                    _localMacroOptions,
                    _macrosAvailable && _onboardWritable,
                    softwareTrigger is not null &&
                    _softwareTriggerBinder is not null &&
                    (_canWrite || !RazerSoftwareMacroAssignmentPolicy.NeedsOff(softwareTrigger)) &&
                    (LayerIndex == 0 || _canObserveHyperShiftLayer),
                    _catalog);
                row.PropertyChanged += OnRowPropertyChanged;
                byte? assignedSlot = null;
                bool? assignmentIsOff = null;
                RazerHidDecodedMapping? decoded = null;
                RazerMappingAssignment? assignment = null;
                Exception? readError = null;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        assignment = await _transport.GetAssignmentAsync(id, mode, cancellationToken)
                            .ConfigureAwait(true);
                        readError = null;
                        break;
                    }
                    catch (Exception exception) when (IsExpected(exception))
                    {
                        readError = exception;
                        if (!IsRetryableMappingRead(exception) || attempt == 2)
                        {
                            break;
                        }

                        await Task.Delay(
                                TimeSpan.FromMilliseconds(40 * (attempt + 1)),
                                cancellationToken)
                            .ConfigureAwait(true);
                    }
                }

                if (assignment is { } readAssignment)
                {
                    assignedSlot = MacroSlotOf(readAssignment.FunctionId, readAssignment.Data);
                    assignmentIsOff = readAssignment.FunctionId == RazerHidMappingFunctions.Off;
                    decoded = _catalog is RazerViperMappingActionCatalog
                        ? null
                        : _catalog.Decode(readAssignment.FunctionId, readAssignment.Data);
                    row.CurrentActionText = DescribeAssignment(
                        readAssignment.FunctionId,
                        readAssignment.Data);
                }
                else
                {
                    unreadable++;
                    row.SetAssignmentReadable(false);
                    row.CurrentActionText = AppStrings.Text("RazerHidMappingUnreadable");
                    _log?.Invoke(
                        $"mapping button {id} read failed ({readError?.GetType().Name}): {readError?.Message}");
                }

                var preferred = defaults.Count > 0 ? defaults[0] : (byte?)null;
                if (preferred is { } value) defaults.Remove(value);
                var slotOptions = FreeSlotChoices();
                if (assignedSlot is { } assigned && slotOptions.All(option => option.Id != assigned))
                {
                    slotOptions = slotOptions
                        .Prepend(new RazerMacroSlotOption(assigned, SlotName(assigned)))
                        .ToArray();
                }
                row.SetMacroSlots(slotOptions, assignedSlot, preferred);
                var localMacroIndex = -1;
                if (assignedSlot is { } assignedMacroSlot &&
                    _slotLocalMacroIds.TryGetValue(assignedMacroSlot, out var macroId))
                {
                    localMacroIndex = LocalMacroIndex(macroId);
                }
                if (decoded is not null)
                {
                    row.ApplyDecodedMapping(decoded, localMacroIndex);
                }
                else if (localMacroIndex >= 0)
                {
                    row.OptionIndex = localMacroIndex;
                }

                var softwareMacroIndex = -1;
                if (softwareTrigger is not null &&
                    (LayerIndex == 0 || _canObserveHyperShiftLayer))
                {
                    softwareMacroIndex = FindSoftwareMacro(
                        softwareTrigger,
                        LayerIndex == 1,
                        out var triggerMode);
                    if (softwareMacroIndex >= 0)
                    {
                        row.GroupIndex = (int)RazerHidMappingGroup.Macro;
                        row.MacroTargetIndex = (int)MacroExecutionTarget.Software;
                        row.OptionIndex = softwareMacroIndex;
                        row.SoftwareTriggerModeIndex = SoftwareTriggerModeIndex(triggerMode);
                        row.ExecutionModeIndex = (int)_localMacros[softwareMacroIndex].PlaybackMode;
                        row.ExecutionRepeatCount = _localMacros[softwareMacroIndex].RepeatCount;
                        row.SoftwareMacroId = _localMacros[softwareMacroIndex].Id;
                        row.MarkSoftwareModeLoaded();
                        row.LoadedOptionIndex = softwareMacroIndex;
                        row.CurrentActionText = AppStrings.FormatText(
                            "RazerMappingMacroSoftwareApplied",
                            _localMacros[softwareMacroIndex].Name);
                    }
                }

                UpdateMacroValidation(row);
                UpdateMacroSlotWarning(row);
                if (softwareMacroIndex >= 0 && assignmentIsOff == false &&
                    RazerSoftwareMacroAssignmentPolicy.NeedsOff(softwareTrigger!))
                {
                    row.SetMacroSlotWarning(
                        AppStrings.Text("RazerMappingMacroAssignmentNotOff"));
                }
                rows.Add(row);
            }

            ReplaceRows(rows, selectedButtonId);
            StartVisualLoad();
            _loaded = true;
            _hasReadAnyLayer = true;
            StatusText = Rows.Count == 0
                ? AppStrings.Text("RazerHidMappingNoButtons")
                : unreadable > 0
                    ? AppStrings.FormatText(
                        "RazerHidMappingReadCountPartial",
                        Rows.Count - unreadable,
                        Rows.Count,
                        unreadable)
                    : _macrosAvailable
                        ? AppStrings.FormatText(
                            "RazerHidMappingReadCountWithMacros", Rows.Count, _slotChoices.Count)
                        : AppStrings.FormatText("RazerHidMappingReadCount", Rows.Count);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _loaded = false;
            ClearRows();
            StatusText = string.Empty;
            ErrorText = exception.Message;
            _log?.Invoke($"mapping read failed: {exception}");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsLoaded));
            OnPropertyChanged(nameof(CanApply));
        }
    }
}
