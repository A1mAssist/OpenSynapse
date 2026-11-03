using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private bool _isTimeToDimBusy;
    private bool _timeToDimEnabled;
    private int _timeToDimSeconds = 60;
    private int _timeToDimPercent = 20;
    private bool _indicatorLedEnabled;
    private bool _isIndicatorLedBusy;
    private int _indicatorLed1Index;
    private int _indicatorLed2Index;
    private int _indicatorLed3Index;
    private bool _isIndicatorLedV2Busy;
    private int _selectedBatteryCurveIndex;
    private bool _isBatteryCurveBusy;
    private bool _gpuCardDetectionEnabled;
    private bool _isGpuCardBusy;
    private bool _forceChromaEnabled;
    private bool _isForceChromaBusy;
    private int _selectedChromaTapStatusIndex;
    private int _selectedChromaTapModeIndex;
    private int _selectedChromaTapFunctionIndex;
    private bool _isChromaTapBusy;
    private int _profileDebounceMake;
    private int _profileDebounceMakeMask;
    private int _profileDebounceBreak;
    private int _profileDebounceBreakMask;
    private bool _isProfileDebounceBusy;
    private bool _lkScrollTactileEnabled;
    private bool _lkScrollFreeSpinEnabled;
    private bool _lkScrollMicroTactileEnabled;
    private bool _isLkScrollBusy;

    public Visibility TimeToDimVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.TimeToDimRead,
        RazerHidBackendCapability.TimeToDimWrite));
    public Visibility TimeToDimWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.TimeToDimWrite));
    public bool TimeToDimEnabled { get => _timeToDimEnabled; set => SetField(ref _timeToDimEnabled, value); }
    public int TimeToDimSeconds
    {
        get => _timeToDimSeconds;
        set
        {
            if (SetField(ref _timeToDimSeconds, Math.Clamp(value, 60, 900)))
            {
                OnPropertyChanged(nameof(TimeToDimMinutes));
                OnPropertyChanged(nameof(CanEditTimeToDim));
            }
        }
    }
    public double TimeToDimMinutes
    {
        get => TimeToDimSeconds / 60d;
        set
        {
            var seconds = (int)Math.Round(Math.Clamp(value, 1d, 15d) * 60d);
            TimeToDimSeconds = seconds;
        }
    }
    public int TimeToDimPercent
    {
        get => _timeToDimPercent;
        set
        {
            if (SetField(ref _timeToDimPercent, Math.Clamp(value, 1, 100)))
                OnPropertyChanged(nameof(CanEditTimeToDim));
        }
    }
    public string TimeToDimText => _basicState is { TimeToDimEnabled: { } enabled, TimeToDimSeconds: { } seconds, TimeToDimPercent: { } percent }
        ? $"{(enabled ? OnText : OffText)} / {seconds / 60d:0.#} min / {percent}%"
        : "--";
    public bool IsTimeToDimBusy { get => _isTimeToDimBusy; private set => SetBusy(ref _isTimeToDimBusy, value, nameof(CanEditTimeToDim), nameof(CanWriteTimeToDim)); }
    public bool CanWriteTimeToDim => CanUse(RazerHidBackendCapability.TimeToDimWrite) && !IsTimeToDimBusy;
    public bool CanEditTimeToDim => CanWriteTimeToDim &&
        TimeToDimSeconds is >= 60 and <= 900 && TimeToDimPercent is >= 1 and <= 100;

    public Visibility IndicatorLedVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.IndicatorLedConfigurationRead,
        RazerHidBackendCapability.IndicatorLedConfigurationWrite));
    public Visibility IndicatorLedWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.IndicatorLedConfigurationWrite));
    // The middleware reports the indicator LED as a status byte, not a brightness enum.
    public bool IndicatorLedEnabled { get => _indicatorLedEnabled; set => SetField(ref _indicatorLedEnabled, value); }
    public string IndicatorLedText => _basicState?.IndicatorLedConfiguration is { } value
        ? value != 0 ? OnText : OffText
        : "--";
    public bool IsIndicatorLedBusy { get => _isIndicatorLedBusy; private set => SetBusy(ref _isIndicatorLedBusy, value, nameof(CanWriteIndicatorLed)); }
    public bool CanWriteIndicatorLed => CanUse(RazerHidBackendCapability.IndicatorLedConfigurationWrite) && !IsIndicatorLedBusy;

    public Visibility IndicatorLedV2Visibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.IndicatorLedConfigurationV2Read,
        RazerHidBackendCapability.IndicatorLedConfigurationV2Write));
    public Visibility IndicatorLedV2WriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.IndicatorLedConfigurationV2Write));
    public IReadOnlyList<string> IndicatorLedModeOptions => [
        AppStrings.Text("Text_RazerHidIndicatorV2Off"),
        AppStrings.Text("Text_RazerHidIndicatorV2Battery"),
        AppStrings.Text("Text_RazerHidIndicatorV2Connection"),
        AppStrings.Text("Text_RazerHidIndicatorV2Polling"),
        AppStrings.Text("Text_RazerHidIndicatorV2Dpi"),
    ];
    public int IndicatorLed1Index
    {
        get => _indicatorLed1Index;
        set => SetField(ref _indicatorLed1Index, Math.Clamp(value, 0, 4));
    }
    public int IndicatorLed2Index
    {
        get => _indicatorLed2Index;
        set => SetField(ref _indicatorLed2Index, Math.Clamp(value, 0, 4));
    }
    public int IndicatorLed3Index
    {
        get => _indicatorLed3Index;
        set => SetField(ref _indicatorLed3Index, Math.Clamp(value, 0, 4));
    }
    public string IndicatorLedV2Text => _basicState?.IndicatorLedConfigurationV2 is { } value
        ? $"{IndicatorLedModeOptions[value.Led1]} / {IndicatorLedModeOptions[value.Led2]} / {IndicatorLedModeOptions[value.Led3]}"
        : "--";
    public bool IsIndicatorLedV2Busy { get => _isIndicatorLedV2Busy; private set => SetBusy(ref _isIndicatorLedV2Busy, value, nameof(CanWriteIndicatorLedV2)); }
    public bool CanWriteIndicatorLedV2 => CanUse(RazerHidBackendCapability.IndicatorLedConfigurationV2Write) && !IsIndicatorLedV2Busy;

    public Visibility BatteryCurveVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.BatteryDischargeCurveRead,
        RazerHidBackendCapability.BatteryDischargeCurveWrite));
    public Visibility BatteryCurveWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.BatteryDischargeCurveWrite));
    public IReadOnlyList<string> BatteryCurveOptions => [
        AppStrings.Text("Text_RazerHidBatteryAlkaline"),
        AppStrings.Text("Text_RazerHidBatteryNiMH"),
        AppStrings.Text("Text_RazerHidBatteryLithium"),
    ];
    public int SelectedBatteryCurveIndex
    {
        get => _selectedBatteryCurveIndex;
        set => SetField(ref _selectedBatteryCurveIndex, Math.Clamp(value, 0, 2));
    }
    public string BatteryCurveText => _basicState?.BatteryDischargeCurve is { } value && value <= 2
        ? BatteryCurveOptions[value]
        : "--";
    public bool IsBatteryCurveBusy { get => _isBatteryCurveBusy; private set => SetBusy(ref _isBatteryCurveBusy, value, nameof(CanWriteBatteryCurve)); }
    public bool CanWriteBatteryCurve => CanUse(RazerHidBackendCapability.BatteryDischargeCurveWrite) && !IsBatteryCurveBusy;

    public Visibility GpuCardVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.GpuCardDetectionRead,
        RazerHidBackendCapability.GpuCardDetectionWrite));
    public Visibility GpuCardWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.GpuCardDetectionWrite));
    public bool GpuCardDetectionEnabled { get => _gpuCardDetectionEnabled; set => SetField(ref _gpuCardDetectionEnabled, value); }
    public string GpuCardText => _basicState?.GpuCardDetectionEnabled is { } enabled ? enabled ? OnText : OffText : "--";
    public bool IsGpuCardBusy { get => _isGpuCardBusy; private set => SetBusy(ref _isGpuCardBusy, value, nameof(CanWriteGpuCard)); }
    public bool CanWriteGpuCard => CanUse(RazerHidBackendCapability.GpuCardDetectionWrite) && !IsGpuCardBusy;

    public Visibility ForceChromaVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ForceChromaCtrlRead,
        RazerHidBackendCapability.ForceChromaCtrlWrite));
    public Visibility ForceChromaWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.ForceChromaCtrlWrite));
    public bool ForceChromaEnabled { get => _forceChromaEnabled; set => SetField(ref _forceChromaEnabled, value); }
    public string ForceChromaText => _basicState?.ForceChromaCtrlEnabled is { } enabled ? enabled ? OnText : OffText : "--";
    public bool IsForceChromaBusy { get => _isForceChromaBusy; private set => SetBusy(ref _isForceChromaBusy, value, nameof(CanWriteForceChroma)); }
    public bool CanWriteForceChroma => CanUse(RazerHidBackendCapability.ForceChromaCtrlWrite) && !IsForceChromaBusy;

    public Visibility ChromaTapVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ChromaTapRead,
        RazerHidBackendCapability.ChromaTapWrite));
    public Visibility ChromaTapWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.ChromaTapWrite));
    public IReadOnlyList<string> ChromaTapStatusOptions => [OnText, OffText];
    public IReadOnlyList<string> ChromaTapModeOptions => [
        AppStrings.Text("Text_RazerHidChromaTapDouble"),
        AppStrings.Text("Text_RazerHidChromaTapTriple"),
    ];
    public IReadOnlyList<string> ChromaTapFunctionOptions => [
        AppStrings.Text("Text_RazerHidChromaTapCycle"),
        AppStrings.Text("Text_RazerHidChromaTapEffect"),
        AppStrings.Text("Text_RazerHidChromaTapSpecific"),
        AppStrings.Text("Text_RazerHidChromaTapLimiter"),
    ];
    public int SelectedChromaTapStatusIndex
    {
        get => _selectedChromaTapStatusIndex;
        set => SetField(ref _selectedChromaTapStatusIndex, Math.Clamp(value, 0, 1));
    }
    public int SelectedChromaTapModeIndex
    {
        get => _selectedChromaTapModeIndex;
        set => SetField(ref _selectedChromaTapModeIndex, Math.Clamp(value, 0, 1));
    }
    public int SelectedChromaTapFunctionIndex
    {
        get => _selectedChromaTapFunctionIndex;
        set => SetField(ref _selectedChromaTapFunctionIndex, Math.Clamp(value, 0, 3));
    }
    public string ChromaTapText => _basicState?.ChromaTap is { } tap
        ? $"{ChromaTapStatusOptions[Math.Clamp(tap.Status == 2 ? 1 : 0, 0, 1)]} / {ChromaTapModeOptions[Math.Clamp((int)tap.TapMode, 0, 1)]} / {ChromaTapFunctionOptions[Math.Clamp(tap.FunctionId - 1, 0, 3)]}"
        : "--";
    public bool IsChromaTapBusy { get => _isChromaTapBusy; private set => SetBusy(ref _isChromaTapBusy, value, nameof(CanWriteChromaTap)); }
    public bool CanWriteChromaTap => CanUse(RazerHidBackendCapability.ChromaTapWrite) && !IsChromaTapBusy;

    public Visibility ProfileDebounceVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.ProfileDebounceTimeRead,
        RazerHidBackendCapability.ProfileDebounceTimeWrite));
    public Visibility ProfileDebounceWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.ProfileDebounceTimeWrite));
    public int ProfileDebounceMake
    {
        get => _profileDebounceMake;
        set
        {
            if (SetField(ref _profileDebounceMake, Math.Clamp(value, 0, 255))) OnPropertyChanged(nameof(CanEditProfileDebounce));
        }
    }
    public int ProfileDebounceMakeMask
    {
        get => _profileDebounceMakeMask;
        set
        {
            if (SetField(ref _profileDebounceMakeMask, Math.Clamp(value, 0, 255))) OnPropertyChanged(nameof(CanEditProfileDebounce));
        }
    }
    public int ProfileDebounceBreak
    {
        get => _profileDebounceBreak;
        set
        {
            if (SetField(ref _profileDebounceBreak, Math.Clamp(value, 0, 255))) OnPropertyChanged(nameof(CanEditProfileDebounce));
        }
    }
    public int ProfileDebounceBreakMask
    {
        get => _profileDebounceBreakMask;
        set
        {
            if (SetField(ref _profileDebounceBreakMask, Math.Clamp(value, 0, 255))) OnPropertyChanged(nameof(CanEditProfileDebounce));
        }
    }
    public string ProfileDebounceText => _basicState?.ProfileDebounce is { } value
        ? $"make {value.MakeDebounceTimeMs}/{value.MakeMaskTimeMs} ms, break {value.BreakDebounceTimeMs}/{value.BreakMaskTimeMs} ms"
        : "--";
    public bool IsProfileDebounceBusy { get => _isProfileDebounceBusy; private set => SetBusy(ref _isProfileDebounceBusy, value, nameof(CanWriteProfileDebounce)); }
    public bool CanWriteProfileDebounce => CanUse(RazerHidBackendCapability.ProfileDebounceTimeWrite) && !IsProfileDebounceBusy;
    public bool CanEditProfileDebounce => CanWriteProfileDebounce &&
        ProfileDebounceMake is >= 0 and <= 255 && ProfileDebounceMakeMask is >= 0 and <= 255 &&
        ProfileDebounceBreak is >= 0 and <= 255 && ProfileDebounceBreakMask is >= 0 and <= 255;

    public Visibility MacroStorageVisibility => VisibleWhen(Has(RazerHidBackendCapability.MacroStorageInfoRead));
    public string MacroStorageText => _basicState?.MacroStorageInfo is { } info
        ? $"maxId {info.MaxId}, available {CombineMemory(info.AvailableMemoryLow, info.AvailableMemoryHigh)} / {CombineMemory(info.MaxMemoryLow, info.MaxMemoryHigh)}"
        : "--";

    public Visibility LkScrollVisibility => VisibleWhen(HasAny(
        RazerHidBackendCapability.LkScrollWheelModeRead,
        RazerHidBackendCapability.LkScrollWheelModeWrite));
    public Visibility LkScrollWriteVisibility => VisibleWhen(Has(RazerHidBackendCapability.LkScrollWheelModeWrite));
    public bool LkScrollTactileEnabled { get => _lkScrollTactileEnabled; set => SetLkScrollFlag(ref _lkScrollTactileEnabled, value); }
    public bool LkScrollFreeSpinEnabled { get => _lkScrollFreeSpinEnabled; set => SetLkScrollFlag(ref _lkScrollFreeSpinEnabled, value); }
    public bool LkScrollMicroTactileEnabled { get => _lkScrollMicroTactileEnabled; set => SetLkScrollFlag(ref _lkScrollMicroTactileEnabled, value); }
    public string LkScrollText => _basicState?.LkScrollWheelModeMask is { } mask ? FormatLkScrollMask(mask) : "--";
    public bool IsLkScrollBusy { get => _isLkScrollBusy; private set => SetBusy(ref _isLkScrollBusy, value, nameof(CanEditLkScroll), nameof(CanWriteLkScroll)); }
    public bool CanWriteLkScroll => CanUse(RazerHidBackendCapability.LkScrollWheelModeWrite) && !IsLkScrollBusy;
    public bool CanEditLkScroll => CanWriteLkScroll && LkScrollMask != 0;

    public Task ApplyTimeToDimAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.TimeToDimWrite,
            () => IsTimeToDimBusy, value => IsTimeToDimBusy = value,
            async () =>
            {
                await _service.SetTimeToDimAsync(Connection, TimeToDimEnabled, TimeToDimSeconds,
                    checked((byte)TimeToDimPercent), cancellationToken);
                if (Has(RazerHidBackendCapability.TimeToDimRead))
                {
                    var applied = await _service.GetTimeToDimAsync(Connection, cancellationToken);
                    TimeToDimEnabled = applied.Enabled;
                    TimeToDimSeconds = applied.Seconds;
                    TimeToDimPercent = applied.Percentage is >= 1 ? applied.Percentage : 20;
                }
                UpdateBasic(state => state with
                {
                    TimeToDimEnabled = TimeToDimEnabled,
                    TimeToDimSeconds = TimeToDimSeconds,
                    TimeToDimPercent = checked((byte)TimeToDimPercent),
                });
            }, cancellationToken);

    public Task ApplyIndicatorLedAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.IndicatorLedConfigurationWrite,
            () => IsIndicatorLedBusy, value => IsIndicatorLedBusy = value,
            async () =>
            {
                var configuration = IndicatorLedEnabled ? (byte)1 : (byte)0;
                await _service.SetIndicatorLedConfigurationAsync(Connection, configuration, cancellationToken);
                if (Has(RazerHidBackendCapability.IndicatorLedConfigurationRead))
                    IndicatorLedEnabled = await _service.GetIndicatorLedConfigurationAsync(Connection, cancellationToken) != 0;
                UpdateBasic(state => state with { IndicatorLedConfiguration = configuration });
            }, cancellationToken);

    public Task ApplyIndicatorLedV2Async(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.IndicatorLedConfigurationV2Write,
            () => IsIndicatorLedV2Busy, value => IsIndicatorLedV2Busy = value,
            async () =>
            {
                var configuration = new RazerHidIndicatorLedConfigurationV2(
                    checked((byte)IndicatorLed1Index),
                    checked((byte)IndicatorLed2Index),
                    checked((byte)IndicatorLed3Index));
                await _service.SetIndicatorLedConfigurationV2Async(Connection, configuration, cancellationToken);
                if (Has(RazerHidBackendCapability.IndicatorLedConfigurationV2Read))
                {
                    configuration = await _service.GetIndicatorLedConfigurationV2Async(Connection, cancellationToken);
                    SetIndicatorLedV2(configuration);
                }
                UpdateBasic(state => state with { IndicatorLedConfigurationV2 = configuration });
            }, cancellationToken);

    public Task ApplyBatteryCurveAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.BatteryDischargeCurveWrite,
            () => IsBatteryCurveBusy, value => IsBatteryCurveBusy = value,
            async () =>
            {
                var chemistry = checked((byte)SelectedBatteryCurveIndex);
                await _service.SetBatteryDischargeCurveAsync(Connection, chemistry, cancellationToken);
                if (Has(RazerHidBackendCapability.BatteryDischargeCurveRead))
                    SelectedBatteryCurveIndex = await _service.GetBatteryDischargeCurveAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { BatteryDischargeCurve = chemistry });
            }, cancellationToken);

    public Task ApplyGpuCardAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.GpuCardDetectionWrite,
            () => IsGpuCardBusy, value => IsGpuCardBusy = value,
            async () =>
            {
                await _service.SetGpuCardDetectionAsync(Connection, GpuCardDetectionEnabled, cancellationToken);
                if (Has(RazerHidBackendCapability.GpuCardDetectionRead))
                    GpuCardDetectionEnabled = (await _service.GetGpuCardDetectionAsync(Connection, cancellationToken)).Enabled;
                UpdateBasic(state => state with { GpuCardDetectionEnabled = GpuCardDetectionEnabled });
            }, cancellationToken);

    public Task ApplyForceChromaAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.ForceChromaCtrlWrite,
            () => IsForceChromaBusy, value => IsForceChromaBusy = value,
            async () =>
            {
                await _service.SetForceChromaCtrlAsync(Connection, ForceChromaEnabled, cancellationToken);
                if (Has(RazerHidBackendCapability.ForceChromaCtrlRead))
                    ForceChromaEnabled = await _service.GetForceChromaCtrlAsync(Connection, cancellationToken);
                UpdateBasic(state => state with { ForceChromaCtrlEnabled = ForceChromaEnabled });
            }, cancellationToken);

    public Task ApplyChromaTapAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.ChromaTapWrite,
            () => IsChromaTapBusy, value => IsChromaTapBusy = value,
            async () =>
            {
                var status = SelectedChromaTapStatusIndex == 0 ? (byte)1 : (byte)2;
                var tapMode = checked((byte)SelectedChromaTapModeIndex);
                var functionId = checked((byte)(SelectedChromaTapFunctionIndex + 1));
                await _service.SetChromaTapAsync(Connection, status, tapMode, functionId, cancellationToken);
                if (Has(RazerHidBackendCapability.ChromaTapRead))
                {
                    var applied = await _service.GetChromaTapAsync(Connection, cancellationToken);
                    SelectedChromaTapStatusIndex = applied.Status == 2 ? 1 : 0;
                    SelectedChromaTapModeIndex = applied.TapMode;
                    SelectedChromaTapFunctionIndex = Math.Clamp(applied.FunctionId - 1, 0, 3);
                }
                UpdateBasic(state => state with
                {
                    ChromaTap = new RazerHidChromaTap(0, status, tapMode, functionId),
                });
            }, cancellationToken);

    public Task ApplyProfileDebounceAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.ProfileDebounceTimeWrite,
            () => IsProfileDebounceBusy, value => IsProfileDebounceBusy = value,
            async () =>
            {
                await _service.SetProfileDebounceTimeAsync(Connection,
                    checked((byte)ProfileDebounceMake), checked((byte)ProfileDebounceMakeMask),
                    checked((byte)ProfileDebounceBreak), checked((byte)ProfileDebounceBreakMask),
                    cancellationToken);
                if (Has(RazerHidBackendCapability.ProfileDebounceTimeRead))
                    SetProfileDebounce(await _service.GetProfileDebounceTimeAsync(Connection, cancellationToken));
                UpdateBasic(state => state with
                {
                    ProfileDebounce = new RazerHidProfileDebounce(0,
                        checked((byte)ProfileDebounceMake), checked((byte)ProfileDebounceMakeMask),
                        checked((byte)ProfileDebounceBreak), checked((byte)ProfileDebounceBreakMask)),
                });
            }, cancellationToken);

    public Task ApplyLkScrollAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(RazerHidBackendCapability.LkScrollWheelModeWrite,
            () => IsLkScrollBusy, value => IsLkScrollBusy = value,
            async () =>
            {
                var mask = LkScrollMask;
                if (mask == 0) return;
                await _service.SetLkScrollWheelModeAsync(Connection, mask, cancellationToken);
                if (Has(RazerHidBackendCapability.LkScrollWheelModeRead))
                    SetLkScrollMask((await _service.GetLkScrollWheelModeAsync(Connection, cancellationToken)).ModeMask);
                UpdateBasic(state => state with { LkScrollWheelModeMask = mask });
            }, cancellationToken);

    public void ApplyMiddlewareState(RazerHidBasicState state)
    {
        if (state.TimeToDimEnabled is { } timeEnabled) TimeToDimEnabled = timeEnabled;
        if (state.TimeToDimSeconds is { } seconds) TimeToDimSeconds = seconds;
        if (state.TimeToDimPercent is { } percent) TimeToDimPercent = percent is >= 1 ? percent : 20;
        if (state.IndicatorLedConfiguration is { } indicator) IndicatorLedEnabled = indicator != 0;
        if (state.IndicatorLedConfigurationV2 is { } indicatorV2) SetIndicatorLedV2(indicatorV2);
        if (state.BatteryDischargeCurve is { } curve) SelectedBatteryCurveIndex = curve;
        if (state.GpuCardDetectionEnabled is { } gpu) GpuCardDetectionEnabled = gpu;
        if (state.ForceChromaCtrlEnabled is { } force) ForceChromaEnabled = force;
        if (state.ProfileDebounce is { } debounce) SetProfileDebounce(debounce);
        if (state.ChromaTap is { } tap)
        {
            SelectedChromaTapStatusIndex = tap.Status == 2 ? 1 : 0;
            SelectedChromaTapModeIndex = tap.TapMode;
            SelectedChromaTapFunctionIndex = Math.Clamp(tap.FunctionId - 1, 0, 3);
        }
        if (state.LkScrollWheelModeMask is { } mask) SetLkScrollMask(mask);
    }

    public void NotifyMiddlewareStateChanged()
    {
        OnPropertyChanged(nameof(TimeToDimVisibility));
        OnPropertyChanged(nameof(TimeToDimWriteVisibility));
        OnPropertyChanged(nameof(TimeToDimText));
        OnPropertyChanged(nameof(CanWriteTimeToDim));
        OnPropertyChanged(nameof(CanEditTimeToDim));
        OnPropertyChanged(nameof(IndicatorLedVisibility));
        OnPropertyChanged(nameof(IndicatorLedWriteVisibility));
        OnPropertyChanged(nameof(IndicatorLedText));
        OnPropertyChanged(nameof(CanWriteIndicatorLed));
        OnPropertyChanged(nameof(IndicatorLedV2Visibility));
        OnPropertyChanged(nameof(IndicatorLedV2WriteVisibility));
        OnPropertyChanged(nameof(IndicatorLedV2Text));
        OnPropertyChanged(nameof(CanWriteIndicatorLedV2));
        OnPropertyChanged(nameof(BatteryCurveVisibility));
        OnPropertyChanged(nameof(BatteryCurveWriteVisibility));
        OnPropertyChanged(nameof(BatteryCurveText));
        OnPropertyChanged(nameof(CanWriteBatteryCurve));
        OnPropertyChanged(nameof(GpuCardVisibility));
        OnPropertyChanged(nameof(GpuCardWriteVisibility));
        OnPropertyChanged(nameof(GpuCardText));
        OnPropertyChanged(nameof(CanWriteGpuCard));
        OnPropertyChanged(nameof(ForceChromaVisibility));
        OnPropertyChanged(nameof(ForceChromaWriteVisibility));
        OnPropertyChanged(nameof(ForceChromaText));
        OnPropertyChanged(nameof(CanWriteForceChroma));
        OnPropertyChanged(nameof(ChromaTapVisibility));
        OnPropertyChanged(nameof(ChromaTapWriteVisibility));
        OnPropertyChanged(nameof(ChromaTapText));
        OnPropertyChanged(nameof(CanWriteChromaTap));
        OnPropertyChanged(nameof(ProfileDebounceVisibility));
        OnPropertyChanged(nameof(ProfileDebounceWriteVisibility));
        OnPropertyChanged(nameof(ProfileDebounceText));
        OnPropertyChanged(nameof(CanWriteProfileDebounce));
        OnPropertyChanged(nameof(CanEditProfileDebounce));
        OnPropertyChanged(nameof(MacroStorageVisibility));
        OnPropertyChanged(nameof(MacroStorageText));
        OnPropertyChanged(nameof(LkScrollVisibility));
        OnPropertyChanged(nameof(LkScrollWriteVisibility));
        OnPropertyChanged(nameof(LkScrollText));
        OnPropertyChanged(nameof(CanWriteLkScroll));
        OnPropertyChanged(nameof(CanEditLkScroll));
    }

    private byte LkScrollMask =>
        (byte)((LkScrollTactileEnabled ? 1 : 0) |
               (LkScrollFreeSpinEnabled ? 2 : 0) |
               (LkScrollMicroTactileEnabled ? 4 : 0));

    private void SetLkScrollFlag(ref bool field, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetField(ref field, value, name)) return;
        OnPropertyChanged(nameof(CanEditLkScroll));
    }

    private void SetLkScrollMask(byte mask)
    {
        LkScrollTactileEnabled = (mask & 1) != 0;
        LkScrollFreeSpinEnabled = (mask & 2) != 0;
        LkScrollMicroTactileEnabled = (mask & 4) != 0;
    }

    private void SetIndicatorLedV2(RazerHidIndicatorLedConfigurationV2 value)
    {
        IndicatorLed1Index = value.Led1;
        IndicatorLed2Index = value.Led2;
        IndicatorLed3Index = value.Led3;
    }

    private void SetProfileDebounce(RazerHidProfileDebounce value)
    {
        ProfileDebounceMake = value.MakeDebounceTimeMs;
        ProfileDebounceMakeMask = value.MakeMaskTimeMs;
        ProfileDebounceBreak = value.BreakDebounceTimeMs;
        ProfileDebounceBreakMask = value.BreakMaskTimeMs;
    }

    private static string FormatLkScrollMask(byte mask)
    {
        var values = new List<string>();
        if ((mask & 1) != 0) values.Add(AppStrings.Text("Text_RazerHidLkTactile"));
        if ((mask & 2) != 0) values.Add(AppStrings.Text("Text_RazerHidLkFreeSpin"));
        if ((mask & 4) != 0) values.Add(AppStrings.Text("Text_RazerHidLkMicroTactile"));
        return values.Count == 0 ? "--" : string.Join(" / ", values);
    }

    private static string OnText => AppStrings.Text("Text_RazerHidOn");
    private static string OffText => AppStrings.Text("Text_RazerHidOff");

    private static long CombineMemory(int low, int high) => ((long)high << 16) | (uint)low;
}
