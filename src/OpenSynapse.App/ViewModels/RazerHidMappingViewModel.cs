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
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHidMappingViewModel : INotifyPropertyChanged
{
    private readonly IRazerMappingTransport _transport;
    private readonly IRazerMappingActionCatalog _catalog;
    private readonly bool _canWrite;
    private readonly Func<RazerOnboardMacroService>? _macroServiceFactory;
    private readonly Func<IReadOnlyList<MacroDefinition>>? _localMacroProvider;
    private readonly Func<CancellationToken, Task>? _afterApplyAsync;
    private readonly Action<string>? _log;
    private readonly ushort _productId;
    private readonly RazerMappingDeviceKind _deviceKind;
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

    public RazerHidMappingViewModel(
        IRazerMappingTransport transport,
        bool canWrite,
        Func<RazerOnboardMacroService>? macroServiceFactory = null,
        Func<IReadOnlyList<MacroDefinition>>? localMacros = null,
        Action<string>? log = null,
        IRazerMappingActionCatalog? actionCatalog = null,
        Func<CancellationToken, Task>? afterApplyAsync = null,
        ushort productId = 0,
        RazerMappingDeviceKind deviceKind = RazerMappingDeviceKind.Mouse,
        RazerMappingAssetService? assetService = null)
    {
        _transport = transport;
        _catalog = actionCatalog ?? RazerHidMappingActionCatalog.Instance;
        _canWrite = canWrite;
        _macroServiceFactory = macroServiceFactory;
        _localMacroProvider = localMacros;
        _afterApplyAsync = afterApplyAsync;
        _log = log;
        _productId = productId;
        _deviceKind = deviceKind;
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
        }
    }
    public bool CanRead => !IsBusy;
    public bool CanApply => _canWrite && !IsBusy && _loaded && Rows.Any(row => row.CanApply);
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
            if (_loaded)
            {
                ClearRows();
                _loaded = false;
                SelectedRow = null;
                StatusText = string.Empty;
                OnPropertyChanged(nameof(IsLoaded));
                OnPropertyChanged(nameof(CanApply));
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
    public bool SupportsMacros => _macrosAvailable;

    private byte Mode => (byte)LayerIndex;

    private void ClearRows()
    {
        Rows.Clear();
        LeftRows.Clear();
        RightRows.Clear();
        OnPropertyChanged(nameof(TableVisibility));
    }

    private void AddRow(RazerHidMappingRowViewModel row) => Rows.Add(row);

    private void PopulateStageRows()
    {
        LeftRows.Clear();
        RightRows.Clear();
        if (_layout is null)
        {
            return;
        }

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

        OnPropertyChanged(nameof(TableVisibility));
    }

    public async Task ReadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            await RefreshMacroContextAsync(cancellationToken).ConfigureAwait(true);
            var ids = await _transport.GetButtonIdsAsync(cancellationToken).ConfigureAwait(true);
            ClearRows();
            SelectedRow = null;
            var defaults = _freeSlots.ToList();
            foreach (var id in ids.Where(id => id != 0))
            {
                var row = new RazerHidMappingRowViewModel(
                    id, _localMacroOptions, _macrosAvailable, _catalog);
                row.PropertyChanged += OnRowPropertyChanged;
                byte? assignedSlot = null;
                try
                {
                    var assignment = await _transport.GetAssignmentAsync(id, Mode, cancellationToken)
                        .ConfigureAwait(true);
                    assignedSlot = MacroSlotOf(assignment.FunctionId, assignment.Data);
                    row.CurrentActionText = _catalog.Describe(
                        assignment.FunctionId,
                        assignment.Data,
                        _slotChoices);
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    row.CurrentActionText = AppStrings.Text("RazerHidMappingUnreadable");
                    _log?.Invoke($"mapping button {id} read failed: {exception.Message}");
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
                AddRow(row);
            }

            PopulateStageRows();
            StartVisualLoad();
            _loaded = true;
            StatusText = Rows.Count == 0
                ? AppStrings.Text("RazerHidMappingNoButtons")
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
        try
        {
            var pending = Rows.Where(row => row.CanApply).ToArray();
            var duplicate = pending
                .Where(row => row.Group == RazerHidMappingGroup.Macro)
                .GroupBy(row => row.SelectedMacroSlotId)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
            {
                throw new InvalidOperationException(AppStrings.FormatText(
                    "RazerMappingMacroDuplicateSlot", duplicate.Key));
            }

            foreach (var row in pending.Where(row => row.Group == RazerHidMappingGroup.Macro))
            {
                var slot = row.SelectedMacroSlotId;
                if (!_freeSlots.Contains(slot) && slot != row.AssignedMacroSlot)
                {
                    throw new InvalidOperationException(AppStrings.FormatText(
                        "RazerMappingMacroSlotInUse", slot));
                }
            }

            foreach (var row in pending)
            {
                if (row.Group == RazerHidMappingGroup.Macro)
                {
                    if (row.OptionIndex < 0 || row.OptionIndex >= _localMacros.Count)
                    {
                        throw new InvalidOperationException(
                            AppStrings.Text("RazerMappingMacroSelectionMissing"));
                    }
                    var slot = row.SelectedMacroSlotId;
                    await WriteMacroToSlotAsync(slot, _localMacros[row.OptionIndex], cancellationToken)
                        .ConfigureAwait(true);
                }

                var action = row.CreateAction();
                if (action is null)
                {
                    continue;
                }
                await _transport.SetAssignmentAsync(
                    row.ButtonId, Mode, action.FunctionId, action.Data, cancellationToken)
                    .ConfigureAwait(true);
                row.CurrentActionText = _catalog.Describe(
                    action.FunctionId,
                    action.Data,
                    _slotChoices);
                row.GroupIndex = 0;
                applied++;
            }

            if (applied > 0 && _afterApplyAsync is not null)
            {
                await _afterApplyAsync(cancellationToken).ConfigureAwait(true);
            }

            StatusText = applied == 0
                ? AppStrings.Text("RazerHidMappingNothingChanged")
                : AppStrings.FormatText("RazerHidMappingSavedCount", applied);
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

    private void StartVisualLoad()
    {
        if (_visualLoadStarted || _productId == 0)
        {
            return;
        }

        _visualLoadStarted = true;
        _ = LoadVisualAsync();
    }

    private async Task LoadVisualAsync()
    {
        try
        {
            var visual = await _assetService.GetAsync(_productId, _deviceKind).ConfigureAwait(true);
            if (visual.Mouse is { } mouse)
            {
                var image = await RazerMappingImageLoader.LoadAsync(mouse.ImageUri).ConfigureAwait(true);
                if (image is null)
                {
                    _log?.Invoke($"mapping image decode failed: {mouse.ImageUri}");
                }
                else
                {
                    _layout = mouse;
                    LayoutImage = image;
                    OnPropertyChanged(nameof(Layout));
                    OnPropertyChanged(nameof(StageVisibility));
                    OnPropertyChanged(nameof(TableVisibility));
                    RazerHidMappingRowViewModel.ApplyButtonNames(Rows, mouse.ButtonNames);
                    PopulateStageRows();
                }
            }

            if (visual.Keyboard is { } keyboard)
            {
                _keyboardLayout = keyboard;
                OnPropertyChanged(nameof(KeyboardLayout));
                OnPropertyChanged(nameof(KeyboardStageVisibility));
                OnPropertyChanged(nameof(KeyboardWidth));
                OnPropertyChanged(nameof(KeyboardHeight));
                OnPropertyChanged(nameof(TableVisibility));
            }

            if (!string.IsNullOrWhiteSpace(visual.Error))
            {
                _log?.Invoke($"mapping visual unavailable: {visual.Error}");
            }
        }
        catch (Exception exception) when (IsExpected(exception) || exception is ArgumentException)
        {
            _log?.Invoke($"mapping visual failed: {exception.Message}");
        }
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

    private async Task WriteMacroToSlotAsync(
        byte slot,
        MacroDefinition macro,
        CancellationToken cancellationToken)
    {
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

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RazerHidMappingRowViewModel.GroupIndex) or
            nameof(RazerHidMappingRowViewModel.MacroSlots))
        {
            OnPropertyChanged(nameof(CanApply));
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or InvalidDataException or NotSupportedException or
            IOException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
