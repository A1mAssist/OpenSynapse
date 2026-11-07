using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly Color DefaultPrimaryColor = Color.FromArgb(255, 0, 255, 102);
    private static readonly Color DefaultSecondaryColor = Color.FromArgb(255, 0, 153, 255);
    private static readonly Color DefaultTertiaryColor = Color.FromArgb(255, 255, 32, 64);
    private readonly RazerHidDeviceService _service;
    private readonly HashSet<RazerHidBackendCapability> _unsupported = [];
    private readonly HashSet<(RazerHidLedZone Zone, RazerHidLightingEffect Effect)> _unsupportedLighting = [];
    private readonly HashSet<RazerHidLedZone> _unsupportedBrightnessWrites = [];
    private readonly HashSet<RazerHidLedZone> _unsupportedBrightnessReads = [];
    private readonly HashSet<RazerHidLedZone> _unsupportedLedStateWrites = [];
    private readonly HashSet<RazerHidLedZone> _unsupportedLedStateReads = [];
    private readonly HashSet<RazerHidLedZone> _unsupportedLedColorReads = [];
    private readonly HashSet<RazerHidLedZone> _unsupportedLedEffectReads = [];
    private RazerHidBasicState? _basicState;
    private string _errorText;
    private bool _requiresRescan;
    private bool _isBasicBusy;
    private bool _isPollingBusy;
    private bool _isDpiBusy;
    private bool _isIdleBusy;
    private bool _isLowBatteryBusy;
    private bool _isAutoSleepBusy;
    private bool _isChargeLimiterBusy;
    private bool _isBrightnessBusy;
    private bool _isLightingBusy;
    private bool _isLightingLoading;
    private bool _isScrollModeBusy;
    private bool _isScrollAccelerationBusy;
    private bool _isSmartReelBusy;
    private bool _isSmartReelThresholdBusy;
    private bool _isScrollFreeNotchedBusy;
    private bool _isScrollAccelerationLevelBusy;
    private bool _isOnboardProfileBusy;
    private bool _isScrollLoading;
    private bool _isKeyswitchBusy;
    private bool _isKeyswitchLoading;
    private bool _isFnBusy;
    private bool _isDpiStagesLoading;
    private bool _isDpiStagesBusy;
    private bool _isHyperPollingBusy;
    private bool _isLedStateBusy;
    private bool _isMatrixBusy;
    private int _selectedPollingRate;
    private int _dpiX = 800;
    private int _dpiY = 800;
    private int _idleTimeoutSeconds = 300;
    private int _lowBatteryThresholdPercent = 15;
    private bool _autoSleepEnabled = true;
    private int _chargeLimiterPercent = 80;
    private bool _chargeLimiterEnabled = true;
    private byte _brightness = 255;
    private RazerHidLedZone _selectedLightingZone;
    private RazerHidLightingEffect _selectedLightingEffect;
    private Color _primaryColor = DefaultPrimaryColor;
    private Color _secondaryColor = DefaultSecondaryColor;
    private Color _tertiaryColor = DefaultTertiaryColor;
    private byte _lightingSpeed = 2;
    private byte _lightingDirection = 1;
    private byte _scrollMode;
    private bool _scrollAccelerationEnabled;
    private bool _smartReelEnabled;
    private byte _smartReelThreshold = 50;
    private int _scrollFreeNotchedModeIndex;
    private byte _scrollAccelerationLevel;
    private byte _onboardProfileCount;
    private byte _onboardMaxProfiles;
    private byte _activeOnboardProfileId;
    private byte _selectedOnboardProfileId = 1;
    private string _onboardProfileStatusText = string.Empty;
    private RazerHidKeyswitchOptimization _keyswitchOptimization;
    private byte _activeDpiStage = 1;
    private byte _hyperPollingIndicatorMode = 1;
    private bool _ledEnabled = true;
    private bool _ledStateKnown;
    private bool _brightnessKnown;
    private readonly Func<PowerSourceKind>? _powerStateProvider;
    private readonly Func<PowerSourceKind, LightingProfile?>? _lightingProfileResolver;
    private readonly Func<PowerSourceKind, LightingProfile, CancellationToken, Task<bool>>? _lightingProfileSaver;
    private readonly Func<bool>? _lightingEnabledResolver;
    private readonly Func<bool>? _chromaOverrideResolver;
    private readonly Func<bool, bool, CancellationToken, Task<bool>>? _lightingSettingsSaver;
    private readonly Func<IReadOnlyList<MacroDefinition>>? _localMacros;
    private readonly RazerHidSoftwareLightingRuntime? _softwareLightingRuntime;
    private WasapiAudioMeterAdapter? _softwareAudioInput;
    private TimeSpan _softwareLightingElapsed;
    private TimeSpan _softwareLightingDelta = TimeSpan.FromMilliseconds(1000d / 60d);
    private double _softwareAudioLevel;
    private int _lightingPowerProfileIndex;
    private bool _lightingEnabled;
    private bool _chromaOverrideEnabled;
    private bool _chromaIntegrationEnabled;
    private bool _isLightingSettingsBusy;
    private bool _operationSucceeded;
    private string? _operationStatusOverride;
    private int _disposed;

    public RazerHidDeviceViewModel(
        RazerHidDeviceService service,
        RazerHidDeviceConnection connection,
        Func<PowerSourceKind>? powerStateProvider = null,
        Func<PowerSourceKind, LightingProfile?>? lightingProfileResolver = null,
        Func<PowerSourceKind, LightingProfile, CancellationToken, Task<bool>>? lightingProfileSaver = null,
        Func<bool>? lightingEnabledResolver = null,
        Func<bool>? chromaOverrideResolver = null,
        Func<bool, bool, CancellationToken, Task<bool>>? lightingSettingsSaver = null,
        bool chromaIntegrationEnabled = true,
        Func<IReadOnlyList<MacroDefinition>>? localMacros = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Soundbar = service.CreateLeviathanV2XAudio(connection) is { } audio
            ? new LeviathanV2XAudioViewModel(audio)
            : null;
        if (connection.Definition.MatrixDimensions is not null &&
            connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite))
        {
            _softwareLightingRuntime = new RazerHidSoftwareLightingRuntime(
                service,
                () => [Connection],
                _ => LightingEnabled,
                _ => ChromaOverrideEnabled,
                connection.InstanceId);
        }
        _powerStateProvider = powerStateProvider;
        _lightingProfileResolver = lightingProfileResolver;
        _lightingProfileSaver = lightingProfileSaver;
        _lightingEnabledResolver = lightingEnabledResolver;
        _chromaOverrideResolver = chromaOverrideResolver;
        _lightingSettingsSaver = lightingSettingsSaver;
        _localMacros = localMacros;
        _lightingEnabled = lightingEnabledResolver?.Invoke() ?? true;
        _chromaOverrideEnabled = chromaOverrideResolver?.Invoke() ?? true;
        _chromaIntegrationEnabled = chromaIntegrationEnabled;
        _errorText = string.IsNullOrWhiteSpace(connection.Error)
            ? string.Empty
            : connection.EndpointState == RazerHidEndpointState.RecognizedButUnresolved
                ? AppStrings.FormatText("RazerHidDeviceNotReady", CategoryText)
                : AppStrings.Text("RazerHidProtocolRescanRequired");
        _selectedLightingZone = LightingZones.Contains(connection.Definition.DefaultLedZone)
            ? connection.Definition.DefaultLedZone : LightingZones.FirstOrDefault();
        _selectedLightingEffect = LightingEffects.FirstOrDefault();
        _selectedPollingRate = PollingOptions.FirstOrDefault();
        InitializeMatrixEditor();
        RebuildScrollWheelModeChoices();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        DisposeMatrixEditor();
        try
        {
            await StopSoftwareLightingAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (_softwareLightingRuntime is not null)
            {
                await _softwareLightingRuntime.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public RazerHidDeviceConnection Connection { get; }
    public LeviathanV2XAudioViewModel? Soundbar { get; }
    public Visibility SoundbarVisibility => Soundbar is null ? Visibility.Collapsed : Visibility.Visible;
    public Task LoadSoundbarAsync(CancellationToken cancellationToken = default) =>
        Soundbar?.LoadAsync(cancellationToken) ?? Task.CompletedTask;
    public string InstanceId => Connection.InstanceId;
    internal bool HasRunningSoftwareLighting => _softwareLightingRuntime?.IsRunning == true;
    public string Name => Connection.Definition.DisplayName;
    public string CategoryText => Connection.Definition.Category switch
    {
        DeviceCategory.Mouse => AppStrings.Text("Text_4B32CEE8"),
        DeviceCategory.Laptop => AppStrings.Text("Text_66E7127F"),
        DeviceCategory.Keyboard => AppStrings.Text("Text_7D4E2D8B"),
        DeviceCategory.MouseMat => AppStrings.Text("Text_A3A74479"),
        DeviceCategory.Monitor => AppStrings.Text("Text_2E486BCB"),
        DeviceCategory.Accessory => AppStrings.Text("Text_71E692AA"),
        _ => AppStrings.Text("Text_CAF15352"),
    };
    public string Identity => $"VID_1532 / PID_{Connection.Definition.ProductId:X4}";
    public string StatusText => RequiresRescan
        ? AppStrings.Text("Text_C0E6F3C9")
        : Connection.EndpointState switch
        {
            RazerHidEndpointState.Resolved when ProtocolTotalCount > 0 =>
                AppStrings.FormatText(
                    ProtocolAvailableCount == ProtocolTotalCount
                        ? "ProtocolAvailableCount"
                        : "ProtocolPartiallyAvailableCount",
                    ProtocolAvailableCount,
                    ProtocolTotalCount),
            RazerHidEndpointState.Resolved => AppStrings.Text("Text_C097B416"),
            RazerHidEndpointState.RecognizedButUnresolved => AppStrings.Text("Text_242E08F4"),
            _ => AppStrings.Text("Text_D3632B96"),
        };
    public int ProtocolTotalCount => Connection.Capabilities.Count;
    public IReadOnlyList<DeviceProtocolDetailViewModel> ProtocolDetails =>
        BuildProtocolDetails(Connection, _unsupported, _basicState?.Errors);

    internal static IReadOnlyList<DeviceProtocolDetailViewModel> BuildProtocolDetails(
        RazerHidDeviceConnection connection,
        IReadOnlySet<RazerHidBackendCapability>? unsupported = null,
        IReadOnlyDictionary<string, string>? errors = null)
    {
        if (connection.EndpointState != RazerHidEndpointState.Resolved)
        {
            return [];
        }

        // The diagnostics page only needs the checks that need attention. Listing every
        // declared capability made one mouse look like a wall of protocol rows.
        return connection.Capabilities
            .OrderBy(capability => capability.ToString(), StringComparer.Ordinal)
            .Select(capability =>
            {
                var unsupportedCapability = unsupported?.Contains(capability) ?? false;
                string? reason = null;
                var failed = errors is not null &&
                    errors.TryGetValue(capability.ToString(), out reason);
                var available = !unsupportedCapability && !failed;
                var detail = failed
                    ? reason ?? AppStrings.Text("DiagnosticProtocolUnavailable")
                    : AppStrings.Text("DiagnosticProtocolUnavailable");

                return new DeviceProtocolDetailViewModel(
                    $"RazerHidCapability{capability}",
                    available,
                    detail,
                    failed: failed);
            })
            .Where(detail => !detail.IsAvailable)
            .ToArray();
    }

    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public string OperationStatusText => _operationSucceeded
        ? _operationStatusOverride ?? AppStrings.Text("RazerHidOperationApplied")
        : string.Empty;
    public bool IsReady => Connection.IsReady;
    public bool RequiresRescan { get => _requiresRescan; private set => SetField(ref _requiresRescan, value); }
    public bool CanWrite => IsReady && !RequiresRescan;

    private bool SupportsSoftwareLighting =>
        Connection.Definition.Category == DeviceCategory.Keyboard &&
        SelectedLightingZone == Connection.Definition.DefaultLedZone &&
        Connection.Definition.MatrixDimensions is not null &&
        Has(RazerHidBackendCapability.MatrixFrameWrite) &&
        _chromaIntegrationEnabled &&
        ChromaOverrideEnabled;
    private bool HasBatteryData => HasAny(
        RazerHidBackendCapability.BatteryRead, RazerHidBackendCapability.ChargingRead);
    private bool HasPollingSection => PollingVisibility == Visibility.Visible;
    public bool HasBasicSection => HasAny(
        RazerHidBackendCapability.FirmwareRead,
        RazerHidBackendCapability.DeviceModeRead) ||
        !string.IsNullOrWhiteSpace(_basicState?.Serial) ||
        HasBatteryData && !HasPollingSection;
    public Visibility BasicVisibility => VisibleWhen(HasBasicSection);
    public Visibility FirmwareVisibility => VisibleWhen(Has(RazerHidBackendCapability.FirmwareRead));
    public Visibility SerialVisibility => VisibleWhen(!string.IsNullOrWhiteSpace(_basicState?.Serial));
    public Visibility SoftwareModeVisibility => VisibleWhen(Has(RazerHidBackendCapability.DeviceModeRead));
    public Visibility BatteryVisibility => VisibleWhen(HasBatteryData);
    // Battery is shown once: in the polling card when it exists, otherwise in the basic info card.
    public Visibility BatteryBasicVisibility => VisibleWhen(HasBatteryData && !HasPollingSection);
    public Visibility BatteryPollingVisibility => VisibleWhen(HasBatteryData && HasPollingSection);
    public Visibility BatteryPercentVisibility => VisibleWhen(Has(RazerHidBackendCapability.BatteryRead));
    public Visibility ChargingVisibility => VisibleWhen(Has(RazerHidBackendCapability.ChargingRead));
    public bool IsBasicBusy { get => _isBasicBusy; private set => SetField(ref _isBasicBusy, value); }
    public IReadOnlyDictionary<string, string> BasicErrors =>
        _basicState?.Errors ?? new Dictionary<string, string>();
    public string FirmwareText => _basicState?.Firmware?.ToString() ?? "--";
    public string SerialText => _basicState?.Serial ?? string.Empty;
    public string SoftwareModeText => _basicState?.SoftwareMode switch
    {
        true => AppStrings.Text("Text_4366F0BC"),
        false => AppStrings.Text("Text_B252C327"),
        null => "--",
    };
    public string BatteryText => _basicState?.BatteryPercent is { } value ? $"{value}%" : "--";
    public string ChargingText => _basicState?.IsCharging switch
    {
        true => AppStrings.Text("Text_8FF04F66"),
        false => AppStrings.Text("Text_EC17E7E7"),
        null => "--",
    };

    public Visibility PollingVisibility => VisibleWhen(Has(RazerHidBackendCapability.PollingRateRead) ||
        Has(RazerHidBackendCapability.PollingRateWrite) && PollingOptions.Count > 0);
    public Visibility PollingReadVisibility => VisibleWhen(Has(RazerHidBackendCapability.PollingRateRead));
    public Visibility PollingWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.PollingRateWrite) &&
        PollingOptions.Count > 0);
    public IReadOnlyList<int> PollingOptions => Connection.Definition.PollingRates;
    public IReadOnlyList<string> PollingOptionTexts => PollingOptions.Select(rate => $"{rate} Hz").ToArray();
    public int SelectedPollingRateIndex
    {
        get => IndexOf(PollingOptions, SelectedPollingRate);
        set
        {
            if (value >= 0 && value < PollingOptions.Count) SelectedPollingRate = PollingOptions[value];
        }
    }
    public int SelectedPollingRate
    {
        get => _selectedPollingRate;
        set
        {
            if (SetField(ref _selectedPollingRate, value))
            {
                OnPropertyChanged(nameof(SelectedPollingRateIndex));
                OnPropertyChanged(nameof(CanEditPolling));
            }
        }
    }
    public string PollingRateText => _basicState?.PollingRate is { } value ? $"{value} Hz" : "--";
    public bool IsPollingBusy { get => _isPollingBusy; private set => SetBusy(ref _isPollingBusy, value, nameof(CanEditPolling), nameof(CanWritePolling)); }
    public bool CanWritePolling => CanUse(RazerHidBackendCapability.PollingRateWrite) &&
        PollingOptions.Count > 0 && !IsPollingBusy;
    public bool CanEditPolling => CanUse(RazerHidBackendCapability.PollingRateWrite) &&
        PollingOptions.Contains(SelectedPollingRate);

    public Visibility DpiVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.DpiRead,
        RazerHidBackendCapability.DpiWrite,
        RazerHidBackendCapability.DpiStagesRead));
    public Visibility DpiReadVisibility => VisibleWhen(Has(RazerHidBackendCapability.DpiRead));
    public Visibility DpiWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.DpiWrite));
    public int DpiX
    {
        get => _dpiX;
        set
        {
            if (SetField(ref _dpiX, value)) OnPropertyChanged(nameof(CanEditDpi));
        }
    }
    public int DpiY
    {
        get => _dpiY;
        set
        {
            if (SetField(ref _dpiY, value)) OnPropertyChanged(nameof(CanEditDpi));
        }
    }
    public int MaximumDpi => Connection.Definition.MaximumDpi ?? ushort.MaxValue;
    public string DpiText => _basicState?.DpiX is { } x && _basicState.DpiY is { } y ? $"{x} x {y} DPI" : "--";
    public bool IsDpiBusy { get => _isDpiBusy; private set => SetBusy(ref _isDpiBusy, value, nameof(CanEditDpi), nameof(CanWriteDpi)); }
    public bool CanWriteDpi => CanUse(RazerHidBackendCapability.DpiWrite) && !IsDpiBusy;
    public bool CanEditDpi => CanUse(RazerHidBackendCapability.DpiWrite) &&
        DpiX is >= 100 && DpiY is >= 100 && DpiX <= MaximumDpi && DpiY <= MaximumDpi;

    public Visibility DpiStagesVisibility => VisibleWhen(Has(RazerHidBackendCapability.DpiStagesRead));
    public Visibility DpiStagesWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.DpiStagesRead) &&
        Has(RazerHidBackendCapability.DpiStagesWrite));
    public ObservableCollection<RazerHidDpiStageRowViewModel> DpiStages { get; } = new();
    public byte ActiveDpiStage
    {
        get => _activeDpiStage;
        set
        {
            if (SetField(ref _activeDpiStage, value)) OnPropertyChanged(nameof(CanEditDpiStages));
        }
    }
    public bool IsDpiStagesLoading
    {
        get => _isDpiStagesLoading;
        private set
        {
            if (SetField(ref _isDpiStagesLoading, value))
                OnPropertyChanged(nameof(DpiStagesLoadingVisibility));
        }
    }
    public Visibility DpiStagesLoadingVisibility => IsDpiStagesLoading ? Visibility.Visible : Visibility.Collapsed;
    public bool IsDpiStagesBusy
    {
        get => _isDpiStagesBusy;
        private set
        {
            if (!SetField(ref _isDpiStagesBusy, value)) return;
            OnPropertyChanged(nameof(CanEditDpiStages));
            OnPropertyChanged(nameof(CanWriteDpiStages));
            foreach (var row in DpiStages) row.SetEditable(CanWriteDpiStages);
        }
    }
    public bool CanWriteDpiStages => CanUse(RazerHidBackendCapability.DpiStagesRead) &&
        CanUse(RazerHidBackendCapability.DpiStagesWrite) && !IsDpiStagesBusy;
    public bool CanEditDpiStages => CanUse(RazerHidBackendCapability.DpiStagesRead) &&
        CanUse(RazerHidBackendCapability.DpiStagesWrite) &&
        DpiStages.Count is >= 1 and <= 5 &&
        ActiveDpiStage >= 1 && ActiveDpiStage <= DpiStages.Count &&
        DpiStages.All(stage => stage.X is >= 100 && stage.Y is >= 100 && stage.X <= MaximumDpi && stage.Y <= MaximumDpi);

    public Visibility PowerVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.IdleTimeoutRead,
        RazerHidBackendCapability.IdleTimeoutWrite,
        RazerHidBackendCapability.LowBatteryThresholdRead,
        RazerHidBackendCapability.LowBatteryThresholdWrite,
        RazerHidBackendCapability.AutoSleepRead,
        RazerHidBackendCapability.AutoSleepWrite,
        RazerHidBackendCapability.BatteryChargeLimiterRead,
        RazerHidBackendCapability.BatteryChargeLimiterWrite,
        RazerHidBackendCapability.TimeToDimRead,
        RazerHidBackendCapability.TimeToDimWrite,
        RazerHidBackendCapability.IndicatorLedConfigurationRead,
        RazerHidBackendCapability.IndicatorLedConfigurationWrite,
        RazerHidBackendCapability.IndicatorLedConfigurationV2Read,
        RazerHidBackendCapability.IndicatorLedConfigurationV2Write,
        RazerHidBackendCapability.BatteryDischargeCurveRead,
        RazerHidBackendCapability.BatteryDischargeCurveWrite));
    public Visibility IdleVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.IdleTimeoutRead,
        RazerHidBackendCapability.IdleTimeoutWrite));
    public Visibility IdleWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.IdleTimeoutWrite));
    public int IdleTimeoutSeconds
    {
        get => _idleTimeoutSeconds;
        set
        {
            if (SetField(ref _idleTimeoutSeconds, value)) OnPropertyChanged(nameof(CanEditIdle));
        }
    }
    public string IdleTimeoutText => _basicState?.IdleTimeoutSeconds is { } value ? $"{value} s" : "--";
    public bool IsIdleBusy { get => _isIdleBusy; private set => SetBusy(ref _isIdleBusy, value, nameof(CanEditIdle), nameof(CanWriteIdle)); }
    public bool CanWriteIdle => CanUse(RazerHidBackendCapability.IdleTimeoutWrite) && !IsIdleBusy;
    public bool CanEditIdle => CanUse(RazerHidBackendCapability.IdleTimeoutWrite) &&
        IdleTimeoutSeconds is >= 60 and <= 900;
    public Visibility LowBatteryVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.LowBatteryThresholdRead,
        RazerHidBackendCapability.LowBatteryThresholdWrite));
    public Visibility LowBatteryWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.LowBatteryThresholdWrite));
    public int LowBatteryThresholdPercent
    {
        get => _lowBatteryThresholdPercent;
        set
        {
            if (SetField(ref _lowBatteryThresholdPercent, value)) OnPropertyChanged(nameof(CanEditLowBattery));
        }
    }
    public string LowBatteryThresholdText => _basicState?.LowBatteryThresholdPercent is { } value ? $"{value}%" : "--";
    public bool IsLowBatteryBusy { get => _isLowBatteryBusy; private set => SetBusy(ref _isLowBatteryBusy, value, nameof(CanEditLowBattery), nameof(CanWriteLowBattery)); }
    public bool CanWriteLowBattery => CanUse(RazerHidBackendCapability.LowBatteryThresholdWrite) && !IsLowBatteryBusy;
    public bool CanEditLowBattery => CanUse(RazerHidBackendCapability.LowBatteryThresholdWrite) &&
        LowBatteryThresholdPercent is >= 5 and <= 25 && LowBatteryThresholdPercent % 5 == 0;
    public Visibility AutoSleepVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.AutoSleepRead,
        RazerHidBackendCapability.AutoSleepWrite));
    public Visibility AutoSleepWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.AutoSleepWrite));
    public bool AutoSleepEnabled
    {
        get => _autoSleepEnabled;
        set => SetField(ref _autoSleepEnabled, value);
    }
    public string AutoSleepText => _basicState?.AutoSleepEnabled is { } value
        ? value ? AppStrings.Text("Text_RazerHidAutoSleepOn") : AppStrings.Text("Text_RazerHidAutoSleepOff")
        : "--";
    public bool IsAutoSleepBusy { get => _isAutoSleepBusy; private set => SetBusy(ref _isAutoSleepBusy, value, nameof(CanWriteAutoSleep)); }
    public bool CanWriteAutoSleep => CanUse(RazerHidBackendCapability.AutoSleepWrite);
    public Visibility ChargeLimiterVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.BatteryChargeLimiterRead,
        RazerHidBackendCapability.BatteryChargeLimiterWrite));
    public Visibility ChargeLimiterWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.BatteryChargeLimiterWrite));
    public bool ChargeLimiterEnabled
    {
        get => _chargeLimiterEnabled;
        set => SetField(ref _chargeLimiterEnabled, value);
    }
    public int ChargeLimiterPercent
    {
        get => _chargeLimiterPercent;
        set
        {
            if (SetField(ref _chargeLimiterPercent, value)) OnPropertyChanged(nameof(CanEditChargeLimiter));
        }
    }
    public string ChargeLimiterText => _basicState?.BatteryChargeLimiterPercent is { } value ? $"{value}%" : "--";
    public bool IsChargeLimiterBusy { get => _isChargeLimiterBusy; private set => SetBusy(ref _isChargeLimiterBusy, value, nameof(CanEditChargeLimiter), nameof(CanWriteChargeLimiter)); }
    public bool CanWriteChargeLimiter => CanUse(RazerHidBackendCapability.BatteryChargeLimiterWrite) && !IsChargeLimiterBusy;
    public bool CanEditChargeLimiter => CanUse(RazerHidBackendCapability.BatteryChargeLimiterWrite) &&
        ChargeLimiterPercent is >= 50 and <= 95 && ChargeLimiterPercent % 5 == 0;

    public Visibility LightingVisibility => VisibleWhen(LightingZones.Count > 0 || HasAny(
        RazerHidBackendCapability.GpuCardDetectionRead,
        RazerHidBackendCapability.GpuCardDetectionWrite,
        RazerHidBackendCapability.ForceChromaCtrlRead,
        RazerHidBackendCapability.ForceChromaCtrlWrite,
        RazerHidBackendCapability.ChromaTapRead,
        RazerHidBackendCapability.ChromaTapWrite));
    public Visibility LightingControlsVisibility => VisibleWhen(
        LightingZones.Count > 0 &&
        (Has(RazerHidBackendCapability.LightingEffectWrite) ||
         Has(RazerHidBackendCapability.MatrixFrameWrite)));
    public Visibility ChromaOverrideVisibility => VisibleWhen(
        _chromaIntegrationEnabled && LightingZones.Count > 0 &&
        Has(RazerHidBackendCapability.MatrixFrameWrite));
    internal void SetChromaIntegrationEnabled(bool enabled)
    {
        if (_chromaIntegrationEnabled == enabled) return;
        _chromaIntegrationEnabled = enabled;
        OnPropertyChanged(nameof(ChromaOverrideVisibility));
        OnLightingSelectionChanged();
    }
    public bool LightingEnabled
    {
        get => _lightingEnabled;
        set
        {
            if (SetField(ref _lightingEnabled, value))
            {
                OnPropertyChanged(nameof(CanEditLightingSettings));
                OnPropertyChanged(nameof(CanApplyLighting));
                OnPropertyChanged(nameof(CanApplyMatrix));
            }
        }
    }
    public bool ChromaOverrideEnabled
    {
        get => _chromaOverrideEnabled;
        set
        {
            if (SetField(ref _chromaOverrideEnabled, value))
            {
                OnLightingSelectionChanged();
            }
        }
    }
    public bool IsLightingSettingsBusy
    {
        get => _isLightingSettingsBusy;
        private set => SetField(ref _isLightingSettingsBusy, value);
    }
    public bool CanEditLightingSettings => _lightingSettingsSaver is not null &&
        IsReady && !RequiresRescan && !IsLightingSettingsBusy;
    public IReadOnlyList<string> LightingPowerProfileOptions =>
        [
            AppStrings.Text("BladeLightingPowerCurrent"),
            AppStrings.Text("BladeLightingPowerPluggedIn"),
            AppStrings.Text("BladeLightingPowerLowAdapter"),
            AppStrings.Text("BladeLightingPowerBattery"),
        ];
    public int LightingPowerProfileIndex
    {
        get => _lightingPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 3);
            if (SetField(ref _lightingPowerProfileIndex, next))
            {
                ApplyConfiguredLightingToEditor();
            }
        }
    }
    public IReadOnlyList<RazerHidLedZone> LightingZones => Connection.LightingZones.Keys
        .Where(IsVisibleLightingZone).Order().ToArray();
    public IReadOnlyList<string> LightingZoneOptions => LightingZones.Select(FormatZone).ToArray();
    public Visibility LightingZoneSelectorVisibility => VisibleWhen(LightingZones.Count > 1);
    public int SelectedLightingZoneIndex
    {
        get => IndexOf(LightingZones, SelectedLightingZone);
        set
        {
            if (value >= 0 && value < LightingZones.Count) SelectedLightingZone = LightingZones[value];
        }
    }
    public RazerHidLedZone SelectedLightingZone
    {
        get => _selectedLightingZone;
        set
        {
            if (!LightingZones.Contains(value) || !SetField(ref _selectedLightingZone, value))
            {
                return;
            }
            _brightnessKnown = false;
            _ledStateKnown = false;
            SelectedLightingEffect = LightingEffects.FirstOrDefault();
            OnLightingSelectionChanged();
        }
    }
    public IReadOnlyList<RazerHidLightingEffect> LightingEffects =>
        (Has(RazerHidBackendCapability.LightingEffectWrite)
            ? SelectedZoneCapabilities?.LightingEffects
                .Where(effect => effect != RazerHidLightingEffect.Custom &&
                    !_unsupportedLighting.Contains((SelectedLightingZone, effect)))
                .Order()
                .ToArray() ?? []
            : [])
        .Concat(SupportsSoftwareLighting
            ? [
                RazerHidLightingEffect.SoftwareAudioMeter,
                RazerHidLightingEffect.SoftwareSpectrum,
                RazerHidLightingEffect.SoftwareWave,
                RazerHidLightingEffect.SoftwareFire,
                RazerHidLightingEffect.SoftwareWheel,
            ]
            : [])
        .ToArray();
    public Visibility LightingEffectVisibility => VisibleWhen(LightingEffects.Count > 0);
    public Visibility LightingPowerProfileVisibility => VisibleWhen(LightingZones.Any(zone =>
        Connection.LightingZones.TryGetValue(zone, out var capabilities) &&
        ((Has(RazerHidBackendCapability.LightingEffectWrite) || SupportsSoftwareLighting) &&
         capabilities.LightingEffects.Any(effect => effect != RazerHidLightingEffect.Custom &&
            !_unsupportedLighting.Contains((zone, effect))) ||
         capabilities.CanWriteBrightness && !_unsupportedBrightnessWrites.Contains(zone) ||
         capabilities.CanWriteState && !_unsupportedLedStateWrites.Contains(zone))));
    private IReadOnlyList<RazerHidLightingEffect> LightingEffectChoices => LightingEffects
        .Where(effect => NativeCounterpart(effect) is not { } native || !LightingEffects.Contains(native))
        .ToArray();
    public IReadOnlyList<string> LightingEffectOptions => LightingEffectChoices
        .Select(effect => $"{FormatEffect(effect)} · {AppStrings.Text(IsSoftwareLightingEffect(effect)
            ? "LightingRenderSoftware"
            : "LightingRenderFirmware")}")
        .ToArray();
    public int SelectedLightingEffectIndex
    {
        get => IndexOf(LightingEffectChoices, NativeCounterpart(SelectedLightingEffect) is { } native &&
            LightingEffects.Contains(native) ? native : SelectedLightingEffect);
        set
        {
            if (value >= 0 && value < LightingEffectChoices.Count && value != SelectedLightingEffectIndex)
                SelectedLightingEffect = LightingEffectChoices[value];
        }
    }
    public IReadOnlyList<string> LightingRenderModeOptions => AppStrings.Texts(
        "LightingRenderFirmware", "LightingRenderSoftware");
    public Visibility LightingRenderModeVisibility => VisibleWhen(
        LightingEffects.Contains(NativeCounterpart(SelectedLightingEffect) ?? SelectedLightingEffect) &&
        SoftwareCounterpart(NativeCounterpart(SelectedLightingEffect) ?? SelectedLightingEffect) is { } software &&
        LightingEffects.Contains(software));
    public int LightingRenderModeIndex
    {
        get => IsSoftwareLightingEffect(SelectedLightingEffect) ? 1 : 0;
        set
        {
            if (LightingRenderModeVisibility != Visibility.Visible || value is not (0 or 1)) return;
            var native = NativeCounterpart(SelectedLightingEffect) ?? SelectedLightingEffect;
            SelectedLightingEffect = value == 0 ? native : SoftwareCounterpart(native)!.Value;
        }
    }
    public RazerHidLightingEffect SelectedLightingEffect
    {
        get => _selectedLightingEffect;
        set
        {
            if (LightingEffects.Contains(value) && SetField(ref _selectedLightingEffect, value))
            {
                LightingSpeed = Math.Min(LightingSpeed, MaximumLightingSpeed);
                OnPropertyChanged(nameof(SelectedLightingEffectIndex));
                OnPropertyChanged(nameof(LightingRenderModeVisibility));
                OnPropertyChanged(nameof(LightingRenderModeIndex));
                OnPropertyChanged(nameof(PrimaryColorVisibility));
                OnPropertyChanged(nameof(LightingPresetVisibility));
                OnPropertyChanged(nameof(SecondaryColorVisibility));
                OnPropertyChanged(nameof(TertiaryColorVisibility));
                OnPropertyChanged(nameof(LightingSpeedVisibility));
                OnPropertyChanged(nameof(MaximumLightingSpeed));
                OnPropertyChanged(nameof(LightingSpeedOptions));
                OnPropertyChanged(nameof(LightingDirectionVisibility));
                OnPropertyChanged(nameof(CanApplyLighting));
            }
        }
    }
    public Color PrimaryColor { get => _primaryColor; set => SetField(ref _primaryColor, value); }
    public Color SecondaryColor { get => _secondaryColor; set => SetField(ref _secondaryColor, value); }
    public Color TertiaryColor { get => _tertiaryColor; set => SetField(ref _tertiaryColor, value); }
    public byte LightingSpeed
    {
        get => _lightingSpeed;
        set
        {
            if (SetField(ref _lightingSpeed, Math.Clamp(value, (byte)1, MaximumLightingSpeed)))
                OnPropertyChanged(nameof(LightingSpeedIndex));
        }
    }
    public IReadOnlyList<string> LightingSpeedOptions => MaximumLightingSpeed == 4
        ? AppStrings.Texts("BladeReactiveSpeedLevel1", "BladeReactiveSpeedLevel2", "BladeReactiveSpeedLevel3", "BladeReactiveSpeedLevel4")
        : AppStrings.Texts("BladeAnimationSpeedSlow", "BladeAnimationSpeedMedium", "BladeAnimationSpeedFast");
    public int LightingSpeedIndex
    {
        get => LightingSpeed - 1;
        set
        {
            if (value >= 0 && value < MaximumLightingSpeed)
                LightingSpeed = checked((byte)(value + 1));
        }
    }
    public byte LightingDirection
    {
        get => _lightingDirection;
        set
        {
            if (SetField(ref _lightingDirection, value)) OnPropertyChanged(nameof(LightingDirectionIndex));
        }
    }
    public int LightingDirectionIndex
    {
        get => Math.Clamp(LightingDirection - 1, 0, 1);
        set
        {
            if (value is >= 0 and <= 1) LightingDirection = checked((byte)(value + 1));
        }
    }
    public Visibility LightingPresetVisibility => VisibleWhen(SecondaryColorVisibility == Visibility.Visible ||
        (PrimaryColorVisibility == Visibility.Visible && SelectedLightingEffect == RazerHidLightingEffect.StarlightSingle &&
         LightingEffects.Contains(RazerHidLightingEffect.StarlightDual)));
    public Visibility PrimaryColorVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        RazerHidLightingEffect.Static or RazerHidLightingEffect.Reactive or RazerHidLightingEffect.Blinking or
        RazerHidLightingEffect.BreathingSingle or RazerHidLightingEffect.BreathingDual or
        RazerHidLightingEffect.StarlightSingle or RazerHidLightingEffect.StarlightDual or
        RazerHidLightingEffect.SoftwareAudioMeter or RazerHidLightingEffect.SoftwareSpectrum or
        RazerHidLightingEffect.SoftwareWave or RazerHidLightingEffect.SoftwareFire or
        RazerHidLightingEffect.SoftwareWheel);
    public Visibility SecondaryColorVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        RazerHidLightingEffect.BreathingDual or RazerHidLightingEffect.StarlightDual or
        RazerHidLightingEffect.SoftwareAudioMeter or RazerHidLightingEffect.SoftwareSpectrum or
        RazerHidLightingEffect.SoftwareWave or RazerHidLightingEffect.SoftwareFire or
        RazerHidLightingEffect.SoftwareWheel);
    public Visibility TertiaryColorVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        RazerHidLightingEffect.SoftwareAudioMeter or RazerHidLightingEffect.SoftwareSpectrum or
        RazerHidLightingEffect.SoftwareWave or RazerHidLightingEffect.SoftwareFire or
        RazerHidLightingEffect.SoftwareWheel);
    public Visibility LightingSpeedVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        RazerHidLightingEffect.Reactive or
        RazerHidLightingEffect.StarlightRandom or RazerHidLightingEffect.StarlightSingle or
        RazerHidLightingEffect.StarlightDual or RazerHidLightingEffect.SoftwareWheel);
    public byte MaximumLightingSpeed => SelectedLightingEffect == RazerHidLightingEffect.Reactive ? (byte)4 : (byte)3;
    public Visibility LightingDirectionVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        RazerHidLightingEffect.Wave or RazerHidLightingEffect.Wheel or
        RazerHidLightingEffect.SoftwareWave or RazerHidLightingEffect.SoftwareWheel);
    public bool IsLightingBusy { get => _isLightingBusy; private set => SetBusy(ref _isLightingBusy, value, nameof(CanApplyLighting), nameof(CanTriggerReactive)); }
    public bool IsLightingLoading { get => _isLightingLoading; private set => SetField(ref _isLightingLoading, value); }
    private bool CanApplyLightingEffect =>
        (IsSoftwareLightingEffect(SelectedLightingEffect)
            ? SupportsSoftwareLighting
            : CanUse(RazerHidBackendCapability.LightingEffectWrite)) &&
        LightingEffects.Contains(SelectedLightingEffect);
    public Visibility LightingSaveVisibility => VisibleWhen(
        CanApplyLightingEffect ||
        BrightnessWriteVisibility == Visibility.Visible ||
        LedStateWriteVisibility == Visibility.Visible);
    public bool CanApplyLighting => LightingEnabled &&
        (CanApplyLightingEffect || CanEditBrightness || CanWriteLedState);
    public Visibility BrightnessVisibility => VisibleWhen(SelectedZoneCapabilities is { } zone &&
        (zone.CanReadBrightness && !_unsupportedBrightnessReads.Contains(SelectedLightingZone) ||
         zone.CanWriteBrightness && !_unsupportedBrightnessWrites.Contains(SelectedLightingZone)));
    public Visibility BrightnessWriteVisibility => VisibleWhen(SelectedZoneCapabilities?.CanWriteBrightness == true &&
        !_unsupportedBrightnessWrites.Contains(SelectedLightingZone));
    public Visibility BrightnessEditorVisibility => BrightnessWriteVisibility;
    public byte Brightness
    {
        get => _brightness;
        set
        {
            if (SetField(ref _brightness, value)) OnPropertyChanged(nameof(BrightnessText));
        }
    }
    public string BrightnessText => _brightnessKnown
        ? $"{Math.Round(Brightness / 255d * 100)}%"
        : "--";
    public bool IsBrightnessBusy { get => _isBrightnessBusy; private set => SetBusy(ref _isBrightnessBusy, value, nameof(CanEditBrightness), nameof(CanWriteBrightness)); }
    public bool CanWriteBrightness => CanWrite && !IsBrightnessBusy &&
        SelectedZoneCapabilities?.CanWriteBrightness == true &&
        !_unsupportedBrightnessWrites.Contains(SelectedLightingZone);
    public bool CanEditBrightness => CanWrite &&
        SelectedZoneCapabilities?.CanWriteBrightness == true &&
        !_unsupportedBrightnessWrites.Contains(SelectedLightingZone);
    public Visibility LedStateVisibility => VisibleWhen(SelectedZoneCapabilities is { } zone &&
        (zone.CanReadState && !_unsupportedLedStateReads.Contains(SelectedLightingZone) ||
         zone.CanWriteState && !_unsupportedLedStateWrites.Contains(SelectedLightingZone)));
    public Visibility LedStateWriteVisibility => VisibleWhen(SelectedZoneCapabilities?.CanWriteState == true &&
        !_unsupportedLedStateWrites.Contains(SelectedLightingZone));
    public Visibility LedStateEditorVisibility => LedStateWriteVisibility;
    public Visibility LedStateReadOnlyVisibility => VisibleWhen(SelectedZoneCapabilities?.CanReadState == true &&
        _unsupportedLedStateReads.Contains(SelectedLightingZone) == false &&
        LedStateWriteVisibility == Visibility.Collapsed);
    public string LedStateText => !_ledStateKnown ? "--" :
        AppStrings.Text(LedEnabled ? "Text_7E6D2390" : "Text_39B523BD");
    public bool LedEnabled
    {
        get => _ledEnabled;
        set
        {
            if (SetField(ref _ledEnabled, value)) OnPropertyChanged(nameof(LedStateText));
        }
    }
    public bool IsLedStateBusy { get => _isLedStateBusy; private set => SetBusy(ref _isLedStateBusy, value, nameof(CanWriteLedState)); }
    public bool CanWriteLedState => CanWrite &&
        SelectedZoneCapabilities?.CanWriteState == true &&
        !_unsupportedLedStateWrites.Contains(SelectedLightingZone);
    public Visibility ReactiveTriggerVisibility => VisibleWhen(Has(RazerHidBackendCapability.ReactiveTriggerWrite));
    public bool CanTriggerReactive => CanUse(RazerHidBackendCapability.ReactiveTriggerWrite);

    public Visibility MatrixVisibility => VisibleWhen(MatrixCells.Count > 0 && Has(RazerHidBackendCapability.MatrixFrameWrite));
    public ObservableCollection<RazerHidMatrixCellViewModel> MatrixCells { get; } = new();
    public bool IsMatrixBusy { get => _isMatrixBusy; private set => SetBusy(ref _isMatrixBusy, value, nameof(CanApplyMatrix)); }
    public bool CanApplyMatrix => CanUse(RazerHidBackendCapability.MatrixFrameWrite) &&
        LightingEnabled;

    public Visibility ScrollVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ScrollAccelerationRead,
        RazerHidBackendCapability.ScrollAccelerationWrite,
        RazerHidBackendCapability.SmartReelRead,
        RazerHidBackendCapability.SmartReelWrite,
        RazerHidBackendCapability.SmartReelThresholdRead,
        RazerHidBackendCapability.SmartReelThresholdWrite,
        RazerHidBackendCapability.ScrollModeRead,
        RazerHidBackendCapability.ScrollModeWrite,
        RazerHidBackendCapability.ScrollFreeNotchedModeRead,
        RazerHidBackendCapability.ScrollFreeNotchedModeWrite,
        RazerHidBackendCapability.ScrollAccelerationLevelRead,
        RazerHidBackendCapability.ScrollAccelerationLevelWrite,
        RazerHidBackendCapability.LkScrollWheelModeRead,
        RazerHidBackendCapability.LkScrollWheelModeWrite));
    public Visibility ScrollModeVisibility => Visibility.Collapsed;
    public Visibility ScrollModeWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.ScrollModeWrite));
    public Visibility ScrollAccelerationVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ScrollAccelerationRead, RazerHidBackendCapability.ScrollAccelerationWrite));
    public Visibility ScrollAccelerationWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.ScrollAccelerationWrite));
    public Visibility SmartReelVisibility => Visibility.Collapsed;
    public Visibility SmartReelWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.SmartReelWrite));
    public Visibility SmartReelThresholdVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.SmartReelThresholdRead, RazerHidBackendCapability.SmartReelThresholdWrite));
    public Visibility SmartReelThresholdWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.SmartReelThresholdWrite));
    public Visibility ScrollFreeNotchedModeVisibility => Visibility.Collapsed;
    public Visibility ScrollFreeNotchedModeWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.ScrollFreeNotchedModeWrite));
    public Visibility ScrollAccelerationLevelVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ScrollAccelerationLevelRead, RazerHidBackendCapability.ScrollAccelerationLevelWrite));
    public Visibility ScrollAccelerationLevelWriteVisibility =>
        VisibleWhen(Has(RazerHidBackendCapability.ScrollAccelerationLevelWrite));
    public bool IsOnboardProfileBusy
    {
        get => _isOnboardProfileBusy;
        private set => SetBusy(
            ref _isOnboardProfileBusy,
            value,
            nameof(CanEditOnboardProfile),
            nameof(CanApplyOnboardProfile),
            nameof(CanCreateOnboardProfile),
            nameof(CanDeleteOnboardProfile),
            nameof(CanResetOnboardProfile));
    }
    public bool CanEditOnboardProfile =>
        CanUse(RazerHidBackendCapability.OnboardProfileWrite) && !IsOnboardProfileBusy;
    public IReadOnlyList<string> ScrollFreeNotchedModeTexts { get; } =
        [AppStrings.Text("RazerHidScrollNotchedMode"), AppStrings.Text("RazerHidScrollFreeSpinMode")];
    public bool IsScrollLoading { get => _isScrollLoading; private set => SetField(ref _isScrollLoading, value); }
    public byte ScrollMode { get => _scrollMode; set => SetField(ref _scrollMode, value); }
    public bool ScrollAccelerationEnabled { get => _scrollAccelerationEnabled; set => SetField(ref _scrollAccelerationEnabled, value); }
    public bool SmartReelEnabled { get => _smartReelEnabled; set => SetField(ref _smartReelEnabled, value); }
    public byte SmartReelThreshold { get => _smartReelThreshold; set => SetField(ref _smartReelThreshold, value); }
    public int ScrollFreeNotchedModeIndex
    {
        get => _scrollFreeNotchedModeIndex;
        set => SetField(ref _scrollFreeNotchedModeIndex, value);
    }
    public byte ScrollAccelerationLevel
    {
        get => _scrollAccelerationLevel;
        set => SetField(ref _scrollAccelerationLevel, value);
    }
    public bool IsScrollFreeNotchedBusy { get => _isScrollFreeNotchedBusy; private set => SetBusy(ref _isScrollFreeNotchedBusy, value, nameof(CanEditScrollFreeNotched), nameof(CanWriteScrollFreeNotched)); }
    public bool IsScrollAccelerationLevelBusy { get => _isScrollAccelerationLevelBusy; private set => SetBusy(ref _isScrollAccelerationLevelBusy, value, nameof(CanEditScrollAccelerationLevel), nameof(CanWriteScrollAccelerationLevel)); }
    public bool IsScrollModeBusy { get => _isScrollModeBusy; private set => SetBusy(ref _isScrollModeBusy, value, nameof(CanEditScrollMode), nameof(CanWriteScrollMode)); }
    public bool IsScrollAccelerationBusy { get => _isScrollAccelerationBusy; private set => SetBusy(ref _isScrollAccelerationBusy, value, nameof(CanEditScrollAcceleration), nameof(CanWriteScrollAcceleration)); }
    public bool IsSmartReelBusy { get => _isSmartReelBusy; private set => SetBusy(ref _isSmartReelBusy, value, nameof(CanEditSmartReel), nameof(CanWriteSmartReel)); }
    public bool IsSmartReelThresholdBusy { get => _isSmartReelThresholdBusy; private set => SetBusy(ref _isSmartReelThresholdBusy, value, nameof(CanEditSmartReelThreshold), nameof(CanWriteSmartReelThreshold)); }
    public bool CanWriteScrollFreeNotched => CanUse(RazerHidBackendCapability.ScrollFreeNotchedModeWrite) && !IsScrollFreeNotchedBusy;
    public bool CanWriteScrollAccelerationLevel => CanUse(RazerHidBackendCapability.ScrollAccelerationLevelWrite) && !IsScrollAccelerationLevelBusy;
    public bool CanWriteScrollMode => CanUse(RazerHidBackendCapability.ScrollModeWrite) && !IsScrollModeBusy;
    public bool CanWriteScrollAcceleration => CanUse(RazerHidBackendCapability.ScrollAccelerationWrite) && !IsScrollAccelerationBusy;
    public bool CanWriteSmartReel => CanUse(RazerHidBackendCapability.SmartReelWrite) && !IsSmartReelBusy;
    public bool CanWriteSmartReelThreshold => CanUse(RazerHidBackendCapability.SmartReelThresholdWrite) && !IsSmartReelThresholdBusy;
    public bool CanEditScrollFreeNotched => CanUse(RazerHidBackendCapability.ScrollFreeNotchedModeWrite);
    public bool CanEditScrollAccelerationLevel => CanUse(RazerHidBackendCapability.ScrollAccelerationLevelWrite);
    public bool CanEditScrollMode => CanUse(RazerHidBackendCapability.ScrollModeWrite);
    public bool CanEditScrollAcceleration => CanUse(RazerHidBackendCapability.ScrollAccelerationWrite);
    public bool CanEditSmartReel => CanUse(RazerHidBackendCapability.SmartReelWrite);
    public bool CanEditSmartReelThreshold => CanUse(RazerHidBackendCapability.SmartReelThresholdWrite);

    public Visibility KeyswitchVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.KeyswitchOptimizationRead,
        RazerHidBackendCapability.KeyswitchOptimizationWrite));
    public Visibility KeyswitchWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.KeyswitchOptimizationWrite));
    public IReadOnlyList<RazerHidKeyswitchOptimization> KeyswitchOptions { get; } =
        Enum.GetValues<RazerHidKeyswitchOptimization>();
    public IReadOnlyList<string> KeyswitchOptionTexts =>
        [AppStrings.Text("RazerHidKeyswitchTyping"), AppStrings.Text("RazerHidKeyswitchGaming")];
    public int KeyswitchOptimizationIndex
    {
        get => (int)KeyswitchOptimization;
        set
        {
            if (Enum.IsDefined((RazerHidKeyswitchOptimization)value))
                KeyswitchOptimization = (RazerHidKeyswitchOptimization)value;
        }
    }
    public RazerHidKeyswitchOptimization KeyswitchOptimization
    {
        get => _keyswitchOptimization;
        set
        {
            if (SetField(ref _keyswitchOptimization, value)) OnPropertyChanged(nameof(KeyswitchOptimizationIndex));
        }
    }
    public bool IsKeyswitchLoading { get => _isKeyswitchLoading; private set => SetField(ref _isKeyswitchLoading, value); }
    public bool IsKeyswitchBusy { get => _isKeyswitchBusy; private set => SetBusy(ref _isKeyswitchBusy, value, nameof(CanEditKeyswitch), nameof(CanWriteKeyswitch)); }
    public bool CanWriteKeyswitch => CanUse(RazerHidBackendCapability.KeyswitchOptimizationWrite) && !IsKeyswitchBusy;
    public bool CanEditKeyswitch => CanUse(RazerHidBackendCapability.KeyswitchOptimizationWrite);

    public Visibility FnPrimaryVisibility => VisibleWhen(Has(RazerHidBackendCapability.FnPrimaryWrite));
    public bool IsFnBusy { get => _isFnBusy; private set => SetBusy(ref _isFnBusy, value, nameof(CanSetFnPrimary)); }
    public bool CanSetFnPrimary => CanUse(RazerHidBackendCapability.FnPrimaryWrite);

    public Visibility HyperPollingVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.HyperPollingIndicatorWrite,
        RazerHidBackendCapability.HyperPollingPairWrite,
        RazerHidBackendCapability.HyperPollingUnpairWrite));
    public Visibility HyperPollingIndicatorVisibility => VisibleWhen(Has(RazerHidBackendCapability.HyperPollingIndicatorWrite));
    public Visibility HyperPollingPairVisibility => VisibleWhen(Has(RazerHidBackendCapability.HyperPollingPairWrite));
    public Visibility HyperPollingUnpairVisibility => VisibleWhen(Has(RazerHidBackendCapability.HyperPollingUnpairWrite));
    public byte HyperPollingIndicatorMode { get => _hyperPollingIndicatorMode; set => SetField(ref _hyperPollingIndicatorMode, value); }
    public bool IsHyperPollingBusy
    {
        get => _isHyperPollingBusy;
        private set
        {
            if (!SetField(ref _isHyperPollingBusy, value)) return;
            OnPropertyChanged(nameof(CanUseHyperPolling));
            OnPropertyChanged(nameof(CanSetHyperPollingIndicator));
            OnPropertyChanged(nameof(CanPairHyperPolling));
            OnPropertyChanged(nameof(CanUnpairHyperPolling));
        }
    }
    public bool CanUseHyperPolling => CanWrite;
    public bool CanSetHyperPollingIndicator => CanUseHyperPolling && Has(RazerHidBackendCapability.HyperPollingIndicatorWrite);
    public bool CanPairHyperPolling => CanUseHyperPolling && Has(RazerHidBackendCapability.HyperPollingPairWrite);
    public bool CanUnpairHyperPolling => CanUseHyperPolling && Has(RazerHidBackendCapability.HyperPollingUnpairWrite);

}
