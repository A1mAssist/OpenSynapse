using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.UI;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Owns the Blade page: the profile-backed performance, fan, lighting, charge and MUX state
/// plus every write that follows it. The shell only binds <c>Blade.*</c> and routes shortcuts.
/// </summary>
public sealed partial class BladeWorkspace : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan BrightnessVerificationDelay = TimeSpan.FromMilliseconds(150);

    private readonly IRazerDeviceTelemetryReader _deviceTelemetryReader;
    private readonly IBladeLightingController? _bladeLightingController;
    private readonly WindowsTouchpadController? _touchpadController;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly Func<ProfileDocument> _getProfile;
    private readonly Action<ProfileDocument> _setProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly Func<MachineSettings> _getMachineSettings;
    private readonly Func<CancellationToken, Task<bool>> _saveMachineSettingsAsync;
    private readonly Func<string, Func<Task>, CancellationToken, Action?, string?, string?, Task> _runDeviceOperation;
    private readonly Func<IReadOnlyList<DeviceDescriptor>> _deviceDescriptors;
    private readonly Func<RazerDeviceTelemetry?> _lastTelemetry;
    private readonly Func<IReadOnlySet<int>?> _refreshRateCycle;
    private readonly Func<bool> _isDisplayAvailable;
    private readonly Func<bool> _isBrightnessRestorePending;
    private readonly Func<bool> _isDisposed;
    private readonly Action<string> _setDeviceOperationError;
    private readonly Action _requestDeviceRefresh;
    private readonly Action<int> _internalDisplayRefreshRateChangedByUser;
    private readonly Action<string?> _applyFirmwareMcuVersion;
    private readonly Func<DisplayWorkspace> _display;
    private readonly Func<FanWorkspace> _fan;
    private readonly Func<LightingWorkspace> _lighting;
    private readonly Func<DeviceRowViewModel?> _bladeRow;
    private readonly Func<DeviceDescriptor?, CancellationToken, Task<string?>> _readSerialAsync;
    private readonly BladeViewModel _blade = new();

    private HashSet<BladePerformanceMode> _bladePerformanceCycleModes = [.. BladePerformanceModes];
    private IReadOnlyList<BladePerformanceModeCardViewModel>? _performanceModeCards;
    private string _activeBladeMappingPreset = BladeProfileSettings.Product710DefaultMappingPreset;
    private bool _activeSnapTapEnabled;
    private bool _suppressBladePerformanceApply;
    private bool _bladeCoolingPadConnected;
    private bool _padlessHyperBoostAccepted;
    private Func<Task<bool>>? _confirmPadlessHyperBoost;
    private string? _bladeSerial;
    private string? _bladeControlDevicePath;
    private ushort _bladeControlProductId;
    private int _bladeLightingPowerProfileIndex;
    private int _bladePerformancePowerProfileIndex;
    private int _bladeRefreshRatePowerProfileIndex;
    private int _bladeLogoPowerProfileIndex;
    private readonly object _bladeBrightnessGate = new();
    private Task _bladeBrightnessWriter = Task.CompletedTask;
    private readonly List<BladeBrightnessRequest> _bladeBrightnessRequests = [];
    private BladeBrightnessRequest? _writingBladeBrightness;
    private bool _bladeBrightnessWriterActive;
    private Task _bladeBrightnessVerification = Task.CompletedTask;
    private long _bladeBrightnessVerificationGeneration;
    private bool _gpuClockOffsetProbeStarted;
    private DateTimeOffset _gpuClockOffsetNextProbe;
    private bool _bladeGpuClockLimitAvailable;
    private double _bladeGpuClockLimitMhz;
    private string? _statusDeviceId;

    internal BladeWorkspace(
        IRazerDeviceTelemetryReader deviceTelemetryReader,
        IBladeLightingController? bladeLightingController,
        WindowsTouchpadController? touchpadController,
        LocalDiagnosticLog diagnosticLog,
        Func<ProfileDocument> getProfile,
        Action<ProfileDocument> setProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        Func<MachineSettings> getMachineSettings,
        Func<CancellationToken, Task<bool>> saveMachineSettingsAsync,
        Func<string, Func<Task>, CancellationToken, Action?, string?, string?, Task> runDeviceOperation,
        Func<IReadOnlyList<DeviceDescriptor>> deviceDescriptors,
        Func<PowerSourceResolver.PowerSourceEvidence> powerSourceEvidence,
        Func<RazerDeviceTelemetry?> lastTelemetry,
        Func<IReadOnlySet<int>?> refreshRateCycle,
        Func<bool> isDisplayAvailable,
        Func<bool> isBrightnessRestorePending,
        Func<bool> isDisposed,
        Action<string> setDeviceOperationError,
        Action requestDeviceRefresh,
        Action<int> internalDisplayRefreshRateChangedByUser,
        Action<string?> applyFirmwareMcuVersion,
        Func<DisplayWorkspace> display,
        Func<FanWorkspace> fan,
        Func<LightingWorkspace> lighting,
        Func<DeviceRowViewModel?> bladeRow,
        Func<DeviceDescriptor?, CancellationToken, Task<string?>> readSerialAsync,
        Func<RazerCoolingPadViewModel?> coolingPad)
    {
        _deviceTelemetryReader = deviceTelemetryReader;
        _bladeLightingController = bladeLightingController;
        _touchpadController = touchpadController;
        _diagnosticLog = diagnosticLog;
        _getProfile = getProfile;
        _setProfile = setProfile;
        _saveProfileAsync = saveProfileAsync;
        _getMachineSettings = getMachineSettings;
        _saveMachineSettingsAsync = saveMachineSettingsAsync;
        _runDeviceOperation = runDeviceOperation;
        _deviceDescriptors = deviceDescriptors;
        _powerSourceEvidence = powerSourceEvidence;
        _lastTelemetry = lastTelemetry;
        _coolingPad = coolingPad;
        _refreshRateCycle = refreshRateCycle;
        _isDisplayAvailable = isDisplayAvailable;
        _isBrightnessRestorePending = isBrightnessRestorePending;
        _isDisposed = isDisposed;
        _setDeviceOperationError = setDeviceOperationError;
        _requestDeviceRefresh = requestDeviceRefresh;
        _internalDisplayRefreshRateChangedByUser = internalDisplayRefreshRateChangedByUser;
        _applyFirmwareMcuVersion = applyFirmwareMcuVersion;
        _display = display;
        _fan = fan;
        _lighting = lighting;
        _bladeRow = bladeRow;
        _readSerialAsync = readSerialAsync;
        _gpuMode = new BladeGpuModeController(
            ReadDisplayMuxStatusAsync,
            SetDisplayMuxModeAsync,
            WindowsDedicatedGpuMode.GetStatus,
            WindowsDedicatedGpuMode.SetEnabledAsync,
            OpenSynapseServiceClient.TrySetEnabledAsync,
            WindowsDedicatedGpuMode.RestartFaultedAsync,
            WindowsDisplayTopology.Read);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<string?, ushort>? ControlDeviceChanged;
    internal event Action<BladePerformanceMode>? PerformanceModeChangedByUser;
    internal event Action<bool>? GamingModeChangedByUser;
    internal event Action<bool>? GamingModeStateChanged;
    internal event Action<bool>? TouchpadChangedByUser;
    internal event Action<bool>? OneTimeFullChargeChangedByUser;
    internal event Action? InputProfileChanged;

    private ProfileDocument Profile { get => _getProfile(); set => _setProfile(value); }
    private IReadOnlyList<DeviceDescriptor> ActiveDescriptors => _deviceDescriptors();
    private RazerDeviceTelemetry? LastTelemetry => _lastTelemetry();
    private bool IsDisplayAvailable => _isDisplayAvailable();
    private bool IsDisposed => _isDisposed();
    private Task<bool> SaveProfileAsync(CancellationToken cancellationToken) =>
        _saveProfileAsync(cancellationToken);

    private Task RunDeviceOperationAsync(
        string label,
        Func<Task> operation,
        CancellationToken cancellationToken,
        Action? restoreSelection = null,
        string? successVerb = null,
        string? failureVerb = null) =>
        _runDeviceOperation(label, operation, cancellationToken, restoreSelection, successVerb, failureVerb);

    private void RequestDeviceRefresh() => _requestDeviceRefresh();
    private void SetDeviceOperationError(string error) => _setDeviceOperationError(error);

    private ProfileDefinition GetActiveProfile() =>
        Profile.Profiles.TryGetValue(Profile.ActiveProfileName, out var profile)
            ? profile
            : throw new InvalidOperationException("The active profile is missing from the profile catalog.");
    public Visibility HyperboostOptionVisibility =>
        _blade.PerformanceModes.Contains(BladePerformanceMode.Hyperboost)
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>
    /// True when HyperBoost is offered without a Razer Cooling Pad. The user accepts
    /// the cooling risk explicitly before the first pad-less write.
    /// </summary>
    public bool HyperboostNeedsCoolingPadWarning =>
        !_bladeCoolingPadConnected &&
        BladeModelCapabilities.SupportsHyperBoost(_blade._bladeProductId);

    public string DeviceName { get => _blade._bladeDeviceName; private set => SetField(ref _blade._bladeDeviceName, value); }
    public string? Serial
    {
        get => _bladeSerial;
        private set
        {
            if (SetField(ref _bladeSerial, value))
            {
                OnPropertyChanged(nameof(SerialVisibility));
            }
        }
    }
    public Visibility SerialVisibility => string.IsNullOrWhiteSpace(Serial)
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string ProtocolText => StatusText;
    public string IdentityText => _bladeRow()?.Identity ?? "VID_1532 / PID_02C6";
    public string StatusText { get => _blade._bladeStatusText; private set => SetField(ref _blade._bladeStatusText, value); }
    public Visibility FilterDriverVisibility => _blade._bladeFilterDriverMissing
        ? Visibility.Visible
        : Visibility.Collapsed;
    public string FilterDriverDownloadUri =>
        RazerDeviceRegistry.BuiltIn.Find(0x1532, _blade._bladeProductId)?.FilterDriverInstallerUrl
        ?? string.Empty;

    internal void SetFilterDriverMissing(bool missing)
    {
        if (_blade._bladeFilterDriverMissing == missing)
        {
            return;
        }

        _blade._bladeFilterDriverMissing = missing;
        OnPropertyChanged(nameof(FilterDriverVisibility));
    }

    public string BrightnessText { get => _blade._bladeBrightnessText; private set => SetField(ref _blade._bladeBrightnessText, value); }
    public string BrightnessSelectionText { get => _blade._bladeBrightnessSelectionText; private set => SetField(ref _blade._bladeBrightnessSelectionText, value); }
    public double BrightnessPercent
    {
        get => _blade._bladeBrightnessPercent;
        set
        {
            if (SetField(ref _blade._bladeBrightnessPercent, Math.Clamp(value, 0, 100)))
            {
                BrightnessSelectionText = $"{_blade._bladeBrightnessPercent:0}%";
            }
        }
    }
    public bool CanSetBrightness
    {
        get => _blade._canSetBladeBrightness;
        private set
        {
            if (SetField(ref _blade._canSetBladeBrightness, value))
            {
                OnPropertyChanged(nameof(CanSetLighting));
                OnPropertyChanged(nameof(CanSetLightingPowerProfile));
                OnPropertyChanged(nameof(CanSetLightingSettings));
                OnPropertyChanged(nameof(CanWriteBrightness));
            }
        }
    }
    public bool CanWriteBrightness =>
        CanSetBrightness && BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId);
    public string PerformanceModeText { get => _blade._bladePerformanceModeText; private set => SetField(ref _blade._bladePerformanceModeText, value); }
    public IReadOnlyList<BladePerformanceModeCardViewModel> PerformanceModeCards =>
        _performanceModeCards ??= _blade.PerformanceModes
            .Select((mode, index) => new BladePerformanceModeCardViewModel(
                mode,
                index,
                AppStrings.Text(BladePerformanceModeTextKey(mode)),
                AppStrings.Text(BladePerformanceModeDescriptionKey(mode)),
                BladePerformanceModeGlyph(mode),
                index == PerformanceModeIndex))
            .ToArray();

    private void RefreshPerformanceModeCardSelection()
    {
        if (_performanceModeCards is null)
        {
            OnPropertyChanged(nameof(PerformanceModeCards));
            return;
        }

        foreach (var card in _performanceModeCards)
        {
            card.SetSelected(card.Index == PerformanceModeIndex);
        }
    }

    private string BladePerformanceModeDescriptionKey(BladePerformanceMode mode) => mode switch
    {
        BladePerformanceMode.Balanced => "BladePerformanceModeBalancedDescription",
        BladePerformanceMode.Gaming when _blade._bladeProductId == BladeModelCapabilities.Blade18_2026ProductId =>
            "BladePerformanceModeTurboDescription",
        BladePerformanceMode.Gaming => "BladePerformanceModeGamingDescription",
        BladePerformanceMode.Performance => "BladePerformanceModePerformanceDescription",
        BladePerformanceMode.BatterySaver => "BladePerformanceModeBatterySaverDescription",
        BladePerformanceMode.Custom => "BladePerformanceModeCustomDescription",
        BladePerformanceMode.Silent => "BladePerformanceModeSilentDescription",
        BladePerformanceMode.BalancedDc => "BladePerformanceModeBalancedDcDescription",
        BladePerformanceMode.Hyperboost => "BladePerformanceModeHyperboostDescription",
        _ => "BladePerformanceModePerformanceDescription",
    };

    private static string BladePerformanceModeGlyph(BladePerformanceMode mode) => mode switch
    {
        BladePerformanceMode.Balanced => "\uE9D2",
        BladePerformanceMode.Gaming => "\uE7FC",
        BladePerformanceMode.Performance => "\uE945",
        BladePerformanceMode.BatterySaver => "\uE83F",
        BladePerformanceMode.Custom => "\uE713",
        BladePerformanceMode.Silent => "\uE74F",
        BladePerformanceMode.BalancedDc => "\uE80F",
        BladePerformanceMode.Hyperboost => "\uEC4A",
        _ => "\uE945",
    };

    public string FanSpeedHelpText => AppStrings.Text("BladeFanSpeedHelp");
    public string FanControlHelpText => AppStrings.Text("BladeFanControlHelp");
    private string BladePerformanceModeTextKey(BladePerformanceMode mode) => mode switch
    {
        BladePerformanceMode.Balanced => "Text_9753B259",
        BladePerformanceMode.Gaming when _blade._bladeProductId == BladeModelCapabilities.Blade18_2026ProductId =>
            "BladePerformanceModeTurbo",
        BladePerformanceMode.Gaming => "Text_GamingPerformanceMode",
        BladePerformanceMode.Performance => "Text_C1DB7AE1",
        BladePerformanceMode.BatterySaver => "Text_EE43F0B1",
        BladePerformanceMode.Custom => "Text_598C5804",
        BladePerformanceMode.Silent => "Text_60E54E25",
        BladePerformanceMode.BalancedDc => "Text_A8CF66DD",
        BladePerformanceMode.Hyperboost => "HyperBoost",
        _ => "Text_C1DB7AE1",
    };
    public int PerformanceModeIndex
    {
        get => _blade._bladePerformanceModeIndex;
        set
        {
            if (!SetField(ref _blade._bladePerformanceModeIndex, value)) return;
            // The fixed-speed range is per mode, so re-clamp the stored target on a switch.
            _fan().ReclampFixedRpm();
            RefreshPerformanceModeCardSelection();
        }
    }
    public bool CanSetPerformanceMode
    {
        get => _blade._canSetBladePerformanceMode;
        private set
        {
            if (SetField(ref _blade._canSetBladePerformanceMode, value))
            {
                OnPropertyChanged(nameof(CanSetPerformancePowerProfile));
                OnPropertyChanged(nameof(PerformanceModeCardVisibility));
                OnPropertyChanged(nameof(CanWritePerformanceMode));
            }
        }
    }
    public bool CanWritePerformanceMode =>
        CanSetPerformanceMode && BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId);
    public bool CanSetLightingPowerProfile => CanSetBrightness;
    public bool CanSetPerformancePowerProfile => CanSetPerformanceMode;
    public bool CanSetLogoPowerProfile => CanSetLogo;
    public string FanText { get => _blade._bladeFanText; private set => SetField(ref _blade._bladeFanText, value); }
    public bool CanSetFan
    {
        get => _blade._canSetBladeFan;
        private set
        {
            if (SetField(ref _blade._canSetBladeFan, value))
            {
                OnPropertyChanged(nameof(FanCardVisibility));
                OnPropertyChanged(nameof(FanControlVisibility));
            }
        }
    }
    public string FanModeText { get => _blade._bladeFanModeText; private set => SetField(ref _blade._bladeFanModeText, value); }
    public string FanTargetRpmText { get => _blade._bladeFanTargetRpmText; private set => SetField(ref _blade._bladeFanTargetRpmText, value); }
    public string CurrentFanCpuRpmText { get => _blade._bladeCurrentFanCpuRpmText; private set => SetField(ref _blade._bladeCurrentFanCpuRpmText, value); }
    public string CurrentFanGpuRpmText { get => _blade._bladeCurrentFanGpuRpmText; private set => SetField(ref _blade._bladeCurrentFanGpuRpmText, value); }
    internal int? CurrentFanCpuRpm { get; private set; }
    internal int? CurrentFanGpuRpm { get; private set; }
    public string AdvancedFanCpuModeRawText { get => _blade._bladeAdvancedFanCpuModeRawText; private set => SetField(ref _blade._bladeAdvancedFanCpuModeRawText, value); }
    public string AdvancedFanGpuModeRawText { get => _blade._bladeAdvancedFanGpuModeRawText; private set => SetField(ref _blade._bladeAdvancedFanGpuModeRawText, value); }
    public string GameModeText { get => _blade._bladeGameModeText; private set => SetField(ref _blade._bladeGameModeText, value); }
    public bool GameModeEnabled
    {
        get => _blade._bladeGameModeEnabled;
        set
        {
            if (SetField(ref _blade._bladeGameModeEnabled, value))
            {
                OnPropertyChanged(nameof(CanApplyGamingMode));
            }
        }
    }
    public bool CanSetGamingMode =>
        _blade._bladeGameModeWriteSupported &&
        BladeModelCapabilities.AllowsControlWrites(_blade._bladeProductId) &&
        _blade._bladeGameModeState is byte state && state != 2;
    public bool CanApplyGamingMode =>
        CanSetGamingMode && GameModeEnabled != (_blade._bladeGameModeState != 0);
    public Visibility GamingModeVisibility =>
        CanSetGamingMode ? Visibility.Visible : Visibility.Collapsed;
    public string StartupAnimationText { get => _blade._bladeStartupAnimationText; private set => SetField(ref _blade._bladeStartupAnimationText, value); }
    public bool StartupAnimationEnabled
    {
        get => _blade._bladeStartupAnimationSelection;
        set
        {
            if (SetField(ref _blade._bladeStartupAnimationSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyStartupAnimation));
            }
        }
    }
    public bool CanSetStartupAnimation => _blade._bladeStartupAnimationEnabled is not null;
    public bool CanApplyStartupAnimation => CanSetStartupAnimation && StartupAnimationEnabled != _blade._bladeStartupAnimationEnabled;
    public Visibility LegacyBladeSettingsVisibility =>
        _blade._bladeAutoSleepEnabled is not null || _blade._bladeTimeToDim is not null ||
        _blade._bladeIndicatorLedEnabled is not null || _blade._bladeGpuCardDetection is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    public bool AutoSleepEnabled
    {
        get => _blade._bladeAutoSleepSelection;
        set
        {
            if (SetField(ref _blade._bladeAutoSleepSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyAutoSleep));
            }
        }
    }
    public bool CanSetAutoSleep =>
        _blade._bladeAutoSleepEnabled is not null && _blade._bladeAutoSleepWritable;
    public bool CanApplyAutoSleep =>
        CanSetAutoSleep && AutoSleepEnabled != _blade._bladeAutoSleepEnabled;
    public Visibility AutoSleepVisibility =>
        _blade._bladeAutoSleepEnabled is not null ? Visibility.Visible : Visibility.Collapsed;
    public IReadOnlyList<string> TimeToDimOptions =>
        [.. TimeToDimSeconds.Select(seconds => seconds.ToString())];
    public int TimeToDimIndex
    {
        get => _blade._bladeTimeToDimIndex;
        set
        {
            if (SetField(ref _blade._bladeTimeToDimIndex, value))
            {
                OnPropertyChanged(nameof(CanApplyTimeToDim));
            }
        }
    }
    public bool CanSetTimeToDim =>
        _blade._bladeTimeToDim is not null && _blade._bladeTimeToDimWritable;
    public bool CanApplyTimeToDim =>
        CanSetTimeToDim && TimeToDimIndex >= 0 && TimeToDimIndex < TimeToDimSeconds.Count &&
        TimeToDimSeconds[TimeToDimIndex] != _blade._bladeTimeToDim!.Value.Seconds;
    public Visibility TimeToDimVisibility =>
        _blade._bladeTimeToDim is not null ? Visibility.Visible : Visibility.Collapsed;
    public bool IndicatorLedEnabled
    {
        get => _blade._bladeIndicatorLedSelection;
        set
        {
            if (SetField(ref _blade._bladeIndicatorLedSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyIndicatorLed));
            }
        }
    }
    public bool CanSetIndicatorLed =>
        _blade._bladeIndicatorLedEnabled is not null && _blade._bladeIndicatorLedWritable;
    public bool CanApplyIndicatorLed =>
        CanSetIndicatorLed && IndicatorLedEnabled != _blade._bladeIndicatorLedEnabled;
    public Visibility IndicatorLedVisibility =>
        _blade._bladeIndicatorLedEnabled is not null ? Visibility.Visible : Visibility.Collapsed;
    public bool GpuCardDetectionEnabled
    {
        get => _blade._bladeGpuCardDetectionSelection;
        set
        {
            if (SetField(ref _blade._bladeGpuCardDetectionSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyGpuCardDetection));
            }
        }
    }
    public bool CanSetGpuCardDetection =>
        _blade._bladeGpuCardDetection is not null && _blade._bladeGpuCardDetectionWritable;
    public bool CanApplyGpuCardDetection =>
        CanSetGpuCardDetection && GpuCardDetectionEnabled != _blade._bladeGpuCardDetection?.Enabled;
    public Visibility GpuCardDetectionVisibility =>
        _blade._bladeGpuCardDetection is not null ? Visibility.Visible : Visibility.Collapsed;
    private IReadOnlyList<int> TimeToDimSeconds => _blade.TimeToDimSeconds;
    public IReadOnlyList<string> NativeDisplayModeOptions => AppStrings.Texts("UHD", "FHD");
    public int NativeDisplayModeIndex
    {
        get => _blade._bladeNativeDisplayModeIndex;
        set
        {
            if (SetField(ref _blade._bladeNativeDisplayModeIndex, value))
            {
                OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
            }
        }
    }
    public bool CanSetNativeDisplayMode =>
        _blade._bladeNativeDisplayMode is not null &&
        BladeModelCapabilities.SupportsNativeDisplayMode(
            _blade._bladeProductId, _blade._bladeSkuHardwareConfiguration);
    public bool CanApplyNativeDisplayMode =>
        CanSetNativeDisplayMode &&
        NativeDisplayModeIndex is >= 0 and <= 1 &&
        NativeDisplayModeIndex != (int)_blade._bladeNativeDisplayMode!.Value;
    public Visibility NativeDisplayModeVisibility =>
        CanSetNativeDisplayMode ? Visibility.Visible : Visibility.Collapsed;
    public string FnKeyText { get => _blade._bladeFnKeyText; private set => SetField(ref _blade._bladeFnKeyText, value); }
    public bool FnKeyMultimediaPrimary
    {
        get => _blade._bladeFnKeySelection;
        set
        {
            if (SetField(ref _blade._bladeFnKeySelection, value))
            {
                OnPropertyChanged(nameof(CanApplyFnKey));
                OnPropertyChanged(nameof(FnKeyFunctionPrimarySelected));
            }
        }
    }
    public bool CanSetFnKey => _blade._bladeFnKeyMultimediaPrimary is not null;
    public bool FnKeyFunctionPrimarySelected => CanSetFnKey && !FnKeyMultimediaPrimary;
    public Visibility FnKeyCardVisibility =>
        CanSetFnKey ? Visibility.Visible : Visibility.Collapsed;
    public bool CanApplyFnKey =>
        CanSetFnKey && FnKeyMultimediaPrimary != _blade._bladeFnKeyMultimediaPrimary;
    public bool LocalDimmingEnabled
    {
        get => _blade._bladeLocalDimmingSelection;
        set
        {
            if (SetField(ref _blade._bladeLocalDimmingSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyLocalDimming));
            }
        }
    }
    public bool CanSetLocalDimming =>
        _blade._bladeLocalDimmingEnabled is not null &&
        BladeModelCapabilities.SupportsLocalDimming(_blade._bladeProductId);
    public bool CanApplyLocalDimming =>
        CanSetLocalDimming && LocalDimmingEnabled != _blade._bladeLocalDimmingEnabled;
    public Visibility LocalDimmingVisibility =>
        CanSetLocalDimming ? Visibility.Visible : Visibility.Collapsed;
    public bool CpuPowerBoostEnabled
    {
        get => _blade._bladeCpuPowerBoostSelection;
        set
        {
            if (SetField(ref _blade._bladeCpuPowerBoostSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
            }
        }
    }
    public bool CanSetCpuPowerBoost => _blade._bladeCpuPowerBoostEnabled is not null;
    public bool CanApplyCpuPowerBoost =>
        CanSetCpuPowerBoost && CpuPowerBoostEnabled != _blade._bladeCpuPowerBoostEnabled;
    public Visibility CpuPowerBoostVisibility =>
        CanSetCpuPowerBoost ? Visibility.Visible : Visibility.Collapsed;
    public string OneTimeFullChargeText { get => _blade._bladeOneTimeFullChargeText; private set => SetField(ref _blade._bladeOneTimeFullChargeText, value); }
    public bool OneTimeFullChargeEnabled
    {
        get => _blade._bladeOneTimeFullChargeSelection;
        set
        {
            if (SetField(ref _blade._bladeOneTimeFullChargeSelection, value))
            {
                OnPropertyChanged(nameof(CanApplyOneTimeFullCharge));
            }
        }
    }
    public bool CanSetOneTimeFullCharge =>
        _blade._bladeOneTimeFullChargeEnabled is not null && _blade._confirmedBladeChargeLimitIndex >= 0 &&
        BladeChargeLimits[_blade._confirmedBladeChargeLimitIndex] < 100;
    public bool CanApplyOneTimeFullCharge =>
        CanSetOneTimeFullCharge && OneTimeFullChargeEnabled != _blade._bladeOneTimeFullChargeEnabled;
    public string ChargeLimitText { get => _blade._bladeChargeLimitText; private set => SetField(ref _blade._bladeChargeLimitText, value); }
    public IReadOnlyList<string> ChargeLimitOptions => AppStrings.Texts("50%", "55%", "60%", "65%", "70%", "75%", "80%", "Text_0E45FC12");
    public int ChargeLimitIndex { get => _blade._bladeChargeLimitIndex; set => SetField(ref _blade._bladeChargeLimitIndex, value); }
    public bool CanSetChargeLimit
    {
        get => _blade._canSetBladeChargeLimit;
        private set
        {
            if (SetField(ref _blade._canSetBladeChargeLimit, value))
            {
                OnPropertyChanged(nameof(ChargeLimitCardVisibility));
            }
        }
    }
    public IReadOnlyList<string> CpuBoostOptions
    {
        get
        {
            var modes = BladeModelCapabilities.CpuBoostModesFor(_blade._bladeProductId);
            return AppStrings.Texts(
                [.. modes.Select(mode => mode switch
                {
                    BladeCpuBoostMode.Low => "Text_CB5F70D1",
                    BladeCpuBoostMode.Medium => "Text_28619638",
                    BladeCpuBoostMode.High => "Text_5C1F32A7",
                    BladeCpuBoostMode.Boost => "BladeCpuBoostHighestOption",
                    BladeCpuBoostMode.Undervolt => "Text_BAA7D10B",
                    _ => throw new ArgumentOutOfRangeException(nameof(mode)),
                })]);
        }
    }
    public string CpuBoostText { get => _blade._bladeCpuBoostText; private set => SetField(ref _blade._bladeCpuBoostText, value); }
    public int CpuBoostIndex { get => _blade._bladeCpuBoostIndex; set => SetField(ref _blade._bladeCpuBoostIndex, value); }
    public bool CanSetCpuBoost => _blade._hasBladeCpuBoost && IsCustomMode;
    public IReadOnlyList<string> GpuBoostOptions => AppStrings.Texts("Text_CB5F70D1", "Text_28619638", "Text_5C1F32A7");
    public string GpuBoostText { get => _blade._bladeGpuBoostText; private set => SetField(ref _blade._bladeGpuBoostText, value); }
    public int GpuBoostIndex { get => _blade._bladeGpuBoostIndex; set => SetField(ref _blade._bladeGpuBoostIndex, value); }
    public bool CanSetGpuBoost => _blade._hasBladeGpuBoost && IsCustomMode;
    public string MaxFanText { get => _blade._bladeMaxFanText; private set => SetField(ref _blade._bladeMaxFanText, value); }
    public bool MaxFanEnabled { get => _blade._bladeMaxFanEnabled; set => SetField(ref _blade._bladeMaxFanEnabled, value); }
    public bool CanSetMaxFan => _blade._hasBladeMaxFan && _blade.SupportsMaxFan && IsCustomMode;

    /// <summary>
    /// Raises the view-model properties whose visibility or option list depends on the
    /// exact Blade product id.
    /// </summary>
    public void RefreshModelCapabilityUi()
    {
        // The model can change the exposed performance-mode list, so the selected index
        // and the shortcut cycle have to be rebuilt against the new list.
        var performanceModes = _blade.PerformanceModes;
        _blade.ResetCustomCapabilityFlags();
        _blade._bladePerformanceModeIndex = -1;
        _blade._confirmedBladePerformanceModeIndex = -1;
        _performanceModeCards = null;
        _bladePerformanceCycleModes.RemoveWhere(mode => !performanceModes.Contains(mode));
        if (_bladePerformanceCycleModes.Count == 0)
        {
            _bladePerformanceCycleModes = [.. performanceModes];
        }
        _lighting().RaiseChromaVisibilityChanged();
        NotifyStateChanged();
        _fan().RaiseCurveStateChanged();
    }

    /// <summary>True when the exact model exposes the given performance mode.</summary>
    internal bool IsPerformanceModeAvailable(BladePerformanceMode mode) =>
        _blade.PerformanceModes.Contains(mode);

    /// <summary>Collapses the Max Fan column entirely on models without that switch.</summary>
    public Visibility MaxFanVisibility => _blade.SupportsMaxFan
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>
    /// Collapses a whole control when the model answered none of its reads. An RazerHid-only
    /// Blade never sees the Power Mode Control bitmask, so these cards would otherwise render
    /// a row of "--" placeholders that look like a broken device.
    /// </summary>
    public Visibility PerformanceModeCardVisibility => CanSetPerformanceMode
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility FanCardVisibility => CanSetFan
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>Only show fan mode controls a model has an admitted write for.</summary>
    public Visibility FanControlVisibility =>
        CanSetFan && (_blade.SupportsFixedFan || _blade.SupportsFanCurve)
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility ChargeLimitCardVisibility => CanSetChargeLimit
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility LogoCardVisibility => CanSetLogo
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility CpuBoostVisibility => _blade._hasBladeCpuBoost
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility GpuBoostVisibility => CanSetGpuBoost
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility CustomPerformanceVisibility =>
        IsCustomMode &&
        (_blade._hasBladeCpuBoost || _blade._hasBladeGpuBoost || _blade._hasBladeMaxFan)
            ? Visibility.Visible
            : Visibility.Collapsed;
    public IReadOnlyList<string> LogoOptions => AppStrings.Texts("Text_39B523BD", "Text_6295D9CB", "Text_7EDA32B9");
    public string LogoText { get => _blade._bladeLogoText; private set => SetField(ref _blade._bladeLogoText, value); }
    public int LogoIndex { get => _blade._bladeLogoIndex; set => SetField(ref _blade._bladeLogoIndex, value); }
    public bool CanSetLogo
    {
        get => _blade._canSetBladeLogo;
        private set
        {
            if (SetField(ref _blade._canSetBladeLogo, value))
            {
                OnPropertyChanged(nameof(CanSetLogoPowerProfile));
                OnPropertyChanged(nameof(LogoCardVisibility));
            }
        }
    }
    public string TouchpadText { get => _blade._bladeTouchpadText; private set => SetField(ref _blade._bladeTouchpadText, value); }
    public bool TouchpadEnabled { get => _blade._bladeTouchpadEnabled; private set => SetField(ref _blade._bladeTouchpadEnabled, value); }
    public bool CanSetTouchpad => _blade._canSetBladeTouchpad;
    private PowerSourceKind SelectedLightingPowerState =>
        SelectedPowerSource(_bladeLightingPowerProfileIndex);
    internal PowerSourceKind SelectedPerformancePowerState =>
        SelectedPowerSource(_bladePerformancePowerProfileIndex);
    private PowerSourceKind SelectedRefreshRatePowerState =>
        SelectedPowerSource(_bladeRefreshRatePowerProfileIndex);
    private PowerSourceKind SelectedLogoPowerState =>
        SelectedPowerSource(_bladeLogoPowerProfileIndex);

    private bool IsSelectedLightingPowerActive =>
        _bladeLightingPowerProfileIndex == 0 || SelectedLightingPowerState == CurrentPowerSource;

    internal bool IsSelectedPerformancePowerActive =>
        _bladePerformancePowerProfileIndex == 0 || SelectedPerformancePowerState == CurrentPowerSource;

    internal bool IsSelectedRefreshRatePowerActive =>
        _bladeRefreshRatePowerProfileIndex == 0 || SelectedRefreshRatePowerState == CurrentPowerSource;

    private bool IsSelectedLogoPowerActive =>
        _bladeLogoPowerProfileIndex == 0 || SelectedLogoPowerState == CurrentPowerSource;

    private PowerProfileOverrides? SelectedLightingPowerOverrides =>
        SelectedPowerOverrides(_bladeLightingPowerProfileIndex);
    private PowerProfileOverrides? SelectedPerformancePowerOverrides =>
        SelectedPowerOverrides(_bladePerformancePowerProfileIndex);
    private PowerProfileOverrides? SelectedRefreshRatePowerOverrides =>
        SelectedPowerOverrides(_bladeRefreshRatePowerProfileIndex);
    private PowerProfileOverrides? SelectedLogoPowerOverrides =>
        SelectedPowerOverrides(_bladeLogoPowerProfileIndex);

    private PowerProfileOverrides? CurrentPowerOverrides => CurrentPowerSource switch
    {
        PowerSourceKind.FullPowerAdapter => GetActiveProfile().PluggedIn,
        PowerSourceKind.LowPowerAdapter => GetActiveProfile().LowPowerAdapter,
        PowerSourceKind.Battery => GetActiveProfile().OnBattery,
        _ => null,
    };

    private BladeProfileSettings EditableLightingBladeProfile =>
        SelectedLightingPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    internal BladeProfileSettings EditablePerformanceProfile =>
        SelectedPerformancePowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    internal BladeProfileSettings EditableRefreshRateProfile =>
        SelectedRefreshRatePowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    private BladeProfileSettings EditableLogoProfile =>
        SelectedLogoPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    private LightingProfile EditableLightingProfile =>
        SelectedLightingPowerOverrides?.Lighting ?? GetActiveProfile().Global.Lighting;

    internal bool IsLightingColorFlyoutOpen { get; set; }

    internal void RefreshPerformanceEditor()
    {
        if (Profile.Profiles.Count == 0)
        {
            return;
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        var powerState = SelectedPerformancePowerState;
        var bladeProfile = blade is null
            ? EditablePerformanceProfile
            : ProfileResolver.Resolve(Profile, blade, powerState).Blade;
        if (bladeProfile.PerformanceMode is byte rawPerformanceMode &&
            Enum.IsDefined(typeof(BladePerformanceMode), rawPerformanceMode))
        {
            SetPerformanceMode((BladePerformanceMode)rawPerformanceMode, confirm: false);
        }
        else PerformanceModeIndex = _blade._confirmedBladePerformanceModeIndex;

        // Restore the Max Fan toggle from the same resolved profile, so the switch shows
        // the configured value instead of only the last value read back from the device.
        if (bladeProfile.MaxFanMode is byte rawMaxFan &&
            Enum.IsDefined(typeof(BladeMaxFanMode), rawMaxFan))
        {
            SetMaxFan((BladeMaxFanMode)rawMaxFan, confirm: false);
        }
        else SetMaxFan(_blade._confirmedBladeMaxFanEnabled ? BladeMaxFanMode.Enabled : BladeMaxFanMode.Disabled, confirm: false);

        _fan().RefreshBladeFanCurveEditor();
    }

    internal void RefreshLogoEditor()
    {
        if (Profile.Profiles.Count == 0)
        {
            return;
        }

        var blade = ActiveDescriptors.FirstOrDefault(device =>
            DeviceProtocolFamilies.IsBlade(device.ProtocolFamily));
        var powerState = SelectedLogoPowerState;
        var bladeProfile = blade is null
            ? EditableLogoProfile
            : ProfileResolver.Resolve(Profile, blade, powerState).Blade;
        if (bladeProfile.LogoMode is byte rawLogoMode &&
            Enum.IsDefined(typeof(BladeLogoMode), (int)rawLogoMode))
        {
            SetLogo((BladeLogoMode)rawLogoMode, confirm: false);
        }
    }

    private void RefreshInternalDisplayRateEditor()
    {
        var configured = EditableRefreshRateProfile.RefreshRateHertz;
        if (configured is int hertz && _display().InternalDisplayRefreshRates.Contains(hertz))
        {
            _display().InternalDisplayRefreshRateHertz = hertz;
        }
        else
        {
            _display().RaiseRefreshRateIndexChanged();
        }
    }

    internal void RefreshLocalization() => OnPropertyChanged(string.Empty);

    /// <summary>
    /// Re-evaluates every binding after a low-frequency state change (mode, device,
    /// capability probe). High-frequency paths must keep notifying individual properties.
    /// </summary>
    private void NotifyStateChanged() => OnPropertyChanged(string.Empty);

    private void SetBrightness(byte brightness, bool confirm = true)
    {
        var percent = Math.Round(brightness * 100d / 255, MidpointRounding.AwayFromZero);
        BrightnessText = $"{percent:0}%";
        lock (_bladeBrightnessGate)
        {
            if (confirm) _blade._confirmedBladeBrightnessPercent = percent;
            if (_bladeBrightnessRequests.Count == 0)
            {
                BrightnessPercent = percent;
            }
        }
    }

    internal void SetPerformanceMode(BladePerformanceMode mode, bool confirm = true)
    {
        var previous = (_blade._bladePerformanceModeText, _blade._bladePerformanceModeIndex, _blade._confirmedBladePerformanceModeIndex);
        _blade.SetPerformanceMode(mode, confirm);
        if (previous == (_blade._bladePerformanceModeText, _blade._bladePerformanceModeIndex, _blade._confirmedBladePerformanceModeIndex))
        {
            return;
        }

        OnPropertyChanged(nameof(PerformanceModeText));
        OnPropertyChanged(nameof(PerformanceModeIndex));
        RefreshPerformanceModeCardSelection();
        OnPropertyChanged(nameof(CanSetCpuBoost));
        OnPropertyChanged(nameof(CanSetGpuBoost));
        OnPropertyChanged(nameof(CanSetMaxFan));
        OnPropertyChanged(nameof(CustomPerformanceVisibility));
        NotifyStateChanged();
    }

    private void SetGameMode(BladeGameModeTelemetry? gameMode)
    {
        _blade.SetGameMode(gameMode);
        GamingModeStateChanged?.Invoke(_blade._bladeGameModeEnabled);
        OnPropertyChanged(nameof(GameModeText));
        OnPropertyChanged(nameof(GameModeEnabled));
        OnPropertyChanged(nameof(CanSetGamingMode));
        OnPropertyChanged(nameof(CanApplyGamingMode));
        OnPropertyChanged(nameof(GamingModeVisibility));
    }

    private void SetFnKey(bool? multimediaPrimary)
    {
        _blade.SetFnKey(multimediaPrimary);
        _blade._bladeFnKeySelection = multimediaPrimary ?? false;
        OnPropertyChanged(nameof(FnKeyText));
        OnPropertyChanged(nameof(FnKeyMultimediaPrimary));
        OnPropertyChanged(nameof(CanSetFnKey));
        OnPropertyChanged(nameof(CanApplyFnKey));
        OnPropertyChanged(nameof(FnKeyCardVisibility));
        OnPropertyChanged(nameof(FnKeyFunctionPrimarySelected));
    }

    private void SetAutoSleep(bool? enabled, bool writable)
    {
        _blade.SetAutoSleep(enabled, writable);
        OnPropertyChanged(nameof(AutoSleepEnabled));
        OnPropertyChanged(nameof(CanSetAutoSleep));
        OnPropertyChanged(nameof(CanApplyAutoSleep));
        OnPropertyChanged(nameof(AutoSleepVisibility));
        OnPropertyChanged(nameof(LegacyBladeSettingsVisibility));
    }

    private void SetTimeToDim(BladeTimeToDimTelemetry? state, bool writable)
    {
        if (_blade._bladeTimeToDim == state &&
            _blade._bladeTimeToDimWritable == writable)
        {
            // Telemetry repeats the same value every tick. Reassigning the ComboBox
            // source here made an empty/zero selection flicker on every refresh.
            return;
        }

        _blade.SetTimeToDim(state, writable);
        OnPropertyChanged(nameof(TimeToDimOptions));
        OnPropertyChanged(nameof(TimeToDimIndex));
        OnPropertyChanged(nameof(CanSetTimeToDim));
        OnPropertyChanged(nameof(CanApplyTimeToDim));
        OnPropertyChanged(nameof(TimeToDimVisibility));
        OnPropertyChanged(nameof(LegacyBladeSettingsVisibility));
    }

    private void SetIndicatorLed(bool? enabled, bool writable)
    {
        _blade.SetIndicatorLed(enabled, writable);
        OnPropertyChanged(nameof(IndicatorLedEnabled));
        OnPropertyChanged(nameof(CanSetIndicatorLed));
        OnPropertyChanged(nameof(CanApplyIndicatorLed));
        OnPropertyChanged(nameof(IndicatorLedVisibility));
        OnPropertyChanged(nameof(LegacyBladeSettingsVisibility));
    }

    private void SetGpuCardDetection(BladeGpuCardDetectionTelemetry? state, bool writable)
    {
        _blade.SetGpuCardDetection(state, writable);
        OnPropertyChanged(nameof(GpuCardDetectionEnabled));
        OnPropertyChanged(nameof(CanSetGpuCardDetection));
        OnPropertyChanged(nameof(CanApplyGpuCardDetection));
        OnPropertyChanged(nameof(GpuCardDetectionVisibility));
        OnPropertyChanged(nameof(LegacyBladeSettingsVisibility));
    }

    private void SetChargeLimit(int percent)
    {
        _blade.SetChargeLimit(percent);
        OnPropertyChanged(nameof(ChargeLimitText));
        OnPropertyChanged(nameof(ChargeLimitIndex));
        OnPropertyChanged(nameof(CanSetOneTimeFullCharge));
        OnPropertyChanged(nameof(CanApplyOneTimeFullCharge));
    }

    internal bool IsCustomMode => _blade.IsCustomMode;

    private void ClearCustomPerformance()
    {
        _blade.ClearCustomPerformance();
        OnPropertyChanged(nameof(CpuBoostText));
        OnPropertyChanged(nameof(CpuBoostIndex));
        OnPropertyChanged(nameof(GpuBoostText));
        OnPropertyChanged(nameof(GpuBoostIndex));
        OnPropertyChanged(nameof(MaxFanText));
        OnPropertyChanged(nameof(MaxFanEnabled));
        _fan().RaiseCurveStateChanged();
    }

    private void SetCpuBoost(BladeCpuBoostMode mode)
    {
        _blade.SetCpuBoost(mode);
        OnPropertyChanged(nameof(CpuBoostText));
        OnPropertyChanged(nameof(CpuBoostIndex));
        OnPropertyChanged(nameof(CanSetCpuBoost));
        OnPropertyChanged(nameof(CpuBoostVisibility));
        OnPropertyChanged(nameof(CustomPerformanceVisibility));
    }

    private void SetGpuBoost(BladeGpuBoostMode mode)
    {
        _blade.SetGpuBoost(mode);
        OnPropertyChanged(nameof(GpuBoostText));
        OnPropertyChanged(nameof(GpuBoostIndex));
        OnPropertyChanged(nameof(CanSetGpuBoost));
        OnPropertyChanged(nameof(GpuBoostVisibility));
        OnPropertyChanged(nameof(CustomPerformanceVisibility));
    }

    private void SetMaxFan(BladeMaxFanMode mode, bool confirm = true)
    {
        _blade.SetMaxFan(mode, confirm);
        OnPropertyChanged(nameof(MaxFanText));
        OnPropertyChanged(nameof(MaxFanEnabled));
        OnPropertyChanged(nameof(CanSetMaxFan));
        OnPropertyChanged(nameof(CustomPerformanceVisibility));
    }

    private void SetLogo(BladeLogoMode mode, bool confirm = true)
    {
        _blade.SetLogo(mode, confirm);
        OnPropertyChanged(nameof(LogoText));
        OnPropertyChanged(nameof(LogoIndex));
    }

    private static string FormatOptionalState(bool? value) => value switch
    {
        true => AppStrings.Text("Text_F55AD712"),
        false => AppStrings.Text("Text_7E3B0F3C"),
        null => "--",
    };

    private static string FormatRawByte(byte? value) =>
        value is byte raw ? $"0x{raw:X2} ({raw})" : AppStrings.Text("Text_D0003708");

}
