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
    private static readonly MacroTriggerMode[] SoftwareTriggerModes = [
        // Persisted by position; append new modes only.
        MacroTriggerMode.Press,
        MacroTriggerMode.Toggle,
        MacroTriggerMode.Hold,
        MacroTriggerMode.Sequence,
        MacroTriggerMode.HoldRepeat,
    ];
    private readonly IRazerMappingTransport _transport;
    private readonly IRazerMappingActionCatalog _catalog;
    private readonly bool _canWrite;
    private readonly bool _canWriteOnboardMacros;
    private readonly Func<byte, MacroInput?>? _softwareTriggerResolver;
    private readonly Func<string, MacroInput, MacroTriggerMode, bool, CancellationToken, Task<string?>>?
        _softwareTriggerBinder;
    private readonly Func<RazerOnboardMacroService>? _macroServiceFactory;
    private readonly Func<IReadOnlyList<MacroDefinition>>? _localMacroProvider;
    private readonly Func<CancellationToken, Task>? _afterApplyAsync;
    private readonly Func<IReadOnlyList<ButtonMappingProfile>, CancellationToken, Task>? _mappingProfileSaver;
    private readonly Action<string>? _log;
    private readonly ushort _productId;
    private readonly RazerMappingDeviceKind _deviceKind;
    private readonly bool _canObserveHyperShiftLayer;
    private readonly RazerMappingAssetService _assetService;
    private ImageSource? _layoutImage;
    private RazerMappingImageLayout? _layout;
    private RazerMappingKeyboardLayout? _keyboardLayout;
    private bool _visualLoadStarted;
    private bool _isBusy;
    private string _statusText = string.Empty;
    private string _errorText = string.Empty;
    private bool _loaded;
    private int _layerIndex;
    private RazerHidMappingRowViewModel? _selectedRow;
    private IReadOnlyList<MacroDefinition> _localMacros = [];
    private IReadOnlyList<RazerHidMacroOption> _localMacroOptions = [];
    private IReadOnlyList<RazerHidMacroOption> _slotChoices = [];
    private IReadOnlyList<byte> _freeSlots = [];
    private bool _macrosAvailable;
    private bool _onboardWritable;
    private Dictionary<byte, string> _slotLocalMacroIds = [];

    public RazerHidMappingViewModel(
        IRazerMappingTransport transport,
        bool canWrite,
        Func<RazerOnboardMacroService>? macroServiceFactory = null,
        Func<IReadOnlyList<MacroDefinition>>? localMacros = null,
        Action<string>? log = null,
        IRazerMappingActionCatalog? actionCatalog = null,
        Func<CancellationToken, Task>? afterApplyAsync = null,
        Func<IReadOnlyList<ButtonMappingProfile>, CancellationToken, Task>? mappingProfileSaver = null,
        ushort productId = 0,
        RazerMappingDeviceKind deviceKind = RazerMappingDeviceKind.Mouse,
        RazerMappingAssetService? assetService = null,
        bool canWriteOnboardMacros = false,
        Func<byte, MacroInput?>? softwareTriggerResolver = null,
        Func<string, MacroInput, MacroTriggerMode, bool, CancellationToken, Task<string?>>? softwareTriggerBinder = null,
        Func<string, CancellationToken, Task<string?>>? softwareTriggerClearer = null)
    {
        _transport = transport;
        _catalog = actionCatalog ?? RazerHidMappingActionCatalog.Instance;
        _canWrite = canWrite;
        _canWriteOnboardMacros = canWriteOnboardMacros;
        _softwareTriggerResolver = softwareTriggerResolver;
        _softwareTriggerBinder = softwareTriggerBinder;
        _softwareTriggerClearer = softwareTriggerClearer;
        _macroServiceFactory = macroServiceFactory;
        _localMacroProvider = localMacros;
        _afterApplyAsync = afterApplyAsync;
        _mappingProfileSaver = mappingProfileSaver;
        _log = log;
        _productId = productId;
        _deviceKind = deviceKind;
        _canObserveHyperShiftLayer = RazerPrivateKeyCatalog.HasHyperShiftKey(productId);
        _assetService = assetService ?? RazerMappingAssetService.Shared;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RazerHidMappingRowViewModel> Rows { get; } = [];
    public ObservableCollection<RazerHidMappingRowViewModel> LeftRows { get; } = [];
    public ObservableCollection<RazerHidMappingRowViewModel> RightRows { get; } = [];
    public bool IsLoaded => _loaded;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRead));
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(BusyVisibility));
            OnPropertyChanged(nameof(ContentOpacity));
        }
    }
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public double ContentOpacity => IsBusy ? 0.55 : 1;
    public bool CanRead => !IsBusy;
    public bool CanApply => !IsBusy && _loaded && Rows.Any(row =>
        row.CanApply &&
        (_canWrite ||
         row.Group == RazerHidMappingGroup.Macro &&
         row.MacroTarget == MacroExecutionTarget.Software));
    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
    }
    public bool HasError => _errorText.Length > 0;
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (_errorText == value) return;
            _errorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }
    public int LayerIndex
    {
        get => _layerIndex;
        set
        {
            if (_layerIndex == value) return;
            _layerIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HyperShiftSoftwareUnavailableVisibility));
            // Keep the old rows visible until the read completes to avoid a layout jump.
            if (_hasReadAnyLayer)
            {
                _ = ReadAsync();
            }
        }
    }
    public IReadOnlyList<string> LayerNames => AppStrings.Texts(
        "RazerMappingLayerNormal", "RazerMappingLayerHyperShift");

    public RazerHidMappingRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (_selectedRow == value) return;
            _selectedRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedButtonName));
            OnPropertyChanged(nameof(EditorVisibility));
        }
    }
    public string SelectedButtonName => _selectedRow?.ButtonName ?? string.Empty;
    public Visibility EditorVisibility => _selectedRow is null ? Visibility.Collapsed : Visibility.Visible;
    public ImageSource? LayoutImage
    {
        get => _layoutImage;
        private set
        {
            if (ReferenceEquals(_layoutImage, value)) return;
            _layoutImage = value;
            OnPropertyChanged();
        }
    }
    public RazerMappingImageLayout? Layout => _layout;
    public RazerMappingKeyboardLayout? KeyboardLayout => _keyboardLayout;
    public Visibility StageVisibility => _layout is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility KeyboardStageVisibility =>
        _keyboardLayout is null ? Visibility.Collapsed : Visibility.Visible;
    public double KeyboardWidth => _keyboardLayout?.Visual.Width ?? 0;
    public double KeyboardHeight => _keyboardLayout?.Visual.Height ?? 0;
    public string KeyboardHint => AppStrings.Text("RazerMappingKeyboardHint");
    // The keyboard keeps the row table below the device so a key that has no
    // reliable name-to-HID match is still editable.
    public Visibility TableVisibility =>
        _keyboardLayout is not null || _layout is null ||
        Rows.Count > LeftRows.Count + RightRows.Count
            ? Visibility.Visible
            : Visibility.Collapsed;
    public bool SupportsMacros => _macrosAvailable || _softwareTriggerBinder is not null;

    private byte Mode => (byte)LayerIndex;

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
            var ids = await _transport.GetButtonIdsAsync(cancellationToken).ConfigureAwait(true);
            if (ids.Count == 0)
            {
                // Razer's UI keeps these ids in the product bundle when the firmware returns no button list.
                var visual = await _assetService.GetAsync(_productId, _deviceKind, cancellationToken)
                    .ConfigureAwait(true);
                var keyboardIds = visual.Keyboard?.ButtonIdsByKeyName.Values.Distinct().Order().ToArray();
                var mouseIds = visual.Mouse?.ButtonNames?.Keys.Order().ToArray();
                ids = _deviceKind == RazerMappingDeviceKind.Keyboard ? keyboardIds ?? mouseIds ?? [] : mouseIds ?? keyboardIds ?? [];
            }
            var rows = new List<RazerHidMappingRowViewModel>(ids.Count);
            var defaults = _freeSlots.ToList();
            var unreadable = 0;
            foreach (var id in ids.Where(id => id != 0))
            {
                var softwareTrigger = _softwareTriggerResolver?.Invoke(id);
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
                        row.SoftwareMacroId = _localMacros[softwareMacroIndex].Id;
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

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply) return;
        IsBusy = true;
        ErrorText = string.Empty;
        var applied = 0;
        ClearedSoftwareTrigger[] clears = [];
        try
        {
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
                 row.MacroTarget == MacroExecutionTarget.Software)).ToArray();
            var onboardRows = pending
                .Where(row => row.Group == RazerHidMappingGroup.Macro &&
                              row.MacroTarget == MacroExecutionTarget.Onboard)
                .ToArray();
            var softwareRows = pending
                .Where(row => row.Group == RazerHidMappingGroup.Macro &&
                              row.MacroTarget == MacroExecutionTarget.Software)
                .ToArray();
            var assignmentRows = pending
                .Where(row => row.Group != RazerHidMappingGroup.Macro ||
                              row.MacroTarget == MacroExecutionTarget.Onboard)
                .ToArray();

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

                if (row.OptionIndex < 0 || row.OptionIndex >= _localMacros.Count)
                {
                    throw new InvalidOperationException(
                        AppStrings.Text("RazerMappingMacroSelectionMissing"));
                }
            }

            clears = SoftwareTriggersToClear(pending, softwareRows);

            var offRows = softwareRows
                .Where(row => _softwareTriggerResolver?.Invoke(row.ButtonId) is { } input &&
                    RazerSoftwareMacroAssignmentPolicy.NeedsOff(input))
                .ToArray();

            var backups = new List<AssignmentBackup>(assignmentRows.Length + offRows.Length);
            foreach (var row in assignmentRows.Concat(offRows))
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

            foreach (var row in offRows)
            {
                try
                {
                    await _transport.SetAssignmentAsync(
                        row.ButtonId, Mode, RazerHidMappingFunctions.Off, Array.Empty<byte>(),
                        cancellationToken).ConfigureAwait(true);
                }
                catch
                {
                    await RestoreAsync(clears, backups).ConfigureAwait(true);
                    throw;
                }
            }

            foreach (var row in softwareRows)
            {
                var macro = _localMacros[row.OptionIndex];
                var input = _softwareTriggerResolver!.Invoke(row.ButtonId)!;
                var mode = SoftwareTriggerModes[
                    Math.Clamp(row.SoftwareTriggerModeIndex, 0, SoftwareTriggerModes.Length - 1)];
                string? error;
                try
                {
                    error = await _softwareTriggerBinder!(
                            macro.Id, input, mode, LayerIndex == 1, cancellationToken)
                        .ConfigureAwait(true);
                }
                catch
                {
                    await RestoreAsync(clears, backups).ConfigureAwait(true);
                    throw;
                }

                if (error is not null)
                {
                    await RestoreAsync(clears, backups).ConfigureAwait(true);
                    throw new InvalidOperationException(error);
                }

                applied++;
            }

            foreach (var row in softwareRows)
            {
                row.CurrentActionText = AppStrings.FormatText(
                    "RazerMappingMacroSoftwareApplied",
                    _localMacros[row.OptionIndex].Name);
                row.SoftwareMacroId = _localMacros[row.OptionIndex].Id;
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

    private async Task RefreshMacroContextAsync(CancellationToken cancellationToken)
    {
        _localMacros = _localMacroProvider?.Invoke() ?? [];
        _localMacroOptions = _localMacros
            .Select((macro, index) => new RazerHidMacroOption(
                (byte)Math.Min(index + 1, byte.MaxValue),
                string.IsNullOrWhiteSpace(macro.Name) ? macro.Id : macro.Name))
            .ToArray();
        _slotChoices = [];
        _freeSlots = [];
        _macrosAvailable = false;
        _onboardWritable = false;
        _slotLocalMacroIds = [];
        if (_macroServiceFactory is null || _localMacros.Count == 0)
        {
            return;
        }

        try
        {
            var service = _macroServiceFactory();
            var storage = await service.ProbeStorageAsync(cancellationToken).ConfigureAwait(true);
            if (storage is null)
            {
                return;
            }

            var slots = await service.ReadSlotInfosAsync(cancellationToken).ConfigureAwait(true);
            _slotChoices = slots
                .Select(slot => new RazerHidMacroOption(slot.Id, DescribeSlot(slot)))
                .ToArray();
            _freeSlots = RazerOnboardMacroSlots.FindFreeSlots(
                storage.MaxId,
                slots.Select(slot => slot.Id).ToArray());
            _macrosAvailable = true;
            _onboardWritable = _canWrite && _canWriteOnboardMacros;
            foreach (var slot in slots)
            {
                if (slot.NameInfo is not { Guid: var guid } || guid == Guid.Empty)
                {
                    continue;
                }

                var local = _localMacros.FirstOrDefault(
                    macro => MacroGuid(macro.Id) == guid);
                if (local is not null)
                {
                    _slotLocalMacroIds[slot.Id] = local.Id;
                }
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _log?.Invoke($"onboard macro slots unavailable: {exception.Message}");
        }
    }

    private string DescribeSlot(RazerOnboardMacroSlotInfo slot)
    {
        var nameInfo = slot.NameInfo;
        var local = nameInfo is { Guid: var guid } && guid != Guid.Empty
            ? _localMacros.FirstOrDefault(macro => MacroGuid(macro.Id) == guid)
            : null;
        if (local is not null)
        {
            return string.IsNullOrWhiteSpace(local.Name) ? local.Id : local.Name;
        }

        return string.IsNullOrWhiteSpace(nameInfo?.Name)
            ? AppStrings.FormatText("RazerMappingMacroSlot", slot.Id)
            : nameInfo.Name;
    }

    private IReadOnlyList<RazerMacroSlotOption> FreeSlotChoices() =>
        _freeSlots
            .Select(id => new RazerMacroSlotOption(id, SlotName(id)))
            .ToArray();

    private string SlotName(byte id) =>
        _slotChoices.FirstOrDefault(slot => slot.Id == id)?.Name ??
        AppStrings.FormatText("RazerMappingMacroSlot", id);

    private static Guid MacroGuid(string id)
    {
        if (Guid.TryParse(id, out var guid) && guid != Guid.Empty)
        {
            return guid;
        }

        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(id)).AsSpan(0, 16));
    }

    private int LocalMacroIndex(string macroId)
    {
        for (var index = 0; index < _localMacros.Count; index++)
        {
            if (StringComparer.Ordinal.Equals(_localMacros[index].Id, macroId))
            {
                return index;
            }
        }

        return -1;
    }

    private async Task WriteMacroToSlotAsync(
        byte slot,
        MacroDefinition macro,
        CancellationToken cancellationToken)
    {
        if (_slotLocalMacroIds.TryGetValue(slot, out var existingMacroId) &&
            StringComparer.Ordinal.Equals(existingMacroId, macro.Id))
        {
            return;
        }

        if (_macroServiceFactory is null)
        {
            throw new InvalidOperationException(AppStrings.Text("RazerMappingMacroUnavailable"));
        }

        var items = OnboardMacroTranslation.FromSteps(macro.Steps);
        if (items.Count == 0)
        {
            throw new InvalidOperationException(AppStrings.Text("RazerMappingMacroEmpty"));
        }

        var guid = MacroGuid(macro.Id);
        var encoded = RazerOnboardMacroCodec.Encode(items);
        var hash = Convert.ToHexString(SHA256.HashData(encoded));
        var name = new RazerOnboardMacroName(
            guid,
            items.Count,
            string.IsNullOrWhiteSpace(macro.Name) ? macro.Id : macro.Name,
            [],
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            hash[..32]);
        await _macroServiceFactory().WriteAsync(slot, items, name, cancellationToken)
            .ConfigureAwait(true);

        var slotName = new RazerHidMacroOption(slot, name.Name);
        _slotChoices = _slotChoices
            .Where(choice => choice.Id != slot)
            .Append(slotName)
            .OrderBy(choice => choice.Id)
            .ToArray();
        _freeSlots = _freeSlots.Where(id => id != slot).ToArray();
        _slotLocalMacroIds[slot] = macro.Id;
        OnPropertyChanged(nameof(SupportsMacros));
    }

    private static byte? MacroSlotOf(byte functionId, IReadOnlyList<byte> data)
    {
        if (functionId is not (RazerHidMappingFunctions.MacroTypeI or
            RazerHidMappingFunctions.MacroTypeII or
            RazerHidMappingFunctions.MacroTypeIII or
            RazerHidMappingFunctions.MacroTypeIV) ||
            data.Count < 2)
        {
            return null;
        }

        var slot = (data[0] << 8) | data[1];
        return slot is > 0 and <= byte.MaxValue ? (byte)slot : null;
    }

    private string DescribeAssignment(byte functionId, IReadOnlyList<byte> data)
    {
        if (functionId is not (RazerHidMappingFunctions.MacroTypeI or
            RazerHidMappingFunctions.MacroTypeII or
            RazerHidMappingFunctions.MacroTypeIII or
            RazerHidMappingFunctions.MacroTypeIV) ||
            data.Count < 2)
        {
            return _catalog.Describe(functionId, data, _slotChoices);
        }

        var slot = (data[0] << 8) | data[1];
        var name = _slotChoices.FirstOrDefault(choice => choice.Id == slot)?.Name ??
            AppStrings.FormatText("RazerMappingMacroSlot", slot);
        return functionId switch
        {
            RazerHidMappingFunctions.MacroTypeI when data.Count >= 3 && data[2] > 1 =>
                AppStrings.FormatText("RazerMappingMacroCurrentRepeat", name, data[2]),
            RazerHidMappingFunctions.MacroTypeI =>
                AppStrings.FormatText("RazerMappingMacroCurrentOnce", name),
            RazerHidMappingFunctions.MacroTypeII =>
                AppStrings.FormatText("RazerMappingMacroCurrentHold", name),
            RazerHidMappingFunctions.MacroTypeIII =>
                AppStrings.FormatText("RazerMappingMacroCurrentToggle", name),
            _ => AppStrings.FormatText("RazerMappingMacroCurrentSequence", name),
        };
    }

    private bool UpdateMacroValidation(RazerHidMappingRowViewModel row)
    {
        if (row.Group != RazerHidMappingGroup.Macro ||
            row.MacroTarget != MacroExecutionTarget.Onboard ||
            !_onboardWritable)
        {
            row.SetMacroValidation(string.Empty);
            return true;
        }

        if (row.OptionIndex < 0 || row.OptionIndex >= _localMacros.Count)
        {
            row.SetMacroValidation(AppStrings.Text("RazerMappingMacroSelectionMissing"));
            return false;
        }

        var macro = _localMacros[row.OptionIndex];
        if (macro.Steps.Count == 0)
        {
            row.SetMacroValidation(AppStrings.Text("RazerMappingMacroEmpty"));
            return false;
        }

        try
        {
            OnboardMacroTranslation.FromSteps(macro.Steps);
            row.SetMacroValidation(string.Empty);
            return true;
        }
        catch (OnboardMacroUnsupportedException exception)
        {
            row.SetMacroValidation(AppStrings.FormatText(
                row.CanUseSoftwareMacro
                    ? "RazerMappingMacroUnsupportedSoftware"
                    : "RazerMappingMacroUnsupported",
                exception.StepIndex,
                exception.Reason));
            return false;
        }
        catch (Exception exception) when (IsExpected(exception) || exception is ArgumentException)
        {
            row.SetMacroValidation(exception.Message);
            return false;
        }
    }

    private void UpdateMacroSlotWarning(RazerHidMappingRowViewModel row)
    {
        if (row.Group != RazerHidMappingGroup.Macro ||
            row.MacroTarget != MacroExecutionTarget.Onboard ||
            row.OptionIndex < 0 || row.OptionIndex >= _localMacros.Count ||
            row.SelectedMacroSlotId == 0 ||
            !_slotLocalMacroIds.TryGetValue(row.SelectedMacroSlotId, out var existingMacroId) ||
            StringComparer.Ordinal.Equals(existingMacroId, _localMacros[row.OptionIndex].Id))
        {
            row.SetMacroSlotWarning(string.Empty);
            return;
        }

        row.SetMacroSlotWarning(AppStrings.Text("RazerMappingMacroSlotOverwrite"));
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RazerHidMappingRowViewModel.GroupIndex) or
            nameof(RazerHidMappingRowViewModel.MacroSlots) or
            nameof(RazerHidMappingRowViewModel.OptionIndex) or
            nameof(RazerHidMappingRowViewModel.MacroTargetIndex) or
            nameof(RazerHidMappingRowViewModel.SelectedMacroSlotIndex))
        {
            if (sender is RazerHidMappingRowViewModel row)
            {
                UpdateMacroValidation(row);
                UpdateMacroSlotWarning(row);
            }

            OnPropertyChanged(nameof(CanApply));
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or InvalidDataException or NotSupportedException or
            IOException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception;

    private static bool IsRetryableMappingRead(Exception exception) =>
        exception is RazerDeviceTimeoutException { IsRetryableBusy: true } or
            TimeoutException or IOException;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
