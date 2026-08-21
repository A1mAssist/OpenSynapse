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

namespace OpenSynapse.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly BladePerformanceMode[] BladePerformanceModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Hyperboost,
    ];
    private static readonly int[] BladeChargeLimits = [50, 55, 60, 65, 70, 75, 80, 100];
    private static readonly BladeCpuBoostMode[] BladeCpuBoostModes =
        [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High, BladeCpuBoostMode.Boost, BladeCpuBoostMode.Undervolt];
    private static readonly BladeGpuBoostMode[] BladeGpuBoostModes =
        [BladeGpuBoostMode.Low, BladeGpuBoostMode.Medium, BladeGpuBoostMode.High];
    private static readonly BladeLogoMode[] BladeLogoModes =
        [BladeLogoMode.Off, BladeLogoMode.Static, BladeLogoMode.Breathing];
    private static readonly BladeLightingMode[] BladeLightingModes =
        [
            BladeLightingMode.Off,
            BladeLightingMode.Static,
            BladeLightingMode.Breathing,
            BladeLightingMode.Spectrum,
            BladeLightingMode.Wave,
            BladeLightingMode.Fire,
            BladeLightingMode.Reactive,
            BladeLightingMode.Ripple,
            BladeLightingMode.AudioMeter,
            BladeLightingMode.Ambient,
            BladeLightingMode.Wheel,
            BladeLightingMode.Starlight,
            BladeLightingMode.Tidal,
        ];
    private static readonly BladeWaveDirection[] BladeWaveDirections =
        [BladeWaveDirection.Right, BladeWaveDirection.Left];

    private readonly IDeviceDiscovery _discovery;
    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly ProfileStore _profileStore;
    private readonly IPowerSourceProvider _powerSourceProvider;
    private readonly IActiveApplicationProvider _activeApplicationProvider;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly IInternalDisplayController? _internalDisplayController;
    private readonly IBladeLightingController? _bladeLightingController;
    private readonly WindowsStartupManager? _startupManager;
    private readonly WindowsTouchpadController? _touchpadController;
    private readonly string? _executablePath;
    private readonly IReadOnlyList<string> _startupDiagnostics;
    private readonly VerifiedProfileApplier _profileApplier = new();
    private readonly BladeFanCurveRuntime _bladeFanRuntime;
    private readonly SemaphoreSlim _deviceOperationGate = new(1, 1);
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
    private string _telemetryTimeText = "等待采样";
    private string _errorText = string.Empty;
    private bool _isBusy;
    private string _bladeDeviceName = "Razer Blade 16 2025";
    private string _bladeStatusText = "未发现";
    private string _bladeBrightnessText = "--";
    private string _bladeBrightnessSelectionText = "--";
    private double _bladeBrightnessPercent;
    private double _confirmedBladeBrightnessPercent;
    private bool _canSetBladeBrightness;
    private string _bladePerformanceModeText = "--";
    private int _bladePerformanceModeIndex = -1;
    private int _confirmedBladePerformanceModeIndex = -1;
    private bool _canSetBladePerformanceMode;
    private string _bladeFanText = "--";
    private string _bladeFanModeText = "--";
    private string _bladeFanTargetRpmText = "--";
    private string _bladeCurrentFanCpuRpmText = "--";
    private string _bladeCurrentFanGpuRpmText = "--";
    private string _bladeAdvancedFanCpuModeRawText = "--";
    private string _bladeAdvancedFanGpuModeRawText = "--";
    private string _bladeGameModeText = "--";
    private byte? _bladeGameModeState;
    private bool _bladeGameModeWriteSupported = true;
    private bool _bladeGameModeEnabled;
    private string _bladeStartupAnimationText = "--";
    private bool? _bladeStartupAnimationEnabled;
    private bool _bladeStartupAnimationSelection;
    private string _bladeNativeDisplayModeText = "--";
    private string _bladeSkuHardwareText = "--";
    private string _bladeLocalDimmingText = "--";
    private string _bladeOneTimeFullChargeText = "--";
    private bool? _bladeOneTimeFullChargeEnabled;
    private bool _bladeOneTimeFullChargeSelection;
    private string _bladeChargeLimitText = "--";
    private int _bladeChargeLimitIndex = -1;
    private int _confirmedBladeChargeLimitIndex = -1;
    private bool _canSetBladeChargeLimit;
    private string _bladeCpuBoostText = "--";
    private int _bladeCpuBoostIndex = -1;
    private int _confirmedBladeCpuBoostIndex = -1;
    private bool _hasBladeCpuBoost;
    private string _bladeGpuBoostText = "--";
    private int _bladeGpuBoostIndex = -1;
    private int _confirmedBladeGpuBoostIndex = -1;
    private bool _hasBladeGpuBoost;
    private string _bladeMaxFanText = "--";
    private bool _bladeMaxFanEnabled;
    private bool _confirmedBladeMaxFanEnabled;
    private bool _hasBladeMaxFan;
    private string _bladeLogoText = "--";
    private int _bladeLogoIndex = -1;
    private int _confirmedBladeLogoIndex = -1;
    private bool _canSetBladeLogo;
    private string _bladeTouchpadText = "--";
    private bool _bladeTouchpadEnabled;
    private bool _confirmedBladeTouchpadEnabled;
    private bool _canSetBladeTouchpad;
    private int _bladeLightingModeIndex = 1;
    private int _bladeWaveDirectionIndex;
    private Color _bladeLightingColor = Color.FromArgb(0xFF, 0x99, 0xDD, 0x72);
    private Color _bladeLightingSecondColor = Color.FromArgb(0xFF, 0x00, 0x66, 0xFF);
    private IReadOnlyList<DeviceDescriptor> _deviceDescriptors = Array.Empty<DeviceDescriptor>();
    private RazerDeviceTelemetry? _lastDeviceTelemetry;
    private string _viperDeviceName = "Razer Viper V3 HyperSpeed";
    private string _viperStatusText = "未发现";
    private string _viperBatteryText = "--";
    private string _viperPollingRateText = "--";
    private int _viperPollingRateIndex = -1;
    private int _confirmedViperPollingRateIndex = -1;
    private bool _canSetViperPollingRate;
    private string _viperDpiText = "--";
    private double _viperDpiXValue;
    private double _viperDpiYValue;
    private double _confirmedViperDpiXValue;
    private double _confirmedViperDpiYValue;
    private bool _canSetViperDpi;
    private string _viperIdleText = "--";
    private string _viperDpiStagesText = "--";
    private string _viperLowBatteryThresholdText = "--";
    private double _viperIdleMinutesValue;
    private double _confirmedViperIdleMinutesValue;
    private bool _canSetViperIdle;
    private int _viperDpiStageCount;
    private int _viperActiveDpiStage;
    private bool _canSetViperDpiStages;
    private ViperDpiStagesTelemetry? _confirmedViperDpiStages;
    private string _viperButtonMappingsText = "未读取";
    private int _viperButtonMappingLayerIndex;
    private bool _canReadViperButtonMappings;
    private bool _canSetViperButtonMappings;
    private string _deviceFingerprint = string.Empty;
    private string _lightingShadowFingerprint = string.Empty;
    private string _bladeLightingDevicePath = string.Empty;
    private string _bladeFanControlFingerprint = string.Empty;
    private Task? _bladeFanControlCompletion;
    private string? _bladeControlDevicePath;
    private DateTimeOffset _nextFullDeviceRefresh = DateTimeOffset.MinValue;
    private int _deviceRefreshRequested;
    private string _cpuName = "CPU";
    private string _cpuValue = "--";
    private double _cpuPercent;
    private string _cpuTemperatureText = "--";
    private string _cpuPowerText = "--";
    private string _cpuClockText = "--";
    private string _gpuName = "GPU";
    private string _gpuValue = "--";
    private double _gpuPercent;
    private string _gpuTemperatureText = "--";
    private string _gpuPowerText = "--";
    private string _gpuClockText = "--";
    private string _gpuMemoryLabel = "GPU 内存";
    private string _gpuMemoryText = "--";
    private string _memoryValue = "--";
    private string _memoryDetail = "-- / -- GB";
    private double _memoryPercent;
    private string _storageValue = "--";
    private string _storageDetail = "-- / -- GB";
    private double _storagePercent;
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<string?>? BladeControlDevicePathChanged;

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<DiagnosticRowViewModel> Diagnostics { get; } = new();
    public ObservableCollection<string> ProfileNames { get; } = new();
    public ObservableCollection<ApplicationBindingRowViewModel> ApplicationBindings { get; } = new();

    public string LastDeviceRefreshText
    {
        get => AppStrings.Get(_lastDeviceRefreshText);
        private set => SetField(ref _lastDeviceRefreshText, value);
    }

    public string TelemetryTimeText
    {
        get => AppStrings.Get(_telemetryTimeText);
        private set => SetField(ref _telemetryTimeText, value);
    }

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
    public bool IsStartupEnabled { get => _isStartupEnabled; private set => SetField(ref _isStartupEnabled, value); }
    public bool CanSetStartup => _startupManager is not null && !string.IsNullOrWhiteSpace(_executablePath);

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool CanRefresh => true;

    public string BladeDeviceName { get => _bladeDeviceName; private set => SetField(ref _bladeDeviceName, value); }
    public string BladeStatusText { get => AppStrings.Get(_bladeStatusText); private set => SetField(ref _bladeStatusText, value); }
    public string BladeBrightnessText { get => _bladeBrightnessText; private set => SetField(ref _bladeBrightnessText, value); }
    public string BladeBrightnessSelectionText { get => _bladeBrightnessSelectionText; private set => SetField(ref _bladeBrightnessSelectionText, value); }
    public double BladeBrightnessPercent
    {
        get => _bladeBrightnessPercent;
        set
        {
            if (SetField(ref _bladeBrightnessPercent, Math.Clamp(value, 0, 100)))
            {
                BladeBrightnessSelectionText = $"{_bladeBrightnessPercent:0}%";
            }
        }
    }
    public bool CanSetBladeBrightness
    {
        get => _canSetBladeBrightness;
        private set
        {
            if (SetField(ref _canSetBladeBrightness, value))
            {
                OnPropertyChanged(nameof(CanSetBladeLighting));
            }
        }
    }
    public string BladePerformanceModeText { get => AppStrings.Get(_bladePerformanceModeText); private set => SetField(ref _bladePerformanceModeText, value); }
    public IReadOnlyList<string> BladePerformanceModeOptions => AppStrings.Get("平衡", "性能", "自定义", "静音", "HyperBoost");
    public int BladePerformanceModeIndex { get => _bladePerformanceModeIndex; set => SetField(ref _bladePerformanceModeIndex, value); }
    public bool CanSetBladePerformanceMode { get => _canSetBladePerformanceMode; private set => SetField(ref _canSetBladePerformanceMode, value); }
    public string BladeFanText { get => AppStrings.Get(_bladeFanText); private set => SetField(ref _bladeFanText, value); }
    public string BladeFanModeText { get => AppStrings.Get(_bladeFanModeText); private set => SetField(ref _bladeFanModeText, value); }
    public string BladeFanTargetRpmText { get => _bladeFanTargetRpmText; private set => SetField(ref _bladeFanTargetRpmText, value); }
    public string BladeCurrentFanCpuRpmText { get => _bladeCurrentFanCpuRpmText; private set => SetField(ref _bladeCurrentFanCpuRpmText, value); }
    public string BladeCurrentFanGpuRpmText { get => _bladeCurrentFanGpuRpmText; private set => SetField(ref _bladeCurrentFanGpuRpmText, value); }
    public string BladeAdvancedFanCpuModeRawText { get => _bladeAdvancedFanCpuModeRawText; private set => SetField(ref _bladeAdvancedFanCpuModeRawText, value); }
    public string BladeAdvancedFanGpuModeRawText { get => _bladeAdvancedFanGpuModeRawText; private set => SetField(ref _bladeAdvancedFanGpuModeRawText, value); }
    public string BladeGameModeText { get => AppStrings.Get(_bladeGameModeText); private set => SetField(ref _bladeGameModeText, value); }
    public bool BladeGameModeEnabled
    {
        get => _bladeGameModeEnabled;
        set
        {
            if (SetField(ref _bladeGameModeEnabled, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeGamingMode));
            }
        }
    }
    public bool CanSetBladeGamingMode =>
        _bladeGameModeWriteSupported && _bladeGameModeState is byte state && state != 2;
    public bool CanApplyBladeGamingMode =>
        CanSetBladeGamingMode && BladeGameModeEnabled != (_bladeGameModeState != 0);
    public string BladeStartupAnimationText { get => AppStrings.Get(_bladeStartupAnimationText); private set => SetField(ref _bladeStartupAnimationText, value); }
    public bool BladeStartupAnimationEnabled
    {
        get => _bladeStartupAnimationSelection;
        set
        {
            if (SetField(ref _bladeStartupAnimationSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeStartupAnimation));
            }
        }
    }
    public bool CanSetBladeStartupAnimation => _bladeStartupAnimationEnabled is not null;
    public bool CanApplyBladeStartupAnimation =>
        CanSetBladeStartupAnimation && BladeStartupAnimationEnabled != _bladeStartupAnimationEnabled;
    public string BladeNativeDisplayModeText { get => _bladeNativeDisplayModeText; private set => SetField(ref _bladeNativeDisplayModeText, value); }
    public string BladeSkuHardwareText { get => _bladeSkuHardwareText; private set => SetField(ref _bladeSkuHardwareText, value); }
    public string BladeLocalDimmingText { get => AppStrings.Get(_bladeLocalDimmingText); private set => SetField(ref _bladeLocalDimmingText, value); }
    public string BladeOneTimeFullChargeText { get => AppStrings.Get(_bladeOneTimeFullChargeText); private set => SetField(ref _bladeOneTimeFullChargeText, value); }
    public bool BladeOneTimeFullChargeEnabled
    {
        get => _bladeOneTimeFullChargeSelection;
        set
        {
            if (SetField(ref _bladeOneTimeFullChargeSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyBladeOneTimeFullCharge));
            }
        }
    }
    public bool CanSetBladeOneTimeFullCharge =>
        _bladeOneTimeFullChargeEnabled is not null && _confirmedBladeChargeLimitIndex >= 0 &&
        BladeChargeLimits[_confirmedBladeChargeLimitIndex] < 100;
    public bool CanApplyBladeOneTimeFullCharge =>
        CanSetBladeOneTimeFullCharge && BladeOneTimeFullChargeEnabled != _bladeOneTimeFullChargeEnabled;
    public string BladeChargeLimitText { get => AppStrings.Get(_bladeChargeLimitText); private set => SetField(ref _bladeChargeLimitText, value); }
    public IReadOnlyList<string> BladeChargeLimitOptions => AppStrings.Get("50%", "55%", "60%", "65%", "70%", "75%", "80%", "关闭限制（100%）");
    public int BladeChargeLimitIndex { get => _bladeChargeLimitIndex; set => SetField(ref _bladeChargeLimitIndex, value); }
    public bool CanSetBladeChargeLimit { get => _canSetBladeChargeLimit; private set => SetField(ref _canSetBladeChargeLimit, value); }
    public IReadOnlyList<string> BladeCpuBoostOptions => AppStrings.Get("低", "中", "高", "Boost", "降压预设");
    public string BladeCpuBoostText { get => AppStrings.Get(_bladeCpuBoostText); private set => SetField(ref _bladeCpuBoostText, value); }
    public int BladeCpuBoostIndex { get => _bladeCpuBoostIndex; set => SetField(ref _bladeCpuBoostIndex, value); }
    public bool CanSetBladeCpuBoost => _hasBladeCpuBoost && IsBladeCustomMode;
    public IReadOnlyList<string> BladeGpuBoostOptions => AppStrings.Get("低", "中", "高");
    public string BladeGpuBoostText { get => AppStrings.Get(_bladeGpuBoostText); private set => SetField(ref _bladeGpuBoostText, value); }
    public int BladeGpuBoostIndex { get => _bladeGpuBoostIndex; set => SetField(ref _bladeGpuBoostIndex, value); }
    public bool CanSetBladeGpuBoost => _hasBladeGpuBoost && IsBladeCustomMode;
    public string BladeMaxFanText { get => AppStrings.Get(_bladeMaxFanText); private set => SetField(ref _bladeMaxFanText, value); }
    public bool BladeMaxFanEnabled { get => _bladeMaxFanEnabled; set => SetField(ref _bladeMaxFanEnabled, value); }
    public bool CanSetBladeMaxFan => _hasBladeMaxFan && IsBladeCustomMode;
    public Visibility BladeCustomPerformanceVisibility => IsBladeCustomMode
        ? Visibility.Visible
        : Visibility.Collapsed;
    public IReadOnlyList<string> BladeLogoOptions => AppStrings.Get("关闭", "常亮", "呼吸");
    public string BladeLogoText { get => AppStrings.Get(_bladeLogoText); private set => SetField(ref _bladeLogoText, value); }
    public int BladeLogoIndex { get => _bladeLogoIndex; set => SetField(ref _bladeLogoIndex, value); }
    public bool CanSetBladeLogo { get => _canSetBladeLogo; private set => SetField(ref _canSetBladeLogo, value); }
    public string BladeTouchpadText { get => AppStrings.Get(_bladeTouchpadText); private set => SetField(ref _bladeTouchpadText, value); }
    public bool BladeTouchpadEnabled { get => _bladeTouchpadEnabled; private set => SetField(ref _bladeTouchpadEnabled, value); }
    public bool CanSetBladeTouchpad => _canSetBladeTouchpad;
    public IReadOnlyList<string> BladeLightingModeOptions => AppStrings.Get(
        "关闭", "静态", "呼吸", "光谱循环", "波浪", "火焰", "响应", "涟漪", "音频律动", "环境感知", "色轮", "星光", "潮汐");
    public int BladeLightingModeIndex
    {
        get => _bladeLightingModeIndex;
        set
        {
            if (SetField(ref _bladeLightingModeIndex, value))
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
    public int BladeWaveDirectionIndex { get => _bladeWaveDirectionIndex; set => SetField(ref _bladeWaveDirectionIndex, value); }
    public Color BladeLightingColor { get => _bladeLightingColor; set => SetField(ref _bladeLightingColor, value); }
    public Color BladeLightingSecondColor { get => _bladeLightingSecondColor; set => SetField(ref _bladeLightingSecondColor, value); }
    public bool CanSetBladeLighting => _canSetBladeBrightness && _bladeLightingController is not null;
    public string ViperDeviceName { get => _viperDeviceName; private set => SetField(ref _viperDeviceName, value); }
    public string ViperStatusText { get => AppStrings.Get(_viperStatusText); private set => SetField(ref _viperStatusText, value); }
    public string ViperBatteryText { get => _viperBatteryText; private set => SetField(ref _viperBatteryText, value); }
    public string ViperPollingRateText { get => _viperPollingRateText; private set => SetField(ref _viperPollingRateText, value); }
    public int ViperPollingRateIndex { get => _viperPollingRateIndex; set => SetField(ref _viperPollingRateIndex, value); }
    public bool CanSetViperPollingRate { get => _canSetViperPollingRate; private set => SetField(ref _canSetViperPollingRate, value); }
    public string ViperDpiText { get => _viperDpiText; private set => SetField(ref _viperDpiText, value); }
    public double ViperDpiXValue { get => _viperDpiXValue; set => SetField(ref _viperDpiXValue, value); }
    public double ViperDpiYValue { get => _viperDpiYValue; set => SetField(ref _viperDpiYValue, value); }
    public bool CanSetViperDpi { get => _canSetViperDpi; private set => SetField(ref _canSetViperDpi, value); }
    public string ViperIdleText { get => AppStrings.Get(_viperIdleText); private set => SetField(ref _viperIdleText, value); }
    public string ViperDpiStagesText { get => AppStrings.Get(_viperDpiStagesText); private set => SetField(ref _viperDpiStagesText, value); }
    public string ViperLowBatteryThresholdText { get => _viperLowBatteryThresholdText; private set => SetField(ref _viperLowBatteryThresholdText, value); }
    public double ViperIdleMinutesValue { get => _viperIdleMinutesValue; set => SetField(ref _viperIdleMinutesValue, value); }
    public bool CanSetViperIdle { get => _canSetViperIdle; private set => SetField(ref _canSetViperIdle, value); }
    public ObservableCollection<ViperDpiStageRowViewModel> ViperDpiStages { get; } = new();
    public int ViperDpiStageCount
    {
        get => _viperDpiStageCount;
        set => ResizeViperDpiStages(Math.Clamp(value, 1, 5));
    }
    public int ViperActiveDpiStage
    {
        get => _viperActiveDpiStage;
        set => SetField(ref _viperActiveDpiStage, Math.Clamp(value, 1, Math.Max(1, ViperDpiStages.Count)));
    }
    public bool CanSetViperDpiStages { get => _canSetViperDpiStages; private set => SetField(ref _canSetViperDpiStages, value); }
    public string ViperButtonMappingsText { get => AppStrings.Get(_viperButtonMappingsText); private set => SetField(ref _viperButtonMappingsText, value); }
    public ObservableCollection<ViperButtonAssignmentRowViewModel> ViperButtonAssignments { get; } = new();
    public IReadOnlyList<string> ViperButtonMappingLayerOptions => AppStrings.Get("普通层", "HyperShift 层");
    public int ViperButtonMappingLayerIndex
    {
        get => _viperButtonMappingLayerIndex;
        set
        {
            if (SetField(ref _viperButtonMappingLayerIndex, Math.Clamp(value, 0, 1)))
            {
                OnPropertyChanged(nameof(VisibleViperButtonAssignments));
            }
        }
    }
    public IReadOnlyList<ViperButtonAssignmentRowViewModel> VisibleViperButtonAssignments =>
        ViperButtonAssignments
            .Where(row => (int)row.Assignment.Layer == ViperButtonMappingLayerIndex)
            .ToArray();
    public bool CanReadViperButtonMappings => _canReadViperButtonMappings;
    public bool CanSetViperButtonMappings => _canSetViperButtonMappings;
    public string InternalDisplayResolutionText { get => _internalDisplayResolutionText; private set => SetField(ref _internalDisplayResolutionText, value); }
    public string InternalDisplayRefreshRateText { get => _internalDisplayRefreshRateText; private set => SetField(ref _internalDisplayRefreshRateText, value); }
    public IReadOnlyList<int> InternalDisplayRefreshRates { get => _internalDisplayRefreshRates; private set => SetField(ref _internalDisplayRefreshRates, value); }
    public int InternalDisplayRefreshRateHertz { get => _internalDisplayRefreshRateHertz; set => SetField(ref _internalDisplayRefreshRateHertz, value); }
    public bool CanSetInternalDisplayRefreshRate { get => _canSetInternalDisplayRefreshRate; private set => SetField(ref _canSetInternalDisplayRefreshRate, value); }

    public string EmptyStateText => Devices.Count == 0
        ? AppStrings.Get("未发现 Blade 16 或 Viper V3 HyperSpeed。")
        : string.Empty;

    public string CpuName { get => _cpuName; private set => SetField(ref _cpuName, value); }
    public string CpuValue { get => _cpuValue; private set => SetField(ref _cpuValue, value); }
    public double CpuPercent { get => _cpuPercent; private set => SetField(ref _cpuPercent, value); }
    public string CpuTemperatureText { get => _cpuTemperatureText; private set => SetField(ref _cpuTemperatureText, value); }
    public string CpuPowerText { get => _cpuPowerText; private set => SetField(ref _cpuPowerText, value); }
    public string CpuClockText { get => _cpuClockText; private set => SetField(ref _cpuClockText, value); }
    public string GpuName { get => _gpuName; private set => SetField(ref _gpuName, value); }
    public string GpuValue { get => _gpuValue; private set => SetField(ref _gpuValue, value); }
    public double GpuPercent { get => _gpuPercent; private set => SetField(ref _gpuPercent, value); }
    public string GpuTemperatureText { get => _gpuTemperatureText; private set => SetField(ref _gpuTemperatureText, value); }
    public string GpuPowerText { get => _gpuPowerText; private set => SetField(ref _gpuPowerText, value); }
    public string GpuClockText { get => _gpuClockText; private set => SetField(ref _gpuClockText, value); }
    public string GpuMemoryLabel { get => AppStrings.Get(_gpuMemoryLabel); private set => SetField(ref _gpuMemoryLabel, value); }
    public string GpuMemoryText { get => _gpuMemoryText; private set => SetField(ref _gpuMemoryText, value); }
    public string MemoryValue { get => _memoryValue; private set => SetField(ref _memoryValue, value); }
    public string MemoryDetail { get => _memoryDetail; private set => SetField(ref _memoryDetail, value); }
    public double MemoryPercent { get => _memoryPercent; private set => SetField(ref _memoryPercent, value); }
    public string StorageValue { get => _storageValue; private set => SetField(ref _storageValue, value); }
    public string StorageDetail { get => _storageDetail; private set => SetField(ref _storageDetail, value); }
    public double StoragePercent { get => _storagePercent; private set => SetField(ref _storagePercent, value); }

    public void RequestDeviceRefresh() => Interlocked.Exchange(ref _deviceRefreshRequested, 1);

    public void RefreshLocalization()
    {
        ProfileStatusText = AppStrings.Format(
            "ProfileLoaded",
            "本地配置已加载 · {0}",
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

        OnPropertyChanged(string.Empty);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadProfileAsync(cancellationToken);
        await RefreshDevicesAsync(cancellationToken);
        if (_bladeGameModeState is byte gameMode)
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
                SetDeviceOperationError(AppStrings.Format(
                    "LabeledError",
                    "{0}：{1}",
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
            RefreshProfileState();
            RefreshStartupState();
            ProfileStatusText = AppStrings.Format("ProfileLoaded", "本地配置已加载 · {0}", ActiveProfileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _profile = ProfileDocument.CreateDefault();
            RefreshProfileState();
            RefreshStartupState();
            ProfileStatusText = AppStrings.Format("DefaultProfileLoaded", "已使用默认本地配置 · {0}", ActiveProfileName);
            ReportApplicationError(AppStrings.Format("ProfileLoadError", "配置加载：{0}", exception.Message));
        }
    }

    public async Task SelectProfileAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            StringComparer.OrdinalIgnoreCase.Equals(name, ActiveProfileName))
        {
            return;
        }

        await RunProfileOperationAsync(AppStrings.Format("SwitchProfile", "切换配置 {0}", name), () =>
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
        await RunProfileOperationAsync(AppStrings.Format("CreateProfile", "新建配置 {0}", name), () =>
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

        await RunProfileOperationAsync(AppStrings.Format("DeleteProfile", "删除配置 {0}", ActiveProfileName), () =>
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
        await RunProfileOperationAsync(AppStrings.Format("CloneProfile", "克隆配置 {0}", name), () =>
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
        await RunProfileOperationAsync(AppStrings.Format("RenameProfile", "重命名配置 {0}", name), () =>
        {
            ProfileCatalog.Rename(_profile, ActiveProfileName, name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task BindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync(AppStrings.Format("BindApplication", "绑定应用 {0}", Path.GetFileName(executablePath)), () =>
        {
            ApplicationProfileBinding.Bind(_profile, executablePath, ActiveProfileName);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);

    public Task UnbindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync(AppStrings.Format("UnbindApplication", "解绑应用 {0}", Path.GetFileName(executablePath)), () =>
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
            SetDeviceOperationError(AppStrings.Format("ProfileImportError", "配置导入：{0}", exception.Message));
            ProfileStatusText = AppStrings.Format("ProfileImportFailed", "配置导入失败：{0}", exception.Message);
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
            ProfileStatusText = AppStrings.Format("ProfileExported", "配置已导出 · {0}", Path.GetFileName(filePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.Format("ProfileExportError", "配置导出：{0}", exception.Message));
            ProfileStatusText = AppStrings.Format("ProfileExportFailed", "配置导出失败：{0}", exception.Message);
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
            _startupManager!.SetEnabled(enabled, _executablePath!);
            IsStartupEnabled = enabled;
            ProfileStatusText = enabled ? "已启用开机启动" : "已关闭开机启动";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            SetDeviceOperationError(AppStrings.Format("StartupError", "开机启动：{0}", exception.Message));
            ProfileStatusText = AppStrings.Format("StartupSettingFailed", "开机启动设置失败：{0}", exception.Message);
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
            RequestDeviceRefresh();
            ProfileStatusText = AppStrings.Format("ProfileOperationSucceeded", "{0} · {1}", label, ActiveProfileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _profile = previous;
            RefreshProfileState();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError(AppStrings.Format("LabeledError", "{0}：{1}", label, exception.Message));
            ProfileStatusText = AppStrings.Format("ProfileOperationFailed", "{0}失败：{1}", label, exception.Message);
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
        ProfileNames.Clear();
        foreach (var name in ProfileCatalog.GetNames(_profile))
        {
            ProfileNames.Add(name);
        }

        ActiveProfileName = _profile.ActiveProfileName;
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
    }

    private void RefreshStartupState()
    {
        try
        {
            IsStartupEnabled = _startupManager is not null &&
                !string.IsNullOrWhiteSpace(_executablePath) &&
                _startupManager.IsEnabled(_executablePath);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            IsStartupEnabled = false;
            SetDeviceOperationError(AppStrings.Format("StartupError", "开机启动：{0}", exception.Message));
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
        if (result.Errors.Count > 0)
        {
            SetDeviceOperationError(AppStrings.Format(
                "ProfileApplyError",
                "配置应用：{0}",
                string.Join("; ", result.Errors)));
        }

        return result;
    }

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
                    return new(AppStrings.Format("InvalidBladeFanMode", "Blade 风扇模式值无效：{0}。", rawMode), Changed: false);
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
                device.ProtocolFamily == "blade-710" && device.Access == DeviceAccessState.Available);
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
                device.ProtocolFamily == "blade-710" && device.Access == DeviceAccessState.Available);
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
            SetDeviceOperationError(AppStrings.Format("ProfileSaveError", "配置保存：{0}", exception.Message));
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
                            ProfileStatusText = AppStrings.Format("ProfileAutoSwitched", "已自动切换 · {0}", ActiveProfileName);
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
                            SetDeviceOperationError(AppStrings.Format(
                                "AutomaticProfileSaveError",
                                "配置自动切换保存：{0}",
                                exception.Message));
                            profileChanged = false;
                        }
                    }
                    var powerChanged = _lastPowerState != powerState;
                    if (!StringComparer.Ordinal.Equals(_deviceFingerprint, CreateDeviceFingerprint(snapshot)) ||
                        powerChanged ||
                        profileChanged ||
                        refreshRequested ||
                        DateTimeOffset.UtcNow >= _nextFullDeviceRefresh)
                    {
                        await RefreshDevicesCoreAsync(
                            snapshot,
                            cancellationToken,
                            applyDisplayProfile: powerChanged || profileChanged);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsExpectedRuntimeException(exception))
                {
                    SetDeviceQueryError(AppStrings.Format("DeviceWatchError", "设备状态监听：{0}", exception.Message));
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

            var blade = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == "blade-710");
            var viper = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == "viper-184");
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
            var bladeProfileBlocked = profileApply?.Errors.Any(error =>
                error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true;
            var fanApply = bladeProfileBlocked
                ? new BladeFanProfileApplyResult(
                    await StopBladeFanControlAsync("profile-error"),
                    Changed: true)
                : await ApplyLoadedFanProfileAsync(blade, powerState, cancellationToken);
            if (fanApply.Changed && blade is { Access: DeviceAccessState.Available })
            {
                telemetry = await _deviceTelemetryReader.ReadAsync(snapshot.Devices, cancellationToken);
                ApplyDeviceTelemetry(telemetry);
            }
            var lightingError = profileApply?.Errors.Any(error =>
                    error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true
                ? null
                : await ApplyLoadedLightingProfileAsync(blade, powerState, cancellationToken);
            Devices.Clear();
            foreach (var device in snapshot.Devices)
            {
                Devices.Add(new DeviceRowViewModel(device, telemetry));
            }

            var errors = telemetry.Errors.ToList();
            if (profileApply is { Errors.Count: > 0 })
            {
                errors.AddRange(profileApply.Errors.Select(error => $"配置应用：{error}"));
            }
            if (!string.IsNullOrWhiteSpace(lightingError))
            {
                errors.Add($"键盘灯效：{lightingError}");
            }
            if (!string.IsNullOrWhiteSpace(fanApply.Error))
            {
                errors.Add($"风扇控制：{fanApply.Error}");
            }
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                errors.Insert(0, snapshot.ErrorMessage);
            }

            RebuildDiagnostics(snapshot, telemetry, errors);
            SetDeviceQueryError(errors.Count == 0
                ? string.Empty
                : AppStrings.Format(
                    "HardwareQueryFailureCount",
                    "{0} 项硬件查询失败，未成功读回的控制已停用。请在“诊断”页查看详情。",
                    errors.Count));
            LastDeviceRefreshText = AppStrings.Format(
                "DeviceScanTime",
                "设备探测 {0:HH:mm:ss}",
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
            var hadBlade = _deviceDescriptors.Any(device => device.ProtocolFamily == "blade-710");
            var hadViper = _deviceDescriptors.Any(device => device.ProtocolFamily == "viper-184");
            _deviceDescriptors = Array.Empty<DeviceDescriptor>();
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
        if (!_canSetBladeBrightness)
        {
            return;
        }

        await RunDeviceOperationAsync(
            "键盘亮度",
            () => ApplyBladeBrightnessCoreAsync(cancellationToken),
            cancellationToken,
            () => BladeBrightnessPercent = _confirmedBladeBrightnessPercent);
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
                    device.ProtocolFamily == "blade-710" &&
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
        if (!_canSetBladePerformanceMode ||
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
        }, cancellationToken, () =>
            BladePerformanceModeIndex = _confirmedBladePerformanceModeIndex);
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
            BladeChargeLimitIndex = _confirmedBladeChargeLimitIndex);
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
        }, cancellationToken, () => BladeCpuBoostIndex = _confirmedBladeCpuBoostIndex);
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
        }, cancellationToken, () => BladeGpuBoostIndex = _confirmedBladeGpuBoostIndex);
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
        }, cancellationToken, () => BladeMaxFanEnabled = _confirmedBladeMaxFanEnabled);
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
        }, cancellationToken, () => BladeLogoIndex = _confirmedBladeLogoIndex);
    }

    public async Task ToggleBladeTouchpadAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync(
            "触控板",
            async () =>
            {
                if (!_canSetBladeTouchpad || _touchpadController is null)
                {
                    throw new InvalidOperationException(AppStrings.Get("触控板状态不可用。"));
                }

                var actual = await Task.Run(
                    _touchpadController.ToggleVerified,
                    cancellationToken);
                BladeTouchpadEnabled = actual;
                _confirmedBladeTouchpadEnabled = actual;
                BladeTouchpadText = actual ? "已启用" : "已禁用";
            },
            cancellationToken,
            () =>
            {
                BladeTouchpadEnabled = _confirmedBladeTouchpadEnabled;
                BladeTouchpadText = _confirmedBladeTouchpadEnabled ? "已启用" : "已禁用";
            },
            successVerb: "切换并读回",
            failureVerb: "切换");
    }

    internal async Task CycleBladePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync("性能模式", async () =>
        {
            if (!_canSetBladePerformanceMode || _confirmedBladePerformanceModeIndex < 0)
            {
                throw new InvalidOperationException(AppStrings.Get("性能模式状态不可用。"));
            }

            BladePerformanceModeIndex =
                (_confirmedBladePerformanceModeIndex + 1) % BladePerformanceModes.Length;
            var actual = await _deviceTelemetryReader.SetBladePerformanceModeAsync(
                _deviceDescriptors,
                BladePerformanceModes[BladePerformanceModeIndex],
                cancellationToken);
            SetBladePerformanceMode(actual);
            _profile.Global.Blade.PerformanceMode = (byte)actual;
            await SaveProfileAsync(cancellationToken);
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladePerformanceModeIndex = _confirmedBladePerformanceModeIndex);
    }

    internal async Task ToggleBladeGamingModeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSetBladeGamingMode)
        {
            return;
        }

        await RunDeviceOperationAsync("游戏模式", async () =>
        {
            var current = _bladeGameModeState is byte state && state != 2
                ? state
                : throw new InvalidOperationException(AppStrings.Get("游戏模式状态不可用。"));
            var actual = await SetBladeGameModeCoreAsync(
                current == 0,
                cancellationToken);
            SetBladeGameMode(actual);
            RequestDeviceRefresh();
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
        }, cancellationToken, () =>
            BladeGameModeEnabled = _bladeGameModeState != 0);
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
            _bladeStartupAnimationEnabled = actual;
            BladeStartupAnimationText = FormatOptionalState(actual);
            BladeStartupAnimationEnabled = actual;
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladeStartupAnimationEnabled = _bladeStartupAnimationEnabled ?? false);
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

            var currentIndex = Array.IndexOf(
                InternalDisplayRefreshRates.ToArray(),
                _confirmedInternalDisplayRefreshRateHertz);
            InternalDisplayRefreshRateHertz = InternalDisplayRefreshRates[
                (Math.Max(currentIndex, 0) + 1) % InternalDisplayRefreshRates.Count];
            var snapshot = _internalDisplayController.SetRefreshRate(
                InternalDisplayRefreshRateHertz);
            ApplyInternalDisplaySnapshot(snapshot);
            _profile.Global.Blade.RefreshRateHertz = snapshot.RefreshRateHertz;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
            InternalDisplayRefreshRateHertz = _confirmedInternalDisplayRefreshRateHertz);
    }

    internal async Task StepBladeBrightnessAsync(
        bool increase,
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync("键盘亮度", async () =>
        {
            if (!_canSetBladeBrightness)
            {
                throw new InvalidOperationException(AppStrings.Get("键盘亮度状态不可用。"));
            }

            BladeBrightnessPercent = Math.Clamp(
                _confirmedBladeBrightnessPercent + (increase ? 6.25 : -6.25),
                0,
                100);
            await ApplyBladeBrightnessCoreAsync(cancellationToken);
        }, cancellationToken, () =>
            BladeBrightnessPercent = _confirmedBladeBrightnessPercent);
    }

    internal async Task ToggleBladeOneTimeFullChargeAsync(
        CancellationToken cancellationToken = default)
    {
        await RunDeviceOperationAsync("一次性充满", async () =>
        {
            var current = _bladeOneTimeFullChargeEnabled ??
                throw new InvalidOperationException(AppStrings.Get("一次性充满状态不可用。"));
            var actual = await _deviceTelemetryReader.SetBladeOneTimeFullChargeAsync(
                _deviceDescriptors,
                !current,
                cancellationToken);
            _bladeOneTimeFullChargeEnabled = actual;
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
            _bladeOneTimeFullChargeEnabled = actual;
            BladeOneTimeFullChargeText = FormatOptionalState(actual);
            BladeOneTimeFullChargeEnabled = actual;
            RequestDeviceRefresh();
        }, cancellationToken, () =>
            BladeOneTimeFullChargeEnabled = _bladeOneTimeFullChargeEnabled ?? false);
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
            _confirmedViperPollingRateIndex = ViperPollingRateIndex;
            _profile.Global.Viper.PollingRateHertz = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () => ViperPollingRateIndex = _confirmedViperPollingRateIndex);
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
            _confirmedViperDpiXValue = actual.X;
            _confirmedViperDpiYValue = actual.Y;
            ViperDpiText = $"{actual.X} × {actual.Y}";
            _profile.Global.Viper.DpiX = actual.X;
            _profile.Global.Viper.DpiY = actual.Y;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () =>
        {
            ViperDpiXValue = _confirmedViperDpiXValue;
            ViperDpiYValue = _confirmedViperDpiYValue;
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
            _confirmedViperIdleMinutesValue = ViperIdleMinutesValue;
            ViperIdleText = FormatDuration(actual);
            _profile.Global.Viper.IdleSeconds = actual;
            await SaveProfileAsync(cancellationToken);
        }, cancellationToken, () => ViperIdleMinutesValue = _confirmedViperIdleMinutesValue);
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
            var assignments = await _deviceTelemetryReader.ReadViperButtonAssignmentsAsync(
                _deviceDescriptors, cancellationToken);
            ViperButtonAssignments.Clear();
            foreach (var assignment in assignments
                .OrderBy(item => item.ButtonId)
                .ThenBy(item => item.Layer))
            {
                ViperButtonAssignments.Add(new(assignment));
            }
            OnPropertyChanged(nameof(VisibleViperButtonAssignments));

            _canSetViperButtonMappings = assignments.Count == 16;
            ViperButtonMappingsText = _canSetViperButtonMappings
                ? AppStrings.Get("Profile 1 · 8 个可映射控制")
                : AppStrings.Format("MappingReadIncomplete", "读取不完整 · {0}/16 条记录", assignments.Count);
            OnPropertyChanged(nameof(CanSetViperButtonMappings));
        }, cancellationToken, () =>
        {
            ViperButtonAssignments.Clear();
            OnPropertyChanged(nameof(VisibleViperButtonAssignments));
            _canSetViperButtonMappings = false;
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
            var actual = await _deviceTelemetryReader.SetViperButtonAssignmentAsync(
                _deviceDescriptors, row.CreateAssignment(), cancellationToken);
            row.Apply(actual);
        }, cancellationToken, row.RestoreSelection);
    }

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
            DeviceTelemetryTimeText = AppStrings.Format(
                "DeviceOperationSucceeded",
                "{0}已{1} {2:HH:mm:ss}",
                AppStrings.Get(label),
                AppStrings.Get(successVerb),
                DateTimeOffset.Now);
        }
        catch (OperationCanceledException exception)
        {
            restoreSelection?.Invoke();
            if (!cancellationToken.IsCancellationRequested)
            {
                SetDeviceOperationError(AppStrings.Format(
                    "DeviceOperationFailed",
                    "{0}{1}：{2}",
                    AppStrings.Get(label),
                    AppStrings.Get(failureVerb),
                    FormatOperationException(exception)));
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or NotSupportedException or ArgumentOutOfRangeException or OverflowException or AggregateException or ObjectDisposedException)
        {
            restoreSelection?.Invoke();
            SetDeviceOperationError(AppStrings.Format(
                "DeviceOperationFailed",
                "{0}{1}：{2}",
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
                SetDeviceOperationError(AppStrings.Format(
                    "LightingRuntimeError",
                    "键盘灯效运行：{0}",
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
            var previousEnabled = _bladeGameModeState != 0;
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

                throw new InvalidOperationException(AppStrings.Get(
                    "游戏模式已切换，但配置保存失败。"));
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
            _bladeGameModeWriteSupported = false;
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
            if (!string.IsNullOrWhiteSpace(snapshot.CpuName)) CpuName = snapshot.CpuName;
            if (snapshot.CpuUsagePercent is double cpuUsage)
            {
                CpuValue = FormatPercent(cpuUsage);
                CpuPercent = cpuUsage;
            }
            if (snapshot.CpuTemperatureCelsius is double cpuTemperature)
                CpuTemperatureText = FormatNumber<double>(cpuTemperature, "0", "°C");
            if (snapshot.CpuPowerWatts is double cpuPower)
                CpuPowerText = FormatNumber<double>(cpuPower, "0.0", " W");
            if (snapshot.CpuClockMegahertz is int cpuClock)
                CpuClockText = FormatNumber<int>(cpuClock, "0", " MHz");

            if (!string.IsNullOrWhiteSpace(snapshot.GpuName) &&
                !StringComparer.Ordinal.Equals(GpuName, snapshot.GpuName))
            {
                GpuName = snapshot.GpuName;
                GpuTemperatureText = "--";
                GpuPowerText = "--";
                GpuClockText = "--";
                GpuMemoryText = "--";
            }
            if (snapshot.GpuUsagePercent is double gpuUsage)
            {
                GpuValue = FormatPercent(gpuUsage);
                GpuPercent = gpuUsage;
            }
            else
            {
                GpuValue = "--";
                GpuPercent = 0;
            }
            GpuTemperatureText = snapshot.GpuTemperatureCelsius is double gpuTemperature
                ? FormatNumber<double>(gpuTemperature, "0", "°C")
                : "--";
            GpuPowerText = snapshot.GpuPowerWatts is double gpuPower
                ? FormatNumber<double>(gpuPower, "0.0", " W")
                : "--";
            GpuClockText = snapshot.GpuClockMegahertz is int gpuClock
                ? FormatNumber<int>(gpuClock, "0", " MHz")
                : "--";
            GpuMemoryLabel = snapshot.GpuMemoryLabel;
            if (snapshot.GpuMemoryUsedMebibytes is long gpuMemoryUsed &&
                snapshot.GpuMemoryTotalMebibytes is long gpuMemoryTotal)
            {
                GpuMemoryText = $"{gpuMemoryUsed:N0} / {gpuMemoryTotal:N0} MiB";
            }
            else
            {
                GpuMemoryText = "--";
            }

            var memoryPercent = CalculatePercent(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes);
            if (memoryPercent is double currentMemoryPercent)
            {
                MemoryPercent = currentMemoryPercent;
                MemoryValue = FormatPercent(currentMemoryPercent);
                MemoryDetail = FormatBytePair(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes);
            }

            var storagePercent = CalculatePercent(snapshot.StorageUsedBytes, snapshot.StorageTotalBytes);
            if (storagePercent is double currentStoragePercent)
            {
                StoragePercent = currentStoragePercent;
                StorageValue = FormatPercent(currentStoragePercent);
                StorageDetail = FormatBytePair(snapshot.StorageUsedBytes, snapshot.StorageTotalBytes);
            }

            TelemetryTimeText = AppStrings.Format(
                "LiveSampleTime",
                "实时采样 {0:HH:mm:ss}",
                snapshot.CapturedAt.ToLocalTime());
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
                    AppStrings.Format("AutomaticFanSpeed", "自动 · CPU {0} / GPU {1} RPM", cpu, gpu),
                BladeFanMode.Automatic => "自动",
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int target && telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.Format("ManualFanCurrentSpeed", "手动 · {0} RPM · 当前 {1} / {2}", target, cpu, gpu),
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int rpm =>
                    AppStrings.Format("ManualFanSpeed", "手动 · {0} RPM", rpm),
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
                     device.ProtocolFamily == "blade-710" &&
                     device.Access == DeviceAccessState.Available) is { } blade)
        {
            var enabled = ProfileResolver
                .Resolve(_profile, blade, _powerSourceProvider.IsPluggedIn)
                .Blade.GamingModeEnabled == true;
            SetBladeGameMode(new(enabled ? (byte)1 : (byte)0, 0, 0));
        }
        if (telemetry.BladeStartupAnimationEnabled is bool startupAnimationEnabled)
        {
            _bladeStartupAnimationEnabled = startupAnimationEnabled;
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
            _bladeOneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
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
        if (_deviceDescriptors.Any(device => device.ProtocolFamily == "blade-710" && device.Access == DeviceAccessState.Available))
        {
            if (_touchpadController?.GetEnabled() is bool touchpadEnabled)
            {
                BladeTouchpadEnabled = touchpadEnabled;
                _confirmedBladeTouchpadEnabled = touchpadEnabled;
                BladeTouchpadText = touchpadEnabled ? "已启用" : "已禁用";
                _canSetBladeTouchpad = true;
                OnPropertyChanged(nameof(CanSetBladeTouchpad));
            }
            else
            {
                BladeTouchpadText = "不可用";
                _canSetBladeTouchpad = false;
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
            _confirmedViperPollingRateIndex = ViperPollingRateIndex;
            CanSetViperPollingRate = true;
        }
        if (telemetry.ViperDpiX is int dpiX && telemetry.ViperDpiY is int dpiY)
        {
            ViperDpiText = $"{dpiX} × {dpiY}";
            ViperDpiXValue = dpiX;
            ViperDpiYValue = dpiY;
            _confirmedViperDpiXValue = dpiX;
            _confirmedViperDpiYValue = dpiY;
            CanSetViperDpi = true;
        }
        if (telemetry.ViperIdleSeconds is int idleSeconds)
        {
            ViperIdleText = FormatDuration(idleSeconds);
            ViperIdleMinutesValue = idleSeconds / 60d;
            _confirmedViperIdleMinutesValue = ViperIdleMinutesValue;
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
        if (_deviceDescriptors.Any(device => device.ProtocolFamily == "viper-184" && device.Access == DeviceAccessState.Available))
        {
            _canReadViperButtonMappings = true;
            OnPropertyChanged(nameof(CanReadViperButtonMappings));
            ViperStatusText = telemetry.ViperBatteryPercent is not null ||
                              telemetry.ViperPollingRateHertz is not null ||
                              telemetry.ViperDpiX is not null ||
                              telemetry.ViperIdleSeconds is not null
                ? "已连接 · 协议可用"
                : "已连接 · 查询失败";
        }

        DeviceTelemetryTimeText = AppStrings.Format(
            "HardwareQueryTime",
            "硬件查询 {0:HH:mm:ss}",
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
                    device => device.ProtocolFamily == "blade-710") ?? new DeviceDescriptor(
                        "internal-display",
                        "Windows internal display",
                        0,
                        0,
                        DeviceAccessState.Available,
                        DeviceCapabilityState.Unsupported,
                        0,
                        0,
                        0,
                        "blade-710");
                var requested = ProfileResolver.Resolve(_profile, blade, powerState).Blade.RefreshRateHertz;
                if (requested is int hertz && hertz != snapshot.RefreshRateHertz)
                {
                    if (!snapshot.SupportedRefreshRates.Contains(hertz))
                    {
                        throw new InvalidOperationException(AppStrings.Format(
                            "UnsupportedDisplayRefreshRate",
                            "配置请求的 {0} Hz 不受当前内置屏 {1} x {2} 支持。",
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
            SetDisplayError(AppStrings.Format("DisplayRefreshRateError", "内置屏刷新率：{0}", exception.Message));
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
        BladeBrightnessPercent = percent;
        _confirmedBladeBrightnessPercent = percent;
    }

    private void SetBladePerformanceMode(BladePerformanceMode mode)
    {
        var modeChanged = _confirmedBladePerformanceModeIndex < 0 ||
            BladePerformanceModes[_confirmedBladePerformanceModeIndex] != mode;
        BladePerformanceModeText = mode switch
        {
            BladePerformanceMode.Balanced => "平衡",
            BladePerformanceMode.Performance => "性能",
            BladePerformanceMode.BatterySaver => "电池节能",
            BladePerformanceMode.Custom => "自定义",
            BladePerformanceMode.Silent => "静音",
            BladePerformanceMode.BalancedDc => "平衡（电池）",
            BladePerformanceMode.Hyperboost => "HyperBoost",
            _ => "--",
        };
        BladePerformanceModeIndex = Array.IndexOf(BladePerformanceModes, mode);
        _confirmedBladePerformanceModeIndex = BladePerformanceModeIndex;
        if (modeChanged)
        {
            ClearBladeCustomPerformance();
        }
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
        OnPropertyChanged(nameof(BladeCustomPerformanceVisibility));
    }

    private void SetBladeGameMode(BladeGameModeTelemetry? gameMode)
    {
        _bladeGameModeState = gameMode?.GameMode;
        BladeGameModeEnabled = gameMode is { GameMode: not 0 };
        OnPropertyChanged(nameof(CanSetBladeGamingMode));
        OnPropertyChanged(nameof(CanApplyBladeGamingMode));
        BladeGameModeText = gameMode is { GameMode: not 0 }
            ? "已启用"
            : gameMode is not null
                ? "已关闭"
            : "--";
    }

    private void SetBladeChargeLimit(int percent)
    {
        BladeChargeLimitText = percent == 100 ? "关闭 · 100%" : $"{percent}%";
        BladeChargeLimitIndex = Array.IndexOf(BladeChargeLimits, percent);
        _confirmedBladeChargeLimitIndex = BladeChargeLimitIndex;
        OnPropertyChanged(nameof(CanSetBladeOneTimeFullCharge));
        OnPropertyChanged(nameof(CanApplyBladeOneTimeFullCharge));
    }

    private bool IsBladeCustomMode =>
        _confirmedBladePerformanceModeIndex >= 0 &&
        BladePerformanceModes[_confirmedBladePerformanceModeIndex] == BladePerformanceMode.Custom;

    private void ClearBladeCustomPerformance()
    {
        BladeCpuBoostText = "--";
        BladeCpuBoostIndex = -1;
        _confirmedBladeCpuBoostIndex = -1;
        _hasBladeCpuBoost = false;
        BladeGpuBoostText = "--";
        BladeGpuBoostIndex = -1;
        _confirmedBladeGpuBoostIndex = -1;
        _hasBladeGpuBoost = false;
        BladeMaxFanText = "--";
        BladeMaxFanEnabled = false;
        _confirmedBladeMaxFanEnabled = false;
        _hasBladeMaxFan = false;
    }

    private void SetBladeCpuBoost(BladeCpuBoostMode mode)
    {
        BladeCpuBoostText = mode switch
        {
            BladeCpuBoostMode.Low => "低",
            BladeCpuBoostMode.Medium => "中",
            BladeCpuBoostMode.High => "高",
            BladeCpuBoostMode.Boost => "Boost",
            BladeCpuBoostMode.Undervolt => "降压预设",
            _ => "--",
        };
        BladeCpuBoostIndex = Array.IndexOf(BladeCpuBoostModes, mode);
        _confirmedBladeCpuBoostIndex = BladeCpuBoostIndex;
        _hasBladeCpuBoost = BladeCpuBoostIndex >= 0;
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
    }

    private void SetBladeGpuBoost(BladeGpuBoostMode mode)
    {
        BladeGpuBoostText = mode switch
        {
            BladeGpuBoostMode.Low => "低",
            BladeGpuBoostMode.Medium => "中",
            BladeGpuBoostMode.High => "高",
            _ => "--",
        };
        BladeGpuBoostIndex = Array.IndexOf(BladeGpuBoostModes, mode);
        _confirmedBladeGpuBoostIndex = BladeGpuBoostIndex;
        _hasBladeGpuBoost = BladeGpuBoostIndex >= 0;
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
    }

    private void SetBladeMaxFan(BladeMaxFanMode mode)
    {
        BladeMaxFanText = mode == BladeMaxFanMode.Enabled ? "开启" : "关闭";
        BladeMaxFanEnabled = mode == BladeMaxFanMode.Enabled;
        _confirmedBladeMaxFanEnabled = BladeMaxFanEnabled;
        _hasBladeMaxFan = true;
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
    }

    private void SetBladeLogo(BladeLogoMode mode)
    {
        BladeLogoText = mode switch
        {
            BladeLogoMode.Off => "关闭",
            BladeLogoMode.Static => "常亮",
            BladeLogoMode.Breathing => "呼吸",
            _ => "--",
        };
        BladeLogoIndex = Array.IndexOf(BladeLogoModes, mode);
        _confirmedBladeLogoIndex = BladeLogoIndex;
    }

    private void SetViperDpiStages(ViperDpiStagesTelemetry stages, bool confirm = true)
    {
        ViperDpiStages.Clear();
        foreach (var stage in stages.Stages)
        {
            ViperDpiStages.Add(new ViperDpiStageRowViewModel(stage.Number, stage.X, stage.Y));
        }
        SetField(ref _viperDpiStageCount, ViperDpiStages.Count, nameof(ViperDpiStageCount));
        ViperActiveDpiStage = stages.ActiveStage;
        ViperDpiStagesText = AppStrings.Format(
            "DpiStageSummary",
            "DPI 档位 {0}/{1} · {2}",
            stages.ActiveStage,
            stages.Stages.Count,
            string.Join(", ", stages.Stages.Select(stage => $"{stage.X}x{stage.Y}")));
        if (confirm)
        {
            _confirmedViperDpiStages = CopyViperDpiStages(stages);
        }
    }

    private void ResizeViperDpiStages(int count)
    {
        if (ViperDpiStages.Count == 0)
        {
            SetField(ref _viperDpiStageCount, 0, nameof(ViperDpiStageCount));
            return;
        }

        while (ViperDpiStages.Count > count)
        {
            ViperDpiStages.RemoveAt(ViperDpiStages.Count - 1);
        }
        while (ViperDpiStages.Count < count)
        {
            var previous = ViperDpiStages[^1];
            ViperDpiStages.Add(new ViperDpiStageRowViewModel(
                ViperDpiStages.Count + 1, checked((int)previous.X), checked((int)previous.Y)));
        }
        SetField(ref _viperDpiStageCount, count, nameof(ViperDpiStageCount));
        ViperActiveDpiStage = Math.Min(ViperActiveDpiStage, count);
    }

    private void RestoreViperDpiStages()
    {
        if (_confirmedViperDpiStages is { } confirmed)
        {
            SetViperDpiStages(confirmed, confirm: false);
        }
    }

    private static ViperDpiStagesTelemetry CopyViperDpiStages(ViperDpiStagesTelemetry stages) =>
        new(stages.ActiveStage, stages.Stages.ToArray());

    private void ResetDeviceTelemetry()
    {
        BladeStatusText = "探测中";
        BladeBrightnessText = "--";
        BladeBrightnessPercent = 0;
        _confirmedBladeBrightnessPercent = 0;
        BladeBrightnessSelectionText = "--";
        CanSetBladeBrightness = false;
        BladePerformanceModeText = "--";
        BladePerformanceModeIndex = -1;
        _confirmedBladePerformanceModeIndex = -1;
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
        _bladeStartupAnimationEnabled = null;
        BladeStartupAnimationEnabled = false;
        BladeNativeDisplayModeText = "--";
        BladeSkuHardwareText = "--";
        BladeLocalDimmingText = "--";
        _bladeOneTimeFullChargeEnabled = null;
        BladeOneTimeFullChargeEnabled = false;
        BladeOneTimeFullChargeText = "--";
        BladeChargeLimitText = "--";
        BladeChargeLimitIndex = -1;
        _confirmedBladeChargeLimitIndex = -1;
        CanSetBladeChargeLimit = false;
        ClearBladeCustomPerformance();
        BladeLogoText = "--";
        BladeLogoIndex = -1;
        _confirmedBladeLogoIndex = -1;
        CanSetBladeLogo = false;
        BladeTouchpadText = "--";
        BladeTouchpadEnabled = false;
        _confirmedBladeTouchpadEnabled = false;
        _canSetBladeTouchpad = false;
        OnPropertyChanged(nameof(CanSetBladeTouchpad));
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
        ViperStatusText = "探测中";
        ViperDpiStagesText = "--";
        ViperLowBatteryThresholdText = "--";
        ViperBatteryText = "--";
        ViperPollingRateText = "--";
        ViperPollingRateIndex = -1;
        _confirmedViperPollingRateIndex = -1;
        CanSetViperPollingRate = false;
        ViperDpiText = "--";
        ViperDpiXValue = 0;
        ViperDpiYValue = 0;
        _confirmedViperDpiXValue = 0;
        _confirmedViperDpiYValue = 0;
        CanSetViperDpi = false;
        ViperIdleText = "--";
        ViperIdleMinutesValue = 0;
        _confirmedViperIdleMinutesValue = 0;
        CanSetViperIdle = false;
        ViperDpiStages.Clear();
        SetField(ref _viperDpiStageCount, 0, nameof(ViperDpiStageCount));
        _viperActiveDpiStage = 0;
        OnPropertyChanged(nameof(ViperActiveDpiStage));
        _confirmedViperDpiStages = null;
        CanSetViperDpiStages = false;
        ViperButtonAssignments.Clear();
        OnPropertyChanged(nameof(VisibleViperButtonAssignments));
        ViperButtonMappingsText = "未读取";
        _canReadViperButtonMappings = false;
        _canSetViperButtonMappings = false;
        OnPropertyChanged(nameof(CanReadViperButtonMappings));
        OnPropertyChanged(nameof(CanSetViperButtonMappings));
        DeviceTelemetryTimeText = "正在查询硬件";
    }

    private static string FormatDeviceStatus(DeviceDescriptor? device) => device switch
    {
        null => "未发现",
        { Access: DeviceAccessState.Available, Capability: DeviceCapabilityState.PendingValidation } => "已发现 · Feature 接口可打开",
        _ => "已发现 · 接口不可访问",
    };

    private static string FormatDuration(int seconds) => seconds switch
    {
        < 60 => AppStrings.Format("DurationSeconds", "{0} 秒", seconds),
        _ when seconds % 60 == 0 => AppStrings.Format("DurationMinutes", "{0} 分钟", seconds / 60),
        _ => AppStrings.Format("DurationMinutesSeconds", "{0} 分 {1} 秒", seconds / 60, seconds % 60),
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
        TelemetryTimeText = "采样失败 · 显示上次值";
        var message = AppStrings.Format("PerformanceSamplingError", "性能采样：{0}", error);
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
        var bladeName = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == "blade-710")?.Name
            ?? "Razer Blade";
        var viperName = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == "viper-184")?.Name
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

    private static double? CalculatePercent<T>(T? used, T? total) where T : struct, IConvertible
    {
        if (used is null || total is null)
        {
            return null;
        }

        var totalValue = total.Value.ToDouble(null);
        return totalValue <= 0 ? null : Math.Clamp(used.Value.ToDouble(null) * 100 / totalValue, 0, 100);
    }

    private static string FormatPercent(double? value) => value is null ? "--" : $"{value:0}%";

    private static string FormatRawByte(byte? value) => value is byte raw ? $"0x{raw:X2} ({raw})" : AppStrings.Get("未知");

    private static string FormatNumber<T>(T? value, string format, string suffix) where T : struct, IFormattable =>
        value is null ? "--" : value.Value.ToString(format, null) + suffix;

    private static string FormatBytePair<T>(T? used, T? total) where T : struct, IConvertible
    {
        if (used is null || total is null)
        {
            return "-- / -- GB";
        }

        var usedGibibytes = used.Value.ToDouble(null) / 1024 / 1024 / 1024;
        var totalGibibytes = total.Value.ToDouble(null) / 1024 / 1024 / 1024;
        return totalGibibytes >= 1024
            ? $"{usedGibibytes / 1024:0.00} / {totalGibibytes / 1024:0.00} TB"
            : $"{usedGibibytes:0.0} / {totalGibibytes:0.0} GB";
    }

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

internal sealed class UnknownPowerSourceProvider : IPowerSourceProvider
{
    public static UnknownPowerSourceProvider Instance { get; } = new();

    public bool? IsPluggedIn => null;
}

internal sealed class UnknownActiveApplicationProvider : IActiveApplicationProvider
{
    public static UnknownActiveApplicationProvider Instance { get; } = new();

    public string? ExecutablePath => null;
}

public sealed class ViperButtonAssignmentRowViewModel : INotifyPropertyChanged
{
    private static readonly byte[] MouseButtonCodes = [1, 2, 3, 4, 5, 9, 10];
    private int _selectedActionIndex;
    private double _keyboardModifierValue;
    private double _keyboardUsageValue = 4;

    public ViperButtonAssignmentRowViewModel(ViperButtonAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        Assignment = assignment;
        RestoreEditorFromAssignment();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    public IReadOnlyList<string> ActionOptions => AppStrings.Get(
        "关闭", "左键", "右键", "滚轮按下", "后退", "前进", "滚轮向上", "滚轮向下", "键盘按键", "双击", "DPI 循环切换", "播放 / 暂停", "HyperShift", "键盘 Turbo", "鼠标 Turbo");
    public string ButtonText => FormatButton(Assignment.ButtonId);
    public string LayerText => Assignment.Layer == ViperButtonMappingLayer.Normal ? AppStrings.Get("普通") : "HyperShift";
    public string CurrentActionText => FormatAction(Assignment);
    public ViperButtonAssignment Assignment { get; private set; }
    public int SelectedActionIndex
    {
        get => _selectedActionIndex;
        set
        {
            if (_selectedActionIndex == value)
            {
                return;
            }
            _selectedActionIndex = value;
            PropertyChanged?.Invoke(this, new(nameof(SelectedActionIndex)));
            PropertyChanged?.Invoke(this, new(nameof(KeyboardParameterVisibility)));
            PropertyChanged?.Invoke(this, new(nameof(DoubleClickParameterVisibility)));
            PropertyChanged?.Invoke(this, new(nameof(CanApply)));
        }
    }
    public double KeyboardModifierValue
    {
        get => _keyboardModifierValue;
        set => SetKeyboardByte(ref _keyboardModifierValue, value);
    }
    public double KeyboardUsageValue
    {
        get => _keyboardUsageValue;
        set => SetKeyboardByte(ref _keyboardUsageValue, value);
    }
    public Visibility KeyboardParameterVisibility => SelectedActionIndex == 8
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility DoubleClickParameterVisibility => SelectedActionIndex == 9
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool CanApply => TryCreateAssignment(out var requested) && !AssignmentsEqual(Assignment, requested);

    public ViperButtonAssignment CreateAssignment()
    {
        if (!TryCreateAssignment(out var assignment))
        {
            throw new InvalidOperationException(AppStrings.Get("请选择已验证的板载映射动作。"));
        }
        return assignment;
    }

    public void Apply(ViperButtonAssignment assignment)
    {
        Assignment = assignment;
        RestoreEditorFromAssignment();
        NotifyEditorChanged();
    }

    public void RestoreSelection()
    {
        RestoreEditorFromAssignment();
        NotifyEditorChanged();
    }

    private bool TryCreateAssignment(out ViperButtonAssignment assignment)
    {
        var (function, data) = SelectedActionIndex switch
        {
            0 => (ViperButtonMappingFunction.Off, Array.Empty<byte>()),
            >= 1 and <= 7 => (
                ViperButtonMappingFunction.MouseButton,
                new byte[] { MouseButtonCodes[SelectedActionIndex - 1] }),
            8 => (
                ViperButtonMappingFunction.KeyboardKey,
                new byte[] { checked((byte)KeyboardModifierValue), checked((byte)KeyboardUsageValue) }),
            9 => (ViperButtonMappingFunction.DoubleClick, new byte[] { 1 }),
            10 => (ViperButtonMappingFunction.Dpi, new byte[] { 6 }),
            11 => (ViperButtonMappingFunction.MediaKey, new byte[] { 0xCD, 0x00 }),
            12 => (ViperButtonMappingFunction.HyperShift, new byte[] { 1 }),
            13 => (ViperButtonMappingFunction.KeyboardTurbo, new byte[] { 0, 4, 100, 0 }),
            14 => (ViperButtonMappingFunction.MouseTurbo, new byte[] { 1, 100, 0 }),
            _ => default,
        };
        if (SelectedActionIndex is < 0 or > 14)
        {
            assignment = Assignment;
            return false;
        }

        assignment = Assignment with { Function = function, FunctionData = data };
        return true;
    }

    private void RestoreEditorFromAssignment()
    {
        _selectedActionIndex = GetActionIndex(Assignment);
        if (Assignment.Function == ViperButtonMappingFunction.KeyboardKey &&
            Assignment.FunctionData.Count == 2)
        {
            _keyboardModifierValue = Assignment.FunctionData[0];
            _keyboardUsageValue = Assignment.FunctionData[1];
        }
        else
        {
            _keyboardModifierValue = 0;
            _keyboardUsageValue = 4;
        }
    }

    private void SetKeyboardByte(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        var normalized = double.IsFinite(value) ? Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue) : field;
        if (field == normalized)
        {
            return;
        }
        field = normalized;
        PropertyChanged?.Invoke(this, new(propertyName));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }

    private void NotifyEditorChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(CurrentActionText)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedActionIndex)));
        PropertyChanged?.Invoke(this, new(nameof(KeyboardModifierValue)));
        PropertyChanged?.Invoke(this, new(nameof(KeyboardUsageValue)));
        PropertyChanged?.Invoke(this, new(nameof(KeyboardParameterVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(DoubleClickParameterVisibility)));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }

    private static int GetActionIndex(ViperButtonAssignment assignment)
    {
        if (assignment.Function == ViperButtonMappingFunction.Off)
        {
            return 0;
        }
        if (assignment.Function == ViperButtonMappingFunction.KeyboardKey &&
            assignment.FunctionData.Count == 2)
        {
            return 8;
        }
        if (assignment.Function == ViperButtonMappingFunction.DoubleClick &&
            assignment.FunctionData.Count == 1 && assignment.FunctionData[0] == 1)
        {
            return 9;
        }
        if (assignment.Function == ViperButtonMappingFunction.Dpi &&
            assignment.FunctionData.SequenceEqual(new byte[] { 6 }))
        {
            return 10;
        }
        if (assignment.Function == ViperButtonMappingFunction.MediaKey &&
            assignment.FunctionData.SequenceEqual(new byte[] { 0xCD, 0x00 }))
        {
            return 11;
        }
        if (assignment.Function == ViperButtonMappingFunction.HyperShift &&
            assignment.FunctionData.SequenceEqual(new byte[] { 1 }))
        {
            return 12;
        }
        if (assignment.Function == ViperButtonMappingFunction.KeyboardTurbo &&
            assignment.FunctionData.SequenceEqual(new byte[] { 0, 4, 100, 0 }))
        {
            return 13;
        }
        if (assignment.Function == ViperButtonMappingFunction.MouseTurbo &&
            assignment.FunctionData.SequenceEqual(new byte[] { 1, 100, 0 }))
        {
            return 14;
        }
        if (assignment.Function != ViperButtonMappingFunction.MouseButton ||
            assignment.FunctionData.Count != 1)
        {
            return -1;
        }

        var index = Array.IndexOf(MouseButtonCodes, assignment.FunctionData[0]);
        return index < 0 ? -1 : index + 1;
    }

    private static string FormatAction(ViperButtonAssignment assignment) =>
        assignment.Function switch
        {
            ViperButtonMappingFunction.Off => AppStrings.Get("关闭"),
            ViperButtonMappingFunction.MouseButton when assignment.FunctionData.Count == 1 =>
                FormatButton(assignment.FunctionData[0]),
            ViperButtonMappingFunction.Dpi when assignment.FunctionData.SequenceEqual(new byte[] { 6 }) =>
                AppStrings.Get("DPI 循环切换"),
            ViperButtonMappingFunction.MediaKey when assignment.FunctionData.SequenceEqual(new byte[] { 0xCD, 0x00 }) =>
                AppStrings.Get("播放 / 暂停"),
            ViperButtonMappingFunction.HyperShift when assignment.FunctionData.SequenceEqual(new byte[] { 1 }) =>
                "HyperShift",
            ViperButtonMappingFunction.KeyboardKey when assignment.FunctionData.Count == 2 =>
                AppStrings.Format(
                    "KeyboardMappingDescription",
                    "键盘按键 · 修饰 0x{0:X2} · Usage 0x{1:X2}",
                    assignment.FunctionData[0],
                    assignment.FunctionData[1]),
            ViperButtonMappingFunction.DoubleClick when assignment.FunctionData.Count == 1 &&
                assignment.FunctionData[0] == 1 => AppStrings.Get("双击"),
            _ => assignment.FunctionData.Count == 0
                ? assignment.Function.ToString()
                : $"{assignment.Function} · {Convert.ToHexString(assignment.FunctionData.ToArray())}",
        };

    private static string FormatButton(byte buttonId) => buttonId switch
    {
        1 => AppStrings.Get("左键"),
        2 => AppStrings.Get("右键"),
        3 => AppStrings.Get("滚轮按下"),
        4 => AppStrings.Get("后退"),
        5 => AppStrings.Get("前进"),
        9 => AppStrings.Get("滚轮向上"),
        10 => AppStrings.Get("滚轮向下"),
        96 => AppStrings.Get("DPI 切换键"),
        _ => AppStrings.Format("MouseControlNumber", "控制 {0}", buttonId),
    };

    private static bool AssignmentsEqual(ViperButtonAssignment left, ViperButtonAssignment right) =>
        left.ProfileId == right.ProfileId &&
        left.ButtonId == right.ButtonId &&
        left.Layer == right.Layer &&
        left.Function == right.Function &&
        left.FunctionData.SequenceEqual(right.FunctionData);
}

public sealed class ViperDpiStageRowViewModel : INotifyPropertyChanged
{
    private double _x;
    private double _y;

    public ViperDpiStageRowViewModel(int number, int x, int y)
    {
        Number = number;
        _x = x;
        _y = y;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Number { get; }
    public double X
    {
        get => _x;
        set => SetDpi(ref _x, value);
    }
    public double Y
    {
        get => _y;
        set => SetDpi(ref _y, value);
    }

    private void SetDpi(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (!double.IsFinite(value))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return;
        }

        var normalized = Math.Clamp(Math.Round(value / 50, MidpointRounding.AwayFromZero) * 50, 100, 30000);
        if (field == normalized)
        {
            return;
        }

        field = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class DeviceRowViewModel : INotifyPropertyChanged
{
    private readonly string _accessSource;
    private readonly string _iconAutomationSource;
    private readonly int _capabilityState;
    private readonly int _successful;
    private readonly int _total;

    public DeviceRowViewModel(DeviceDescriptor descriptor, RazerDeviceTelemetry telemetry)
    {
        Name = descriptor.Name;
        Identity = $"VID_{descriptor.VendorId:X4} / PID_{descriptor.ProductId:X4}";
        _accessSource = descriptor.Access == DeviceAccessState.Available
            ? "HID 控制通道可打开"
            : "Synapse 占用或访问被拒绝";
        ReportInfo = descriptor.FeatureReportByteLength > 0
            ? $"HID {descriptor.UsagePage:X4}:{descriptor.Usage:X4} · Feature {descriptor.FeatureReportByteLength} B"
            : "Feature report --";
        (IconGlyph, _iconAutomationSource) = descriptor.ProtocolFamily switch
        {
            "blade-710" => ("\uE7F8", "笔记本设备"),
            "viper-184" => ("\uE962", "鼠标设备"),
            _ => ("\uE772", "设备"),
        };

        (_successful, _total) = CountCapabilities(descriptor.ProtocolFamily, telemetry);

        if (descriptor.Access != DeviceAccessState.Available ||
            descriptor.Capability != DeviceCapabilityState.PendingValidation)
        {
            _capabilityState = 0;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 181, 71));
        }
        else if (_successful == _total && _total > 0)
        {
            _capabilityState = 1;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 93, 219, 66));
        }
        else if (_successful > 0)
        {
            _capabilityState = 2;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 240, 185, 90));
        }
        else
        {
            _capabilityState = 3;
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 107, 107));
        }

        IsAvailable = _successful > 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));

    internal static (int Successful, int Total) CountCapabilities(
        string? protocolFamily,
        RazerDeviceTelemetry telemetry)
    {
        var supportsLocalDimming =
            telemetry.BladeSkuHardwareConfiguration?.MiniLedResolution == true;
        return protocolFamily switch
        {
            "blade-710" => (
                CountAvailable(
                    telemetry.BladeKeyboardBrightness,
                    telemetry.BladePerformanceMode,
                    telemetry.BladeChargeLimitPercent,
                    telemetry.BladeCpuBoostMode,
                    telemetry.BladeGpuBoostMode,
                    telemetry.BladeMaxFanMode,
                    telemetry.BladeLogoMode,
                    telemetry.BladeFanMode,
                    telemetry.BladeCurrentFanCpuRpm,
                    telemetry.BladeCurrentFanGpuRpm,
                    telemetry.BladeAdvancedFanCpuModeRaw,
                    telemetry.BladeAdvancedFanGpuModeRaw,
                    telemetry.BladeStartupAnimationEnabled,
                    telemetry.BladeNativeDisplayMode,
                    telemetry.BladeSkuHardwareConfiguration,
                    telemetry.BladeOneTimeFullChargeEnabled) +
                (supportsLocalDimming && telemetry.BladeLocalDimmingEnabled is not null ? 1 : 0),
                16 + (supportsLocalDimming ? 1 : 0)),
            "viper-184" => (
                CountAvailable(
                    telemetry.ViperBatteryPercent,
                    telemetry.ViperPollingRateHertz,
                    telemetry.ViperDpiX,
                    telemetry.ViperIdleSeconds,
                    telemetry.ViperDpiStages,
                    telemetry.ViperLowBatteryThresholdRaw),
                6),
            _ => (0, 0),
        };
    }

    public string Name { get; }
    public string Identity { get; }
    public string Access => AppStrings.Get(_accessSource);
    public string Capability => _capabilityState switch
    {
        0 => AppStrings.Get("控制通道不可用"),
        1 => AppStrings.Format("ProtocolAvailableCount", "协议可用 {0}/{1}", _successful, _total),
        2 => AppStrings.Format("ProtocolPartiallyAvailableCount", "部分可用 {0}/{1}", _successful, _total),
        _ => AppStrings.Get("协议查询失败"),
    };
    public string ReportInfo { get; }
    public string IconGlyph { get; }
    public string IconAutomationName => AppStrings.Get(_iconAutomationSource);
    public bool IsAvailable { get; }
    public Brush StatusBrush { get; }

    private static int CountAvailable(params object?[] values) => values.Count(value => value is not null);
}

public sealed class DiagnosticRowViewModel(
    string device,
    string capability,
    string status,
    string detail,
    Brush statusBrush) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Device => AppStrings.Get(device);
    public string Capability => AppStrings.Get(capability);
    public string Status => AppStrings.Get(status);
    public string Detail => AppStrings.Get(detail);
    public Brush StatusBrush { get; } = statusBrush;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new(string.Empty));
}

public sealed class ApplicationBindingRowViewModel(string executablePath, string profileName)
{
    public string ExecutablePath { get; } = executablePath;
    public string ExecutableName { get; } = Path.GetFileName(executablePath);
    public string ProfileName { get; } = profileName;
}
