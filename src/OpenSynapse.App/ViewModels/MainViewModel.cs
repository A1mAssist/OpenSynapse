using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly BladePerformanceMode[] BladePerformanceModes =
    [
        BladePerformanceMode.Balanced,
        BladePerformanceMode.Performance,
        BladePerformanceMode.Custom,
        BladePerformanceMode.Silent,
        BladePerformanceMode.Battery,
        BladePerformanceMode.Hyperboost,
    ];
    private static readonly int[] BladeChargeLimits = [50, 55, 60, 65, 70, 75, 80, 100];
    private static readonly BladeCpuBoostMode[] BladeCpuBoostModes =
        [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High, BladeCpuBoostMode.Boost, BladeCpuBoostMode.Undervolt];
    private static readonly BladeGpuBoostMode[] BladeGpuBoostModes =
        [BladeGpuBoostMode.Low, BladeGpuBoostMode.Medium, BladeGpuBoostMode.High];
    private static readonly BladeLogoMode[] BladeLogoModes = [BladeLogoMode.Off, BladeLogoMode.Static];
    private static readonly BladeLightingMode[] BladeLightingModes =
        [BladeLightingMode.Off, BladeLightingMode.Static, BladeLightingMode.Breathing, BladeLightingMode.Spectrum, BladeLightingMode.Wave, BladeLightingMode.Fire, BladeLightingMode.Reactive, BladeLightingMode.Ripple];
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
    private readonly string? _executablePath;
    private readonly IReadOnlyList<string> _startupDiagnostics;
    private readonly VerifiedProfileApplier _profileApplier = new();
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
    private int _bladeLightingModeIndex = 1;
    private int _bladeWaveDirectionIndex;
    private Color _bladeLightingColor = Color.FromArgb(0xFF, 0x99, 0xDD, 0x72);
    private IReadOnlyList<DeviceDescriptor> _deviceDescriptors = Array.Empty<DeviceDescriptor>();
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
    private string _deviceFingerprint = string.Empty;
    private string _lightingShadowFingerprint = string.Empty;
    private string _bladeLightingDevicePath = string.Empty;
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
    private string _gpuMemoryText = "显存 --";
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
        IReadOnlyList<string>? startupDiagnostics = null)
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
        _executablePath = executablePath;
        _startupDiagnostics = startupDiagnostics?.ToArray() ?? Array.Empty<string>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<DiagnosticRowViewModel> Diagnostics { get; } = new();
    public ObservableCollection<string> ProfileNames { get; } = new();
    public ObservableCollection<ApplicationBindingRowViewModel> ApplicationBindings { get; } = new();

    public string LastDeviceRefreshText
    {
        get => _lastDeviceRefreshText;
        private set => SetField(ref _lastDeviceRefreshText, value);
    }

    public string TelemetryTimeText
    {
        get => _telemetryTimeText;
        private set => SetField(ref _telemetryTimeText, value);
    }

    public string DeviceTelemetryTimeText { get => _deviceTelemetryTimeText; private set => SetField(ref _deviceTelemetryTimeText, value); }

    public string DeviceErrorText
    {
        get => _deviceErrorText;
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
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string ProfileStatusText { get => _profileStatusText; private set => SetField(ref _profileStatusText, value); }
    public string ActiveProfileName { get => _activeProfileName; private set => SetField(ref _activeProfileName, value); }
    public string ProfileNameInput { get => _profileNameInput; set => SetField(ref _profileNameInput, value); }
    public bool CanDeleteProfile => ProfileNames.Count > 1 && !IsBusy;
    public bool IsStartupEnabled { get => _isStartupEnabled; private set => SetField(ref _isStartupEnabled, value); }
    public bool CanSetStartup => _startupManager is not null && !string.IsNullOrWhiteSpace(_executablePath) && !IsBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(CanSetBladeBrightness));
                OnPropertyChanged(nameof(CanSetBladePerformanceMode));
                OnPropertyChanged(nameof(CanSetBladeChargeLimit));
                OnPropertyChanged(nameof(CanSetBladeCpuBoost));
                OnPropertyChanged(nameof(CanSetBladeGpuBoost));
                OnPropertyChanged(nameof(CanSetBladeMaxFan));
                OnPropertyChanged(nameof(CanSetBladeLogo));
                OnPropertyChanged(nameof(CanSetBladeLighting));
                OnPropertyChanged(nameof(CanSetViperPollingRate));
                OnPropertyChanged(nameof(CanSetViperDpi));
                OnPropertyChanged(nameof(CanSetViperDpiStages));
                OnPropertyChanged(nameof(CanSetViperIdle));
                OnPropertyChanged(nameof(CanSetInternalDisplayRefreshRate));
                OnPropertyChanged(nameof(CanDeleteProfile));
                OnPropertyChanged(nameof(CanSetStartup));
            }
        }
    }

    public bool CanRefresh => !IsBusy;

    public string BladeDeviceName { get => _bladeDeviceName; private set => SetField(ref _bladeDeviceName, value); }
    public string BladeStatusText { get => _bladeStatusText; private set => SetField(ref _bladeStatusText, value); }
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
        get => _canSetBladeBrightness && !IsBusy;
        private set
        {
            if (SetField(ref _canSetBladeBrightness, value))
            {
                OnPropertyChanged(nameof(CanSetBladeLighting));
            }
        }
    }
    public string BladePerformanceModeText { get => _bladePerformanceModeText; private set => SetField(ref _bladePerformanceModeText, value); }
    public IReadOnlyList<string> BladePerformanceModeOptions { get; } = ["平衡", "性能", "自定义", "静音", "电池", "HyperBoost"];
    public int BladePerformanceModeIndex { get => _bladePerformanceModeIndex; set => SetField(ref _bladePerformanceModeIndex, value); }
    public bool CanSetBladePerformanceMode { get => _canSetBladePerformanceMode && !IsBusy; private set => SetField(ref _canSetBladePerformanceMode, value); }
    public string BladeFanText { get => _bladeFanText; private set => SetField(ref _bladeFanText, value); }
    public string BladeChargeLimitText { get => _bladeChargeLimitText; private set => SetField(ref _bladeChargeLimitText, value); }
    public IReadOnlyList<string> BladeChargeLimitOptions { get; } = ["50%", "55%", "60%", "65%", "70%", "75%", "80%", "关闭限制（100%）"];
    public int BladeChargeLimitIndex { get => _bladeChargeLimitIndex; set => SetField(ref _bladeChargeLimitIndex, value); }
    public bool CanSetBladeChargeLimit { get => _canSetBladeChargeLimit && !IsBusy; private set => SetField(ref _canSetBladeChargeLimit, value); }
    public IReadOnlyList<string> BladeCpuBoostOptions { get; } = ["低", "中", "高", "Boost", "降压"];
    public string BladeCpuBoostText { get => _bladeCpuBoostText; private set => SetField(ref _bladeCpuBoostText, value); }
    public int BladeCpuBoostIndex { get => _bladeCpuBoostIndex; set => SetField(ref _bladeCpuBoostIndex, value); }
    public bool CanSetBladeCpuBoost => _hasBladeCpuBoost && IsBladeCustomMode && !IsBusy;
    public IReadOnlyList<string> BladeGpuBoostOptions { get; } = ["低", "中", "高"];
    public string BladeGpuBoostText { get => _bladeGpuBoostText; private set => SetField(ref _bladeGpuBoostText, value); }
    public int BladeGpuBoostIndex { get => _bladeGpuBoostIndex; set => SetField(ref _bladeGpuBoostIndex, value); }
    public bool CanSetBladeGpuBoost => _hasBladeGpuBoost && IsBladeCustomMode && !IsBusy;
    public string BladeMaxFanText { get => _bladeMaxFanText; private set => SetField(ref _bladeMaxFanText, value); }
    public bool BladeMaxFanEnabled { get => _bladeMaxFanEnabled; set => SetField(ref _bladeMaxFanEnabled, value); }
    public bool CanSetBladeMaxFan => _hasBladeMaxFan && IsBladeCustomMode && !IsBusy;
    public IReadOnlyList<string> BladeLogoOptions { get; } = ["关闭", "常亮"];
    public string BladeLogoText { get => _bladeLogoText; private set => SetField(ref _bladeLogoText, value); }
    public int BladeLogoIndex { get => _bladeLogoIndex; set => SetField(ref _bladeLogoIndex, value); }
    public bool CanSetBladeLogo { get => _canSetBladeLogo && !IsBusy; private set => SetField(ref _canSetBladeLogo, value); }
    public IReadOnlyList<string> BladeLightingModeOptions { get; } = ["关闭", "静态", "呼吸", "光谱循环", "波浪", "火焰", "响应", "涟漪"];
    public int BladeLightingModeIndex { get => _bladeLightingModeIndex; set => SetField(ref _bladeLightingModeIndex, value); }
    public IReadOnlyList<string> BladeWaveDirectionOptions { get; } = ["向右", "向左"];
    public int BladeWaveDirectionIndex { get => _bladeWaveDirectionIndex; set => SetField(ref _bladeWaveDirectionIndex, value); }
    public Color BladeLightingColor { get => _bladeLightingColor; set => SetField(ref _bladeLightingColor, value); }
    public bool CanSetBladeLighting => _canSetBladeBrightness && _bladeLightingController is not null && !IsBusy;
    public string ViperDeviceName { get => _viperDeviceName; private set => SetField(ref _viperDeviceName, value); }
    public string ViperStatusText { get => _viperStatusText; private set => SetField(ref _viperStatusText, value); }
    public string ViperBatteryText { get => _viperBatteryText; private set => SetField(ref _viperBatteryText, value); }
    public string ViperPollingRateText { get => _viperPollingRateText; private set => SetField(ref _viperPollingRateText, value); }
    public int ViperPollingRateIndex { get => _viperPollingRateIndex; set => SetField(ref _viperPollingRateIndex, value); }
    public bool CanSetViperPollingRate { get => _canSetViperPollingRate && !IsBusy; private set => SetField(ref _canSetViperPollingRate, value); }
    public string ViperDpiText { get => _viperDpiText; private set => SetField(ref _viperDpiText, value); }
    public double ViperDpiXValue { get => _viperDpiXValue; set => SetField(ref _viperDpiXValue, value); }
    public double ViperDpiYValue { get => _viperDpiYValue; set => SetField(ref _viperDpiYValue, value); }
    public bool CanSetViperDpi { get => _canSetViperDpi && !IsBusy; private set => SetField(ref _canSetViperDpi, value); }
    public string ViperIdleText { get => _viperIdleText; private set => SetField(ref _viperIdleText, value); }
    public string ViperDpiStagesText { get => _viperDpiStagesText; private set => SetField(ref _viperDpiStagesText, value); }
    public string ViperLowBatteryThresholdText { get => _viperLowBatteryThresholdText; private set => SetField(ref _viperLowBatteryThresholdText, value); }
    public double ViperIdleMinutesValue { get => _viperIdleMinutesValue; set => SetField(ref _viperIdleMinutesValue, value); }
    public bool CanSetViperIdle { get => _canSetViperIdle && !IsBusy; private set => SetField(ref _canSetViperIdle, value); }
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
    public bool CanSetViperDpiStages { get => _canSetViperDpiStages && !IsBusy; private set => SetField(ref _canSetViperDpiStages, value); }
    public string InternalDisplayResolutionText { get => _internalDisplayResolutionText; private set => SetField(ref _internalDisplayResolutionText, value); }
    public string InternalDisplayRefreshRateText { get => _internalDisplayRefreshRateText; private set => SetField(ref _internalDisplayRefreshRateText, value); }
    public IReadOnlyList<int> InternalDisplayRefreshRates { get => _internalDisplayRefreshRates; private set => SetField(ref _internalDisplayRefreshRates, value); }
    public int InternalDisplayRefreshRateHertz { get => _internalDisplayRefreshRateHertz; set => SetField(ref _internalDisplayRefreshRateHertz, value); }
    public bool CanSetInternalDisplayRefreshRate { get => _canSetInternalDisplayRefreshRate && !IsBusy; private set => SetField(ref _canSetInternalDisplayRefreshRate, value); }

    public string EmptyStateText => Devices.Count == 0
        ? "未发现 Blade 16 或 Viper V3 HyperSpeed。"
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
    public string GpuMemoryText { get => _gpuMemoryText; private set => SetField(ref _gpuMemoryText, value); }
    public string MemoryValue { get => _memoryValue; private set => SetField(ref _memoryValue, value); }
    public string MemoryDetail { get => _memoryDetail; private set => SetField(ref _memoryDetail, value); }
    public double MemoryPercent { get => _memoryPercent; private set => SetField(ref _memoryPercent, value); }
    public string StorageValue { get => _storageValue; private set => SetField(ref _storageValue, value); }
    public string StorageDetail { get => _storageDetail; private set => SetField(ref _storageDetail, value); }
    public double StoragePercent { get => _storagePercent; private set => SetField(ref _storagePercent, value); }

    public void RequestDeviceRefresh() => Interlocked.Exchange(ref _deviceRefreshRequested, 1);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadProfileAsync(cancellationToken);
        await RefreshDevicesAsync(cancellationToken);
        await RefreshPerformanceAsync(cancellationToken);
    }

    private async Task LoadProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            _profile = await _profileStore.LoadAsync(cancellationToken);
            RefreshProfileState();
            RefreshStartupState();
            ProfileStatusText = $"本地配置已加载 · {ActiveProfileName}";
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
            ProfileStatusText = $"已使用默认本地配置 · {ActiveProfileName}";
            ReportApplicationError($"配置加载：{exception.Message}");
        }
    }

    public async Task SelectProfileAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(name) ||
            StringComparer.OrdinalIgnoreCase.Equals(name, ActiveProfileName))
        {
            return;
        }

        await RunProfileOperationAsync($"切换配置 {name}", () =>
        {
            ProfileCatalog.Select(_profile, name);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task CreateProfileAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        await RunProfileOperationAsync($"新建配置 {name}", () =>
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
        if (IsBusy || !CanDeleteProfile)
        {
            return;
        }

        await RunProfileOperationAsync($"删除配置 {ActiveProfileName}", () =>
        {
            ProfileCatalog.Delete(_profile, ActiveProfileName);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task CloneActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        await RunProfileOperationAsync($"克隆配置 {name}", () =>
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
        if (IsBusy || string.IsNullOrWhiteSpace(ProfileNameInput))
        {
            return;
        }

        var name = ProfileNameInput;
        await RunProfileOperationAsync($"重命名配置 {name}", () =>
        {
            ProfileCatalog.Rename(_profile, ActiveProfileName, name);
            ProfileNameInput = string.Empty;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task BindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync($"绑定应用 {Path.GetFileName(executablePath)}", () =>
        {
            ApplicationProfileBinding.Bind(_profile, executablePath, ActiveProfileName);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);

    public Task UnbindApplicationAsync(string executablePath, CancellationToken cancellationToken = default) =>
        RunProfileOperationAsync($"解绑应用 {Path.GetFileName(executablePath)}", () =>
        {
            ApplicationProfileBinding.Unbind(_profile, executablePath);
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);

    public async Task ImportProfilesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

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
            SetDeviceOperationError($"配置导入：{exception.Message}");
            ProfileStatusText = $"配置导入失败：{exception.Message}";
            return;
        }

        await RunProfileOperationAsync("导入配置", () =>
        {
            _profile = imported;
            RefreshProfileState();
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task ExportProfilesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await ProfileStore.ExportAsync(_profile.Clone(), filePath, cancellationToken);
            ProfileStatusText = $"配置已导出 · {Path.GetFileName(filePath)}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError($"配置导出：{exception.Message}");
            ProfileStatusText = $"配置导出失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!CanSetStartup || cancellationToken.IsCancellationRequested)
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
            SetDeviceOperationError($"开机启动：{exception.Message}");
            ProfileStatusText = $"开机启动设置失败：{exception.Message}";
            RefreshStartupState();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunProfileOperationAsync(
        string label,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        var previous = _profile.Clone();
        IsBusy = true;
        try
        {
            await operation();
            await _profileStore.SaveAsync(_profile, cancellationToken);
            RequestDeviceRefresh();
            ProfileStatusText = $"{label} · {ActiveProfileName}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _profile = previous;
            RefreshProfileState();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            SetDeviceOperationError($"{label}：{exception.Message}");
            ProfileStatusText = $"{label}失败：{exception.Message}";
            _profile = previous;
            RefreshProfileState();
        }
        finally
        {
            IsBusy = false;
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
            SetDeviceOperationError($"开机启动：{exception.Message}");
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
            SetDeviceOperationError($"配置应用：{string.Join("；", result.Errors)}");
        }

        return result;
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
            SetDeviceOperationError($"配置保存：{exception.Message}");
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
                if (IsBusy)
                {
                    continue;
                }

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
                            ProfileStatusText = $"已自动切换 · {ActiveProfileName}";
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
                            SetDeviceOperationError($"配置自动切换保存：{exception.Message}");
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
                    SetDeviceQueryError($"设备状态监听：{exception.Message}");
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
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ResetDeviceTelemetry();
        SetDeviceQueryError(string.Empty);
        SetDeviceOperationError(string.Empty);
        var powerState = _powerSourceProvider.IsPluggedIn;
        applyDisplayProfile |= _lastPowerState != powerState;
        _lastPowerState = powerState;
        try
        {
            var snapshot = knownSnapshot ?? await _discovery.DiscoverAsync(cancellationToken);
            _deviceFingerprint = CreateDeviceFingerprint(snapshot);
            _deviceDescriptors = snapshot.Devices;
            RefreshInternalDisplay(powerState, applyDisplayProfile);
            Devices.Clear();

            var blade = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == "blade-710");
            var viper = snapshot.Devices.FirstOrDefault(device => device.ProtocolFamily == "viper-184");
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
            var lightingError = profileApply?.Errors.Any(error =>
                    error.StartsWith("Blade", StringComparison.OrdinalIgnoreCase)) == true
                ? null
                : await ApplyLoadedLightingProfileAsync(blade, powerState, cancellationToken);
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
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
            {
                errors.Insert(0, snapshot.ErrorMessage);
            }

            RebuildDiagnostics(snapshot, telemetry, errors);
            SetDeviceQueryError(errors.Count == 0
                ? string.Empty
                : $"{errors.Count} 项硬件查询失败，未成功读回的控制已停用。请在“诊断”页查看详情。");
            LastDeviceRefreshText = $"设备探测 {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}";
            _nextFullDeviceRefresh = DateTimeOffset.UtcNow.AddSeconds(30);
            Interlocked.Exchange(ref _deviceRefreshRequested, 0);
            OnPropertyChanged(nameof(EmptyStateText));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            _deviceFingerprint = string.Empty;
            _deviceDescriptors = Array.Empty<DeviceDescriptor>();
            _lightingShadowFingerprint = string.Empty;
            _bladeLightingDevicePath = string.Empty;
            RefreshInternalDisplay(powerState, applyDisplayProfile);
            Devices.Clear();
            SetDeviceQueryError(exception.Message);
            LastDeviceRefreshText = "设备探测失败";
            OnPropertyChanged(nameof(EmptyStateText));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyBladeBrightnessAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !CanSetBladeBrightness)
        {
            return;
        }

        IsBusy = true;
        SetDeviceOperationError(string.Empty);
        try
        {
            var requested = checked((byte)Math.Round(BladeBrightnessPercent * 255 / 100, MidpointRounding.AwayFromZero));
            var actual = await _deviceTelemetryReader.SetBladeKeyboardBrightnessAsync(_deviceDescriptors, requested, cancellationToken);
            SetBladeBrightness(actual);
            _profile.Global.Blade.KeyboardBrightness = actual;
            await SaveProfileAsync(cancellationToken);
            DeviceTelemetryTimeText = $"亮度已应用并读回 {DateTimeOffset.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OverflowException)
        {
            BladeBrightnessPercent = _confirmedBladeBrightnessPercent;
            SetDeviceOperationError($"键盘亮度写入：{exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
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
                    throw new InvalidOperationException("灯效已启动，但配置保存失败，已恢复内存中的配置。");
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
            reportsReadBack: false);
    }

    public Task ApplySelectedBladeLightingEffectAsync(CancellationToken cancellationToken = default)
    {
        if (BladeLightingModeIndex < 0 || BladeLightingModeIndex >= BladeLightingModes.Length ||
            BladeWaveDirectionIndex < 0 || BladeWaveDirectionIndex >= BladeWaveDirections.Length)
        {
            return Task.CompletedTask;
        }

        var color = new RazerRgb(BladeLightingColor.R, BladeLightingColor.G, BladeLightingColor.B);
        var effect = new BladeLightingEffect(
            BladeLightingModes[BladeLightingModeIndex],
            color,
            BladeWaveDirections[BladeWaveDirectionIndex]);
        return ApplyBladeLightingEffectAsync(effect, cancellationToken);
    }

    public async Task ApplyBladePerformanceModeAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !CanSetBladePerformanceMode ||
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
        if (IsBusy || !CanSetBladeChargeLimit ||
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
                throw new InvalidOperationException("CPU Boost 已写入，但配置保存失败，已恢复内存中的配置。");
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
                throw new InvalidOperationException("GPU Boost 已写入，但配置保存失败，已恢复内存中的配置。");
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
                throw new InvalidOperationException("Logo 已写入，但配置保存失败，已恢复内存中的配置。");
            }
        }, cancellationToken, () => BladeLogoIndex = _confirmedBladeLogoIndex);
    }

    public async Task ApplyViperPollingRateAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !CanSetViperPollingRate)
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
        if (IsBusy || !CanSetViperDpi)
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
        if (IsBusy || !CanSetViperIdle)
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
                throw new InvalidOperationException("DPI 档位已写入，但配置保存失败，已恢复内存中的配置。");
            }
        }, cancellationToken, RestoreViperDpiStages);
    }

    public async Task ApplyInternalDisplayRefreshRateAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || !CanSetInternalDisplayRefreshRate || _internalDisplayController is null)
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
        bool reportsReadBack = true)
    {
        IsBusy = true;
        SetDeviceOperationError(string.Empty);
        try
        {
            await operation();
            DeviceTelemetryTimeText = $"{label}已{(reportsReadBack ? "应用并读回" : "启动")} {DateTimeOffset.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            restoreSelection?.Invoke();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentOutOfRangeException or OverflowException or AggregateException or ObjectDisposedException)
        {
            restoreSelection?.Invoke();
            SetDeviceOperationError($"{label}写入：{FormatOperationException(exception)}");
        }
        finally
        {
            IsBusy = false;
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
                SetDeviceOperationError($"键盘灯效运行：{FormatOperationException(exception)}");
            }
        }
    }

    private static string FormatOperationException(Exception exception)
    {
        var exceptions = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [exception];
        return string.Join("；", exceptions.Select(error => error.Message).Where(message => !string.IsNullOrWhiteSpace(message)));
    }

    private async Task RefreshPerformanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _performanceMonitor.SampleAsync(cancellationToken);
            CpuName = snapshot.CpuName;
            CpuValue = FormatPercent(snapshot.CpuUsagePercent);
            CpuPercent = snapshot.CpuUsagePercent ?? 0;
            CpuTemperatureText = FormatNumber(snapshot.CpuTemperatureCelsius, "0", "°C");
            CpuPowerText = FormatNumber(snapshot.CpuPowerWatts, "0.0", " W");
            CpuClockText = FormatNumber(snapshot.CpuClockMegahertz, "0", " MHz");

            GpuName = snapshot.GpuName;
            GpuValue = FormatPercent(snapshot.GpuUsagePercent);
            GpuPercent = snapshot.GpuUsagePercent ?? 0;
            GpuTemperatureText = FormatNumber(snapshot.GpuTemperatureCelsius, "0", "°C");
            GpuPowerText = FormatNumber(snapshot.GpuPowerWatts, "0.0", " W");
            GpuClockText = FormatNumber(snapshot.GpuClockMegahertz, "0", " MHz");
            GpuMemoryText = snapshot.GpuMemoryUsedMebibytes is not null && snapshot.GpuMemoryTotalMebibytes is not null
                ? $"{snapshot.GpuMemoryUsedMebibytes:N0} / {snapshot.GpuMemoryTotalMebibytes:N0} MiB"
                : "--";

            var memoryPercent = CalculatePercent(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes);
            MemoryPercent = memoryPercent ?? 0;
            MemoryValue = FormatPercent(memoryPercent);
            MemoryDetail = FormatBytePair(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes);

            var storagePercent = CalculatePercent(snapshot.StorageUsedBytes, snapshot.StorageTotalBytes);
            StoragePercent = storagePercent ?? 0;
            StorageValue = FormatPercent(storagePercent);
            StorageDetail = FormatBytePair(snapshot.StorageUsedBytes, snapshot.StorageTotalBytes);

            TelemetryTimeText = $"实时采样 {snapshot.CapturedAt.ToLocalTime():HH:mm:ss}";
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
        BladeFanText = telemetry.BladeFanMode switch
        {
            BladeFanMode.Automatic when telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu => $"自动 · CPU {cpu} / GPU {gpu} RPM",
            BladeFanMode.Automatic => "自动",
            BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int target && telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu => $"手动 · {target} RPM · 当前 {cpu} / {gpu}",
            BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int rpm => $"手动 · {rpm} RPM",
            BladeFanMode.Manual => "手动 · -- RPM",
            _ => "--",
        };
        if (telemetry.BladeChargeLimitPercent is int chargeLimit)
        {
            SetBladeChargeLimit(chargeLimit);
            CanSetBladeChargeLimit = true;
        }
        if (_deviceDescriptors.Any(device => device.ProtocolFamily == "blade-710" && device.Access == DeviceAccessState.Available))
        {
            BladeStatusText = telemetry.BladeKeyboardBrightness is not null ||
                              telemetry.BladePerformanceMode is not null ||
                              telemetry.BladeChargeLimitPercent is not null
                ? "已连接 · 已读取可用控制"
                : "已连接 · 硬件查询失败";
        }

        ViperBatteryText = telemetry.ViperBatteryPercent is int battery ? $"{battery}%" : "--";
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
        ViperLowBatteryThresholdText = telemetry.ViperLowBatteryThresholdRaw is byte raw
            ? ViperLowBatteryThresholdProtocol.Format(raw)
            : "--";
        if (_deviceDescriptors.Any(device => device.ProtocolFamily == "viper-184" && device.Access == DeviceAccessState.Available))
        {
            ViperStatusText = telemetry.ViperBatteryPercent is not null ||
                              telemetry.ViperPollingRateHertz is not null ||
                              telemetry.ViperDpiX is not null ||
                              telemetry.ViperIdleSeconds is not null
                ? "已连接 · 协议可用"
                : "已连接 · 查询失败";
        }

        DeviceTelemetryTimeText = $"硬件查询 {telemetry.CapturedAt.ToLocalTime():HH:mm:ss}";
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
                        throw new InvalidOperationException(
                            $"配置请求的 {hertz} Hz 不受当前内置屏 {snapshot.Width} x {snapshot.Height} 支持。");
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
            SetDisplayError($"内置屏刷新率：{exception.Message}");
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
        InternalDisplayResolutionText = "--";
        InternalDisplayRefreshRateText = "--";
        InternalDisplayRefreshRates = Array.Empty<int>();
        InternalDisplayRefreshRateHertz = 0;
        _confirmedInternalDisplayRefreshRateHertz = 0;
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
            BladePerformanceMode.Custom => "自定义",
            BladePerformanceMode.Silent => "静音",
            BladePerformanceMode.Battery => "电池",
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
    }

    private void SetBladeChargeLimit(int percent)
    {
        BladeChargeLimitText = percent == 100 ? "关闭 · 100%" : $"{percent}%";
        BladeChargeLimitIndex = Array.IndexOf(BladeChargeLimits, percent);
        _confirmedBladeChargeLimitIndex = BladeChargeLimitIndex;
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
            BladeCpuBoostMode.Undervolt => "降压",
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
            BladeLogoMode.Breathing => "呼吸（只读）",
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
        ViperDpiStagesText = $"DPI 档位 {stages.ActiveStage}/{stages.Stages.Count} · " +
            string.Join(", ", stages.Stages.Select(stage => $"{stage.X}x{stage.Y}"));
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
        CanSetBladePerformanceMode = false;
        BladeFanText = "--";
        BladeChargeLimitText = "--";
        BladeChargeLimitIndex = -1;
        _confirmedBladeChargeLimitIndex = -1;
        CanSetBladeChargeLimit = false;
        ClearBladeCustomPerformance();
        BladeLogoText = "--";
        BladeLogoIndex = -1;
        _confirmedBladeLogoIndex = -1;
        CanSetBladeLogo = false;
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
        < 60 => $"{seconds} 秒",
        _ when seconds % 60 == 0 => $"{seconds / 60} 分钟",
        _ => $"{seconds / 60} 分 {seconds % 60} 秒",
    };

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
        CpuValue = "--";
        CpuPercent = 0;
        CpuTemperatureText = "--";
        CpuPowerText = "--";
        CpuClockText = "--";
        GpuValue = "--";
        GpuPercent = 0;
        GpuTemperatureText = "--";
        GpuPowerText = "--";
        GpuClockText = "--";
        GpuMemoryText = "--";
        MemoryValue = "--";
        MemoryDetail = "-- / -- GB";
        MemoryPercent = 0;
        StorageValue = "--";
        StorageDetail = "-- / -- GB";
        StoragePercent = 0;
        TelemetryTimeText = "实时采样不可用";
        var message = $"性能采样：{error}";
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
        exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException;

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

public sealed class DeviceRowViewModel
{
    public DeviceRowViewModel(DeviceDescriptor descriptor, RazerDeviceTelemetry telemetry)
    {
        Name = descriptor.Name;
        Identity = $"VID_{descriptor.VendorId:X4} / PID_{descriptor.ProductId:X4}";
        Access = descriptor.Access == DeviceAccessState.Available
            ? "HID 控制通道可打开"
            : "Synapse 占用或访问被拒绝";
        ReportInfo = descriptor.FeatureReportByteLength > 0
            ? $"HID {descriptor.UsagePage:X4}:{descriptor.Usage:X4} · Feature {descriptor.FeatureReportByteLength} B"
            : "Feature report --";
        (IconGlyph, IconAutomationName) = descriptor.ProtocolFamily switch
        {
            "blade-710" => ("\uE7F8", "笔记本设备"),
            "viper-184" => ("\uE962", "鼠标设备"),
            _ => ("\uE772", "设备"),
        };

        var (successful, total) = descriptor.ProtocolFamily switch
        {
            "blade-710" => (
                CountAvailable(
                    telemetry.BladeKeyboardBrightness,
                    telemetry.BladePerformanceMode,
                    telemetry.BladeChargeLimitPercent,
                    telemetry.BladeCpuBoostMode,
                    telemetry.BladeGpuBoostMode,
                    telemetry.BladeMaxFanMode,
                    telemetry.BladeLogoMode),
                7),
            "viper-184" => (
                CountAvailable(
                    telemetry.ViperPollingRateHertz,
                    telemetry.ViperDpiX,
                    telemetry.ViperIdleSeconds,
                    telemetry.ViperDpiStages),
                4),
            _ => (0, 0),
        };

        if (descriptor.Access != DeviceAccessState.Available ||
            descriptor.Capability != DeviceCapabilityState.PendingValidation)
        {
            Capability = "控制通道不可用";
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 181, 71));
        }
        else if (successful == total && total > 0)
        {
            Capability = $"协议可用 {successful}/{total}";
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 93, 219, 66));
        }
        else if (successful > 0)
        {
            Capability = $"部分可用 {successful}/{total}";
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 240, 185, 90));
        }
        else
        {
            Capability = "协议查询失败";
            StatusBrush = new SolidColorBrush(Color.FromArgb(255, 255, 107, 107));
        }

        IsAvailable = successful > 0;
    }

    public string Name { get; }
    public string Identity { get; }
    public string Access { get; }
    public string Capability { get; }
    public string ReportInfo { get; }
    public string IconGlyph { get; }
    public string IconAutomationName { get; }
    public bool IsAvailable { get; }
    public Brush StatusBrush { get; }

    private static int CountAvailable(params object?[] values) => values.Count(value => value is not null);
}

public sealed class DiagnosticRowViewModel(
    string device,
    string capability,
    string status,
    string detail,
    Brush statusBrush)
{
    public string Device { get; } = device;
    public string Capability { get; } = capability;
    public string Status { get; } = status;
    public string Detail { get; } = detail;
    public Brush StatusBrush { get; } = statusBrush;
}

public sealed class ApplicationBindingRowViewModel(string executablePath, string profileName)
{
    public string ExecutablePath { get; } = executablePath;
    public string ExecutableName { get; } = Path.GetFileName(executablePath);
    public string ProfileName { get; } = profileName;
}
