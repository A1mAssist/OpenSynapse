using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class OpenRazerDeviceViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly Color DefaultPrimaryColor = Color.FromArgb(255, 0, 255, 102);
    private static readonly Color DefaultSecondaryColor = Color.FromArgb(255, 0, 153, 255);
    private static readonly Color DefaultTertiaryColor = Color.FromArgb(255, 255, 32, 64);
    private readonly OpenRazerDeviceService _service;
    private readonly HashSet<OpenRazerBackendCapability> _unsupported = [];
    private readonly HashSet<(OpenRazerLedZone Zone, OpenRazerLightingEffect Effect)> _unsupportedLighting = [];
    private readonly HashSet<OpenRazerLedZone> _unsupportedBrightnessWrites = [];
    private readonly HashSet<OpenRazerLedZone> _unsupportedBrightnessReads = [];
    private readonly HashSet<OpenRazerLedZone> _unsupportedLedStateWrites = [];
    private readonly HashSet<OpenRazerLedZone> _unsupportedLedStateReads = [];
    private readonly HashSet<OpenRazerLedZone> _unsupportedLedColorReads = [];
    private readonly HashSet<OpenRazerLedZone> _unsupportedLedEffectReads = [];
    private OpenRazerBasicState? _basicState;
    private string _errorText;
    private bool _requiresRescan;
    private bool _isBasicBusy;
    private bool _isPollingBusy;
    private bool _isDpiBusy;
    private bool _isIdleBusy;
    private bool _isLowBatteryBusy;
    private bool _isBrightnessBusy;
    private bool _isLightingBusy;
    private bool _isLightingLoading;
    private bool _isScrollModeBusy;
    private bool _isScrollAccelerationBusy;
    private bool _isSmartReelBusy;
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
    private byte _brightness = 255;
    private OpenRazerLedZone _selectedLightingZone;
    private OpenRazerLightingEffect _selectedLightingEffect;
    private Color _primaryColor = DefaultPrimaryColor;
    private Color _secondaryColor = DefaultSecondaryColor;
    private Color _tertiaryColor = DefaultTertiaryColor;
    private byte _lightingSpeed = 2;
    private byte _lightingDirection = 1;
    private byte _scrollMode;
    private bool _scrollAccelerationEnabled;
    private bool _smartReelEnabled;
    private OpenRazerKeyswitchOptimization _keyswitchOptimization;
    private byte _activeDpiStage = 1;
    private byte _hyperPollingIndicatorMode = 1;
    private bool _ledEnabled = true;
    private bool _ledStateKnown;
    private bool _brightnessKnown;
    private readonly Func<bool?>? _powerStateProvider;
    private readonly Func<bool?, LightingProfile?>? _lightingProfileResolver;
    private readonly Func<bool?, LightingProfile, CancellationToken, Task<bool>>? _lightingProfileSaver;
    private readonly Func<bool>? _lightingEnabledResolver;
    private readonly Func<bool>? _chromaOverrideResolver;
    private readonly Func<bool, bool, CancellationToken, Task<bool>>? _lightingSettingsSaver;
    private readonly OpenRazerSoftwareLightingRuntime? _softwareLightingRuntime;
    private WasapiAudioMeterAdapter? _softwareAudioInput;
    private TimeSpan _softwareLightingElapsed;
    private double _softwareAudioLevel;
    private int _lightingPowerProfileIndex;
    private bool _lightingEnabled;
    private bool _chromaOverrideEnabled;
    private bool _chromaIntegrationEnabled;
    private bool _isLightingSettingsBusy;
    private bool _operationSucceeded;
    private int _disposed;

    public OpenRazerDeviceViewModel(
        OpenRazerDeviceService service,
        OpenRazerDeviceConnection connection,
        Func<bool?>? powerStateProvider = null,
        Func<bool?, LightingProfile?>? lightingProfileResolver = null,
        Func<bool?, LightingProfile, CancellationToken, Task<bool>>? lightingProfileSaver = null,
        Func<bool>? lightingEnabledResolver = null,
        Func<bool>? chromaOverrideResolver = null,
        Func<bool, bool, CancellationToken, Task<bool>>? lightingSettingsSaver = null,
        bool chromaIntegrationEnabled = true)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        if (connection.Definition.MatrixDimensions is not null &&
            connection.Capabilities.Contains(OpenRazerBackendCapability.MatrixFrameWrite))
        {
            _softwareLightingRuntime = new OpenRazerSoftwareLightingRuntime(
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
        _lightingEnabled = lightingEnabledResolver?.Invoke() ?? true;
        _chromaOverrideEnabled = chromaOverrideResolver?.Invoke() ?? true;
        _chromaIntegrationEnabled = chromaIntegrationEnabled;
        _errorText = string.IsNullOrWhiteSpace(connection.Error)
            ? string.Empty
            : connection.EndpointState == OpenRazerEndpointState.RecognizedButUnresolved
                ? AppStrings.FormatText("OpenRazerDeviceNotReady", CategoryText)
                : AppStrings.Text("OpenRazerProtocolRescanRequired");
        _selectedLightingZone = LightingZones.Contains(connection.Definition.DefaultLedZone)
            ? connection.Definition.DefaultLedZone : LightingZones.FirstOrDefault();
        _selectedLightingEffect = LightingEffects.FirstOrDefault();
        _selectedPollingRate = PollingOptions.FirstOrDefault();
        if (connection.Definition.MatrixDimensions is { } matrix &&
            connection.Capabilities.Contains(OpenRazerBackendCapability.MatrixFrameWrite))
        {
            for (byte row = 0; row < matrix.Rows; row++)
            for (byte column = 0; column < matrix.Columns; column++)
                MatrixCells.Add(new OpenRazerMatrixCellViewModel(row, column));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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

    public OpenRazerDeviceConnection Connection { get; }
    public string InstanceId => Connection.InstanceId;
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
            OpenRazerEndpointState.Resolved when ProtocolTotalCount > 0 =>
                AppStrings.FormatText(
                    ProtocolAvailableCount == ProtocolTotalCount
                        ? "ProtocolAvailableCount"
                        : "ProtocolPartiallyAvailableCount",
                    ProtocolAvailableCount,
                    ProtocolTotalCount),
            OpenRazerEndpointState.Resolved => AppStrings.Text("Text_C097B416"),
            OpenRazerEndpointState.RecognizedButUnresolved => AppStrings.Text("Text_242E08F4"),
            _ => AppStrings.Text("Text_D3632B96"),
        };
    public int ProtocolAvailableCount => Math.Max(0, Connection.Capabilities.Count - _unsupported.Count);
    public int ProtocolTotalCount => Connection.Capabilities.Count;
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public string OperationStatusText => _operationSucceeded
        ? AppStrings.Text("OpenRazerOperationApplied")
        : string.Empty;
    public bool IsReady => Connection.IsReady;
    public bool RequiresRescan { get => _requiresRescan; private set => SetField(ref _requiresRescan, value); }
    public bool CanWrite => IsReady && !RequiresRescan;

    private bool IsMouseDevice => Connection.Definition.Category == DeviceCategory.Mouse;
    private bool SupportsSoftwareLighting =>
        Connection.Definition.Category == DeviceCategory.Keyboard &&
        Connection.Definition.MatrixDimensions is not null &&
        Has(OpenRazerBackendCapability.MatrixFrameWrite) &&
        _chromaIntegrationEnabled &&
        ChromaOverrideEnabled;
    private bool HasBatteryData => HasAny(
        OpenRazerBackendCapability.BatteryRead, OpenRazerBackendCapability.ChargingRead);
    private bool HasPollingSection => PollingVisibility == Visibility.Visible;
    public bool HasBasicSection => HasAny(
        OpenRazerBackendCapability.FirmwareRead,
        OpenRazerBackendCapability.DeviceModeRead) ||
        !string.IsNullOrWhiteSpace(_basicState?.Serial) ||
        HasBatteryData && (!IsMouseDevice || !HasPollingSection);
    public Visibility BasicVisibility => VisibleWhen(HasBasicSection);
    public Visibility FirmwareVisibility => VisibleWhen(Has(OpenRazerBackendCapability.FirmwareRead));
    public Visibility SerialVisibility => VisibleWhen(!string.IsNullOrWhiteSpace(_basicState?.Serial));
    public Visibility SoftwareModeVisibility => VisibleWhen(Has(OpenRazerBackendCapability.DeviceModeRead));
    public Visibility BatteryVisibility => VisibleWhen(HasBatteryData);
    public Visibility BatteryBasicVisibility => VisibleWhen(!IsMouseDevice || !HasPollingSection);
    public Visibility BatteryPollingVisibility => VisibleWhen(IsMouseDevice && HasPollingSection && HasBatteryData);
    public Visibility BatteryPercentVisibility => VisibleWhen(Has(OpenRazerBackendCapability.BatteryRead));
    public Visibility ChargingVisibility => VisibleWhen(Has(OpenRazerBackendCapability.ChargingRead));
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

    public Visibility PollingVisibility => VisibleWhen(Has(OpenRazerBackendCapability.PollingRateRead) ||
        Has(OpenRazerBackendCapability.PollingRateWrite) && PollingOptions.Count > 0);
    public Visibility PollingReadVisibility => VisibleWhen(Has(OpenRazerBackendCapability.PollingRateRead));
    public Visibility PollingWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.PollingRateWrite) &&
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
    public bool CanWritePolling => CanUse(OpenRazerBackendCapability.PollingRateWrite) &&
        PollingOptions.Count > 0 && !IsPollingBusy;
    public bool CanEditPolling => CanUse(OpenRazerBackendCapability.PollingRateWrite) &&
        !IsPollingBusy && PollingOptions.Contains(SelectedPollingRate);

    public Visibility DpiVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.DpiRead,
        OpenRazerBackendCapability.DpiWrite,
        OpenRazerBackendCapability.DpiStagesRead));
    public Visibility DpiReadVisibility => VisibleWhen(Has(OpenRazerBackendCapability.DpiRead));
    public Visibility DpiWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.DpiWrite));
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
    public bool CanWriteDpi => CanUse(OpenRazerBackendCapability.DpiWrite) && !IsDpiBusy;
    public bool CanEditDpi => CanUse(OpenRazerBackendCapability.DpiWrite) && !IsDpiBusy &&
        DpiX is >= 100 && DpiY is >= 100 && DpiX <= MaximumDpi && DpiY <= MaximumDpi;

    public Visibility DpiStagesVisibility => VisibleWhen(Has(OpenRazerBackendCapability.DpiStagesRead));
    public Visibility DpiStagesWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.DpiStagesRead) &&
        Has(OpenRazerBackendCapability.DpiStagesWrite));
    public ObservableCollection<OpenRazerDpiStageRowViewModel> DpiStages { get; } = new();
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
    public bool CanWriteDpiStages => CanUse(OpenRazerBackendCapability.DpiStagesRead) &&
        CanUse(OpenRazerBackendCapability.DpiStagesWrite) && !IsDpiStagesBusy;
    public bool CanEditDpiStages => CanWriteDpiStages && DpiStages.Count is >= 1 and <= 5 &&
        ActiveDpiStage >= 1 && ActiveDpiStage <= DpiStages.Count &&
        DpiStages.All(stage => stage.X is >= 100 && stage.Y is >= 100 && stage.X <= MaximumDpi && stage.Y <= MaximumDpi);

    public Visibility PowerVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.IdleTimeoutRead,
        OpenRazerBackendCapability.IdleTimeoutWrite,
        OpenRazerBackendCapability.LowBatteryThresholdRead,
        OpenRazerBackendCapability.LowBatteryThresholdWrite));
    public Visibility IdleVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.IdleTimeoutRead,
        OpenRazerBackendCapability.IdleTimeoutWrite));
    public Visibility IdleWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.IdleTimeoutWrite));
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
    public bool CanWriteIdle => CanUse(OpenRazerBackendCapability.IdleTimeoutWrite) && !IsIdleBusy;
    public bool CanEditIdle => CanUse(OpenRazerBackendCapability.IdleTimeoutWrite) && !IsIdleBusy &&
        IdleTimeoutSeconds is >= 60 and <= 900;
    public Visibility LowBatteryVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.LowBatteryThresholdRead,
        OpenRazerBackendCapability.LowBatteryThresholdWrite));
    public Visibility LowBatteryWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.LowBatteryThresholdWrite));
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
    public bool CanWriteLowBattery => CanUse(OpenRazerBackendCapability.LowBatteryThresholdWrite) && !IsLowBatteryBusy;
    public bool CanEditLowBattery => CanUse(OpenRazerBackendCapability.LowBatteryThresholdWrite) &&
        !IsLowBatteryBusy && LowBatteryThresholdPercent is >= 5 and <= 25 && LowBatteryThresholdPercent % 5 == 0;

    public Visibility LightingVisibility => VisibleWhen(LightingZones.Count > 0);
    public Visibility LightingControlsVisibility => VisibleWhen(
        LightingZones.Count > 0 &&
        (Has(OpenRazerBackendCapability.LightingEffectWrite) ||
         Has(OpenRazerBackendCapability.MatrixFrameWrite)));
    public Visibility ChromaOverrideVisibility => VisibleWhen(
        _chromaIntegrationEnabled && LightingZones.Count > 0 &&
        Has(OpenRazerBackendCapability.MatrixFrameWrite));
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
            AppStrings.Text("BladeLightingPowerBattery"),
        ];
    public int LightingPowerProfileIndex
    {
        get => _lightingPowerProfileIndex;
        set
        {
            var next = Math.Clamp(value, 0, 2);
            if (SetField(ref _lightingPowerProfileIndex, next))
            {
                ApplyConfiguredLightingToEditor();
            }
        }
    }
    public IReadOnlyList<OpenRazerLedZone> LightingZones => Connection.LightingZones.Keys
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
    public OpenRazerLedZone SelectedLightingZone
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
    public IReadOnlyList<OpenRazerLightingEffect> LightingEffects =>
        (Has(OpenRazerBackendCapability.LightingEffectWrite)
            ? SelectedZoneCapabilities?.LightingEffects
                .Where(effect => effect != OpenRazerLightingEffect.Custom &&
                    !_unsupportedLighting.Contains((SelectedLightingZone, effect)))
                .Order()
                .ToArray() ?? []
            : [])
        .Concat(SupportsSoftwareLighting
            ? [
                OpenRazerLightingEffect.SoftwareAudioMeter,
                OpenRazerLightingEffect.SoftwareSpectrum,
                OpenRazerLightingEffect.SoftwareWave,
                OpenRazerLightingEffect.SoftwareFire,
                OpenRazerLightingEffect.SoftwareWheel,
            ]
            : [])
        .ToArray();
    public Visibility LightingEffectVisibility => VisibleWhen(LightingEffects.Count > 0);
    public Visibility LightingPowerProfileVisibility => VisibleWhen(LightingZones.Any(zone =>
        Connection.LightingZones.TryGetValue(zone, out var capabilities) &&
        ((Has(OpenRazerBackendCapability.LightingEffectWrite) || SupportsSoftwareLighting) &&
         capabilities.LightingEffects.Any(effect => effect != OpenRazerLightingEffect.Custom &&
            !_unsupportedLighting.Contains((zone, effect))) ||
         capabilities.CanWriteBrightness && !_unsupportedBrightnessWrites.Contains(zone) ||
         capabilities.CanWriteState && !_unsupportedLedStateWrites.Contains(zone))));
    public IReadOnlyList<string> LightingEffectOptions => LightingEffects.Select(FormatEffect).ToArray();
    public int SelectedLightingEffectIndex
    {
        get => IndexOf(LightingEffects, SelectedLightingEffect);
        set
        {
            if (value >= 0 && value < LightingEffects.Count) SelectedLightingEffect = LightingEffects[value];
        }
    }
    public OpenRazerLightingEffect SelectedLightingEffect
    {
        get => _selectedLightingEffect;
        set
        {
            if (LightingEffects.Contains(value) && SetField(ref _selectedLightingEffect, value))
            {
                LightingSpeed = Math.Min(LightingSpeed, MaximumLightingSpeed);
                OnPropertyChanged(nameof(SelectedLightingEffectIndex));
                OnPropertyChanged(nameof(PrimaryColorVisibility));
                OnPropertyChanged(nameof(SecondaryColorVisibility));
                OnPropertyChanged(nameof(TertiaryColorVisibility));
                OnPropertyChanged(nameof(LightingSpeedVisibility));
                OnPropertyChanged(nameof(MaximumLightingSpeed));
                OnPropertyChanged(nameof(LightingDirectionVisibility));
                OnPropertyChanged(nameof(CanApplyLighting));
            }
        }
    }
    public Color PrimaryColor { get => _primaryColor; set => SetField(ref _primaryColor, value); }
    public Color SecondaryColor { get => _secondaryColor; set => SetField(ref _secondaryColor, value); }
    public Color TertiaryColor { get => _tertiaryColor; set => SetField(ref _tertiaryColor, value); }
    public byte LightingSpeed { get => _lightingSpeed; set => SetField(ref _lightingSpeed,
        Math.Clamp(value, (byte)1, MaximumLightingSpeed)); }
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
    public Visibility PrimaryColorVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        OpenRazerLightingEffect.Static or OpenRazerLightingEffect.Reactive or OpenRazerLightingEffect.Blinking or
        OpenRazerLightingEffect.BreathingSingle or OpenRazerLightingEffect.BreathingDual or
        OpenRazerLightingEffect.StarlightSingle or OpenRazerLightingEffect.StarlightDual or
        OpenRazerLightingEffect.SoftwareAudioMeter or OpenRazerLightingEffect.SoftwareSpectrum or
        OpenRazerLightingEffect.SoftwareWave or OpenRazerLightingEffect.SoftwareFire or
        OpenRazerLightingEffect.SoftwareWheel);
    public Visibility SecondaryColorVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        OpenRazerLightingEffect.BreathingDual or OpenRazerLightingEffect.StarlightDual or
        OpenRazerLightingEffect.SoftwareAudioMeter or OpenRazerLightingEffect.SoftwareSpectrum or
        OpenRazerLightingEffect.SoftwareWave or OpenRazerLightingEffect.SoftwareFire or
        OpenRazerLightingEffect.SoftwareWheel);
    public Visibility TertiaryColorVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        OpenRazerLightingEffect.SoftwareAudioMeter or OpenRazerLightingEffect.SoftwareSpectrum or
        OpenRazerLightingEffect.SoftwareWave or OpenRazerLightingEffect.SoftwareFire or
        OpenRazerLightingEffect.SoftwareWheel);
    public Visibility LightingSpeedVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        OpenRazerLightingEffect.Reactive or
        OpenRazerLightingEffect.StarlightRandom or OpenRazerLightingEffect.StarlightSingle or
        OpenRazerLightingEffect.StarlightDual or OpenRazerLightingEffect.SoftwareWheel);
    public byte MaximumLightingSpeed => SelectedLightingEffect == OpenRazerLightingEffect.Reactive ? (byte)4 : (byte)3;
    public Visibility LightingDirectionVisibility => VisibleWhen(LightingEffects.Contains(SelectedLightingEffect) &&
        SelectedLightingEffect is
        OpenRazerLightingEffect.Wave or OpenRazerLightingEffect.Wheel or
        OpenRazerLightingEffect.SoftwareWave or OpenRazerLightingEffect.SoftwareWheel);
    public bool IsLightingBusy { get => _isLightingBusy; private set => SetBusy(ref _isLightingBusy, value, nameof(CanApplyLighting), nameof(CanTriggerReactive)); }
    public bool IsLightingLoading { get => _isLightingLoading; private set => SetField(ref _isLightingLoading, value); }
    private bool CanApplyLightingEffect =>
        (IsSoftwareLightingEffect(SelectedLightingEffect)
            ? SupportsSoftwareLighting
            : CanUse(OpenRazerBackendCapability.LightingEffectWrite)) &&
        LightingEffects.Contains(SelectedLightingEffect);
    public Visibility LightingSaveVisibility => VisibleWhen(
        CanApplyLightingEffect ||
        BrightnessWriteVisibility == Visibility.Visible ||
        LedStateWriteVisibility == Visibility.Visible);
    public bool CanApplyLighting => LightingEnabled && !IsLightingBusy &&
        (CanApplyLightingEffect || CanWriteBrightness || CanWriteLedState);
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
    public string BrightnessText => _brightnessKnown || BrightnessWriteVisibility == Visibility.Visible
        ? $"{Math.Round(Brightness / 255d * 100)}%"
        : "--";
    public bool IsBrightnessBusy { get => _isBrightnessBusy; private set => SetBusy(ref _isBrightnessBusy, value, nameof(CanEditBrightness), nameof(CanWriteBrightness)); }
    public bool CanWriteBrightness => CanWrite && !IsBrightnessBusy &&
        SelectedZoneCapabilities?.CanWriteBrightness == true &&
        !_unsupportedBrightnessWrites.Contains(SelectedLightingZone);
    public bool CanEditBrightness => CanWrite && !IsBrightnessBusy &&
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
    public bool CanWriteLedState => CanWrite && !IsLedStateBusy &&
        SelectedZoneCapabilities?.CanWriteState == true &&
        !_unsupportedLedStateWrites.Contains(SelectedLightingZone);
    public Visibility ReactiveTriggerVisibility => VisibleWhen(Has(OpenRazerBackendCapability.ReactiveTriggerWrite));
    public bool CanTriggerReactive => CanUse(OpenRazerBackendCapability.ReactiveTriggerWrite) && !IsLightingBusy;

    public Visibility MatrixVisibility => VisibleWhen(MatrixCells.Count > 0 && Has(OpenRazerBackendCapability.MatrixFrameWrite));
    public ObservableCollection<OpenRazerMatrixCellViewModel> MatrixCells { get; } = new();
    public int MatrixColumns => Connection.Definition.MatrixDimensions?.Columns ?? 1;
    public bool IsMatrixBusy { get => _isMatrixBusy; private set => SetBusy(ref _isMatrixBusy, value, nameof(CanApplyMatrix)); }
    public bool CanApplyMatrix => CanUse(OpenRazerBackendCapability.MatrixFrameWrite) &&
        LightingEnabled && !IsMatrixBusy;

    public Visibility ScrollVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.ScrollAccelerationRead,
        OpenRazerBackendCapability.ScrollAccelerationWrite,
        OpenRazerBackendCapability.SmartReelRead,
        OpenRazerBackendCapability.SmartReelWrite));
    public Visibility ScrollModeVisibility => Visibility.Collapsed;
    public Visibility ScrollModeWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.ScrollModeWrite));
    public Visibility ScrollAccelerationVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.ScrollAccelerationRead, OpenRazerBackendCapability.ScrollAccelerationWrite));
    public Visibility ScrollAccelerationWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.ScrollAccelerationWrite));
    public Visibility SmartReelVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.SmartReelRead, OpenRazerBackendCapability.SmartReelWrite));
    public Visibility SmartReelWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.SmartReelWrite));
    public bool IsScrollLoading { get => _isScrollLoading; private set => SetField(ref _isScrollLoading, value); }
    public byte ScrollMode { get => _scrollMode; set => SetField(ref _scrollMode, value); }
    public bool ScrollAccelerationEnabled { get => _scrollAccelerationEnabled; set => SetField(ref _scrollAccelerationEnabled, value); }
    public bool SmartReelEnabled { get => _smartReelEnabled; set => SetField(ref _smartReelEnabled, value); }
    public bool IsScrollModeBusy { get => _isScrollModeBusy; private set => SetBusy(ref _isScrollModeBusy, value, nameof(CanEditScrollMode), nameof(CanWriteScrollMode)); }
    public bool IsScrollAccelerationBusy { get => _isScrollAccelerationBusy; private set => SetBusy(ref _isScrollAccelerationBusy, value, nameof(CanEditScrollAcceleration), nameof(CanWriteScrollAcceleration)); }
    public bool IsSmartReelBusy { get => _isSmartReelBusy; private set => SetBusy(ref _isSmartReelBusy, value, nameof(CanEditSmartReel), nameof(CanWriteSmartReel)); }
    public bool CanWriteScrollMode => CanUse(OpenRazerBackendCapability.ScrollModeWrite) && !IsScrollModeBusy;
    public bool CanWriteScrollAcceleration => CanUse(OpenRazerBackendCapability.ScrollAccelerationWrite) && !IsScrollAccelerationBusy;
    public bool CanWriteSmartReel => CanUse(OpenRazerBackendCapability.SmartReelWrite) && !IsSmartReelBusy;
    public bool CanEditScrollMode => CanUse(OpenRazerBackendCapability.ScrollModeWrite) && !IsScrollModeBusy;
    public bool CanEditScrollAcceleration => CanUse(OpenRazerBackendCapability.ScrollAccelerationWrite) && !IsScrollAccelerationBusy;
    public bool CanEditSmartReel => CanUse(OpenRazerBackendCapability.SmartReelWrite) && !IsSmartReelBusy;

    public Visibility KeyswitchVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.KeyswitchOptimizationRead,
        OpenRazerBackendCapability.KeyswitchOptimizationWrite));
    public Visibility KeyswitchWriteVisibility => VisibleWhen(Has(OpenRazerBackendCapability.KeyswitchOptimizationWrite));
    public IReadOnlyList<OpenRazerKeyswitchOptimization> KeyswitchOptions { get; } =
        Enum.GetValues<OpenRazerKeyswitchOptimization>();
    public IReadOnlyList<string> KeyswitchOptionTexts =>
        [AppStrings.Text("OpenRazerKeyswitchTyping"), AppStrings.Text("OpenRazerKeyswitchGaming")];
    public int KeyswitchOptimizationIndex
    {
        get => (int)KeyswitchOptimization;
        set
        {
            if (Enum.IsDefined((OpenRazerKeyswitchOptimization)value))
                KeyswitchOptimization = (OpenRazerKeyswitchOptimization)value;
        }
    }
    public OpenRazerKeyswitchOptimization KeyswitchOptimization
    {
        get => _keyswitchOptimization;
        set
        {
            if (SetField(ref _keyswitchOptimization, value)) OnPropertyChanged(nameof(KeyswitchOptimizationIndex));
        }
    }
    public bool IsKeyswitchLoading { get => _isKeyswitchLoading; private set => SetField(ref _isKeyswitchLoading, value); }
    public bool IsKeyswitchBusy { get => _isKeyswitchBusy; private set => SetBusy(ref _isKeyswitchBusy, value, nameof(CanEditKeyswitch), nameof(CanWriteKeyswitch)); }
    public bool CanWriteKeyswitch => CanUse(OpenRazerBackendCapability.KeyswitchOptimizationWrite) && !IsKeyswitchBusy;
    public bool CanEditKeyswitch => CanUse(OpenRazerBackendCapability.KeyswitchOptimizationWrite) && !IsKeyswitchBusy;

    public Visibility FnPrimaryVisibility => VisibleWhen(Has(OpenRazerBackendCapability.FnPrimaryWrite));
    public bool IsFnBusy { get => _isFnBusy; private set => SetBusy(ref _isFnBusy, value, nameof(CanSetFnPrimary)); }
    public bool CanSetFnPrimary => CanUse(OpenRazerBackendCapability.FnPrimaryWrite) && !IsFnBusy;

    public Visibility HyperPollingVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.HyperPollingIndicatorWrite,
        OpenRazerBackendCapability.HyperPollingPairWrite,
        OpenRazerBackendCapability.HyperPollingUnpairWrite));
    public Visibility HyperPollingIndicatorVisibility => VisibleWhen(Has(OpenRazerBackendCapability.HyperPollingIndicatorWrite));
    public Visibility HyperPollingPairVisibility => VisibleWhen(Has(OpenRazerBackendCapability.HyperPollingPairWrite));
    public Visibility HyperPollingUnpairVisibility => VisibleWhen(Has(OpenRazerBackendCapability.HyperPollingUnpairWrite));
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
    public bool CanUseHyperPolling => CanWrite && !IsHyperPollingBusy;
    public bool CanSetHyperPollingIndicator => CanUseHyperPolling && Has(OpenRazerBackendCapability.HyperPollingIndicatorWrite);
    public bool CanPairHyperPolling => CanUseHyperPolling && Has(OpenRazerBackendCapability.HyperPollingPairWrite);
    public bool CanUnpairHyperPolling => CanUseHyperPolling && Has(OpenRazerBackendCapability.HyperPollingUnpairWrite);

}
