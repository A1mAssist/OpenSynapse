using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Devices;
using Windows.UI;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan BladeBrightnessVerificationDelay =
        TimeSpan.FromMilliseconds(150);

    private readonly IDeviceDiscovery _discovery;
    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly ProfileStore _profileStore;
    private readonly IPowerSourceProvider _powerSourceProvider;
    private readonly IActiveApplicationProvider _activeApplicationProvider;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly SystemTelemetryViewModel _systemTelemetry = new();
    private readonly BladeViewModel _blade = new();
    private readonly ViperViewModel _viper = new();
    private HashSet<BladePerformanceMode> _bladePerformanceCycleModes =
        [.. BladePerformanceModes];
    private HashSet<int>? _internalDisplayRefreshRateCycleHertz;
    private IReadOnlyList<BladePerformanceMode>? _legacyPerformanceCycleModes;
    private IReadOnlyList<int>? _legacyRefreshRateCycleHertz;
    private string _activeBladeMappingPreset = BladeProfileSettings.Product710DefaultMappingPreset;
    private bool _activeSnapTapEnabled;
    private readonly IInternalDisplayController? _internalDisplayController;
    private readonly IBladeLightingController? _bladeLightingController;
    private readonly WindowsStartupManager? _startupManager;
    private readonly WindowsTouchpadController? _touchpadController;
    private readonly string? _executablePath;
    private readonly IReadOnlyList<string> _startupDiagnostics;
    private readonly VerifiedProfileApplier _profileApplier = new();
    private readonly BladeFanCurveRuntime _bladeFanRuntime;
    private readonly SemaphoreSlim _deviceOperationGate = new(1, 1);
    private readonly object _bladeBrightnessGate = new();
    private Task _bladeBrightnessWriter = Task.CompletedTask;
    private byte? _desiredBladeBrightness;
    private bool _bladeBrightnessWriterActive;
    private Task _bladeBrightnessVerification = Task.CompletedTask;
    private long _bladeBrightnessVerificationGeneration;
    private ApplicationProfileSwitcher _applicationProfileSwitcher = new();
    private ProfileDocument _profile = ProfileDocument.CreateDefault();
    private bool? _lastPowerState;
    private string _lastDeviceRefreshText = "尚未探测";
    private string _deviceTelemetryTimeText = "等待硬件查询";
    private string _deviceErrorText = string.Empty;
    private string _deviceQueryErrorText = string.Empty;
    private string _deviceOperationErrorText = string.Empty;
    private string _performanceErrorText = string.Empty;
    private string _displayErrorText = string.Empty;
    private string _profileStatusText = "等待加载本地配置";
    private string _activeProfileName = ProfileCatalog.DefaultProfileName;
    private string _profileNameInput = string.Empty;
    private bool _isStartupEnabled;
    private bool _isSilentStartupEnabled;
    private string _errorText = string.Empty;
    private bool _isBusy;
    private IReadOnlyList<DeviceDescriptor> _deviceDescriptors = Array.Empty<DeviceDescriptor>();
    private RazerDeviceTelemetry? _lastDeviceTelemetry;
    private string _deviceFingerprint = string.Empty;
    private string _lightingShadowFingerprint = string.Empty;
    private string _bladeLightingDevicePath = string.Empty;
    private string _bladeFanControlFingerprint = string.Empty;
    private Task? _bladeFanControlCompletion;
    private string? _bladeControlDevicePath;
    private DateTimeOffset _nextFullDeviceRefresh = DateTimeOffset.MinValue;
    private int _deviceRefreshRequested;
    private int _displayProfileApplyRequested;
    private string _internalDisplayResolutionText = "--";
    private string _internalDisplayRefreshRateText = "--";
    private IReadOnlyList<int> _internalDisplayRefreshRates = Array.Empty<int>();
    private int _internalDisplayRefreshRateHertz;
    private int _confirmedInternalDisplayRefreshRateHertz;
    private bool _canSetInternalDisplayRefreshRate;
    private int _disposed;

    public MainViewModel(
        IDeviceDiscovery discovery,
        IRazerDeviceTelemetryReader deviceTelemetryReader,
        IPerformanceMonitor performanceMonitor,
        ProfileStore? profileStore = null,
        IPowerSourceProvider? powerSourceProvider = null,
        IActiveApplicationProvider? activeApplicationProvider = null,
        LocalDiagnosticLog? diagnosticLog = null,
        IInternalDisplayController? internalDisplayController = null,
        IBladeLightingController? bladeLightingController = null,
        WindowsStartupManager? startupManager = null,
        string? executablePath = null,
        IReadOnlyList<string>? startupDiagnostics = null,
        WindowsTouchpadController? touchpadController = null)
    {
        _discovery = discovery;
        _deviceTelemetryReader = deviceTelemetryReader;
        _performanceMonitor = performanceMonitor;
        _profileStore = profileStore ?? new ProfileStore();
        _powerSourceProvider = powerSourceProvider ?? UnknownPowerSourceProvider.Instance;
        _activeApplicationProvider = activeApplicationProvider ?? UnknownActiveApplicationProvider.Instance;
        _diagnosticLog = diagnosticLog ?? new LocalDiagnosticLog();
        _internalDisplayController = internalDisplayController;
        _bladeLightingController = bladeLightingController;
        _startupManager = startupManager;
        _touchpadController = touchpadController;
        _bladeFanRuntime = new BladeFanCurveRuntime(deviceTelemetryReader, performanceMonitor);
        _executablePath = executablePath;
        _startupDiagnostics = startupDiagnostics?.ToArray() ?? Array.Empty<string>();
        _systemTelemetry.PropertyChanged += (_, args) => OnPropertyChanged(args.PropertyName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<string?>? BladeControlDevicePathChanged;
    internal event Action<BladePerformanceMode>? BladePerformanceModeChangedByUser;
    internal event Action<bool>? BladeGamingModeChangedByUser;
    internal event Action? BladeInputProfileChanged;

    internal IReadOnlySet<BladePerformanceMode> BladePerformanceCycleModes =>
        _bladePerformanceCycleModes;
    internal IReadOnlySet<int>? InternalDisplayRefreshRateCycleHertz =>
        _internalDisplayRefreshRateCycleHertz;
    internal bool ActiveSnapTapEnabled => _activeSnapTapEnabled;
    internal string ActiveBladeMappingPreset => _activeBladeMappingPreset;

    internal void SetLegacyShortcutCycleDefaults(
        IEnumerable<BladePerformanceMode> performanceModes,
        IEnumerable<int>? refreshRates)
    {
        _legacyPerformanceCycleModes = performanceModes
            .Where(BladePerformanceModes.Contains)
            .Distinct()
            .ToArray();
        _legacyRefreshRateCycleHertz = refreshRates?
            .Where(hertz => hertz > 0)
            .Distinct()
            .Order()
            .ToArray();
    }

    internal void SetBladePerformanceCycleModes(IEnumerable<BladePerformanceMode> modes)
    {
        var selected = modes.Where(BladePerformanceModes.Contains).ToHashSet();
        if (selected.Count == 0)
        {
            throw new ArgumentException("At least one performance mode must remain in the shortcut cycle.", nameof(modes));
        }
        _bladePerformanceCycleModes = selected;
    }

    internal void SetInternalDisplayRefreshRateCycle(IEnumerable<int> refreshRates)
    {
        var selected = refreshRates.Where(hertz => hertz > 0).ToHashSet();
        if (selected.Count == 0)
        {
            throw new ArgumentException("At least one refresh rate must remain in the shortcut cycle.", nameof(refreshRates));
        }
        _internalDisplayRefreshRateCycleHertz = selected;
    }

    internal async Task<bool> SavePerformanceCycleModesAsync(
        IEnumerable<BladePerformanceMode> modes,
        CancellationToken cancellationToken = default)
    {
        var selected = modes.Where(BladePerformanceModes.Contains).Distinct().ToArray();
        if (selected.Length == 0)
        {
            return false;
        }

        var previous = _bladePerformanceCycleModes;
        _bladePerformanceCycleModes = selected.ToHashSet();
        GetActiveProfile().Shortcuts.PerformanceCycleModes = selected.ToList();
        if (await SaveProfileAsync(cancellationToken))
        {
            OnPropertyChanged(nameof(BladePerformanceCycleModes));
            return true;
        }

        _bladePerformanceCycleModes = previous;
        GetActiveProfile().Shortcuts.PerformanceCycleModes = previous.ToList();
        return false;
    }

    internal async Task<bool> SaveRefreshRateCycleAsync(
        IEnumerable<int> refreshRates,
        CancellationToken cancellationToken = default)
    {
        var selected = refreshRates.Where(hertz => hertz > 0).Distinct().Order().ToArray();
        if (selected.Length == 0)
        {
            return false;
        }

        var previous = _internalDisplayRefreshRateCycleHertz;
        _internalDisplayRefreshRateCycleHertz = selected.ToHashSet();
        GetActiveProfile().Shortcuts.RefreshRateCycleHertz = selected.ToList();
        if (await SaveProfileAsync(cancellationToken))
        {
            OnPropertyChanged(nameof(InternalDisplayRefreshRateCycleHertz));
            return true;
        }

        _internalDisplayRefreshRateCycleHertz = previous;
        GetActiveProfile().Shortcuts.RefreshRateCycleHertz = previous?.Order().ToList();
        return false;
    }

    internal async Task SetBladeSnapTapEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var previous = _profile.Global.Blade.SnapTapEnabled;
        _profile.Global.Blade.SnapTapEnabled = enabled;
        if (!await SaveProfileAsync(cancellationToken))
        {
            _profile.Global.Blade.SnapTapEnabled = previous;
            return;
        }
        _activeSnapTapEnabled = enabled;
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<DiagnosticRowViewModel> Diagnostics { get; } = new();
    public ObservableCollection<string> ProfileNames { get; } = new();
    public ObservableCollection<ApplicationBindingRowViewModel> ApplicationBindings { get; } = new();

    public string LastDeviceRefreshText
    {
        get => AppStrings.Get(_lastDeviceRefreshText);
        private set => SetField(ref _lastDeviceRefreshText, value);
    }

    public string TelemetryTimeText => _systemTelemetry.TelemetryTimeText;

    public string DeviceTelemetryTimeText { get => AppStrings.Get(_deviceTelemetryTimeText); private set => SetField(ref _deviceTelemetryTimeText, value); }

    public string DeviceErrorText
    {
        get => AppStrings.Get(_deviceErrorText);
        private set
        {
            if (SetField(ref _deviceErrorText, value))
            {
                OnPropertyChanged(nameof(HasDeviceError));
                UpdateErrorText();
            }
        }
    }

    public bool HasDeviceError => !string.IsNullOrWhiteSpace(DeviceErrorText);

    public string ErrorText
    {
        get => AppStrings.Get(_errorText);
        private set
        {
            if (SetField(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string ProfileStatusText { get => AppStrings.Get(_profileStatusText); private set => SetField(ref _profileStatusText, value); }
    public string ActiveProfileName { get => _activeProfileName; private set => SetField(ref _activeProfileName, value); }
    public string ProfileNameInput { get => _profileNameInput; set => SetField(ref _profileNameInput, value); }
    public bool CanDeleteProfile => ProfileNames.Count > 1;
    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        private set
        {
            if (SetField(ref _isStartupEnabled, value))
            {
                OnPropertyChanged(nameof(CanSetSilentStartup));
            }
        }
    }
    public bool IsSilentStartupEnabled { get => _isSilentStartupEnabled; private set => SetField(ref _isSilentStartupEnabled, value); }
    public bool CanSetStartup => _startupManager is not null && !string.IsNullOrWhiteSpace(_executablePath);
    public bool CanSetSilentStartup => CanSetStartup && IsStartupEnabled;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool CanRefresh => true;

    public string BladeDeviceName { get => _blade._bladeDeviceName; private set => SetField(ref _blade._bladeDeviceName, value); }
    public string BladeStatusText { get => AppStrings.Get(_blade._bladeStatusText); private set => SetField(ref _blade._bladeStatusText, value); }
    public string BladeBrightnessText { get => _blade._bladeBrightnessText; private set => SetField(ref _blade._bladeBrightnessText, value); }
    public string BladeBrightnessSelectionText { get => _blade._bladeBrightnessSelectionText; private set => SetField(ref _blade._bladeBrightnessSelectionText, value); }
    public double BladeBrightnessPercent
    {
        get => _blade._bladeBrightnessPercent;
        set
        {
            if (SetField(ref _blade._bladeBrightnessPercent, Math.Clamp(value, 0, 100)))
            {
                BladeBrightnessSelectionText = $"{_blade._bladeBrightnessPercent:0}%";
            }
        }
    }
    public bool CanSetBladeBrightness
    {
        get => _blade._canSetBladeBrightness;
        private set
        {
            if (SetField(ref _blade._canSetBladeBrightness, value))
            {
                OnPropertyChanged(nameof(CanSetBladeLighting));
            }
        }
    }
    public string BladePerformanceModeText { get => AppStrings.Get(_blade._bladePerformanceModeText); private set => SetField(ref _blade._bladePerformanceModeText, value); }
    public IReadOnlyList<string> BladePerformanceModeOptions => AppStrings.Get("平衡", "性能", "自定义", "静音", "HyperBoost");
    public int BladePerformanceModeIndex { get => _blade._bladePerformanceModeIndex; set => SetField(ref _blade._bladePerformanceModeIndex, value); }
    public bool CanSetBladePerformanceMode { get => _blade._canSetBladePerformanceMode; private set => SetField(ref _blade._canSetBladePerformanceMode, value); }
    public string BladeFanText { get => AppStrings.Get(_blade._bladeFanText); private set => SetField(ref _blade._bladeFanText, value); }
    public string BladeFanModeText { get => AppStrings.Get(_blade._bladeFanModeText); private set => SetField(ref _blade._bladeFanModeText, value); }
    public string BladeFanTargetRpmText { get => _blade._bladeFanTargetRpmText; private set => SetField(ref _blade._bladeFanTargetRpmText, value); }
    public string BladeCurrentFanCpuRpmText { get => _blade._bladeCurrentFanCpuRpmText; private set => SetField(ref _blade._bladeCurrentFanCpuRpmText, value); }
    public string BladeCurrentFanGpuRpmText { get => _blade._bladeCurrentFanGpuRpmText; private set => SetField(ref _blade._bladeCurrentFanGpuRpmText, value); }
    public string BladeAdvancedFanCpuModeRawText { get => _blade._bladeAdvancedFanCpuModeRawText; private set => SetField(ref _blade._bladeAdvancedFanCpuModeRawText, value); }
    public string BladeAdvancedFanGpuModeRawText { get => _blade._bladeAdvancedFanGpuModeRawText; private set => SetField(ref _blade._bladeAdvancedFanGpuModeRawText, value); }
    public string BladeGameModeText { get => AppStrings.Get(_blade._bladeGameModeText); private set => SetField(ref _blade._bladeGameModeText, value); }
    public bool BladeGameModeEnabled
    {
        get => _blade._bladeGameModeEnabled;
        set
        {
            if (SetField(ref _blade._bladeGameModeEnabled, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeGamingMode));
            }
        }
    }
    public bool CanSetBladeGamingMode =>
        _blade._bladeGameModeWriteSupported && _blade._bladeGameModeState is byte state && state != 2;
    public bool CanApplyBladeGamingMode =>
        CanSetBladeGamingMode && BladeGameModeEnabled != (_blade._bladeGameModeState != 0);
    public string BladeStartupAnimationText { get => AppStrings.Get(_blade._bladeStartupAnimationText); private set => SetField(ref _blade._bladeStartupAnimationText, value); }
    public bool BladeStartupAnimationEnabled
    {
        get => _blade._bladeStartupAnimationSelection;
        set
        {
            if (SetField(ref _blade._bladeStartupAnimationSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeStartupAnimation));
            }
        }
    }
    public bool CanSetBladeStartupAnimation => _blade._bladeStartupAnimationEnabled is not null;
    public bool CanApplyBladeStartupAnimation =>
        CanSetBladeStartupAnimation && BladeStartupAnimationEnabled != _blade._bladeStartupAnimationEnabled;
    public string BladeNativeDisplayModeText { get => _blade._bladeNativeDisplayModeText; private set => SetField(ref _blade._bladeNativeDisplayModeText, value); }
    public string BladeSkuHardwareText { get => _blade._bladeSkuHardwareText; private set => SetField(ref _blade._bladeSkuHardwareText, value); }
    public string BladeLocalDimmingText { get => AppStrings.Get(_blade._bladeLocalDimmingText); private set => SetField(ref _blade._bladeLocalDimmingText, value); }
    public string BladeOneTimeFullChargeText { get => AppStrings.Get(_blade._bladeOneTimeFullChargeText); private set => SetField(ref _blade._bladeOneTimeFullChargeText, value); }
    public bool BladeOneTimeFullChargeEnabled
    {
        get => _blade._bladeOneTimeFullChargeSelection;
        set
        {
            if (SetField(ref _blade._bladeOneTimeFullChargeSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeOneTimeFullCharge));
            }
        }
    }
    public bool CanSetBladeOneTimeFullCharge =>
        _blade._bladeOneTimeFullChargeEnabled is not null && _blade._confirmedBladeChargeLimitIndex >= 0 &&
        BladeChargeLimits[_blade._confirmedBladeChargeLimitIndex] < 100;
    public bool CanApplyBladeOneTimeFullCharge =>
        CanSetBladeOneTimeFullCharge && BladeOneTimeFullChargeEnabled != _blade._bladeOneTimeFullChargeEnabled;
    public string BladeChargeLimitText { get => AppStrings.Get(_blade._bladeChargeLimitText); private set => SetField(ref _blade._bladeChargeLimitText, value); }
    public IReadOnlyList<string> BladeChargeLimitOptions => AppStrings.Get("50%", "55%", "60%", "65%", "70%", "75%", "80%", "关闭限制（100%）");
    public int BladeChargeLimitIndex { get => _blade._bladeChargeLimitIndex; set => SetField(ref _blade._bladeChargeLimitIndex, value); }
    public bool CanSetBladeChargeLimit { get => _blade._canSetBladeChargeLimit; private set => SetField(ref _blade._canSetBladeChargeLimit, value); }
    public IReadOnlyList<string> BladeCpuBoostOptions => AppStrings.Get("低", "中", "高", "Boost", "降压预设");
    public string BladeCpuBoostText { get => AppStrings.Get(_blade._bladeCpuBoostText); private set => SetField(ref _blade._bladeCpuBoostText, value); }
    public int BladeCpuBoostIndex { get => _blade._bladeCpuBoostIndex; set => SetField(ref _blade._bladeCpuBoostIndex, value); }
    public bool CanSetBladeCpuBoost => _blade._hasBladeCpuBoost && IsBladeCustomMode;
    public IReadOnlyList<string> BladeGpuBoostOptions => AppStrings.Get("低", "中", "高");
    public string BladeGpuBoostText { get => AppStrings.Get(_blade._bladeGpuBoostText); private set => SetField(ref _blade._bladeGpuBoostText, value); }
    public int BladeGpuBoostIndex { get => _blade._bladeGpuBoostIndex; set => SetField(ref _blade._bladeGpuBoostIndex, value); }
    public bool CanSetBladeGpuBoost => _blade._hasBladeGpuBoost && IsBladeCustomMode;
    public string BladeMaxFanText { get => AppStrings.Get(_blade._bladeMaxFanText); private set => SetField(ref _blade._bladeMaxFanText, value); }
    public bool BladeMaxFanEnabled { get => _blade._bladeMaxFanEnabled; set => SetField(ref _blade._bladeMaxFanEnabled, value); }
    public bool CanSetBladeMaxFan => _blade._hasBladeMaxFan && IsBladeCustomMode;
    public Visibility BladeCustomPerformanceVisibility => IsBladeCustomMode
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> BladeLogoOptions => AppStrings.Get("关闭", "常亮", "呼吸");
    public string BladeLogoText { get => AppStrings.Get(_blade._bladeLogoText); private set => SetField(ref _blade._bladeLogoText, value); }
    public int BladeLogoIndex { get => _blade._bladeLogoIndex; set => SetField(ref _blade._bladeLogoIndex, value); }
    public bool CanSetBladeLogo { get => _blade._canSetBladeLogo; private set => SetField(ref _blade._canSetBladeLogo, value); }
    public string BladeTouchpadText { get => AppStrings.Get(_blade._bladeTouchpadText); private set => SetField(ref _blade._bladeTouchpadText, value); }
    public bool BladeTouchpadEnabled { get => _blade._bladeTouchpadEnabled; private set => SetField(ref _blade._bladeTouchpadEnabled, value); }
    public bool CanSetBladeTouchpad => _blade._canSetBladeTouchpad;
    public IReadOnlyList<string> BladeLightingModeOptions => AppStrings.Get(
        "关闭", "静态", "呼吸", "光谱循环", "波浪", "火焰", "响应", "涟漪", "音频律动", "环境感知", "色轮", "星光", "潮汐");
    public int BladeLightingModeIndex
    {
        get => _blade._bladeLightingModeIndex;
        set
        {
            if (SetField(ref _blade._bladeLightingModeIndex, value))
            {
                OnPropertyChanged(nameof(BladeLightingColorVisibility));
                OnPropertyChanged(nameof(BladeLightingSecondColorVisibility));
                OnPropertyChanged(nameof(BladeWaveDirectionVisibility));
            }
        }
    }
    private BladeLightingMode? SelectedBladeLightingMode =>
        BladeLightingModeIndex >= 0 && BladeLightingModeIndex < BladeLightingModes.Length
            ? BladeLightingModes[BladeLightingModeIndex]
            : null;
    public Visibility BladeLightingColorVisibility => SelectedBladeLightingMode is
        BladeLightingMode.Static or BladeLightingMode.Breathing or BladeLightingMode.Reactive or
        BladeLightingMode.Ripple or BladeLightingMode.Starlight or BladeLightingMode.Tidal
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeLightingSecondColorVisibility => SelectedBladeLightingMode == BladeLightingMode.Tidal
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility BladeWaveDirectionVisibility => SelectedBladeLightingMode is BladeLightingMode.Wave or BladeLightingMode.Wheel
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> BladeWaveDirectionOptions => AppStrings.Get("向右 / 顺时针", "向左 / 逆时针");
    public int BladeWaveDirectionIndex { get => _blade._bladeWaveDirectionIndex; set => SetField(ref _blade._bladeWaveDirectionIndex, value); }
    public Color BladeLightingColor { get => _blade._bladeLightingColor; set => SetField(ref _blade._bladeLightingColor, value); }
    public Color BladeLightingSecondColor { get => _blade._bladeLightingSecondColor; set => SetField(ref _blade._bladeLightingSecondColor, value); }
    public bool CanSetBladeLighting => _blade._canSetBladeBrightness && _bladeLightingController is not null;
    public string ViperDeviceName { get => _viper._viperDeviceName; private set => SetField(ref _viper._viperDeviceName, value); }
    public Visibility ViperDeviceVisibility { get => _viper._viperDeviceVisibility; private set => SetField(ref _viper._viperDeviceVisibility, value); }
    public string ViperStatusText { get => AppStrings.Get(_viper._viperStatusText); private set => SetField(ref _viper._viperStatusText, value); }
    public string ViperBatteryText { get => _viper._viperBatteryText; private set => SetField(ref _viper._viperBatteryText, value); }
    public string ViperPollingRateText { get => _viper._viperPollingRateText; private set => SetField(ref _viper._viperPollingRateText, value); }
    public int ViperPollingRateIndex { get => _viper._viperPollingRateIndex; set => SetField(ref _viper._viperPollingRateIndex, value); }
    public bool CanSetViperPollingRate { get => _viper._canSetViperPollingRate; private set => SetField(ref _viper._canSetViperPollingRate, value); }
    public string ViperDpiText { get => _viper._viperDpiText; private set => SetField(ref _viper._viperDpiText, value); }
    public double ViperDpiXValue { get => _viper._viperDpiXValue; set => SetField(ref _viper._viperDpiXValue, value); }
    public double ViperDpiYValue { get => _viper._viperDpiYValue; set => SetField(ref _viper._viperDpiYValue, value); }
    public bool CanSetViperDpi { get => _viper._canSetViperDpi; private set => SetField(ref _viper._canSetViperDpi, value); }
    public string ViperIdleText { get => AppStrings.Get(_viper._viperIdleText); private set => SetField(ref _viper._viperIdleText, value); }
    public string ViperDpiStagesText { get => AppStrings.Get(_viper._viperDpiStagesText); private set => SetField(ref _viper._viperDpiStagesText, value); }
    public string ViperLowBatteryThresholdText { get => _viper._viperLowBatteryThresholdText; private set => SetField(ref _viper._viperLowBatteryThresholdText, value); }
    public double ViperIdleMinutesValue { get => _viper._viperIdleMinutesValue; set => SetField(ref _viper._viperIdleMinutesValue, value); }
    public bool CanSetViperIdle { get => _viper._canSetViperIdle; private set => SetField(ref _viper._canSetViperIdle, value); }
    public ObservableCollection<ViperDpiStageRowViewModel> ViperDpiStages => _viper.ViperDpiStages;
    public int ViperDpiStageCount
    {
        get => _viper._viperDpiStageCount;
        set => ResizeViperDpiStages(Math.Clamp(value, 1, 5));
    }
    public int ViperActiveDpiStage
    {
        get => _viper._viperActiveDpiStage;
        set => SetField(ref _viper._viperActiveDpiStage, Math.Clamp(value, 1, Math.Max(1, ViperDpiStages.Count)));
    }
    public bool CanSetViperDpiStages { get => _viper._canSetViperDpiStages; private set => SetField(ref _viper._canSetViperDpiStages, value); }
    public string ViperButtonMappingsText { get => AppStrings.Get(_viper._viperButtonMappingsText); private set => SetField(ref _viper._viperButtonMappingsText, value); }
    public ObservableCollection<ViperButtonAssignmentRowViewModel> ViperButtonAssignments => _viper.ViperButtonAssignments;
    public IReadOnlyList<string> ViperButtonMappingLayerOptions => AppStrings.Get("普通层", "HyperShift 层");
    public int ViperButtonMappingLayerIndex
    {
        get => _viper._viperButtonMappingLayerIndex;
        set
        {
            if (SetField(ref _viper._viperButtonMappingLayerIndex, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(VisibleViperButtonAssignments));
            }
        }
    }
    public IReadOnlyList<ViperButtonAssignmentRowViewModel> VisibleViperButtonAssignments =>
        ViperButtonAssignments
            .Where(row => (int)row.Assignment.Layer == ViperButtonMappingLayerIndex)
            .ToArray();
    public bool CanReadViperButtonMappings => _viper._canReadViperButtonMappings;
    public bool CanSetViperButtonMappings => _viper._canSetViperButtonMappings;
    public string InternalDisplayResolutionText { get => _internalDisplayResolutionText; private set => SetField(ref _internalDisplayResolutionText, value); }
    public string InternalDisplayRefreshRateText { get => _internalDisplayRefreshRateText; private set => SetField(ref _internalDisplayRefreshRateText, value); }
    public IReadOnlyList<int> InternalDisplayRefreshRates
    {
        get => _internalDisplayRefreshRates;
        private set
        {
            if (SetField(ref _internalDisplayRefreshRates, value))
            {
                OnPropertyChanged(nameof(InternalDisplayRefreshRateOptions));
                OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
            }
        }
    }
    public IReadOnlyList<string> InternalDisplayRefreshRateOptions => InternalDisplayRefreshRates
        .Select(rate => $"{rate} Hz")
        .ToArray();
    public int InternalDisplayRefreshRateHertz
    {
        get => _internalDisplayRefreshRateHertz;
        set
        {
            if (SetField(ref _internalDisplayRefreshRateHertz, value))
            {
                OnPropertyChanged(nameof(InternalDisplayRefreshRateIndex));
            }
        }
    }
    public int InternalDisplayRefreshRateIndex
    {
        get => Array.IndexOf(InternalDisplayRefreshRates.ToArray(), InternalDisplayRefreshRateHertz);
        set
        {
            if (value >= 0 && value < InternalDisplayRefreshRates.Count)
            {
                InternalDisplayRefreshRateHertz = InternalDisplayRefreshRates[value];
            }
        }
    }
    public bool CanSetInternalDisplayRefreshRate { get => _canSetInternalDisplayRefreshRate; private set => SetField(ref _canSetInternalDisplayRefreshRate, value); }

    public string EmptyStateText => Devices.Count == 0
        ? AppStrings.Get("未发现 Blade 16 或 Viper V3 HyperSpeed。")
        : string.Empty;

    public string CpuName => _systemTelemetry.CpuName;
    public string CpuValue => _systemTelemetry.CpuValue;
    public double CpuPercent => _systemTelemetry.CpuPercent;
    public string CpuTemperatureText => _systemTelemetry.CpuTemperatureText;
    public string CpuPowerText => _systemTelemetry.CpuPowerText;
    public string CpuClockText => _systemTelemetry.CpuClockText;
    public string GpuName => _systemTelemetry.GpuName;
    public string GpuValue => _systemTelemetry.GpuValue;
    public double GpuPercent => _systemTelemetry.GpuPercent;
    public string GpuTemperatureText => _systemTelemetry.GpuTemperatureText;
    public string GpuPowerText => _systemTelemetry.GpuPowerText;
    public string GpuClockText => _systemTelemetry.GpuClockText;
    public string GpuMemoryLabel => _systemTelemetry.GpuMemoryLabel;
    public string GpuMemoryText => _systemTelemetry.GpuMemoryText;
    public string MemoryValue => _systemTelemetry.MemoryValue;
    public string MemoryDetail => _systemTelemetry.MemoryDetail;
    public double MemoryPercent => _systemTelemetry.MemoryPercent;
    public string StorageValue => _systemTelemetry.StorageValue;
    public string StorageDetail => _systemTelemetry.StorageDetail;
    public double StoragePercent => _systemTelemetry.StoragePercent;

    public void RequestDeviceRefresh() => Interlocked.Exchange(ref _deviceRefreshRequested, 1);

    private void RequestProfileApply()
    {
        Interlocked.Exchange(ref _displayProfileApplyRequested, 1);
        RequestDeviceRefresh();
    }

    public void RefreshLocalization()
    {
        ProfileStatusText = AppStrings.FormatText("ProfileLoaded",
            ActiveProfileName);

        if (_lastDeviceTelemetry is { } telemetry)
        {
            ApplyDeviceTelemetry(telemetry);
        }

        foreach (var row in Devices)
        {
            row.RefreshLocalization();
        }
        foreach (var row in Diagnostics)
        {
            row.RefreshLocalization();
        }
        foreach (var row in ViperButtonAssignments)
        {
            row.RefreshLocalization();
        }

        _systemTelemetry.RefreshLocalization();

        OnPropertyChanged(string.Empty);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadProfileAsync(cancellationToken);
        await RefreshDevicesAsync(cancellationToken);
        if (_blade._bladeGameModeState is byte gameMode)
        {
            try
            {
                SetBladeGameMode(await _deviceTelemetryReader.SetBladeGameModeAsync(
                    _deviceDescriptors,
                    gameMode != 0,
                    cancellationToken));
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception))
            {
                _diagnosticLog.TryWrite(
                    "device-operation",
                    $"game mode indicator startup sync failed: {exception}");
                SetDeviceOperationError(AppStrings.FormatText("LabeledError",
                    "游戏模式指示灯",
                    exception.Message));
            }
        }
        await RefreshPerformanceAsync(cancellationToken);
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            _profile = await _profileStore.LoadAsync(cancellationToken);
            var migrated = EnsureActiveProfileExtensions();
            RefreshProfileState();
            if (migrated)
            {
                await _profileStore.SaveAsync(_profile, cancellationToken);
            }
            RefreshStartupState();
            ProfileStatusText = AppStrings.FormatText("ProfileLoaded", ActiveProfileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _profile = ProfileDocument.CreateDefault();
            RefreshProfileState();
            RefreshStartupState();
            ProfileStatusText = AppStrings.FormatText("DefaultProfileLoaded", ActiveProfileName);
            ReportApplicationError(AppStrings.FormatText("ProfileLoadError", exception.Message));
        }
    }

    public async Task SelectProfileAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            StringComparer.OrdinalIgnoreCase.Equals(name, ActiveProfileName))
        {
            return;
        }

        await RunProfileOperationAsync(AppStrings.FormatText("SwitchProfile", name), () =>
        {
            ProfileCatalog.Select(_profile, name);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task CreateProfileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        await RunProfileOperationAsync(AppStrings.FormatText("CreateProfile", name), () =>
        {
            ProfileCatalog.Create(_profile, name);
            ProfileCatalog.Select(_profile, name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task DeleteActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeleteProfile)
        {
            return;
        }

        await RunProfileOperationAsync(AppStrings.FormatText("DeleteProfile", ActiveProfileName), () =>
        {
            ProfileCatalog.Delete(_profile, ActiveProfileName);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task CloneActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        await RunProfileOperationAsync(AppStrings.FormatText("CloneProfile", name), () =>
        {
            ProfileCatalog.Clone(_profile, ActiveProfileName, name);
            ProfileCatalog.Select(_profile, name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task RenameActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        await RunProfileOperationAsync(AppStrings.FormatText("RenameProfile", name), () =>
        {
            ProfileCatalog.Rename(_profile, ActiveProfileName, name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task BindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync(AppStrings.FormatText("BindApplication", Path.GetFileName(executablePath)), () =>
        {
            ApplicationProfileBinding.Bind(_profile, executablePath, ActiveProfileName);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);

    public Task UnbindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync(AppStrings.FormatText("UnbindApplication", Path.GetFileName(executablePath)), () =>
        {
            ApplicationProfileBinding.Unbind(_profile, executablePath);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);

    public async Task ImportProfilesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ProfileDocument imported;
        try
        {
            imported = await ProfileStore.ImportAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.FormatText("ProfileImportError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileImportFailed", exception.Message);
            return;
        }

        await RunProfileOperationAsync(AppStrings.Get("导入配置"), () =>
        {
            _profile = imported;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task ExportProfilesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        IsBusy = true;
        try
        {
            await ProfileStore.ExportAsync(_profile.Clone(), filePath, cancellationToken);
            ProfileStatusText = AppStrings.FormatText("ProfileExported", Path.GetFileName(filePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.FormatText("ProfileExportError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileExportFailed", exception.Message);
        }
        finally
        {
            IsBusy = false;
            _deviceOperationGate.Release();
        }
    }

    public async Task SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!CanSetStartup || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        IsBusy = true;
        try
        {
            _startupManager!.SetEnabled(enabled, _executablePath!, IsSilentStartupEnabled);
            IsStartupEnabled = enabled;
            if (!enabled)
            {
                IsSilentStartupEnabled = false;
            }
            ProfileStatusText = enabled ? "已启用开机启动" : "已关闭开机启动";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            SetDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("StartupSettingFailed", exception.Message);
            RefreshStartupState();
        }
        finally
        {
            IsBusy = false;
            _deviceOperationGate.Release();
        }
    }

    public async Task SetSilentStartupEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (!CanSetSilentStartup || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        IsBusy = true;
        try
        {
            _startupManager!.SetEnabled(true, _executablePath!, enabled);
            IsSilentStartupEnabled = enabled;
            ProfileStatusText = AppStrings.Text(
                enabled ? "SilentStartupEnabled" : "SilentStartupDisabled");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            SetDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
            ProfileStatusText = AppStrings.FormatText("StartupSettingFailed", exception.Message);
            RefreshStartupState();
        }
        finally
        {
            IsBusy = false;
            _deviceOperationGate.Release();
        }
    }

    private async Task RunProfileOperationAsync(
        string label,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        var previous = _profile.Clone();
        IsBusy = true;
        try
        {
            await operation();
            await _profileStore.SaveAsync(_profile, cancellationToken);
            RequestProfileApply();
            ProfileStatusText = AppStrings.FormatText("ProfileOperationSucceeded", label, ActiveProfileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _profile = previous;
            RefreshProfileState();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.FormatText("LabeledError", label, exception.Message));
            ProfileStatusText = AppStrings.FormatText("ProfileOperationFailed", label, exception.Message);
            _profile = previous;
            RefreshProfileState();
        }
        finally
        {
            IsBusy = false;
            _deviceOperationGate.Release();
        }
    }

    private void RefreshProfileState()
    {
        EnsureActiveProfileExtensions();
        var snapTap = _profile.Global.Blade.SnapTapEnabled == true;
        var mappingPreset = _profile.Global.Blade.MappingPreset ??
            BladeProfileSettings.Product710DefaultMappingPreset;
        var bladeInputChanged = snapTap != _activeSnapTapEnabled ||
            !StringComparer.Ordinal.Equals(mappingPreset, _activeBladeMappingPreset);
        _activeSnapTapEnabled = snapTap;
        _activeBladeMappingPreset = mappingPreset;
        ProfileNames.Clear();
        foreach (var name in ProfileCatalog.GetNames(_profile))
        {
            ProfileNames.Add(name);
        }

        ActiveProfileName = _profile.ActiveProfileName;
        var shortcuts = GetActiveProfile().Shortcuts;
        _bladePerformanceCycleModes = shortcuts.PerformanceCycleModes!.ToHashSet();
        _internalDisplayRefreshRateCycleHertz = shortcuts.RefreshRateCycleHertz?.ToHashSet();
        var lighting = BladeLightingProfileCodec.Parse(_profile.Global.Lighting);
        var lightingIndex = Array.IndexOf(BladeLightingModes, lighting.Mode);
        if (lightingIndex >= 0)
        {
            BladeLightingModeIndex = lightingIndex;
            BladeWaveDirectionIndex = Array.IndexOf(BladeWaveDirections, lighting.Direction);
            BladeLightingColor = Color.FromArgb(
                0xFF, lighting.Color.Red, lighting.Color.Green, lighting.Color.Blue);
            if (lighting.Mode == BladeLightingMode.Tidal)
            {
                BladeLightingSecondColor = Color.FromArgb(
                    0xFF,
                    lighting.SecondColor.Red,
                    lighting.SecondColor.Green,
                    lighting.SecondColor.Blue);
            }
        }
        ApplicationBindings.Clear();
        foreach (var binding in _profile.ApplicationBindings.OrderBy(binding => binding.Key, StringComparer.OrdinalIgnoreCase))
        {
            ApplicationBindings.Add(new ApplicationBindingRowViewModel(binding.Key, binding.Value));
        }
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(CanDeleteProfile));
        OnPropertyChanged(nameof(BladePerformanceCycleModes));
        OnPropertyChanged(nameof(InternalDisplayRefreshRateCycleHertz));
        if (bladeInputChanged)
        {
            BladeInputProfileChanged?.Invoke();
        }
    }

    private ProfileDefinition GetActiveProfile() =>
        _profile.Profiles.TryGetValue(_profile.ActiveProfileName, out var profile)
            ? profile
            : throw new InvalidOperationException("The active profile is missing from the profile catalog.");

    private bool EnsureActiveProfileExtensions()
    {
        var profile = GetActiveProfile();
        var changed = false;
        if (profile.Shortcuts.PerformanceCycleModes is null)
        {
            profile.Shortcuts.PerformanceCycleModes = (_legacyPerformanceCycleModes is { Count: > 0 }
                    ? _legacyPerformanceCycleModes
                    : BladePerformanceModes)
                .Distinct()
                .ToList();
            changed = true;
        }
        if (profile.Shortcuts.RefreshRateCycleHertz is null)
        {
            var defaults = _legacyRefreshRateCycleHertz is { Count: > 0 }
                ? _legacyRefreshRateCycleHertz
                : InternalDisplayRefreshRates;
            if (defaults.Count > 0)
            {
                profile.Shortcuts.RefreshRateCycleHertz = defaults
                    .Where(hertz => hertz > 0)
                    .Distinct()
                    .Order()
                    .ToList();
                changed = true;
            }
        }
        if (profile.Global.Blade.MappingPreset is null)
        {
            profile.Global.Blade.MappingPreset = BladeProfileSettings.Product710DefaultMappingPreset;
            changed = true;
        }
        if (profile.Global.Blade.SnapTapEnabled is null)
        {
            profile.Global.Blade.SnapTapEnabled = false;
            changed = true;
        }
        return changed;
    }

    private void RefreshStartupState()
    {
        try
        {
            IsStartupEnabled = _startupManager is not null &&
                !string.IsNullOrWhiteSpace(_executablePath) &&
                _startupManager.IsEnabled(_executablePath);
            IsSilentStartupEnabled = IsStartupEnabled &&
                _startupManager!.IsSilentEnabled(_executablePath!);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            IsStartupEnabled = false;
            IsSilentStartupEnabled = false;
            SetDeviceOperationError(AppStrings.FormatText("StartupError", exception.Message));
        }
        OnPropertyChanged(nameof(CanSetStartup));
    }

    private async Task<ProfileApplyResult?> ApplyLoadedProfileAsync(
        RazerDeviceTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        if (_deviceDescriptors.Count == 0)
        {
            return null;
        }

        var result = await _profileApplier.ApplyAsync(
            _profile,
            _deviceDescriptors,
            telemetry,
            _deviceTelemetryReader,
            _powerSourceProvider.IsPluggedIn,
            cancellationToken);
        return result;
    }

    private async Task<string?> ApplyLoadedViperMappingProfileAsync(
        DeviceDescriptor? viper,
        bool? powerState,
        CancellationToken cancellationToken)
    {
        if (viper is null || viper.Access != DeviceAccessState.Available)
        {
            _viper._viperMappingProfileFingerprint = string.Empty;
            return null;
        }

        var profile = ProfileResolver.Resolve(_profile, viper, powerState).Viper;
        var fingerprint = CreateViperMappingFingerprint(viper.Id, profile.ButtonAssignments);
        if (StringComparer.Ordinal.Equals(_viper._viperMappingProfileFingerprint, fingerprint))
        {
            return null;
        }

        try
        {
            IReadOnlyList<ViperButtonAssignment> actual;
            if (profile.ButtonAssignments is null)
            {
                actual = await _deviceTelemetryReader.ReadViperButtonAssignmentsAsync(
                    _deviceDescriptors, cancellationToken);
                var previous = _profile.Global.Viper.ButtonAssignments;
                _profile.Global.Viper.ButtonAssignments = actual.Select(ToProfileAssignment).ToList();
                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile.Global.Viper.ButtonAssignments = previous;
                    return AppStrings.Text("ViperMappingReadProfileSaveFailed");
                }
                fingerprint = CreateViperMappingFingerprint(
                    viper.Id,
                    _profile.Global.Viper.ButtonAssignments);
            }
            else
            {
                actual = await _deviceTelemetryReader.SetViperButtonAssignmentsAsync(
                    _deviceDescriptors,
                    profile.ButtonAssignments.Select(ToDeviceAssignment).ToArray(),
                    cancellationToken);
            }

            SetViperButtonAssignments(actual);
            _viper._viperMappingProfileFingerprint = fingerprint;
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _viper._viperMappingProfileFingerprint = string.Empty;
            return AppStrings.FormatText("ViperMappingProfileError", exception.Message);
        }
    }

    private string CreateViperMappingFingerprint(
        string devicePath,
        IReadOnlyList<ViperButtonAssignmentProfile>? assignments) =>
        string.Join('|',
            _profile.ActiveProfileName,
            devicePath,
            assignments is null
                ? "unmanaged"
                : string.Join(';', assignments
                    .OrderBy(item => item.ButtonId)
                    .ThenBy(item => item.Layer)
                    .Select(item => $"{item.ProfileId:X2}:{item.ButtonId:X2}:{(byte)item.Layer:X2}:{(byte)item.Function:X2}:{Convert.ToHexString(item.FunctionData.ToArray())}")));

    private async Task<BladeFanProfileApplyResult> ApplyLoadedFanProfileAsync(
        DeviceDescriptor? blade,
        bool? powerState,
        CancellationToken cancellationToken)
    {
        if (blade is null || blade.Access != DeviceAccessState.Available)
        {
            return new(await StopBladeFanControlAsync("device-unavailable"), Changed: true);
        }

        var profile = ProfileResolver.Resolve(_profile, blade, powerState).Blade;
        BladeFanCurve? curve = null;
        try
        {
            if (profile.FanCurve is not null)
            {
                if (profile.FanMode is not null || profile.FanTargetRpm is not null)
                {
                    return new(AppStrings.Get("固定转速和智能曲线不能同时配置。"), Changed: false);
                }

                curve = profile.FanCurve.CreateCurve();
            }
            else if (profile.FanMode is byte rawMode)
            {
                if (!Enum.IsDefined(typeof(BladeFanMode), rawMode))
                {
                    return new(AppStrings.FormatText("InvalidBladeFanMode", rawMode), Changed: false);
                }

                var mode = (BladeFanMode)rawMode;
                if (mode == BladeFanMode.Manual && profile.FanTargetRpm is null ||
                    mode == BladeFanMode.Automatic && profile.FanTargetRpm is not null)
                {
                    return new(AppStrings.Get("Blade 固定风扇模式和目标转速不匹配。"), Changed: false);
                }
            }
            else if (profile.FanTargetRpm is not null)
            {
                return new(AppStrings.Get("Blade 固定风扇缺少模式。"), Changed: false);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new(exception.Message, Changed: false);
        }

        var fingerprint = CreateBladeFanFingerprint(profile, blade.Id, powerState);
        if (StringComparer.Ordinal.Equals(_bladeFanControlFingerprint, fingerprint) &&
            _bladeFanControlCompletion is { IsCompleted: false })
        {
            return new(null, Changed: false);
        }

        var hadControl = _bladeFanRuntime.IsRunning || _bladeFanControlFingerprint.Length > 0;
        var stopError = await StopBladeFanControlAsync("profile-change");
        if (stopError is not null)
        {
            return new(stopError, Changed: true);
        }

        if (fingerprint.Length == 0)
        {
            return new(null, Changed: hadControl);
        }

        try
        {
            if (curve is not null)
            {
                await _bladeFanRuntime.StartAsync(
                    _deviceDescriptors,
                    curve,
                    cancellationToken);
            }
            else
            {
                var mode = (BladeFanMode)profile.FanMode!.Value;
                await _bladeFanRuntime.StartFixedAsync(
                    _deviceDescriptors,
                    mode,
                    profile.FanTargetRpm,
                    cancellationToken);
            }

            _bladeFanControlFingerprint = fingerprint;
            _bladeFanControlCompletion = _bladeFanRuntime.Completion;
            _ = ObserveBladeFanControlAsync(_bladeFanControlCompletion);
            return new(null, Changed: true);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _bladeFanControlFingerprint = string.Empty;
            _bladeFanControlCompletion = null;
            return new(FormatOperationException(exception), Changed: true);
        }
    }

    public async Task ApplyBladeFixedFanAsync(
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync("固定风扇", async () =>
        {
            var previous = _profile.Clone();
            _profile.Global.Blade.FanCurve = null;
            _profile.Global.Blade.FanMode = (byte)mode;
            _profile.Global.Blade.FanTargetRpm = targetRpm;
            var blade = _deviceDescriptors.FirstOrDefault(device =>
                device.ProtocolFamily == DeviceProtocolFamilies.Blade && device.Access == DeviceAccessState.Available);
            var result = await ApplyLoadedFanProfileAsync(
                blade,
                _powerSourceProvider.IsPluggedIn,
                cancellationToken);
            if (result.Error is not null)
            {
                _profile = previous;
                throw new InvalidOperationException(result.Error);
            }

            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previous;
                await ApplyLoadedFanProfileAsync(
                    blade,
                    _powerSourceProvider.IsPluggedIn,
                    cancellationToken);
                throw new InvalidOperationException(AppStrings.Get("固定风扇已写入，但配置保存失败，已恢复内存配置。"));
            }
        }, cancellationToken);
    }

    public async Task ApplyBladeFanCurveAsync(
        BladeFanCurve curve,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(curve);
        await RunDeviceOperationAsync("智能风扇曲线", async () =>
        {
            var previous = _profile.Clone();
            _profile.Global.Blade.FanCurve = BladeFanCurveProfile.FromCurve(curve);
            _profile.Global.Blade.FanMode = null;
            _profile.Global.Blade.FanTargetRpm = null;
            var blade = _deviceDescriptors.FirstOrDefault(device =>
                device.ProtocolFamily == DeviceProtocolFamilies.Blade && device.Access == DeviceAccessState.Available);
            var result = await ApplyLoadedFanProfileAsync(
                blade,
                _powerSourceProvider.IsPluggedIn,
                cancellationToken);
            if (result.Error is not null)
            {
                _profile = previous;
                throw new InvalidOperationException(result.Error);
            }

            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previous;
                await ApplyLoadedFanProfileAsync(
                    blade,
                    _powerSourceProvider.IsPluggedIn,
                    cancellationToken);
                throw new InvalidOperationException(AppStrings.Get("智能风扇曲线已启动，但配置保存失败，已恢复内存配置。"));
            }
        }, cancellationToken);
    }

    public async Task StopBladeFanControlAsync() =>
        _ = await StopBladeFanControlAsync("explicit-stop");

    private async Task<string?> StopBladeFanControlAsync(string reason)
    {
        _bladeFanControlCompletion = null;
        _bladeFanControlFingerprint = string.Empty;
        if (!_bladeFanRuntime.IsRunning)
        {
            return null;
        }

        try
        {
            await _bladeFanRuntime.StopAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _diagnosticLog.TryWrite("blade-fan", $"{reason} restore failed: {exception}");
            return FormatOperationException(exception);
        }
    }

    private async Task ObserveBladeFanControlAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            if (ReferenceEquals(_bladeFanControlCompletion, completion))
            {
                _diagnosticLog.TryWrite("blade-fan", $"运行失败：{exception}");
                _bladeFanControlFingerprint = string.Empty;
                _bladeFanControlCompletion = null;
                try
                {
                    await _bladeFanRuntime.StopAsync().ConfigureAwait(false);
                }
                catch (Exception restoreException) when (IsExpectedFanException(restoreException))
                {
                    _diagnosticLog.TryWrite("blade-fan", $"失败后恢复失败：{restoreException}");
                }
                RequestDeviceRefresh();
            }
        }
    }

    private static string CreateBladeFanFingerprint(
        BladeProfileSettings profile,
        string devicePath,
        bool? powerState)
    {
        if (profile.FanCurve is { } curve)
        {
            var points = string.Join(",", curve.CpuPoints.Select(FormatPoint)) + "/" +
                string.Join(",", curve.GpuPoints.Select(FormatPoint));
            return $"curve\n{devicePath}\n{powerState}\n{curve.TemperatureMode}\n" +
                $"{curve.MinimumCpuTemperatureCelsius},{curve.MinimumGpuTemperatureCelsius}," +
                $"{curve.MinimumFanSpeedRpm}\n{points}";
        }

        return profile.FanMode is byte mode
            ? $"fixed\n{devicePath}\n{powerState}\n{mode}\n{profile.FanTargetRpm}"
            : string.Empty;

        static string FormatPoint(BladeFanCurvePoint point) =>
            $"{point.TemperatureCelsius}:{point.CpuFanSpeedRpm}:{point.GpuFanSpeedRpm}";
    }

    public async Task PrepareForSuspendAsync()
    {
        var error = await StopBladeFanControlAsync("suspend").ConfigureAwait(false);
        if (error is not null)
        {
            _diagnosticLog.TryWrite("blade-fan", $"suspend restore incomplete: {error}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Task brightnessWriter;
        lock (_bladeBrightnessGate)
        {
            _desiredBladeBrightness = null;
            brightnessWriter = _bladeBrightnessWriter;
        }
        await brightnessWriter.ConfigureAwait(false);
        Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        await _bladeBrightnessVerification.ConfigureAwait(false);
        _ = await StopBladeFanControlAsync("application-exit").ConfigureAwait(false);
        try
        {
            await _bladeFanRuntime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedFanException(exception))
        {
            _diagnosticLog.TryWrite("blade-fan", $"dispose restore failed: {exception}");
        }
    }

    private async Task<string?> ApplyLoadedLightingProfileAsync(
        DeviceDescriptor? blade,
        bool? powerState,
        CancellationToken cancellationToken)
    {
        if (_bladeLightingController is null)
        {
            return null;
        }

        if (blade is null || blade.Access != DeviceAccessState.Available)
        {
            if (_bladeLightingDevicePath.Length > 0)
            {
                try
                {
                    await _bladeLightingController.StopAsync();
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception))
                {
                    _diagnosticLog.TryWrite("keyboard-lighting", $"disconnect restore failed: {exception}");
                }
            }

            _bladeLightingDevicePath = string.Empty;
            _lightingShadowFingerprint = string.Empty;
            return null;
        }

        var profile = ProfileResolver.Resolve(_profile, blade, powerState).Lighting;
        string fingerprint;
        BladeLightingEffect effect;
        try
        {
            effect = BladeLightingProfileCodec.Parse(profile);
            fingerprint = CreateLightingFingerprint(profile, blade.Id, powerState);
        }
        catch (InvalidOperationException exception)
        {
            _lightingShadowFingerprint = string.Empty;
            return exception.Message;
        }

        if (StringComparer.Ordinal.Equals(_lightingShadowFingerprint, fingerprint))
        {
            return null;
        }

        if (_bladeLightingDevicePath.Length > 0 &&
            !StringComparer.OrdinalIgnoreCase.Equals(_bladeLightingDevicePath, blade.Id))
        {
            try
            {
                await _bladeLightingController.StopAsync();
            }
            catch (Exception exception) when (IsExpectedRuntimeException(exception))
            {
                _diagnosticLog.TryWrite("keyboard-lighting", $"path-change restore failed: {exception}");
            }
        }

        try
        {
            await _bladeLightingController.ApplyAsync(_deviceDescriptors, effect, cancellationToken);
            _bladeLightingDevicePath = blade.Id;
            _lightingShadowFingerprint = fingerprint;
            _ = ObserveBladeLightingRuntimeAsync(_bladeLightingController.RuntimeCompletion);
            return null;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _bladeLightingDevicePath = string.Empty;
            _lightingShadowFingerprint = string.Empty;
            return FormatOperationException(exception);
        }
    }

    private string CreateLightingFingerprint(
        LightingProfile profile,
        string devicePath,
        bool? powerState) =>
        $"{_profile.ActiveProfileName}\n{powerState}\n{BladeLightingProfileCodec.Fingerprint(profile, devicePath)}";

    private async Task<bool> SaveProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _profileStore.SaveAsync(_profile, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.FormatText("ProfileSaveError", exception.Message));
            return false;
        }
    }

    public async Task RunPerformanceLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshPerformanceAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunDeviceWatchLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    var snapshot = await _discovery.DiscoverAsync(cancellationToken);
                    var powerState = _powerSourceProvider.IsPluggedIn;
                    var refreshRequested = Volatile.Read(ref _deviceRefreshRequested) != 0;
                    var previousProfile = _profile.Clone();
                    var previousProfileSwitcher = _applicationProfileSwitcher.Clone();
                    var profileChanged = _applicationProfileSwitcher.Update(
                        _profile, _activeApplicationProvider.ExecutablePath);
                    if (profileChanged)
                    {
                        RefreshProfileState();
                        try
                        {
                            await _profileStore.SaveAsync(_profile, cancellationToken);
                            ProfileStatusText = AppStrings.FormatText("ProfileAutoSwitched", ActiveProfileName);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            _profile = previousProfile;
                            _applicationProfileSwitcher = previousProfileSwitcher;
                            RefreshProfileState();
                            SetDeviceOperationError(AppStrings.FormatText("AutomaticProfileSaveError",
                                exception.Message));
                            profileChanged = false;
                        }
                    }
                    var powerChanged = _lastPowerState != powerState;
                    var displayProfileRequested =
                        Interlocked.Exchange(ref _displayProfileApplyRequested, 0) != 0;
                    if (!StringComparer.Ordinal.Equals(_deviceFingerprint, CreateDeviceFingerprint(snapshot)) ||
                        powerChanged ||
                        profileChanged ||
                        displayProfileRequested ||
                        refreshRequested ||
                        DateTimeOffset.UtcNow >= _nextFullDeviceRefresh)
                    {
                        await RefreshDevicesCoreAsync(
                            snapshot,
                            cancellationToken,
                            applyDisplayProfile: powerChanged || profileChanged || displayProfileRequested);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception))
                {
                    SetDeviceQueryError(AppStrings.FormatText("DeviceWatchError", exception.Message));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public Task RefreshDevicesAsync(CancellationToken cancellationToken = default) =>
        RefreshDevicesCoreAsync(null, cancellationToken);

    private async Task RefreshDevicesCoreAsync(
        DeviceSnapshot? knownSnapshot,
        CancellationToken cancellationToken,
        bool applyDisplayProfile = false)
    {
        if (!await TryEnterOperationAsync(cancellationToken))
        {
            return;
        }
        SetDeviceQueryError(string.Empty);
        SetDeviceOperationError(string.Empty);
        var powerState = _powerSourceProvider.IsPluggedIn;
        applyDisplayProfile |= _lastPowerState != powerState;
        _lastPowerState = powerState;
        try
        {
            var snapshot = knownSnapshot ?? await _discovery.DiscoverAsync(cancellationToken);
            var nextFingerprint = CreateDeviceFingerprint(snapshot);
            if (!StringComparer.Ordinal.Equals(_deviceFingerprint, nextFingerprint))
            {
                ResetDeviceTelemetry();
            }
            _deviceFingerprint = nextFingerprint;
            _deviceDescriptors = snapshot.Devices;
            RefreshInternalDisplay(powerState, applyDisplayProfile);

            var blade = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Blade);
            var viper = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper);
            SetBladeControlDevicePath(
                blade is
                {
                    Access: DeviceAccessState.Available,
                    FeatureReportByteLength: RazerFeatureReport.Length,
                }
                    ? blade.Id
                    : null);
            BladeDeviceName = blade?.Name ?? "Razer Blade";
            ViperDeviceName = viper?.Name ?? "Razer Viper";
            BladeStatusText = FormatDeviceStatus(blade);
            ViperStatusText = FormatDeviceStatus(viper);

            var telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
            ApplyDeviceTelemetry(telemetry);
            var profileApply = await ApplyLoadedProfileAsync(telemetry, cancellationToken);
            if (profileApply is { AppliedCount: > 0 })
            {
                telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
                ApplyDeviceTelemetry(telemetry);
            }
            var viperAvailable = viper is not null &&
                DeviceRowViewModel.CountCapabilities(DeviceProtocolFamilies.Viper, telemetry).Successful > 0;
            var bladeProfileBlocked = profileApply?.Errors.Any(error =>
                error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true;
            var fanApply = bladeProfileBlocked
                ? new BladeFanProfileApplyResult(
                    await StopBladeFanControlAsync("profile-error"),
                    Changed: true)
                : await ApplyLoadedFanProfileAsync(blade, powerState, cancellationToken);
            var viperMappingError = await ApplyLoadedViperMappingProfileAsync(
                viperAvailable ? viper : null, powerState, cancellationToken);
            if (fanApply.Changed && blade is { Access: DeviceAccessState.Available })
            {
                telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
                ApplyDeviceTelemetry(telemetry);
            }
            var lightingError = profileApply?.Errors.Any(error =>
                    error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true
                ? null
                : await ApplyLoadedLightingProfileAsync(blade, powerState, cancellationToken);
            var profileOperationErrors = profileApply?.Errors
                .Where(error => viperAvailable ||
                    !error.StartsWith("Viper", StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? [];
            var profileOperationError = profileOperationErrors.Length == 0
                ? string.Empty
                : AppStrings.FormatText("ProfileApplyError",
                    string.Join("; ", profileOperationErrors));
            SetDeviceOperationError(string.Join(
                Environment.NewLine,
                new[]
                {
                    profileOperationError,
                    viperAvailable ? viperMappingError : null,
                }.Where(error => !string.IsNullOrWhiteSpace(error))));
            var visibleDevices = viperAvailable
                ? snapshot.Devices
                : snapshot.Devices.Where(device => device.ProtocolFamily != DeviceProtocolFamilies.Viper).ToArray();
            _deviceDescriptors = visibleDevices;
            ViperDeviceVisibility = viperAvailable ? Visibility.Visible : Visibility.Collapsed;
            if (!viperAvailable)
            {
                ResetViperTelemetry();
                ViperStatusText = "未发现";
            }
            Devices.Clear();
            foreach (var device in visibleDevices)
            {
                Devices.Add(new DeviceRowViewModel(device, telemetry));
            }

            var errors = telemetry.Errors
                .Where(error => viperAvailable || !error.StartsWith("鼠标", StringComparison.Ordinal))
                .ToList();
            if (profileApply is { Errors.Count: > 0 })
            {
                errors.AddRange(profileApply.Errors
                    .Where(error => viperAvailable || !error.StartsWith("Viper", StringComparison.OrdinalIgnoreCase))
                    .Select(error => $"配置应用：{error}"));
            }
            if (!string.IsNullOrWhiteSpace(lightingError))
            {
                errors.Add($"键盘灯效：{lightingError}");
            }
            if (!string.IsNullOrWhiteSpace(fanApply.Error))
            {
                errors.Add($"风扇控制：{fanApply.Error}");
            }
            if (!string.IsNullOrWhiteSpace(viperMappingError) && viperAvailable)
            {
                errors.Add(viperMappingError);
            }
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                errors.Insert(0, snapshot.ErrorMessage);
            }

            RebuildDiagnostics(snapshot with { Devices = visibleDevices }, telemetry, errors);
            SetDeviceQueryError(errors.Count == 0
                ? string.Empty
                : AppStrings.FormatText("HardwareQueryFailureCount",
                    errors.Count));
            LastDeviceRefreshText = AppStrings.FormatText("DeviceScanTime",
                snapshot.CapturedAt.ToLocalTime());
            _nextFullDeviceRefresh = DateTimeOffset.UtcNow.AddSeconds(30);
            Interlocked.Exchange(ref _deviceRefreshRequested, 0);
            OnPropertyChanged(nameof(EmptyStateText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            var hadBlade = _deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Blade);
            var hadViper = _deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper);
            _deviceDescriptors = Array.Empty<DeviceDescriptor>();
            ViperDeviceVisibility = Visibility.Collapsed;
            if (_bladeLightingController is not null && _bladeLightingDevicePath.Length > 0)
            {
                try
                {
                    await _bladeLightingController.StopAsync();
                }
                catch (Exception restoreException) when (IsExpectedRuntimeException(restoreException))
                {
                    _diagnosticLog.TryWrite(
                        "keyboard-lighting",
                        $"refresh-failure restore failed: {restoreException}");
                }
            }
            _ = await StopBladeFanControlAsync("refresh-failure");
            SetBladeControlDevicePath(null);
            _lightingShadowFingerprint = string.Empty;
            _bladeLightingDevicePath = string.Empty;
            RefreshInternalDisplay(powerState, applyDisplayProfile);
            BladeStatusText = hadBlade ? "查询失败 · 显示上次值" : "未发现";
            ViperStatusText = hadViper ? "查询失败 · 显示上次值" : "未发现";
            SetDeviceQueryError(exception.Message);
            LastDeviceRefreshText = "设备探测失败";
            OnPropertyChanged(nameof(EmptyStateText));
        }
        finally
        {
            _deviceOperationGate.Release();
        }
    }

    public async Task ApplyBladeBrightnessAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade._canSetBladeBrightness)
        {
            return;
        }

        await RunDeviceOperationAsync(
            "键盘亮度",
            () => ApplyBladeBrightnessCoreAsync(cancellationToken),
            cancellationToken,
            () => BladeBrightnessPercent = _blade._confirmedBladeBrightnessPercent);
    }

    private async Task ApplyBladeBrightnessCoreAsync(CancellationToken cancellationToken)
    {
        var requested = checked((byte)Math.Round(
            BladeBrightnessPercent * 255 / 100,
            MidpointRounding.AwayFromZero));
        var actual = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
            _deviceDescriptors,
            requested,
            cancellationToken);
        SetBladeBrightness(actual);
        _profile.Global.Blade.KeyboardBrightness = actual;
        await SaveProfileAsync(cancellationToken);
    }

    public async Task ApplyBladeLightingEffectAsync(
        BladeLightingEffect effect,
        CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeLighting || _bladeLightingController is null)
        {
            return;
        }

        await RunDeviceOperationAsync(
            "键盘灯效",
            async () =>
            {
                var previousProfile = _profile.Clone();
                await _bladeLightingController.ApplyAsync(
                    _deviceDescriptors, effect, cancellationToken);
                _profile.Global.Lighting = BladeLightingProfileCodec.Create(effect);
                if (!await SaveProfileAsync(cancellationToken))
                {
                    _profile = previousProfile;
                    _lightingShadowFingerprint = string.Empty;
                    throw new InvalidOperationException(AppStrings.Get("灯效已启动，但配置保存失败，已恢复内存中的配置。"));
                }

                var blade = _deviceDescriptors.FirstOrDefault(device =>
                    device.ProtocolFamily == DeviceProtocolFamilies.Blade &&
                    device.Access == DeviceAccessState.Available);
                if (blade is not null)
                {
                    _bladeLightingDevicePath = blade.Id;
                    _lightingShadowFingerprint = CreateLightingFingerprint(
                        _profile.Global.Lighting, blade.Id, _powerSourceProvider.IsPluggedIn);
                }
                _ = ObserveBladeLightingRuntimeAsync(
                    _bladeLightingController.RuntimeCompletion);
            },
            cancellationToken,
            successVerb: "启动");
    }

    public Task ApplySelectedBladeLightingEffectAsync(CancellationToken cancellationToken = default)
    {
        if (BladeLightingModeIndex < 0 || BladeLightingModeIndex >= BladeLightingModes.Length ||
            BladeWaveDirectionIndex < 0 || BladeWaveDirectionIndex >= BladeWaveDirections.Length)
        {
            return Task.CompletedTask;
        }

        var color = new RazerRgb(BladeLightingColor.R, BladeLightingColor.G, BladeLightingColor.B);
        var secondColor = new RazerRgb(
            BladeLightingSecondColor.R,
            BladeLightingSecondColor.G,
            BladeLightingSecondColor.B);
        var effect = new BladeLightingEffect(
            BladeLightingModes[BladeLightingModeIndex],
            color,
            BladeWaveDirections[BladeWaveDirectionIndex],
            secondColor);
        return ApplyBladeLightingEffectAsync(effect, cancellationToken);
    }

    public async Task ApplyBladePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        if (!_blade._canSetBladePerformanceMode ||
            BladePerformanceModeIndex < 0 || BladePerformanceModeIndex >= BladePerformanceModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("性能模式", async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                _deviceDescriptors, BladePerformanceModes[BladePerformanceModeIndex], cancellationToken);
            SetBladePerformanceMode(actual);
            _profile.Global.Blade.PerformanceMode = (byte)actual;
            await SaveProfileAsync(cancellationToken);
            RequestDeviceRefresh();
            BladePerformanceModeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            BladePerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex);
    }

    public async Task ApplyBladeChargeLimitAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeChargeLimit ||
            BladeChargeLimitIndex < 0 || BladeChargeLimitIndex >= BladeChargeLimits.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("充电上限", async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeChargeLimitAsync(
                _deviceDescriptors, BladeChargeLimits[BladeChargeLimitIndex], cancellationToken);
            SetBladeChargeLimit(actual);
            _profile.Global.Blade.ChargeLimitPercent = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
            BladeChargeLimitIndex = _blade._confirmedBladeChargeLimitIndex);
    }

    public async Task ApplyBladeCpuBoostAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeCpuBoost || BladeCpuBoostIndex < 0 || BladeCpuBoostIndex >= BladeCpuBoostModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("CPU Boost", async () =>
        {
            var previousProfile = _profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeCpuBoostModeAsync(
                _deviceDescriptors, BladeCpuBoostModes[BladeCpuBoostIndex], cancellationToken);
            SetBladeCpuBoost(actual);
            _profile.Global.Blade.CpuBoostMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Get("CPU Boost 已写入，但配置保存失败，已恢复内存中的配置。"));
            }
        }, cancellationToken, () => BladeCpuBoostIndex = _blade._confirmedBladeCpuBoostIndex);
    }

    public async Task ApplyBladeGpuBoostAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeGpuBoost || BladeGpuBoostIndex < 0 || BladeGpuBoostIndex >= BladeGpuBoostModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("GPU Boost", async () =>
        {
            var previousProfile = _profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeGpuBoostModeAsync(
                _deviceDescriptors, BladeGpuBoostModes[BladeGpuBoostIndex], cancellationToken);
            SetBladeGpuBoost(actual);
            _profile.Global.Blade.GpuBoostMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Get("GPU Boost 已写入，但配置保存失败，已恢复内存中的配置。"));
            }
        }, cancellationToken, () => BladeGpuBoostIndex = _blade._confirmedBladeGpuBoostIndex);
    }

    public async Task ApplyBladeMaxFanAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeMaxFan)
        {
            return;
        }

        await RunDeviceOperationAsync("Max Fan", async () =>
        {
            var requested = BladeMaxFanEnabled ? BladeMaxFanMode.Enabled : BladeMaxFanMode.Disabled;
            var actual = await _deviceTelemetryReader.SetBladeMaxFanModeAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetBladeMaxFan(actual);
            _profile.Global.Blade.MaxFanMode = (byte)actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () => BladeMaxFanEnabled = _blade._confirmedBladeMaxFanEnabled);
    }

    public async Task ApplyBladeLogoAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeLogo || BladeLogoIndex < 0 || BladeLogoIndex >= BladeLogoModes.Length)
        {
            return;
        }

        await RunDeviceOperationAsync("机身 Logo", async () =>
        {
            var previousProfile = _profile.Clone();
            var actual = await _deviceTelemetryReader.SetBladeLogoModeAsync(
                _deviceDescriptors, BladeLogoModes[BladeLogoIndex], cancellationToken);
            SetBladeLogo(actual);
            _profile.Global.Blade.LogoMode = (byte)actual;
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Get("Logo 已写入，但配置保存失败，已恢复内存中的配置。"));
            }
        }, cancellationToken, () => BladeLogoIndex = _blade._confirmedBladeLogoIndex);
    }

    public async Task ToggleBladeTouchpadAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(
            "触控板",
            async () =>
            {
                if (!_blade._canSetBladeTouchpad || _touchpadController is null)
                {
                    throw new InvalidOperationException(AppStrings.Get("触控板状态不可用。"));
                }

                var actual = await Task.Run(
                    _touchpadController.ToggleVerified,
                    cancellationToken);
                BladeTouchpadEnabled = actual;
                _blade._confirmedBladeTouchpadEnabled = actual;
                BladeTouchpadText = actual ? "已启用" : "已禁用";
            },
            cancellationToken,
            () =>
            {
                BladeTouchpadEnabled = _blade._confirmedBladeTouchpadEnabled;
                BladeTouchpadText = _blade._confirmedBladeTouchpadEnabled ? "已启用" : "已禁用";
            },
            successVerb: "切换并读回",
            failureVerb: "切换");
    }

    internal async Task CycleBladePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync("性能模式", async () =>
        {
            if (!_blade._canSetBladePerformanceMode || _blade._confirmedBladePerformanceModeIndex < 0)
            {
                throw new InvalidOperationException(AppStrings.Get("性能模式状态不可用。"));
            }

            var nextMode = BladePerformanceModeCycle.GetNext(
                BladePerformanceModes[_blade._confirmedBladePerformanceModeIndex],
                BladePerformanceModes,
                _bladePerformanceCycleModes);
            BladePerformanceModeIndex = Array.IndexOf(BladePerformanceModes, nextMode);
            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                _deviceDescriptors,
                BladePerformanceModes[BladePerformanceModeIndex],
                cancellationToken);
            SetBladePerformanceMode(actual);
            _profile.Global.Blade.PerformanceMode = (byte)actual;
            await SaveProfileAsync(cancellationToken);
            RequestDeviceRefresh();
            BladePerformanceModeChangedByUser?.Invoke(actual);
        }, cancellationToken, () =>
            BladePerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex);
    }

    internal async Task ToggleBladeGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync("游戏模式", async () =>
        {
            var current = _blade._bladeGameModeState is byte state && state != 2
                ? state
                : throw new InvalidOperationException(AppStrings.Get("游戏模式状态不可用。"));
            var actual = await SetBladeGameModeCoreAsync(
                current == 0,
                cancellationToken);
            SetBladeGameMode(actual);
            RequestDeviceRefresh();
            BladeGamingModeChangedByUser?.Invoke(actual.GameMode != 0);
        }, cancellationToken);
    }

    public async Task ApplyBladeGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync("游戏模式", async () =>
        {
            var actual = await SetBladeGameModeCoreAsync(
                BladeGameModeEnabled,
                cancellationToken);
            SetBladeGameMode(actual);
            RequestDeviceRefresh();
            BladeGamingModeChangedByUser?.Invoke(actual.GameMode != 0);
        }, cancellationToken, () =>
            BladeGameModeEnabled = _blade._bladeGameModeState != 0);
    }

    public async Task ApplyBladeStartupAnimationAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeStartupAnimation)
        {
            return;
        }

        await RunDeviceOperationAsync("启动动画", async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeStartupAnimationAsync(
                _deviceDescriptors, BladeStartupAnimationEnabled, cancellationToken);
            _blade._bladeStartupAnimationEnabled = actual;
            BladeStartupAnimationText = FormatOptionalState(actual);
            BladeStartupAnimationEnabled = actual;
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladeStartupAnimationEnabled = _blade._bladeStartupAnimationEnabled ?? false);
    }

    internal async Task CycleInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync("内置屏刷新率", async () =>
        {
            if (!_canSetInternalDisplayRefreshRate ||
                _internalDisplayController is null ||
                InternalDisplayRefreshRates.Count == 0)
            {
                throw new InvalidOperationException(AppStrings.Get("内置屏刷新率状态不可用。"));
            }

            var includedRates = _internalDisplayRefreshRateCycleHertz is { Count: > 0 }
                ? _internalDisplayRefreshRateCycleHertz
                : InternalDisplayRefreshRates.ToHashSet();
            InternalDisplayRefreshRateHertz = BladePerformanceModeCycle.GetNext(
                _confirmedInternalDisplayRefreshRateHertz,
                InternalDisplayRefreshRates,
                includedRates);
            var snapshot = _internalDisplayController.SetRefreshRate(
                InternalDisplayRefreshRateHertz);
            ApplyInternalDisplaySnapshot(snapshot);
            _profile.Global.Blade.RefreshRateHertz = snapshot.RefreshRateHertz;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
            InternalDisplayRefreshRateHertz = _confirmedInternalDisplayRefreshRateHertz);
    }

    internal Task StepBladeBrightnessAsync(
        bool increase,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_blade._canSetBladeBrightness)
        {
            throw new InvalidOperationException(AppStrings.Get("键盘亮度状态不可用。"));
        }

        lock (_bladeBrightnessGate)
        {
            var current = _desiredBladeBrightness ?? ToBladeBrightness(BladeBrightnessPercent);
            var requested = (byte)Math.Clamp(current + (increase ? 16 : -16), 0, 255);
            if (requested == current)
            {
                return Task.CompletedTask;
            }

            _desiredBladeBrightness = requested;
            BladeBrightnessPercent = Math.Round(
                requested * 100d / 255,
                MidpointRounding.AwayFromZero);
            Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
            if (!_bladeBrightnessWriterActive)
            {
                _bladeBrightnessWriterActive = true;
                _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
            }
        }
        return Task.CompletedTask;
    }

    private async Task WriteDesiredBladeBrightnessAsync()
    {
        try
        {
            await WriteDesiredBladeBrightnessCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_bladeBrightnessGate)
            {
                _bladeBrightnessWriterActive = false;
                if (_desiredBladeBrightness is not null && Volatile.Read(ref _disposed) == 0)
                {
                    _bladeBrightnessWriterActive = true;
                    _bladeBrightnessWriter = WriteDesiredBladeBrightnessAsync();
                }
            }
        }
    }

    private async Task WriteDesiredBladeBrightnessCoreAsync()
    {
        byte? lastWritten = null;
        while (Volatile.Read(ref _disposed) == 0)
        {
            byte requested;
            lock (_bladeBrightnessGate)
            {
                if (_desiredBladeBrightness is not byte desired)
                {
                    break;
                }
                requested = desired;
            }

            var original = ToBladeBrightness(_blade._confirmedBladeBrightnessPercent);
            var applied = false;
            await RunDeviceOperationAsync("键盘亮度", async () =>
            {
                try
                {
                    await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                        _deviceDescriptors,
                        requested,
                        CancellationToken.None,
                        verifyReadback: false);
                }
                catch (Exception writeError) when (IsExpectedRuntimeException(writeError))
                {
                    try
                    {
                        var restored = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(
                            _deviceDescriptors,
                            original,
                            CancellationToken.None);
                        lock (_bladeBrightnessGate)
                        {
                            _desiredBladeBrightness = null;
                        }
                        SetBladeBrightness(restored);
                    }
                    catch (Exception restoreError) when (IsExpectedRuntimeException(restoreError))
                    {
                        throw new AggregateException(writeError, restoreError);
                    }

                    throw;
                }

                lock (_bladeBrightnessGate)
                {
                    if (_desiredBladeBrightness == requested)
                    {
                        _desiredBladeBrightness = null;
                    }
                }
                SetBladeBrightness(requested);
                lastWritten = requested;
                applied = true;
            }, CancellationToken.None, () =>
            {
                lock (_bladeBrightnessGate)
                {
                    _desiredBladeBrightness = null;
                }
                BladeBrightnessPercent = _blade._confirmedBladeBrightnessPercent;
            }, successVerb: "即时写入");

            if (!applied)
            {
                return;
            }
        }

        if (lastWritten is not null && Volatile.Read(ref _disposed) == 0)
        {
            ScheduleBladeBrightnessVerification();
        }
    }

    private void ScheduleBladeBrightnessVerification()
    {
        var generation = Interlocked.Increment(ref _bladeBrightnessVerificationGeneration);
        _bladeBrightnessVerification = VerifyBladeBrightnessAfterIdleAsync(generation);
    }

    private async Task VerifyBladeBrightnessAfterIdleAsync(long generation)
    {
        await Task.Delay(BladeBrightnessVerificationDelay);
        if (generation != Volatile.Read(ref _bladeBrightnessVerificationGeneration) ||
            Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await RunDeviceOperationAsync("键盘亮度", async () =>
        {
            bool hasPendingBrightness;
            lock (_bladeBrightnessGate)
            {
                hasPendingBrightness = _desiredBladeBrightness is not null;
            }
            if (generation != Volatile.Read(ref _bladeBrightnessVerificationGeneration) ||
                hasPendingBrightness)
            {
                return;
            }

            var actual = await _deviceTelemetryReader.ReadBladeKeyboardBrightnessAsync(
                _deviceDescriptors,
                CancellationToken.None);
            SetBladeBrightness(actual);
            _profile.Global.Blade.KeyboardBrightness = actual;
            await SaveProfileAsync(CancellationToken.None);
        }, CancellationToken.None, () =>
            RequestDeviceRefresh());
    }

    private static byte ToBladeBrightness(double percent) => checked((byte)Math.Round(
        Math.Clamp(percent, 0, 100) * 255 / 100,
        MidpointRounding.AwayFromZero));

    internal async Task ToggleBladeOneTimeFullChargeAsync(
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync("一次性充满", async () =>
        {
            var current = _blade._bladeOneTimeFullChargeEnabled ??
                throw new InvalidOperationException(AppStrings.Get("一次性充满状态不可用。"));
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                _deviceDescriptors,
                !current,
                cancellationToken);
            _blade._bladeOneTimeFullChargeEnabled = actual;
            BladeOneTimeFullChargeEnabled = actual;
            BladeOneTimeFullChargeText = FormatOptionalState(actual);
            RequestDeviceRefresh();
        }, cancellationToken);
    }

    public async Task ApplyBladeOneTimeFullChargeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplyBladeOneTimeFullCharge)
        {
            return;
        }

        await RunDeviceOperationAsync("一次性充满", async () =>
        {
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                _deviceDescriptors, BladeOneTimeFullChargeEnabled, cancellationToken);
            _blade._bladeOneTimeFullChargeEnabled = actual;
            BladeOneTimeFullChargeText = FormatOptionalState(actual);
            BladeOneTimeFullChargeEnabled = actual;
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladeOneTimeFullChargeEnabled = _blade._bladeOneTimeFullChargeEnabled ?? false);
    }

    public async Task ApplyViperPollingRateAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperPollingRate)
        {
            return;
        }

        var hertz = ViperPollingRateIndex switch
        {
            0 => 125,
            1 => 500,
            2 => 1000,
            _ => 0,
        };
        await RunDeviceOperationAsync("鼠标轮询率", async () =>
        {
            var actual = await _deviceTelemetryReader.SetViperPollingRateAsync(_deviceDescriptors, hertz, cancellationToken);
            ViperPollingRateText = $"{actual} Hz";
            ViperPollingRateIndex = actual switch { 125 => 0, 500 => 1, _ => 2 };
            _viper._confirmedViperPollingRateIndex = ViperPollingRateIndex;
            _profile.Global.Viper.PollingRateHertz = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () => ViperPollingRateIndex = _viper._confirmedViperPollingRateIndex);
    }

    public async Task ApplyViperDpiAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperDpi)
        {
            return;
        }

        await RunDeviceOperationAsync("鼠标 DPI", async () =>
        {
            var x = checked((int)Math.Round(ViperDpiXValue, MidpointRounding.AwayFromZero));
            var y = checked((int)Math.Round(ViperDpiYValue, MidpointRounding.AwayFromZero));
            var actual = await _deviceTelemetryReader.SetViperDpiAsync(_deviceDescriptors, x, y, cancellationToken);
            ViperDpiXValue = actual.X;
            ViperDpiYValue = actual.Y;
            _viper._confirmedViperDpiXValue = actual.X;
            _viper._confirmedViperDpiYValue = actual.Y;
            ViperDpiText = $"{actual.X} × {actual.Y}";
            _profile.Global.Viper.DpiX = actual.X;
            _profile.Global.Viper.DpiY = actual.Y;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
        {
            ViperDpiXValue = _viper._confirmedViperDpiXValue;
            ViperDpiYValue = _viper._confirmedViperDpiYValue;
        });
    }

    public async Task ApplyViperIdleAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperIdle)
        {
            return;
        }

        await RunDeviceOperationAsync("鼠标休眠", async () =>
        {
            var minutes = checked((int)Math.Round(ViperIdleMinutesValue, MidpointRounding.AwayFromZero));
            var seconds = checked(minutes * 60);
            var actual = await _deviceTelemetryReader.SetViperIdleSecondsAsync(_deviceDescriptors, seconds, cancellationToken);
            ViperIdleMinutesValue = actual / 60d;
            _viper._confirmedViperIdleMinutesValue = ViperIdleMinutesValue;
            ViperIdleText = FormatDuration(actual);
            _profile.Global.Viper.IdleSeconds = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () => ViperIdleMinutesValue = _viper._confirmedViperIdleMinutesValue);
    }

    public async Task ApplyViperDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperDpiStages || ViperDpiStages.Count is < 1 or > 5)
        {
            return;
        }

        await RunDeviceOperationAsync("鼠标 DPI 档位", async () =>
        {
            var previousProfile = _profile.Clone();
            var requested = new ViperDpiStagesTelemetry(
                checked((byte)ViperActiveDpiStage),
                ViperDpiStages.Select(row => new ViperDpiStageTelemetry(
                    checked((byte)row.Number), checked((int)row.X), checked((int)row.Y))).ToArray());
            var actual = await _deviceTelemetryReader.SetViperDpiStagesAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetViperDpiStages(actual);
            _profile.Global.Viper.DpiStages = new ViperDpiStagesProfile
            {
                ActiveStage = actual.ActiveStage,
                Stages = actual.Stages.Select(stage => new ViperDpiStageProfile
                {
                    Number = stage.Number,
                    X = stage.X,
                    Y = stage.Y,
                }).ToList(),
            };
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile = previousProfile;
                throw new InvalidOperationException(AppStrings.Get("DPI 档位已写入，但配置保存失败，已恢复内存中的配置。"));
            }
        }, cancellationToken, RestoreViperDpiStages);
    }

    public async Task ReadViperButtonMappingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanReadViperButtonMappings)
        {
            return;
        }

        await RunDeviceOperationAsync("鼠标板载映射", async () =>
        {
            var previousProfileAssignments = _profile.Global.Viper.ButtonAssignments;
            var assignments = await _deviceTelemetryReader.ReadViperButtonAssignmentsAsync(
                _deviceDescriptors, cancellationToken);
            SetViperButtonAssignments(assignments);
            _profile.Global.Viper.ButtonAssignments = assignments
                .Select(ToProfileAssignment)
                .ToList();
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile.Global.Viper.ButtonAssignments = previousProfileAssignments;
                throw new InvalidOperationException(AppStrings.Text(
                    "ViperMappingReadProfileSaveFailed"));
            }
        }, cancellationToken, () =>
        {
            ViperButtonAssignments.Clear();
            OnPropertyChanged(nameof(VisibleViperButtonAssignments));
            _viper._canSetViperButtonMappings = false;
            ViperButtonMappingsText = "读取失败";
            OnPropertyChanged(nameof(CanSetViperButtonMappings));
        });
    }

    public async Task ApplyViperButtonMappingAsync(
        ViperButtonAssignmentRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!CanSetViperButtonMappings || !row.CanApply)
        {
            return;
        }

        await RunDeviceOperationAsync("鼠标板载映射", async () =>
        {
            var previous = row.Assignment;
            var previousProfileAssignments = _profile.Global.Viper.ButtonAssignments;
            var actual = await _deviceTelemetryReader.SetViperButtonAssignmentAsync(
                _deviceDescriptors, row.CreateAssignment(), cancellationToken);
            row.Apply(actual);
            _profile.Global.Viper.ButtonAssignments = ViperButtonAssignments
                .Select(item => ToProfileAssignment(item.Assignment))
                .ToList();
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile.Global.Viper.ButtonAssignments = previousProfileAssignments;
                var restored = await _deviceTelemetryReader.SetViperButtonAssignmentAsync(
                    _deviceDescriptors, previous, CancellationToken.None);
                row.Apply(restored);
                throw new InvalidOperationException(AppStrings.Text(
                    "ViperMappingSaveFailedRestored"));
            }
        }, cancellationToken, row.RestoreSelection);
    }

    public async Task ApplyAllViperButtonMappingsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetViperButtonMappings)
        {
            return;
        }

        if (!ViperButtonAssignments.Any(row => row.CanApply))
        {
            return;
        }

        await RunDeviceOperationAsync("鼠标板载映射", async () =>
        {
            var previous = ViperButtonAssignments.Select(row => row.Assignment).ToArray();
            var previousProfileAssignments = _profile.Global.Viper.ButtonAssignments;
            var requested = ViperButtonAssignments.Select(row => row.CreateAssignment()).ToArray();
            var actual = await _deviceTelemetryReader.SetViperButtonAssignmentsAsync(
                _deviceDescriptors, requested, cancellationToken);
            SetViperButtonAssignments(actual);
            _profile.Global.Viper.ButtonAssignments = actual
                .Select(ToProfileAssignment)
                .ToList();
            if (!await SaveProfileAsync(cancellationToken))
            {
                _profile.Global.Viper.ButtonAssignments = previousProfileAssignments;
                var restored = await _deviceTelemetryReader.SetViperButtonAssignmentsAsync(
                    _deviceDescriptors, previous, CancellationToken.None);
                SetViperButtonAssignments(restored);
                throw new InvalidOperationException(AppStrings.Text(
                    "ViperMappingBatchSaveFailedRestored"));
            }
        }, cancellationToken, () =>
        {
            foreach (var row in ViperButtonAssignments.Where(row => row.CanApply))
            {
                row.RestoreSelection();
            }
        });
    }

    private void SetViperButtonAssignments(IReadOnlyList<ViperButtonAssignment> assignments)
    {
        ViperButtonAssignments.Clear();
        foreach (var assignment in assignments
            .OrderBy(item => item.ButtonId)
            .ThenBy(item => item.Layer))
        {
            ViperButtonAssignments.Add(new(assignment));
        }
        OnPropertyChanged(nameof(VisibleViperButtonAssignments));
        _viper._canSetViperButtonMappings = assignments.Count == 16;
        ViperButtonMappingsText = _viper._canSetViperButtonMappings
            ? AppStrings.Get("Profile 1 · 8 个可映射控制")
            : AppStrings.FormatText("MappingReadIncomplete", assignments.Count);
        OnPropertyChanged(nameof(CanSetViperButtonMappings));
    }

    private static ViperButtonAssignmentProfile ToProfileAssignment(ViperButtonAssignment assignment) => new()
    {
        ProfileId = assignment.ProfileId,
        ButtonId = assignment.ButtonId,
        Layer = assignment.Layer,
        Function = assignment.Function,
        FunctionData = assignment.FunctionData.ToList(),
    };

    private static ViperButtonAssignment ToDeviceAssignment(ViperButtonAssignmentProfile assignment) => new(
        assignment.ProfileId,
        assignment.ButtonId,
        assignment.Layer,
        assignment.Function,
        assignment.FunctionData.ToArray());

    public async Task ApplyInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        if (!_canSetInternalDisplayRefreshRate || _internalDisplayController is null)
        {
            return;
        }

        await RunDeviceOperationAsync("内置屏刷新率", async () =>
        {
            var snapshot = _internalDisplayController.SetRefreshRate(InternalDisplayRefreshRateHertz);
            ApplyInternalDisplaySnapshot(snapshot);
            _profile.Global.Blade.RefreshRateHertz = snapshot.RefreshRateHertz;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
            InternalDisplayRefreshRateHertz = _confirmedInternalDisplayRefreshRateHertz);
    }

    private async Task RunDeviceOperationAsync(
        string label,
        Func<Task> operation,
        CancellationToken cancellationToken,
        Action? restoreSelection = null,
        string successVerb = "应用并读回",
        string failureVerb = "写入")
    {
        if (!await TryEnterOperationAsync(cancellationToken))
        {
            restoreSelection?.Invoke();
            return;
        }
        IsBusy = true;
        SetDeviceOperationError(string.Empty);
        try
        {
            await operation();
            DeviceTelemetryTimeText = AppStrings.FormatText("DeviceOperationSucceeded",
                AppStrings.Get(label),
                AppStrings.Get(successVerb),
                DateTimeOffset.Now);
        }
        catch (OperationCanceledException exception)
        {
            restoreSelection?.Invoke();
            if (!cancellationToken.IsCancellationRequested)
            {
                SetDeviceOperationError(AppStrings.FormatText("DeviceOperationFailed",
                    AppStrings.Get(label),
                    AppStrings.Get(failureVerb),
                    FormatOperationException(exception)));
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or NotSupportedException or ArgumentOutOfRangeException or OverflowException or AggregateException or ObjectDisposedException)
        {
            restoreSelection?.Invoke();
            SetDeviceOperationError(AppStrings.FormatText("DeviceOperationFailed",
                AppStrings.Get(label),
                AppStrings.Get(failureVerb),
                FormatOperationException(exception)));
        }
        finally
        {
            IsBusy = false;
            _deviceOperationGate.Release();
        }
    }

    private async Task<bool> TryEnterOperationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _deviceOperationGate.WaitAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task ObserveBladeLightingRuntimeAsync(Task completion)
    {
        try
        {
            await completion;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or AggregateException or ObjectDisposedException)
        {
            if (_bladeLightingController?.RuntimeCompletion == completion)
            {
                _lightingShadowFingerprint = string.Empty;
                _bladeLightingDevicePath = string.Empty;
                SetDeviceOperationError(AppStrings.FormatText("LightingRuntimeError",
                    FormatOperationException(exception)));
            }
        }
    }

    private static string FormatOperationException(Exception exception)
    {
        var exceptions = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [exception];
        return string.Join(
            "；",
            exceptions
                .Select(error => AppStrings.Get(error.Message))
                .Where(message => !string.IsNullOrWhiteSpace(message)));
    }

    private async Task<BladeGameModeTelemetry> SetBladeGameModeCoreAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            var previousProfileValue = _profile.Global.Blade.GamingModeEnabled;
            var previousEnabled = _blade._bladeGameModeState != 0;
            var actual = await _deviceTelemetryReader.SetBladeGameModeAsync(
                _deviceDescriptors,
                enabled,
                cancellationToken);
            _profile.Global.Blade.GamingModeEnabled = actual.GameMode != 0;
            try
            {
                if (await SaveProfileAsync(cancellationToken))
                {
                    return actual;
                }

                throw new InvalidOperationException(AppStrings.Text("GamingModeProfileSaveFailed"));
            }
            catch
            {
                _profile.Global.Blade.GamingModeEnabled = previousProfileValue;
                await _deviceTelemetryReader.SetBladeGameModeAsync(
                    _deviceDescriptors,
                    previousEnabled,
                    CancellationToken.None);
                throw;
            }
        }
        catch (NotSupportedException)
        {
            _blade._bladeGameModeWriteSupported = false;
            OnPropertyChanged(nameof(CanSetBladeGamingMode));
            OnPropertyChanged(nameof(CanApplyBladeGamingMode));
            throw;
        }
    }

    private void SetBladeControlDevicePath(string? value)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(_bladeControlDevicePath, value))
        {
            return;
        }

        _bladeControlDevicePath = value;
        BladeControlDevicePathChanged?.Invoke(value);
    }

    private async Task RefreshPerformanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _performanceMonitor.SampleAsync(cancellationToken);
            _systemTelemetry.Apply(snapshot);
            _performanceErrorText = snapshot.ErrorMessage ?? string.Empty;
            UpdateErrorText();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            SetPerformanceUnavailable(exception.Message);
        }
    }

    private void ApplyDeviceTelemetry(RazerDeviceTelemetry telemetry)
    {
        _lastDeviceTelemetry = telemetry;
        if (telemetry.BladeKeyboardBrightness is byte brightness)
        {
            SetBladeBrightness(brightness);
            CanSetBladeBrightness = true;
        }
        if (telemetry.BladePerformanceMode is BladePerformanceMode performanceMode)
        {
            SetBladePerformanceMode(performanceMode);
            CanSetBladePerformanceMode = true;
        }
        if (telemetry.BladeCpuBoostMode is BladeCpuBoostMode cpuBoost)
        {
            SetBladeCpuBoost(cpuBoost);
        }
        if (telemetry.BladeGpuBoostMode is BladeGpuBoostMode gpuBoost)
        {
            SetBladeGpuBoost(gpuBoost);
        }
        if (telemetry.BladeMaxFanMode is BladeMaxFanMode maxFan)
        {
            SetBladeMaxFan(maxFan);
        }
        if (telemetry.BladeLogoMode is BladeLogoMode logo)
        {
            SetBladeLogo(logo);
            CanSetBladeLogo = true;
        }
        if (telemetry.BladeFanMode is BladeFanMode fanMode)
        {
            BladeFanText = fanMode switch
            {
                BladeFanMode.Automatic when telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.FormatText("AutomaticFanSpeed", cpu, gpu),
                BladeFanMode.Automatic => "自动",
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int target && telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.FormatText("ManualFanCurrentSpeed", target, cpu, gpu),
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int rpm =>
                    AppStrings.FormatText("ManualFanSpeed", rpm),
                BladeFanMode.Manual => "手动 · -- RPM",
                _ => BladeFanText,
            };
            BladeFanModeText = fanMode switch
            {
                BladeFanMode.Automatic => "自动",
                BladeFanMode.Manual => "手动",
                _ => BladeFanModeText,
            };
            if (fanMode == BladeFanMode.Automatic)
            {
                BladeFanTargetRpmText = "--";
            }
            else if (telemetry.BladeFanTargetRpm is int targetRpm)
            {
                BladeFanTargetRpmText = $"{targetRpm:N0} RPM";
            }
        }
        if (telemetry.BladeCurrentFanCpuRpm is int cpuRpm)
        {
            BladeCurrentFanCpuRpmText = $"{cpuRpm:N0} RPM";
        }
        if (telemetry.BladeCurrentFanGpuRpm is int gpuRpm)
        {
            BladeCurrentFanGpuRpmText = $"{gpuRpm:N0} RPM";
        }
        if (telemetry.BladeAdvancedFanCpuModeRaw is byte cpuFanMode)
        {
            BladeAdvancedFanCpuModeRawText = FormatRawByte(cpuFanMode);
        }
        if (telemetry.BladeAdvancedFanGpuModeRaw is byte gpuFanMode)
        {
            BladeAdvancedFanGpuModeRawText = FormatRawByte(gpuFanMode);
        }
        if (telemetry.BladeGameMode is { } gameMode)
        {
            SetBladeGameMode(gameMode);
        }
        else if (_deviceDescriptors.FirstOrDefault(device =>
                     device.ProtocolFamily == DeviceProtocolFamilies.Blade &&
                     device.Access == DeviceAccessState.Available) is { } blade)
        {
            var enabled = ProfileResolver
                .Resolve(_profile, blade, _powerSourceProvider.IsPluggedIn)
                .Blade.GamingModeEnabled == true;
            SetBladeGameMode(new(enabled ? (byte)1 : (byte)0, 0, 0));
        }
        if (telemetry.BladeStartupAnimationEnabled is bool startupAnimationEnabled)
        {
            _blade._bladeStartupAnimationEnabled = startupAnimationEnabled;
            BladeStartupAnimationEnabled = startupAnimationEnabled;
            BladeStartupAnimationText = FormatOptionalState(startupAnimationEnabled);
            OnPropertyChanged(nameof(CanSetBladeStartupAnimation));
            OnPropertyChanged(nameof(CanApplyBladeStartupAnimation));
        }
        if (telemetry.BladeNativeDisplayMode is BladeNativeDisplayMode nativeDisplayMode)
        {
            BladeNativeDisplayModeText = nativeDisplayMode == BladeNativeDisplayMode.Uhd ? "UHD" : "FHD";
        }
        if (telemetry.BladeSkuHardwareConfiguration is { } sku)
        {
            BladeSkuHardwareText =
                $"0x{sku.Raw:X2} · DDS {FormatState(sku.Dds)} · MiniLED {FormatState(sku.MiniLedResolution)} · Battery {FormatState(sku.IllegalBatterySupport)}";
            if (!sku.MiniLedResolution)
            {
                BladeLocalDimmingText = "不适用（非 MiniLED）";
            }
            else if (telemetry.BladeLocalDimmingEnabled is bool localDimmingEnabled)
            {
                BladeLocalDimmingText = FormatOptionalState(localDimmingEnabled);
            }
        }
        if (telemetry.BladeOneTimeFullChargeEnabled is bool oneTimeFullChargeEnabled)
        {
            _blade._bladeOneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
            BladeOneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
            BladeOneTimeFullChargeText = FormatOptionalState(oneTimeFullChargeEnabled);
            OnPropertyChanged(nameof(CanSetBladeOneTimeFullCharge));
            OnPropertyChanged(nameof(CanApplyBladeOneTimeFullCharge));
        }
        if (telemetry.BladeChargeLimitPercent is int chargeLimit)
        {
            SetBladeChargeLimit(chargeLimit);
            CanSetBladeChargeLimit = true;
        }
        if (_deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Blade && device.Access == DeviceAccessState.Available))
        {
            if (_touchpadController?.GetEnabled() is bool touchpadEnabled)
            {
                BladeTouchpadEnabled = touchpadEnabled;
                _blade._confirmedBladeTouchpadEnabled = touchpadEnabled;
                BladeTouchpadText = touchpadEnabled ? "已启用" : "已禁用";
                _blade._canSetBladeTouchpad = true;
                OnPropertyChanged(nameof(CanSetBladeTouchpad));
            }
            else
            {
                BladeTouchpadText = "不可用";
                _blade._canSetBladeTouchpad = false;
                OnPropertyChanged(nameof(CanSetBladeTouchpad));
            }

            BladeStatusText = telemetry.BladeKeyboardBrightness is not null ||
                              telemetry.BladePerformanceMode is not null ||
                              telemetry.BladeChargeLimitPercent is not null
                ? "已连接 · 已读取可用控制"
                : "已连接 · 硬件查询失败";
        }
        if (telemetry.ViperBatteryPercent is int battery)
        {
            ViperBatteryText = $"{battery}%";
        }
        if (telemetry.ViperPollingRateHertz is int pollingRate)
        {
            ViperPollingRateText = $"{pollingRate} Hz";
            ViperPollingRateIndex = pollingRate switch { 125 => 0, 500 => 1, _ => 2 };
            _viper._confirmedViperPollingRateIndex = ViperPollingRateIndex;
            CanSetViperPollingRate = true;
        }
        if (telemetry.ViperDpiX is int dpiX && telemetry.ViperDpiY is int dpiY)
        {
            ViperDpiText = $"{dpiX} × {dpiY}";
            ViperDpiXValue = dpiX;
            ViperDpiYValue = dpiY;
            _viper._confirmedViperDpiXValue = dpiX;
            _viper._confirmedViperDpiYValue = dpiY;
            CanSetViperDpi = true;
        }
        if (telemetry.ViperIdleSeconds is int idleSeconds)
        {
            ViperIdleText = FormatDuration(idleSeconds);
            ViperIdleMinutesValue = idleSeconds / 60d;
            _viper._confirmedViperIdleMinutesValue = ViperIdleMinutesValue;
            CanSetViperIdle = true;
        }
        if (telemetry.ViperDpiStages is { } stages)
        {
            SetViperDpiStages(stages);
            CanSetViperDpiStages = true;
        }
        if (telemetry.ViperLowBatteryThresholdRaw is byte raw)
        {
            ViperLowBatteryThresholdText = ViperLowBatteryThresholdProtocol.Format(raw);
        }
        if (_deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper && device.Access == DeviceAccessState.Available))
        {
            _viper._canReadViperButtonMappings = true;
            OnPropertyChanged(nameof(CanReadViperButtonMappings));
            ViperStatusText = telemetry.ViperBatteryPercent is not null ||
                              telemetry.ViperPollingRateHertz is not null ||
                              telemetry.ViperDpiX is not null ||
                              telemetry.ViperIdleSeconds is not null
                ? "已连接 · 协议可用"
                : "已连接 · 查询失败";
        }

        DeviceTelemetryTimeText = AppStrings.FormatText("HardwareQueryTime",
            telemetry.CapturedAt.ToLocalTime());
    }

    private void RefreshInternalDisplay(bool? powerState, bool applyProfile)
    {
        if (_internalDisplayController is null)
        {
            return;
        }

        try
        {
            var snapshot = _internalDisplayController.Read();
            if (applyProfile)
            {
                var blade = _deviceDescriptors.FirstOrDefault(
                    device => device.ProtocolFamily == DeviceProtocolFamilies.Blade) ?? new DeviceDescriptor(
                        "internal-display",
                        "Windows internal display",
                        0,
                        0,
                        DeviceAccessState.Available,
                        DeviceCapabilityState.Unsupported,
                        0,
                        0,
                        0,
                        DeviceProtocolFamilies.Blade);
                var requested = ProfileResolver.Resolve(_profile, blade, powerState).Blade.RefreshRateHertz;
                if (requested is int hertz && hertz != snapshot.RefreshRateHertz)
                {
                    if (!snapshot.SupportedRefreshRates.Contains(hertz))
                    {
                        throw new InvalidOperationException(AppStrings.FormatText("UnsupportedDisplayRefreshRate",
                            hertz,
                            snapshot.Width,
                            snapshot.Height));
                    }

                    snapshot = _internalDisplayController.SetRefreshRate(hertz);
                }
            }

            ApplyInternalDisplaySnapshot(snapshot);
            SetDisplayError(string.Empty);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentOutOfRangeException)
        {
            SetInternalDisplayUnavailable();
            SetDisplayError(AppStrings.FormatText("DisplayRefreshRateError", exception.Message));
        }
    }

    private void ApplyInternalDisplaySnapshot(InternalDisplaySnapshot snapshot)
    {
        InternalDisplayResolutionText = $"{snapshot.Width} x {snapshot.Height}";
        InternalDisplayRefreshRateText = $"{snapshot.RefreshRateHertz} Hz";
        InternalDisplayRefreshRates = snapshot.SupportedRefreshRates;
        InternalDisplayRefreshRateHertz = snapshot.RefreshRateHertz;
        _confirmedInternalDisplayRefreshRateHertz = snapshot.RefreshRateHertz;
        CanSetInternalDisplayRefreshRate = snapshot.CanSetRefreshRate;
    }

    private void SetInternalDisplayUnavailable()
    {
        CanSetInternalDisplayRefreshRate = false;
    }

    private void SetBladeBrightness(byte brightness)
    {
        var percent = Math.Round(brightness * 100d / 255, MidpointRounding.AwayFromZero);
        BladeBrightnessText = $"{percent:0}%";
        lock (_bladeBrightnessGate)
        {
            if (_desiredBladeBrightness is null)
            {
                BladeBrightnessPercent = percent;
            }
        }
        _blade._confirmedBladeBrightnessPercent = percent;
    }

    private void SetBladePerformanceMode(BladePerformanceMode mode)
    {
        _blade.SetPerformanceMode(mode);
        OnPropertyChanged(nameof(BladePerformanceModeText));
        OnPropertyChanged(nameof(BladePerformanceModeIndex));
        OnPropertyChanged(nameof(BladeCpuBoostText));
        OnPropertyChanged(nameof(BladeCpuBoostIndex));
        OnPropertyChanged(nameof(BladeGpuBoostText));
        OnPropertyChanged(nameof(BladeGpuBoostIndex));
        OnPropertyChanged(nameof(BladeMaxFanText));
        OnPropertyChanged(nameof(BladeMaxFanEnabled));
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
        OnPropertyChanged(nameof(BladeCustomPerformanceVisibility));
    }

    private void SetBladeGameMode(BladeGameModeTelemetry? gameMode)
    {
        _blade.SetGameMode(gameMode);
        OnPropertyChanged(nameof(BladeGameModeText));
        OnPropertyChanged(nameof(BladeGameModeEnabled));
        OnPropertyChanged(nameof(CanSetBladeGamingMode));
        OnPropertyChanged(nameof(CanApplyBladeGamingMode));
    }

    private void SetBladeChargeLimit(int percent)
    {
        _blade.SetChargeLimit(percent);
        OnPropertyChanged(nameof(BladeChargeLimitText));
        OnPropertyChanged(nameof(BladeChargeLimitIndex));
        OnPropertyChanged(nameof(CanSetBladeOneTimeFullCharge));
        OnPropertyChanged(nameof(CanApplyBladeOneTimeFullCharge));
    }

    private bool IsBladeCustomMode => _blade.IsCustomMode;

    private void ClearBladeCustomPerformance()
    {
        _blade.ClearCustomPerformance();
        OnPropertyChanged(nameof(BladeCpuBoostText));
        OnPropertyChanged(nameof(BladeCpuBoostIndex));
        OnPropertyChanged(nameof(BladeGpuBoostText));
        OnPropertyChanged(nameof(BladeGpuBoostIndex));
        OnPropertyChanged(nameof(BladeMaxFanText));
        OnPropertyChanged(nameof(BladeMaxFanEnabled));
    }

    private void SetBladeCpuBoost(BladeCpuBoostMode mode)
    {
        _blade.SetCpuBoost(mode);
        OnPropertyChanged(nameof(BladeCpuBoostText));
        OnPropertyChanged(nameof(BladeCpuBoostIndex));
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
    }

    private void SetBladeGpuBoost(BladeGpuBoostMode mode)
    {
        _blade.SetGpuBoost(mode);
        OnPropertyChanged(nameof(BladeGpuBoostText));
        OnPropertyChanged(nameof(BladeGpuBoostIndex));
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
    }

    private void SetBladeMaxFan(BladeMaxFanMode mode)
    {
        _blade.SetMaxFan(mode);
        OnPropertyChanged(nameof(BladeMaxFanText));
        OnPropertyChanged(nameof(BladeMaxFanEnabled));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
    }

    private void SetBladeLogo(BladeLogoMode mode)
    {
        _blade.SetLogo(mode);
        OnPropertyChanged(nameof(BladeLogoText));
        OnPropertyChanged(nameof(BladeLogoIndex));
    }

    private void SetViperDpiStages(ViperDpiStagesTelemetry stages, bool confirm = true)
    {
        _viper.SetDpiStages(stages, confirm);
        OnPropertyChanged(nameof(ViperDpiStageCount));
        OnPropertyChanged(nameof(ViperActiveDpiStage));
        OnPropertyChanged(nameof(ViperDpiStagesText));
    }

    private void ResizeViperDpiStages(int count)
    {
        _viper.ResizeDpiStages(count);
        OnPropertyChanged(nameof(ViperDpiStageCount));
        OnPropertyChanged(nameof(ViperActiveDpiStage));
    }

    private void RestoreViperDpiStages()
    {
        _viper.RestoreDpiStages();
        OnPropertyChanged(nameof(ViperDpiStageCount));
        OnPropertyChanged(nameof(ViperActiveDpiStage));
        OnPropertyChanged(nameof(ViperDpiStagesText));
    }

    private void ResetDeviceTelemetry()
    {
        BladeStatusText = "探测中";
        BladeBrightnessText = "--";
        BladeBrightnessPercent = 0;
        _blade._confirmedBladeBrightnessPercent = 0;
        BladeBrightnessSelectionText = "--";
        CanSetBladeBrightness = false;
        BladePerformanceModeText = "--";
        BladePerformanceModeIndex = -1;
        _blade._confirmedBladePerformanceModeIndex = -1;
        OnPropertyChanged(nameof(BladeCustomPerformanceVisibility));
        CanSetBladePerformanceMode = false;
        BladeFanText = "--";
        BladeFanModeText = "--";
        BladeFanTargetRpmText = "--";
        BladeCurrentFanCpuRpmText = "--";
        BladeCurrentFanGpuRpmText = "--";
        BladeAdvancedFanCpuModeRawText = "--";
        BladeAdvancedFanGpuModeRawText = "--";
        SetBladeGameMode(null);
        BladeStartupAnimationText = "--";
        _blade._bladeStartupAnimationEnabled = null;
        BladeStartupAnimationEnabled = false;
        BladeNativeDisplayModeText = "--";
        BladeSkuHardwareText = "--";
        BladeLocalDimmingText = "--";
        _blade._bladeOneTimeFullChargeEnabled = null;
        BladeOneTimeFullChargeEnabled = false;
        BladeOneTimeFullChargeText = "--";
        BladeChargeLimitText = "--";
        BladeChargeLimitIndex = -1;
        _blade._confirmedBladeChargeLimitIndex = -1;
        CanSetBladeChargeLimit = false;
        ClearBladeCustomPerformance();
        BladeLogoText = "--";
        BladeLogoIndex = -1;
        _blade._confirmedBladeLogoIndex = -1;
        CanSetBladeLogo = false;
        BladeTouchpadText = "--";
        BladeTouchpadEnabled = false;
        _blade._confirmedBladeTouchpadEnabled = false;
        _blade._canSetBladeTouchpad = false;
        OnPropertyChanged(nameof(CanSetBladeTouchpad));
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
        ResetViperTelemetry();
        DeviceTelemetryTimeText = "正在查询硬件";
    }

    private void ResetViperTelemetry()
    {
        _viper.Reset();
        foreach (var propertyName in new[]
        {
            nameof(ViperStatusText), nameof(ViperDpiStagesText), nameof(ViperLowBatteryThresholdText),
            nameof(ViperBatteryText), nameof(ViperPollingRateText), nameof(ViperPollingRateIndex),
            nameof(CanSetViperPollingRate), nameof(ViperDpiText), nameof(ViperDpiXValue),
            nameof(ViperDpiYValue), nameof(CanSetViperDpi), nameof(ViperIdleText),
            nameof(ViperIdleMinutesValue), nameof(CanSetViperIdle), nameof(ViperDpiStageCount),
            nameof(ViperActiveDpiStage), nameof(CanSetViperDpiStages), nameof(VisibleViperButtonAssignments),
            nameof(ViperButtonMappingsText), nameof(CanReadViperButtonMappings), nameof(CanSetViperButtonMappings),
        })
        {
            OnPropertyChanged(propertyName);
        }
    }

    private static string FormatDeviceStatus(DeviceDescriptor? device) => device switch
    {
        null => "未发现",
        { Access: DeviceAccessState.Available, Capability: DeviceCapabilityState.PendingValidation } => "已发现 · Feature 接口可打开",
        _ => "已发现 · 接口不可访问",
    };

    private static string FormatDuration(int seconds) => seconds switch
    {
        < 60 => AppStrings.FormatText("DurationSeconds", seconds),
        _ when seconds % 60 == 0 => AppStrings.FormatText("DurationMinutes", seconds / 60),
        _ => AppStrings.FormatText("DurationMinutesSeconds", seconds / 60, seconds % 60),
    };

    private static string FormatOptionalState(bool? value) => value switch
    {
        true => AppStrings.Get("已启用"),
        false => AppStrings.Get("已禁用"),
        null => "--",
    };

    private static string FormatState(bool value) => AppStrings.Get(value ? "是" : "否");

    private void UpdateErrorText()
    {
        ErrorText = string.Join(
            Environment.NewLine,
            new[] { DeviceErrorText, _displayErrorText, _performanceErrorText }.Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    private void SetDisplayError(string error)
    {
        LogChangedError("display", _displayErrorText, error);
        _displayErrorText = error;
        UpdateErrorText();
    }

    private void SetDeviceQueryError(string error)
    {
        LogChangedError("device-query", _deviceQueryErrorText, error);
        _deviceQueryErrorText = error;
        UpdateDeviceErrorText();
    }

    private void SetDeviceOperationError(string error)
    {
        LogChangedError("device-operation", _deviceOperationErrorText, error);
        _deviceOperationErrorText = error;
        UpdateDeviceErrorText();
    }

    private void UpdateDeviceErrorText()
    {
        DeviceErrorText = string.Join(
            Environment.NewLine,
            new[] { _deviceQueryErrorText, _deviceOperationErrorText }.Where(error => !string.IsNullOrWhiteSpace(error)));
    }

    public void ReportApplicationError(string message)
    {
        LogChangedError("application", _performanceErrorText, message);
        _performanceErrorText = message;
        UpdateErrorText();
    }

    private void SetPerformanceUnavailable(string error)
    {
        _systemTelemetry.MarkUnavailable();
        var message = AppStrings.FormatText("PerformanceSamplingError", error);
        LogChangedError("performance", _performanceErrorText, message);
        _performanceErrorText = message;
        UpdateErrorText();
    }

    private void LogChangedError(string source, string previous, string current)
    {
        if (!string.IsNullOrWhiteSpace(current) && !StringComparer.Ordinal.Equals(previous, current))
        {
            _diagnosticLog.TryWrite(source, current);
        }
    }

    private void RebuildDiagnostics(
        DeviceSnapshot snapshot,
        RazerDeviceTelemetry telemetry,
        IReadOnlyList<string> errors)
    {
        Diagnostics.Clear();
        var bladeName = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Blade)?.Name
            ?? "Razer Blade";
        var viperName = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper)?.Name
            ?? "Razer Viper";
        foreach (var device in snapshot.Devices)
        {
            var row = new DeviceRowViewModel(device, telemetry);
            Diagnostics.Add(new DiagnosticRowViewModel(
                row.Name,
                "设备通道",
                row.Capability,
                $"{row.Access} · {row.ReportInfo}",
                row.StatusBrush));
        }

        foreach (var error in errors)
        {
            var separator = error.IndexOf('：');
            var capability = separator > 0 ? error[..separator] : "设备发现";
            var detail = separator > 0 ? error[(separator + 1)..] : error;
            var device = capability.StartsWith("鼠标", StringComparison.Ordinal)
                ? viperName
                : capability is "设备发现"
                    ? "Windows HID"
                    : bladeName;
            Diagnostics.Add(new DiagnosticRowViewModel(
                device,
                capability,
                "查询失败",
                detail,
                new SolidColorBrush(Color.FromArgb(255, 255, 107, 107))));
        }

        foreach (var error in _startupDiagnostics)
        {
            var separator = error.IndexOf('：');
            var source = separator > 0 ? error[..separator] : "外部配置";
            var detail = separator > 0 ? error[(separator + 1)..] : error;
            Diagnostics.Add(new DiagnosticRowViewModel(
                "外部 manifest",
                source,
                "加载失败",
                detail,
                new SolidColorBrush(Color.FromArgb(255, 255, 107, 107))));
        }

        if (Diagnostics.Count == 0)
        {
            Diagnostics.Add(new DiagnosticRowViewModel(
                "Windows HID",
                "设备发现",
                "未发现目标设备",
                "连接受支持的 Blade 或 Viper 设备后重新探测。",
                new SolidColorBrush(Color.FromArgb(255, 255, 181, 71))));
        }
    }

    private static string CreateDeviceFingerprint(DeviceSnapshot snapshot) =>
        string.Join(
            "|",
            snapshot.Devices
                .OrderBy(device => device.ProductId)
                .ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
                .Select(device => $"{device.ProductId:X4}:{device.Id}:{device.Access}:{device.Capability}")) +
        $"|{snapshot.ErrorMessage}";

    private static bool IsExpectedRuntimeException(Exception exception) =>
        exception is Win32Exception or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException;

    private static bool IsExpectedFanException(Exception exception) =>
        IsExpectedRuntimeException(exception) ||
        exception is ArgumentException or AggregateException or ObjectDisposedException;

    private static string FormatRawByte(byte? value) => value is byte raw ? $"0x{raw:X2} ({raw})" : AppStrings.Get("未知");

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private readonly record struct BladeFanProfileApplyResult(string? Error, bool Changed);
}
