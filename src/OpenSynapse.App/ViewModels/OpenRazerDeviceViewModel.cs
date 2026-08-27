using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class OpenRazerDeviceViewModel : INotifyPropertyChanged
{
    private static readonly Color DefaultPrimaryColor = Color.FromArgb(255, 0, 255, 102);
    private static readonly Color DefaultSecondaryColor = Color.FromArgb(255, 0, 153, 255);
    private readonly OpenRazerDeviceService _service;
    private readonly HashSet<OpenRazerBackendCapability> _unsupported = [];
    private readonly HashSet<(OpenRazerLedZone Zone, OpenRazerLightingEffect Effect)> _unsupportedLighting = [];
    private readonly HashSet<OpenRazerLedZone> _unsupportedBrightnessWrites = [];
    private readonly HashSet<OpenRazerLedZone> _unsupportedLedStateWrites = [];
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
    private byte _lightingSpeed = 2;
    private byte _lightingDirection = 1;
    private byte _scrollMode;
    private bool _scrollAccelerationEnabled;
    private bool _smartReelEnabled;
    private OpenRazerKeyswitchOptimization _keyswitchOptimization;
    private byte _activeDpiStage = 1;
    private byte _hyperPollingIndicatorMode = 1;
    private bool _ledEnabled = true;

    public OpenRazerDeviceViewModel(
        OpenRazerDeviceService service,
        OpenRazerDeviceConnection connection)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _errorText = connection.Error ?? string.Empty;
        LightingZones = connection.LightingZones.Keys.Order().ToArray();
        _selectedLightingZone = LightingZones.FirstOrDefault();
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

    public OpenRazerDeviceConnection Connection { get; }
    public string InstanceId => Connection.InstanceId;
    public string Name => Connection.Definition.DisplayName;
    public string CategoryText => Connection.Definition.Category switch
    {
        DeviceCategory.Mouse => AppStrings.Get("鼠标"),
        DeviceCategory.Laptop => AppStrings.Get("笔记本"),
        DeviceCategory.Keyboard => AppStrings.Get("键盘"),
        DeviceCategory.MouseMat => AppStrings.Get("鼠标垫"),
        DeviceCategory.Monitor => AppStrings.Get("显示器"),
        DeviceCategory.Accessory => AppStrings.Get("配件"),
        _ => AppStrings.Get("设备"),
    };
    public string Identity => $"VID_1532 / PID_{Connection.Definition.ProductId:X4}";
    public string StatusText => RequiresRescan
        ? AppStrings.Get("需要重新扫描")
        : Connection.EndpointState switch
        {
            OpenRazerEndpointState.Resolved => AppStrings.Get("已解析"),
            OpenRazerEndpointState.RecognizedButUnresolved => AppStrings.Get("控制通道未解析"),
            _ => AppStrings.Get("忙或不可用"),
        };
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsReady => Connection.IsReady;
    public bool RequiresRescan { get => _requiresRescan; private set => SetField(ref _requiresRescan, value); }
    public bool CanWrite => IsReady && !RequiresRescan;

    public bool HasBasicSection => HasAny(
        OpenRazerBackendCapability.FirmwareRead,
        OpenRazerBackendCapability.SerialRead,
        OpenRazerBackendCapability.DeviceModeRead,
        OpenRazerBackendCapability.BatteryRead,
        OpenRazerBackendCapability.ChargingRead);
    public Visibility BasicVisibility => VisibleWhen(HasBasicSection);
    public bool IsBasicBusy { get => _isBasicBusy; private set => SetField(ref _isBasicBusy, value); }
    public IReadOnlyDictionary<string, string> BasicErrors =>
        _basicState?.Errors ?? new Dictionary<string, string>();
    public string FirmwareText => _basicState?.Firmware?.ToString() ?? "--";
    public string SerialText => _basicState?.Serial ?? "--";
    public string SoftwareModeText => _basicState?.SoftwareMode switch
    {
        true => AppStrings.Get("软件模式"),
        false => AppStrings.Get("硬件模式"),
        null => "--",
    };
    public string BatteryText => _basicState?.BatteryPercent is { } value ? $"{value}%" : "--";
    public string ChargingText => _basicState?.IsCharging switch
    {
        true => AppStrings.Get("充电中"),
        false => AppStrings.Get("未充电"),
        null => "--",
    };

    public Visibility PollingVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.PollingRateRead,
        OpenRazerBackendCapability.PollingRateWrite));
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
    public bool CanWritePolling => CanUse(OpenRazerBackendCapability.PollingRateWrite) && !IsPollingBusy;
    public bool CanEditPolling => CanUse(OpenRazerBackendCapability.PollingRateWrite) &&
        !IsPollingBusy && PollingOptions.Contains(SelectedPollingRate);

    public Visibility DpiVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.DpiRead,
        OpenRazerBackendCapability.DpiWrite));
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

    public Visibility DpiStagesVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.DpiStagesRead,
        OpenRazerBackendCapability.DpiStagesWrite));
    public ObservableCollection<OpenRazerDpiStageRowViewModel> DpiStages { get; } = new();
    public byte ActiveDpiStage
    {
        get => _activeDpiStage;
        set
        {
            if (SetField(ref _activeDpiStage, value)) OnPropertyChanged(nameof(CanEditDpiStages));
        }
    }
    public bool IsDpiStagesLoading { get => _isDpiStagesLoading; private set => SetField(ref _isDpiStagesLoading, value); }
    public bool IsDpiStagesBusy { get => _isDpiStagesBusy; private set => SetBusy(ref _isDpiStagesBusy, value, nameof(CanEditDpiStages), nameof(CanWriteDpiStages)); }
    public bool CanWriteDpiStages => CanUse(OpenRazerBackendCapability.DpiStagesWrite) && !IsDpiStagesBusy;
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
    public IReadOnlyList<OpenRazerLedZone> LightingZones { get; }
    public IReadOnlyList<string> LightingZoneOptions => LightingZones.Select(FormatZone).ToArray();
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
            SelectedLightingEffect = LightingEffects.FirstOrDefault();
            OnLightingSelectionChanged();
        }
    }
    public IReadOnlyList<OpenRazerLightingEffect> LightingEffects =>
        SelectedZoneCapabilities?.LightingEffects
            .Where(effect => !_unsupportedLighting.Contains((SelectedLightingZone, effect)))
            .Order()
            .ToArray() ?? [];
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
                OnPropertyChanged(nameof(SelectedLightingEffectIndex));
                OnPropertyChanged(nameof(PrimaryColorVisibility));
                OnPropertyChanged(nameof(SecondaryColorVisibility));
                OnPropertyChanged(nameof(LightingSpeedVisibility));
                OnPropertyChanged(nameof(MaximumLightingSpeed));
                OnPropertyChanged(nameof(LightingDirectionVisibility));
                OnPropertyChanged(nameof(CanApplyLighting));
            }
        }
    }
    public Color PrimaryColor { get => _primaryColor; set => SetField(ref _primaryColor, value); }
    public Color SecondaryColor { get => _secondaryColor; set => SetField(ref _secondaryColor, value); }
    public byte LightingSpeed { get => _lightingSpeed; set => SetField(ref _lightingSpeed, value); }
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
    public Visibility PrimaryColorVisibility => VisibleWhen(SelectedLightingEffect is
        OpenRazerLightingEffect.Static or OpenRazerLightingEffect.Reactive or OpenRazerLightingEffect.Blinking or
        OpenRazerLightingEffect.BreathingSingle or OpenRazerLightingEffect.BreathingDual or
        OpenRazerLightingEffect.StarlightSingle or OpenRazerLightingEffect.StarlightDual);
    public Visibility SecondaryColorVisibility => VisibleWhen(SelectedLightingEffect is
        OpenRazerLightingEffect.BreathingDual or OpenRazerLightingEffect.StarlightDual);
    public Visibility LightingSpeedVisibility => VisibleWhen(SelectedLightingEffect is
        OpenRazerLightingEffect.Reactive or OpenRazerLightingEffect.BreathingRandom or
        OpenRazerLightingEffect.BreathingSingle or OpenRazerLightingEffect.BreathingDual or
        OpenRazerLightingEffect.StarlightRandom or OpenRazerLightingEffect.StarlightSingle or
        OpenRazerLightingEffect.StarlightDual);
    public byte MaximumLightingSpeed => SelectedLightingEffect == OpenRazerLightingEffect.Reactive ? (byte)4 : (byte)3;
    public Visibility LightingDirectionVisibility => VisibleWhen(SelectedLightingEffect is
        OpenRazerLightingEffect.Wave or OpenRazerLightingEffect.Wheel);
    public bool IsLightingBusy { get => _isLightingBusy; private set => SetBusy(ref _isLightingBusy, value, nameof(CanApplyLighting), nameof(CanTriggerReactive)); }
    public bool IsLightingLoading { get => _isLightingLoading; private set => SetField(ref _isLightingLoading, value); }
    public bool CanApplyLighting => CanUse(OpenRazerBackendCapability.LightingEffectWrite) &&
        !IsLightingBusy && LightingEffects.Contains(SelectedLightingEffect);
    public Visibility BrightnessVisibility => VisibleWhen(SelectedZoneCapabilities is { } zone &&
        (zone.CanReadBrightness || zone.CanWriteBrightness));
    public byte Brightness
    {
        get => _brightness;
        set
        {
            if (SetField(ref _brightness, value)) OnPropertyChanged(nameof(BrightnessText));
        }
    }
    public string BrightnessText => $"{Math.Round(Brightness / 255d * 100)}%";
    public bool IsBrightnessBusy { get => _isBrightnessBusy; private set => SetBusy(ref _isBrightnessBusy, value, nameof(CanEditBrightness), nameof(CanWriteBrightness)); }
    public bool CanWriteBrightness => CanWrite && !IsBrightnessBusy &&
        SelectedZoneCapabilities?.CanWriteBrightness == true &&
        !_unsupportedBrightnessWrites.Contains(SelectedLightingZone);
    public bool CanEditBrightness => CanWrite && !IsBrightnessBusy &&
        SelectedZoneCapabilities?.CanWriteBrightness == true &&
        !_unsupportedBrightnessWrites.Contains(SelectedLightingZone);
    public Visibility LedStateVisibility => VisibleWhen(SelectedZoneCapabilities is { } zone &&
        (zone.CanReadState || zone.CanWriteState));
    public bool LedEnabled { get => _ledEnabled; set => SetField(ref _ledEnabled, value); }
    public bool IsLedStateBusy { get => _isLedStateBusy; private set => SetBusy(ref _isLedStateBusy, value, nameof(CanWriteLedState)); }
    public bool CanWriteLedState => CanWrite && !IsLedStateBusy &&
        SelectedZoneCapabilities?.CanWriteState == true &&
        !_unsupportedLedStateWrites.Contains(SelectedLightingZone);
    public Visibility ReactiveTriggerVisibility => VisibleWhen(Has(OpenRazerBackendCapability.ReactiveTriggerWrite));
    public bool CanTriggerReactive => CanUse(OpenRazerBackendCapability.ReactiveTriggerWrite) && !IsLightingBusy;

    public Visibility MatrixVisibility => VisibleWhen(MatrixCells.Count > 0);
    public ObservableCollection<OpenRazerMatrixCellViewModel> MatrixCells { get; } = new();
    public int MatrixColumns => Connection.Definition.MatrixDimensions?.Columns ?? 1;
    public bool IsMatrixBusy { get => _isMatrixBusy; private set => SetBusy(ref _isMatrixBusy, value, nameof(CanApplyMatrix)); }
    public bool CanApplyMatrix => CanUse(OpenRazerBackendCapability.MatrixFrameWrite) && !IsMatrixBusy;

    public Visibility ScrollVisibility => VisibleWhen(HasAny(
        OpenRazerBackendCapability.ScrollModeRead,
        OpenRazerBackendCapability.ScrollModeWrite,
        OpenRazerBackendCapability.ScrollAccelerationRead,
        OpenRazerBackendCapability.ScrollAccelerationWrite,
        OpenRazerBackendCapability.SmartReelRead,
        OpenRazerBackendCapability.SmartReelWrite));
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

    public async Task LoadBasicStateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsBasicBusy)
        {
            return;
        }
        IsBasicBusy = true;
        try
        {
            var state = await _service.ReadBasicStateAsync(Connection, cancellationToken);
            _basicState = state;
            if (state.PollingRate is { } polling) SelectedPollingRate = polling;
            if (state.DpiX is { } x) DpiX = x;
            if (state.DpiY is { } y) DpiY = y;
            if (state.IdleTimeoutSeconds is { } idle) IdleTimeoutSeconds = idle;
            if (state.LowBatteryThresholdPercent is { } threshold) LowBatteryThresholdPercent = threshold;
            if (state.Brightness is { } brightness) Brightness = brightness;
            if (state.Errors.Count > 0)
            {
                MarkRequiresRescan(string.Join(Environment.NewLine, state.Errors.Values));
            }
            OnBasicStateChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
        finally
        {
            IsBasicBusy = false;
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        LoadBasicStateAsync(cancellationToken);

    public Task ApplyPollingAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.PollingRateWrite,
            () => IsPollingBusy, value => IsPollingBusy = value,
            async () =>
            {
                if (!PollingOptions.Contains(SelectedPollingRate)) throw new ArgumentOutOfRangeException(nameof(SelectedPollingRate));
                await _service.SetPollingRateAsync(Connection, SelectedPollingRate, cancellationToken);
                if (Has(OpenRazerBackendCapability.PollingRateRead))
                    SelectedPollingRate = await _service.GetPollingRateAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { PollingRate = SelectedPollingRate });
            }, cancellationToken);

    public Task ApplyDpiAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.DpiWrite,
            () => IsDpiBusy, value => IsDpiBusy = value,
            async () =>
            {
                await _service.SetDpiAsync(Connection, DpiX, DpiY, cancellationToken);
                if (Has(OpenRazerBackendCapability.DpiRead))
                    (DpiX, DpiY) = await _service.GetDpiAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { DpiX = DpiX, DpiY = DpiY });
            }, cancellationToken);

    public async Task LoadDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        if (!Has(OpenRazerBackendCapability.DpiStagesRead) || RequiresRescan || IsDpiStagesLoading) return;
        IsDpiStagesLoading = true;
        try
        {
            ReplaceDpiStages(await _service.GetDpiStagesAsync(Connection, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, OpenRazerBackendCapability.DpiStagesRead);
        }
        finally
        {
            IsDpiStagesLoading = false;
        }
    }

    public Task ApplyDpiStagesAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.DpiStagesWrite,
            () => IsDpiStagesBusy, value => IsDpiStagesBusy = value,
            async () =>
            {
                var state = new OpenRazerDpiStages(ActiveDpiStage,
                    DpiStages.Select(stage => new OpenRazerDpiStage(stage.Number, stage.X, stage.Y)).ToArray());
                await _service.SetDpiStagesAsync(Connection, state, cancellationToken);
                if (Has(OpenRazerBackendCapability.DpiStagesRead))
                    ReplaceDpiStages(await _service.GetDpiStagesAsync(Connection, cancellationToken));
            }, cancellationToken);

    public Task ApplyIdleTimeoutAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.IdleTimeoutWrite,
            () => IsIdleBusy, value => IsIdleBusy = value,
            async () =>
            {
                await _service.SetIdleTimeoutAsync(Connection, IdleTimeoutSeconds, cancellationToken);
                if (Has(OpenRazerBackendCapability.IdleTimeoutRead))
                    IdleTimeoutSeconds = await _service.GetIdleTimeoutAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { IdleTimeoutSeconds = IdleTimeoutSeconds });
            }, cancellationToken);

    public Task ApplyLowBatteryThresholdAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.LowBatteryThresholdWrite,
            () => IsLowBatteryBusy, value => IsLowBatteryBusy = value,
            async () =>
            {
                await _service.SetLowBatteryThresholdAsync(Connection, LowBatteryThresholdPercent, cancellationToken);
                if (Has(OpenRazerBackendCapability.LowBatteryThresholdRead))
                    LowBatteryThresholdPercent = await _service.GetLowBatteryThresholdAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { LowBatteryThresholdPercent = LowBatteryThresholdPercent });
            }, cancellationToken);

    public async Task LoadLightingAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsLightingLoading || SelectedZoneCapabilities is null)
        {
            return;
        }
        IsLightingLoading = true;
        try
        {
            var zone = SelectedZoneCapabilities;
            if (zone.CanReadBrightness)
            {
                Brightness = await _service.GetBrightnessAsync(Connection,
                    Connection.Definition.DefaultStorage, SelectedLightingZone, cancellationToken);
            }
            if (zone.CanReadState)
            {
                LedEnabled = await _service.GetLedStateAsync(Connection,
                    Connection.Definition.DefaultStorage, SelectedLightingZone, cancellationToken);
            }
            if (zone.CanReadColor)
            {
                var color = await _service.GetLedColorAsync(Connection,
                    Connection.Definition.DefaultStorage, SelectedLightingZone, cancellationToken);
                PrimaryColor = ToColor(color);
            }
            if (zone.CanReadEffect)
            {
                var effect = await _service.GetLedEffectAsync(Connection,
                    Connection.Definition.DefaultStorage, SelectedLightingZone, cancellationToken);
                var mapped = effect switch
                {
                    OpenRazerClassicLedEffect.Static => OpenRazerLightingEffect.Static,
                    OpenRazerClassicLedEffect.Blinking => OpenRazerLightingEffect.Blinking,
                    OpenRazerClassicLedEffect.Breathing => OpenRazerLightingEffect.BreathingSingle,
                    OpenRazerClassicLedEffect.Spectrum => OpenRazerLightingEffect.Spectrum,
                    _ => (OpenRazerLightingEffect?)null,
                };
                if (mapped is { } value && LightingEffects.Contains(value)) SelectedLightingEffect = value;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
        finally
        {
            IsLightingLoading = false;
        }
    }

    public Task ApplyBrightnessAsync(CancellationToken cancellationToken = default)
    {
        var zone = SelectedLightingZone;
        var capabilities = SelectedZoneCapabilities;
        return RunZoneWriteAsync(() => capabilities?.CanWriteBrightness == true &&
                !_unsupportedBrightnessWrites.Contains(zone),
            () => IsBrightnessBusy, value => IsBrightnessBusy = value,
            async () =>
            {
                await _service.SetBrightnessAsync(Connection, Brightness,
                    Connection.Definition.DefaultStorage, zone, cancellationToken);
                if (capabilities?.CanReadBrightness == true)
                    Brightness = await _service.GetBrightnessAsync(Connection,
                        Connection.Definition.DefaultStorage, zone, cancellationToken);
                OnPropertyChanged(nameof(BrightnessText));
            }, cancellationToken,
            () =>
            {
                _unsupportedBrightnessWrites.Add(zone);
                OnPropertyChanged(nameof(CanWriteBrightness));
                OnPropertyChanged(nameof(CanEditBrightness));
            });
    }

    public Task ApplyLedStateAsync(CancellationToken cancellationToken = default)
    {
        var zone = SelectedLightingZone;
        var enabled = LedEnabled;
        return RunZoneWriteAsync(
            () => Connection.LightingZones.GetValueOrDefault(zone)?.CanWriteState == true &&
                !_unsupportedLedStateWrites.Contains(zone),
            () => IsLedStateBusy, value => IsLedStateBusy = value,
            async () =>
            {
                await _service.SetLedStateAsync(Connection, Connection.Definition.DefaultStorage, zone, enabled, cancellationToken);
                if (Connection.LightingZones.GetValueOrDefault(zone)?.CanReadState == true)
                    LedEnabled = await _service.GetLedStateAsync(Connection, Connection.Definition.DefaultStorage, zone, cancellationToken);
            }, cancellationToken,
            () =>
            {
                _unsupportedLedStateWrites.Add(zone);
                OnLightingSelectionChanged();
            });
    }

    public Task TriggerReactiveAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.ReactiveTriggerWrite,
            () => IsLightingBusy, value => IsLightingBusy = value,
            () => _service.TriggerReactiveAsync(Connection, cancellationToken), cancellationToken);

    public Task ApplyMatrixAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.MatrixFrameWrite,
            () => IsMatrixBusy, value => IsMatrixBusy = value,
            async () =>
            {
                foreach (var row in MatrixCells.GroupBy(cell => cell.Row).OrderBy(group => group.Key))
                {
                    var colors = row.OrderBy(cell => cell.Column)
                        .Select(cell => ToOpenRazerColor(cell.Color)).ToArray();
                    await _service.SetCustomRowAsync(Connection, row.Key, 0, colors, cancellationToken);
                }
            }, cancellationToken);

    public Task ApplyLightingAsync(CancellationToken cancellationToken = default)
    {
        var zone = SelectedLightingZone;
        var effect = SelectedLightingEffect;
        return RunZoneWriteAsync(
            () => Connection.Capabilities.Contains(OpenRazerBackendCapability.LightingEffectWrite) &&
                Connection.LightingZones.TryGetValue(zone, out var capability) &&
                capability.LightingEffects.Contains(effect) && !_unsupportedLighting.Contains((zone, effect)),
            () => IsLightingBusy, value => IsLightingBusy = value,
            () => _service.SetLightingAsync(Connection, new OpenRazerLightingSettings(
                effect,
                LightingSpeed,
                LightingDirection,
                ToOpenRazerColor(PrimaryColor),
                ToOpenRazerColor(SecondaryColor),
                null,
                zone), cancellationToken),
            cancellationToken,
            () =>
            {
                _unsupportedLighting.Add((zone, effect));
                if (SelectedLightingZone == zone && SelectedLightingEffect == effect)
                    _selectedLightingEffect = LightingEffects.FirstOrDefault();
                OnLightingSelectionChanged();
            });
    }

    public async Task LoadScrollAsync(CancellationToken cancellationToken = default)
    {
        if (!IsReady || RequiresRescan || IsScrollLoading)
        {
            return;
        }
        IsScrollLoading = true;
        try
        {
            await ReadOptionalAsync(OpenRazerBackendCapability.ScrollModeRead,
                async () => ScrollMode = await _service.GetScrollModeAsync(Connection, cancellationToken));
            await ReadOptionalAsync(OpenRazerBackendCapability.ScrollAccelerationRead,
                async () => ScrollAccelerationEnabled = await _service.GetScrollAccelerationAsync(Connection, cancellationToken));
            await ReadOptionalAsync(OpenRazerBackendCapability.SmartReelRead,
                async () => SmartReelEnabled = await _service.GetSmartReelAsync(Connection, cancellationToken));
        }
        finally
        {
            IsScrollLoading = false;
        }
    }

    public Task ApplyScrollModeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.ScrollModeWrite,
            () => IsScrollModeBusy, value => IsScrollModeBusy = value,
            async () =>
            {
                await _service.SetScrollModeAsync(Connection, ScrollMode, cancellationToken);
                if (Has(OpenRazerBackendCapability.ScrollModeRead))
                    ScrollMode = await _service.GetScrollModeAsync(Connection, cancellationToken);
            }, cancellationToken);

    public Task ApplyScrollAccelerationAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.ScrollAccelerationWrite,
            () => IsScrollAccelerationBusy, value => IsScrollAccelerationBusy = value,
            async () =>
            {
                await _service.SetScrollAccelerationAsync(Connection, ScrollAccelerationEnabled, cancellationToken);
                if (Has(OpenRazerBackendCapability.ScrollAccelerationRead))
                    ScrollAccelerationEnabled = await _service.GetScrollAccelerationAsync(Connection, cancellationToken);
            }, cancellationToken);

    public Task ApplySmartReelAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.SmartReelWrite,
            () => IsSmartReelBusy, value => IsSmartReelBusy = value,
            async () =>
            {
                await _service.SetSmartReelAsync(Connection, SmartReelEnabled, cancellationToken);
                if (Has(OpenRazerBackendCapability.SmartReelRead))
                    SmartReelEnabled = await _service.GetSmartReelAsync(Connection, cancellationToken);
            }, cancellationToken);

    public async Task LoadKeyswitchAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUse(OpenRazerBackendCapability.KeyswitchOptimizationRead) || IsKeyswitchLoading)
        {
            return;
        }
        IsKeyswitchLoading = true;
        try
        {
            KeyswitchOptimization = await _service.GetKeyswitchOptimizationAsync(Connection, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, OpenRazerBackendCapability.KeyswitchOptimizationRead);
        }
        finally
        {
            IsKeyswitchLoading = false;
        }
    }

    public Task ApplyKeyswitchAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.KeyswitchOptimizationWrite,
            () => IsKeyswitchBusy, value => IsKeyswitchBusy = value,
            async () =>
            {
                await _service.SetKeyswitchOptimizationAsync(Connection, KeyswitchOptimization, cancellationToken);
                if (Has(OpenRazerBackendCapability.KeyswitchOptimizationRead))
                    KeyswitchOptimization = await _service.GetKeyswitchOptimizationAsync(Connection, cancellationToken);
            }, cancellationToken);

    public Task SetFnPrimaryAsync(bool fnFunctionsArePrimary, CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.FnPrimaryWrite,
            () => IsFnBusy, value => IsFnBusy = value,
            () => _service.SetFnPrimaryAsync(Connection, fnFunctionsArePrimary, cancellationToken),
            cancellationToken);

    public Task SetHyperPollingIndicatorAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.HyperPollingIndicatorWrite,
            () => IsHyperPollingBusy, value => IsHyperPollingBusy = value,
            () => _service.SetHyperPollingIndicatorAsync(Connection, HyperPollingIndicatorMode, cancellationToken),
            cancellationToken);

    public Task PairHyperPollingAsync(ushort mouseProductId, CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.HyperPollingPairWrite,
            () => IsHyperPollingBusy, value => IsHyperPollingBusy = value,
            () => _service.PairHyperPollingAsync(Connection, mouseProductId, cancellationToken),
            cancellationToken);

    public Task UnpairHyperPollingAsync(ushort mouseProductId, CancellationToken cancellationToken = default) =>
        RunWriteAsync(OpenRazerBackendCapability.HyperPollingUnpairWrite,
            () => IsHyperPollingBusy, value => IsHyperPollingBusy = value,
            () => _service.UnpairHyperPollingAsync(Connection, mouseProductId, cancellationToken),
            cancellationToken);

    public void RefreshLocalization()
    {
        foreach (var row in DpiStages) row.RefreshLocalization();
        foreach (var cell in MatrixCells) cell.RefreshLocalization();
        OnPropertyChanged(string.Empty);
    }

    private void ReplaceDpiStages(OpenRazerDpiStages state)
    {
        DpiStages.Clear();
        foreach (var stage in state.Stages)
        {
            var row = new OpenRazerDpiStageRowViewModel(stage.Number, stage.X, stage.Y, CanWriteDpiStages);
            row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanEditDpiStages));
            DpiStages.Add(row);
        }
        ActiveDpiStage = state.ActiveStage;
        OnPropertyChanged(nameof(CanEditDpiStages));
    }

    private OpenRazerLightingZoneCapabilities? SelectedZoneCapabilities =>
        Connection.LightingZones.GetValueOrDefault(SelectedLightingZone);

    private bool Has(OpenRazerBackendCapability capability) =>
        Connection.Capabilities.Contains(capability) && !_unsupported.Contains(capability);

    private bool HasAny(params OpenRazerBackendCapability[] capabilities) => capabilities.Any(Has);

    private bool CanUse(OpenRazerBackendCapability capability) => CanWrite && Has(capability);

    private static Visibility VisibleWhen(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private async Task RunWriteAsync(
        OpenRazerBackendCapability capability,
        Func<bool> isBusy,
        Action<bool> setBusy,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        if (!CanUse(capability) || isBusy())
        {
            return;
        }
        setBusy(true);
        try
        {
            await operation();
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailure(exception, capability);
        }
        finally
        {
            setBusy(false);
        }
    }

    private async Task RunZoneWriteAsync(
        Func<bool> isSupported,
        Func<bool> isBusy,
        Action<bool> setBusy,
        Func<Task> operation,
        CancellationToken cancellationToken,
        Action? onNotSupported = null)
    {
        if (!CanWrite || !isSupported() || isBusy())
        {
            return;
        }
        setBusy(true);
        try
        {
            await operation();
            ErrorText = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (NotSupportedException exception)
        {
            onNotSupported?.Invoke();
            ErrorText = exception.Message;
        }
        catch (Exception exception)
        {
            HandleFailure(exception);
        }
        finally
        {
            setBusy(false);
        }
    }

    private async Task ReadOptionalAsync(OpenRazerBackendCapability capability, Func<Task> read)
    {
        if (!Has(capability) || RequiresRescan)
        {
            return;
        }
        try
        {
            await read();
        }
        catch (Exception exception)
        {
            HandleFailure(exception, capability);
        }
    }

    private void HandleFailure(Exception exception, OpenRazerBackendCapability? capability = null)
    {
        if (exception is NotSupportedException && capability is { } unsupported)
        {
            _unsupported.Add(unsupported);
            OnCapabilityChanged();
        }
        if (exception is IOException or Win32Exception or InvalidOperationException)
        {
            MarkRequiresRescan(exception.Message);
            return;
        }
        ErrorText = exception.Message;
    }

    private void MarkRequiresRescan(string message)
    {
        ErrorText = message;
        RequiresRescan = true;
        OnCapabilityChanged();
    }

    private void UpdateBasic(Func<OpenRazerBasicState, OpenRazerBasicState> update)
    {
        if (_basicState is not null)
        {
            _basicState = update(_basicState);
            OnBasicStateChanged();
        }
    }

    private void OnBasicStateChanged()
    {
        OnPropertyChanged(nameof(BasicErrors));
        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(SerialText));
        OnPropertyChanged(nameof(SoftwareModeText));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ChargingText));
        OnPropertyChanged(nameof(PollingRateText));
        OnPropertyChanged(nameof(DpiText));
        OnPropertyChanged(nameof(IdleTimeoutText));
        OnPropertyChanged(nameof(LowBatteryThresholdText));
        OnPropertyChanged(nameof(BrightnessText));
    }

    private void OnLightingSelectionChanged()
    {
        OnPropertyChanged(nameof(LightingZoneOptions));
        OnPropertyChanged(nameof(SelectedLightingZoneIndex));
        OnPropertyChanged(nameof(LightingEffects));
        OnPropertyChanged(nameof(LightingEffectOptions));
        OnPropertyChanged(nameof(SelectedLightingEffectIndex));
        OnPropertyChanged(nameof(SelectedLightingEffect));
        OnPropertyChanged(nameof(BrightnessVisibility));
        OnPropertyChanged(nameof(CanWriteBrightness));
        OnPropertyChanged(nameof(CanEditBrightness));
        OnPropertyChanged(nameof(LedStateVisibility));
        OnPropertyChanged(nameof(CanWriteLedState));
        OnPropertyChanged(nameof(PrimaryColorVisibility));
        OnPropertyChanged(nameof(SecondaryColorVisibility));
        OnPropertyChanged(nameof(LightingSpeedVisibility));
        OnPropertyChanged(nameof(MaximumLightingSpeed));
        OnPropertyChanged(nameof(LightingDirectionVisibility));
        OnPropertyChanged(nameof(CanApplyLighting));
    }

    private static string FormatZone(OpenRazerLedZone zone) => AppStrings.Text($"OpenRazerZone{zone}");
    private static string FormatEffect(OpenRazerLightingEffect effect) => AppStrings.Text($"OpenRazerEffect{effect}");

    private static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return index;
        }
        return -1;
    }

    private void OnCapabilityChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(PollingVisibility));
        OnPropertyChanged(nameof(CanEditPolling));
        OnPropertyChanged(nameof(CanWritePolling));
        OnPropertyChanged(nameof(DpiVisibility));
        OnPropertyChanged(nameof(CanEditDpi));
        OnPropertyChanged(nameof(CanWriteDpi));
        OnPropertyChanged(nameof(DpiStagesVisibility));
        OnPropertyChanged(nameof(CanEditDpiStages));
        OnPropertyChanged(nameof(CanWriteDpiStages));
        OnPropertyChanged(nameof(PowerVisibility));
        OnPropertyChanged(nameof(CanEditIdle));
        OnPropertyChanged(nameof(CanWriteIdle));
        OnPropertyChanged(nameof(CanEditLowBattery));
        OnPropertyChanged(nameof(CanWriteLowBattery));
        OnPropertyChanged(nameof(CanEditBrightness));
        OnPropertyChanged(nameof(CanWriteBrightness));
        OnPropertyChanged(nameof(LedStateVisibility));
        OnPropertyChanged(nameof(CanWriteLedState));
        OnPropertyChanged(nameof(ReactiveTriggerVisibility));
        OnPropertyChanged(nameof(CanTriggerReactive));
        OnPropertyChanged(nameof(MatrixVisibility));
        OnPropertyChanged(nameof(CanApplyMatrix));
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(ScrollVisibility));
        OnPropertyChanged(nameof(CanEditScrollMode));
        OnPropertyChanged(nameof(CanWriteScrollMode));
        OnPropertyChanged(nameof(CanEditScrollAcceleration));
        OnPropertyChanged(nameof(CanWriteScrollAcceleration));
        OnPropertyChanged(nameof(CanEditSmartReel));
        OnPropertyChanged(nameof(CanWriteSmartReel));
        OnPropertyChanged(nameof(KeyswitchVisibility));
        OnPropertyChanged(nameof(CanEditKeyswitch));
        OnPropertyChanged(nameof(CanWriteKeyswitch));
        OnPropertyChanged(nameof(FnPrimaryVisibility));
        OnPropertyChanged(nameof(CanSetFnPrimary));
        OnPropertyChanged(nameof(HyperPollingVisibility));
        OnPropertyChanged(nameof(CanUseHyperPolling));
        OnPropertyChanged(nameof(CanSetHyperPollingIndicator));
        OnPropertyChanged(nameof(CanPairHyperPolling));
        OnPropertyChanged(nameof(CanUnpairHyperPolling));
    }

    private static OpenRazerColor ToOpenRazerColor(Color color) => new(color.R, color.G, color.B);
    private static Color ToColor(OpenRazerColor color) => Color.FromArgb(255, color.Red, color.Green, color.Blue);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(ErrorText)) OnPropertyChanged(nameof(HasError));
        return true;
    }

    private void SetBusy(ref bool field, bool value, string dependentProperty, [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            OnPropertyChanged(dependentProperty);
        }
    }

    private void SetBusy(ref bool field, bool value, string canEditProperty, string canWriteProperty, [CallerMemberName] string? propertyName = null)
    {
        if (SetField(ref field, value, propertyName))
        {
            OnPropertyChanged(canEditProperty);
            OnPropertyChanged(canWriteProperty);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
