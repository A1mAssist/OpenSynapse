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
        TableRows.Clear();
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

        PopulateFallbackRows();
        OnPropertyChanged(nameof(TableVisibility));
    }

    // Keep only the rows the art cannot address: the keyboard SVG selects one key
    // per click, and mouse callouts already cover every hotspot row.
    private void PopulateFallbackRows()
    {
        TableRows.Clear();
        if (_layout is not null)
        {
            return;
        }

        var mapped = _keyboardLayout?.ButtonIdsByKeyName.Values.ToHashSet();
        foreach (var row in Rows.Where(row => mapped is null || !mapped.Contains(row.ButtonId)))
        {
            TableRows.Add(row);
        }
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

        // Private-key names do not depend on the product art being cached.
        if (_deviceKind == RazerMappingDeviceKind.Mouse)
        {
            RazerHidMappingRowViewModel.ApplyButtonNames(rows, null, _productId);
        }

        PopulateStageRows();
        SelectedRow = selectedButtonId is { } id
            ? rows.FirstOrDefault(row => row.ButtonId == id)
            : null;
        OnPropertyChanged(nameof(CanApply));
    }

    private bool _readQueued;
    private int _readStarting;
    private bool _hasReadAnyLayer;

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default) =>
        IsLoaded || IsBusy ? Task.CompletedTask : ReadAsync(cancellationToken);

    /// <summary>
    /// Reads the current layer. A layer change while a read is running queues one
    /// more read so the rows always end up on the layer the user selected.
    /// </summary>
    public async Task ReadAsync(CancellationToken cancellationToken = default)
    {
        // Entering the mapping-mode session is asynchronous. A second view load
        // can arrive before IsBusy is set and otherwise starts a duplicate HID
        // read, interleaving two mode sessions.
        if (IsBusy || Interlocked.CompareExchange(ref _readStarting, 1, 0) != 0)
        {
            _readQueued = true;
            return;
        }

        try
        {
            await _transport.RunInMappingModeAsync(async token =>
            {
                do
                {
                    _readQueued = false;
                    await ReadCoreAsync(token).ConfigureAwait(true);
                } while (_readQueued && !token.IsCancellationRequested);
            }, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            Volatile.Write(ref _readStarting, 0);
        }
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
                var visual = await _assetService.GetAsync(
                        _productId, _deviceKind, cancellationToken: cancellationToken)
                    .ConfigureAwait(true);
                var analogIds = AnalogKeyboardLayouts.TryGet(_productId, out var analogLayout)
                    ? analogLayout.Keys
                        .Select(key => key.KeyId)
                        .Distinct()
                        .Order()
                        .ToArray()
                    : null;
                var keyboardIds = visual.Keyboard is { } keyboard
                    ? keyboard.ButtonIdsByKeyName.Values
                        .Concat(RazerKeypadKeyCatalog.ExtraButtonIds(_productId))
                        .Concat(RazerKeyboardExtraKeyCatalog.ExtraButtonIds(_productId))
                        .Distinct()
                        .Order()
                        .ToArray()
                    : analogIds;
                var mouseIds = visual.Mouse?.ButtonNames?.Keys.Order().ToArray();
                ids = _deviceKind == RazerMappingDeviceKind.Keyboard ? keyboardIds ?? mouseIds ?? [] : mouseIds ?? keyboardIds ?? [];
            }

            var additional = await _transport.GetAdditionalButtonIdsAsync(
                    mode, cancellationToken)
                .ConfigureAwait(true);
            if (additional.Count > 0)
            {
                ids = ids.Concat(additional).Distinct().Order().ToArray();
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
                if (softwareTrigger is null &&
                    !RazerKeyboardExtraKeyCatalog.TryGetDialProtocolId(_productId, id, out _))
                {
                    _log?.Invoke($"mapping pid=0x{_productId:X4} button 0x{id:X2} has no software trigger");
                }
                var row = new RazerHidMappingRowViewModel(
                    id,
                    _localMacroOptions,
                    _macrosAvailable && _onboardWritable,
                    softwareTrigger is not null &&
                    _softwareTriggerBinder is not null &&
                    (_canWrite ||
                     !RazerSoftwareMacroAssignmentPolicy.NeedsDeviceAssignment(softwareTrigger)) &&
                    (LayerIndex == 0 || _canObserveHyperShiftLayer),
                    _catalog,
                    RazerKeyboardExtraKeyCatalog.IsHostOnlyRow(_productId, id));
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

                var shortcutIndex = -1;
                if (softwareTrigger is not null &&
                    (LayerIndex == 0 || _canObserveHyperShiftLayer) &&
                    FindShortcutMacro(softwareTrigger, LayerIndex == 1) is { } shortcutMacro &&
                    SoftwareShortcutCatalog.ActionOf(shortcutMacro.Id) is { } shortcut)
                {
                    shortcutIndex = SoftwareShortcutCatalog.IndexOf(shortcut.Id);
                    row.GroupIndex = (int)RazerHidMappingGroup.WindowsShortcut;
                    row.OptionIndex = shortcutIndex;
                    row.SoftwareMacroId = shortcut.MacroId;
                    row.LoadedOptionIndex = shortcutIndex;
                    row.CurrentActionText = RazerMappingActionNames.Localize(shortcut.Name);
                }

                var softwareMacroIndex = -1;
                if (shortcutIndex < 0 &&
                    softwareTrigger is not null &&
                    (LayerIndex == 0 || _canObserveHyperShiftLayer))
                {
                    softwareMacroIndex = FindSoftwareMacro(
                        softwareTrigger,
                        LayerIndex == 1,
                        out var triggerMode);
                    if (softwareMacroIndex >= 0 &&
                        SoftwareBindingFor(
                            _localMacros[softwareMacroIndex],
                            softwareTrigger,
                            LayerIndex == 1) is { ButtonId: > 0 } binding &&
                        binding.ButtonId != id)
                    {
                        // Two mapping buttons can report the same private key.
                        // Only the row the binding belongs to shows the macro.
                        softwareMacroIndex = -1;
                    }
                    if (softwareMacroIndex >= 0)
                    {
                        var softwareMacro = _localMacros[softwareMacroIndex];
                        var softwareTriggerDefinition = softwareMacro.FindTrigger(
                            softwareTrigger,
                            LayerIndex == 1);
                        row.GroupIndex = (int)RazerHidMappingGroup.Macro;
                        row.MacroTargetIndex = (int)MacroExecutionTarget.Software;
                        row.OptionIndex = softwareMacroIndex;
                        row.SoftwareTriggerModeIndex = SoftwareTriggerModeIndex(triggerMode);
                        row.ExecutionModeIndex =
                            (int)softwareMacro.EffectivePlaybackMode(softwareTriggerDefinition);
                        row.ExecutionRepeatCount =
                            softwareMacro.EffectiveRepeatCount(softwareTriggerDefinition);
                        row.SoftwareMacroId = softwareMacro.Id;
                        row.MarkSoftwareModeLoaded();
                        row.LoadedOptionIndex = softwareMacroIndex;
                        row.CurrentActionText = AppStrings.FormatText(
                            "RazerMappingMacroSoftwareApplied",
                            softwareMacro.Name);
                    }
                }

                UpdateMacroValidation(row);
                UpdateMacroSlotWarning(row);
                if ((softwareMacroIndex >= 0 || shortcutIndex >= 0) && assignmentIsOff == false &&
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
