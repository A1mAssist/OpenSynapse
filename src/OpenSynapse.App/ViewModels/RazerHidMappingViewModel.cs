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
    private readonly IRazerMappingTransport _transport;
    private readonly IRazerMappingActionCatalog _catalog;
    private readonly bool _canWrite;
    private readonly bool _canWriteOnboardMacros;
    private readonly Func<byte, MacroInput?>? _softwareTriggerResolver;
    private readonly Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
        MacroSoftwareBinding?, CancellationToken, Task<string?>>? _softwareTriggerBinder;
    private readonly Func<RazerOnboardMacroService>? _macroServiceFactory;
    private readonly Func<IReadOnlyList<MacroDefinition>>? _localMacroProvider;
    private readonly Func<IReadOnlyList<MacroDefinition>>? _systemMacroProvider;
    private readonly Func<CancellationToken, Task>? _afterApplyAsync;
    private readonly Func<IReadOnlyList<ButtonMappingProfile>, CancellationToken, Task>? _mappingProfileSaver;
    private readonly Func<CancellationToken, Task<KeypadMacroBindingTransaction>>? _keypadMacroTransactionFactory;
    private KeypadMacroBindingTransaction? _keypadMacroTransaction;
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
    private string _readErrorText = string.Empty;
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
        Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
            MacroSoftwareBinding?, CancellationToken, Task<string?>>? softwareTriggerBinder = null,
        Func<string, MacroInput, bool, CancellationToken, Task<string?>>? softwareTriggerClearer = null,
        Func<IReadOnlyList<MacroDefinition>>? systemMacros = null,
        Func<CancellationToken, Task<KeypadMacroBindingTransaction>>? keypadMacroTransactionFactory = null)
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
        _systemMacroProvider = systemMacros;
        _afterApplyAsync = afterApplyAsync;
        _mappingProfileSaver = mappingProfileSaver;
        _keypadMacroTransactionFactory = keypadMacroTransactionFactory;
        _log = log;
        _productId = productId;
        _deviceKind = deviceKind;
        _canObserveHyperShiftLayer = RazerKeypadInputCatalog.UsesHostMapping(productId) ||
            RazerPrivateKeyCatalog.HasHyperShiftKey(productId);
        _assetService = assetService ?? RazerMappingAssetService.Shared;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RazerHidMappingRowViewModel> Rows { get; } = [];
    public ObservableCollection<RazerHidMappingRowViewModel> LeftRows { get; } = [];
    public ObservableCollection<RazerHidMappingRowViewModel> RightRows { get; } = [];
    /// <summary>Rows with no hotspot on the device art; empty when every row is clickable.</summary>
    public ObservableCollection<RazerHidMappingRowViewModel> TableRows { get; } = [];
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
         row.Group == RazerHidMappingGroup.Macro && row.MacroTarget == MacroExecutionTarget.Software ||
         row.Group == RazerHidMappingGroup.WindowsShortcut && _softwareTriggerBinder is not null));
    public string StatusText
    {
        get => _statusText;
        private set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
    }
    public bool HasError => _errorText.Length > 0;
    internal string ReadErrorText
    {
        get => _readErrorText;
        private set
        {
            if (_readErrorText == value) return;
            _readErrorText = value;
            OnPropertyChanged();
        }
    }
    public Visibility HostMappingHintVisibility => RazerKeypadInputCatalog.UsesHostMapping(_productId)
        ? Visibility.Visible : Visibility.Collapsed;
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
            if (_layerIndex == value || value < 0 || value >= LayerNames.Count) return;
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
    public IReadOnlyList<string> LayerNames => _transport.SupportsHyperShift
        ? AppStrings.Texts("RazerMappingLayerNormal", "RazerMappingLayerHyperShift")
        : AppStrings.Texts("RazerMappingLayerNormal");

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
    public Visibility TableVisibility =>
        TableRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool SupportsMacros => _macrosAvailable || _softwareTriggerBinder is not null;

    private byte Mode => (byte)LayerIndex;

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or InvalidDataException or NotSupportedException or
            IOException or TimeoutException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception ||
        exception is AggregateException aggregate && aggregate.InnerExceptions.All(IsExpected);

    private static string MappingErrorText(Exception exception) => exception switch
    {
        RazerKeypadMappingUnavailableException { Reason: RazerKeypadMappingUnavailableReason.DriverMissing } =>
            AppStrings.Text("RazerKeypadDriverMissing"),
        RazerKeypadMappingUnavailableException { Reason: RazerKeypadMappingUnavailableReason.DriverInUse } =>
            AppStrings.Text("RazerKeypadDriverInUse"),
        RazerKeypadMappingUnavailableException { Reason: RazerKeypadMappingUnavailableReason.AmbiguousDevice } =>
            AppStrings.Text("RazerKeypadAmbiguousDevice"),
        RazerKeypadMappingUnavailableException { Reason: RazerKeypadMappingUnavailableReason.RecoveryMissing } =>
            AppStrings.Text("RazerKeypadRecoveryMissing"),
        RazerKeypadMappingUnavailableException { Reason: RazerKeypadMappingUnavailableReason.Stopped } =>
            AppStrings.Text("RazerKeypadRuntimeStopped"),
        _ => exception.Message,
    };

    private static bool IsRetryableMappingRead(Exception exception) =>
        exception is RazerDeviceTimeoutException { IsRetryableBusy: true } or
            TimeoutException or IOException;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
