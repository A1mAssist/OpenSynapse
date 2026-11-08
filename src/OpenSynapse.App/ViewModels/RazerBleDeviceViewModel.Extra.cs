using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Protocol 30 settings outside the main panels: battery chemistry, smart dimming, Snap Tap and
/// debounce. Each one probes on load and stays hidden when the device refuses the read.
/// </summary>
public sealed partial class RazerBleDeviceViewModel
{
    private const int BatteryChemistryCount = 3;
    private const int SmartIlluminationModeCount = 3;

    private Visibility _batteryChemistryVisibility = Visibility.Collapsed;
    private Visibility _smartIlluminationVisibility = Visibility.Collapsed;
    private Visibility _snapTapVisibility = Visibility.Collapsed;
    private Visibility _debounceVisibility = Visibility.Collapsed;
    private int _selectedBatteryChemistryIndex;
    private bool _smartIlluminationEnabled;
    private int _selectedSmartIlluminationModeIndex;
    private bool _snapTapEnabled;
    private int _snapTapPairCount;
    private double _debounceMake = 8;
    private double _debounceBreak = 8;
    private byte _debounceMakeMask;
    private byte _debounceBreakMask;
    private Visibility _powerPlanVisibility = Visibility.Collapsed;
    private Visibility _tactileModeVisibility = Visibility.Collapsed;
    private Visibility _accelerationLevelVisibility = Visibility.Collapsed;
    private Visibility _maskingTimeVisibility = Visibility.Collapsed;
    private int _selectedPowerPlanIndex;
    private int _selectedTactileModeIndex;
    private double _scrollAccelerationLevel;
    private double _scrollMaskingTime;
    private Visibility _hapticStageVisibility = Visibility.Collapsed;
    private double _selectedHapticStage = 1;
    private int _maxHapticStage = 1;
    private readonly Dictionary<byte, string> _profileNames = [];

    public IReadOnlyList<string> BatteryChemistryOptions =>
        AppStrings.Texts("BleBatteryTypeAlkaline", "BleBatteryTypeNiMH", "BleBatteryTypeLithium");

    public Visibility BatteryChemistryVisibility
    {
        get => _batteryChemistryVisibility;
        private set
        {
            if (_batteryChemistryVisibility == value) return;
            _batteryChemistryVisibility = value;
            OnPropertyChanged();
        }
    }

    public int SelectedBatteryChemistryIndex
    {
        get => _selectedBatteryChemistryIndex;
        set => SetField(ref _selectedBatteryChemistryIndex, Math.Clamp(value, 0, BatteryChemistryCount - 1));
    }

    public Visibility SmartIlluminationVisibility
    {
        get => _smartIlluminationVisibility;
        private set
        {
            if (_smartIlluminationVisibility == value) return;
            _smartIlluminationVisibility = value;
            OnPropertyChanged();
        }
    }

    public bool SmartIlluminationEnabled
    {
        get => _smartIlluminationEnabled;
        set => SetField(ref _smartIlluminationEnabled, value);
    }

    public IReadOnlyList<string> SmartIlluminationModeOptions =>
        AppStrings.Texts("BleSmartDimHalf", "BleSmartDimQuarter", "BleSmartDimOff");

    public int SelectedSmartIlluminationModeIndex
    {
        get => _selectedSmartIlluminationModeIndex;
        set => SetField(
            ref _selectedSmartIlluminationModeIndex,
            Math.Clamp(value, 0, SmartIlluminationModeCount - 1));
    }

    /// <summary>Snap Tap only shows when the device reports at least one configured key pair.</summary>
    public Visibility SnapTapVisibility
    {
        get => _snapTapVisibility;
        private set
        {
            if (_snapTapVisibility == value) return;
            _snapTapVisibility = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeysVisibility));
        }
    }

    public bool SnapTapEnabled
    {
        get => _snapTapEnabled;
        set => SetField(ref _snapTapEnabled, value);
    }

    public string SnapTapSummary => _snapTapPairCount == 0
        ? "—"
        : AppStrings.FormatText("BleSnapTapPairCount", _snapTapPairCount);

    public Visibility DebounceVisibility
    {
        get => _debounceVisibility;
        private set
        {
            if (_debounceVisibility == value) return;
            _debounceVisibility = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeysVisibility));
        }
    }

    public double DebounceMake
    {
        get => _debounceMake;
        set => SetField(ref _debounceMake, Math.Clamp(value, 0d, 255d));
    }

    public double DebounceBreak
    {
        get => _debounceBreak;
        set => SetField(ref _debounceBreak, Math.Clamp(value, 0d, 255d));
    }

    public Visibility KeysVisibility => _snapTapVisibility == Visibility.Visible ||
        _debounceVisibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility PowerPlanVisibility
    {
        get => _powerPlanVisibility;
        private set => SetVisibility(ref _powerPlanVisibility, value, nameof(PowerPlanVisibility));
    }

    public IReadOnlyList<string> PowerPlanOptions =>
        AppStrings.Texts("BlePowerPlanSave", "BlePowerPlanBalance", "BlePowerPlanPerformance");

    public int SelectedPowerPlanIndex
    {
        get => _selectedPowerPlanIndex;
        set => SetField(ref _selectedPowerPlanIndex, Math.Clamp(value, 0, 2));
    }

    public Visibility TactileModeVisibility
    {
        get => _tactileModeVisibility;
        private set => SetVisibility(ref _tactileModeVisibility, value, nameof(TactileModeVisibility));
    }

    public IReadOnlyList<string> TactileModeOptions =>
        AppStrings.Texts("BleScrollTactile", "BleScrollFreeSpin", "BleScrollMicroTactile");

    public int SelectedTactileModeIndex
    {
        get => _selectedTactileModeIndex;
        set => SetField(ref _selectedTactileModeIndex, Math.Clamp(value, 0, 2));
    }

    public Visibility AccelerationLevelVisibility
    {
        get => _accelerationLevelVisibility;
        private set => SetVisibility(
            ref _accelerationLevelVisibility, value, nameof(AccelerationLevelVisibility));
    }

    public double ScrollAccelerationLevel
    {
        get => _scrollAccelerationLevel;
        set => SetField(ref _scrollAccelerationLevel, Math.Clamp(value, 0d, 255d));
    }

    public Visibility MaskingTimeVisibility
    {
        get => _maskingTimeVisibility;
        private set => SetVisibility(ref _maskingTimeVisibility, value, nameof(MaskingTimeVisibility));
    }

    public double ScrollMaskingTime
    {
        get => _scrollMaskingTime;
        set => SetField(ref _scrollMaskingTime, Math.Clamp(value, 0d, 255d));
    }

    public Visibility HapticStageVisibility
    {
        get => _hapticStageVisibility;
        private set => SetVisibility(ref _hapticStageVisibility, value, nameof(HapticStageVisibility));
    }

    public double SelectedHapticStage
    {
        get => _selectedHapticStage;
        set => SetField(ref _selectedHapticStage, Math.Clamp(value, 1d, _maxHapticStage));
    }

    public int MaxHapticStage
    {
        get => _maxHapticStage;
        private set => SetField(ref _maxHapticStage, value < 1 ? 1 : value);
    }

    private string DescribeProfile(byte id) =>
        _profileNames.TryGetValue(id, out var name) && name.Length > 0 ? name : $"Profile {id}";

    /// <summary>Haptic stages live on the active onboard profile; profile 1 is the middleware default.</summary>
    private byte HapticProfileId => _profileIds.Count == 0
        ? (byte)1
        : _profileIds[Math.Clamp(SelectedProfileIndex, 0, _profileIds.Count - 1)];

    public async Task LoadExtrasAsync(CancellationToken cancellationToken = default)
    {
        await LoadExtraAsync(
            () => _service!.ReadBatteryChemistryAsync(cancellationToken),
            state =>
            {
                SelectedBatteryChemistryIndex = (int)state;
                BatteryChemistryVisibility = Visibility.Visible;
            },
            () => BatteryChemistryVisibility = Visibility.Collapsed);

        await LoadExtraAsync(
            () => _service!.ReadSmartIlluminationAsync(cancellationToken: cancellationToken),
            state =>
            {
                SmartIlluminationEnabled = state.Status != 0;
                SelectedSmartIlluminationModeIndex = state.Mode;
                SmartIlluminationVisibility = Visibility.Visible;
            },
            () => SmartIlluminationVisibility = Visibility.Collapsed);

        await LoadExtraAsync(
            () => _service!.ReadSnapTapAsync(cancellationToken: cancellationToken),
            state =>
            {
                _snapTapPairCount = state.Pairs.Count;
                SnapTapEnabled = state.Enabled;
                SnapTapVisibility = state.Pairs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                OnPropertyChanged(nameof(SnapTapSummary));
            },
            () => SnapTapVisibility = Visibility.Collapsed);

        await LoadExtraAsync(
            () => _service!.ReadDebounceAsync(cancellationToken),
            state =>
            {
                ApplyDebounce(state);
                DebounceVisibility = Visibility.Visible;
            },
            () => DebounceVisibility = Visibility.Collapsed);

        await LoadExtraAsync(
            () => _service!.ReadPowerPlanAsync(cancellationToken: cancellationToken),
            plan =>
            {
                SelectedPowerPlanIndex = (int)plan;
                PowerPlanVisibility = Visibility.Visible;
            },
            () => PowerPlanVisibility = Visibility.Collapsed);

        await LoadExtraAsync(
            () => _service!.ReadScrollTactileModeAsync(cancellationToken),
            mode =>
            {
                SelectedTactileModeIndex = (int)mode;
                TactileModeVisibility = Visibility.Visible;
            },
            () => TactileModeVisibility = Visibility.Collapsed);

        await LoadExtraAsync(
            () => _service!.ReadScrollAccelerationLevelAsync(cancellationToken),
            level =>
            {
                ScrollAccelerationLevel = level;
                AccelerationLevelVisibility = Visibility.Visible;
            },
            () => AccelerationLevelVisibility = Visibility.Collapsed);

        await LoadExtraAsync(
            () => _service!.ReadScrollMaskingTimeAsync(cancellationToken),
            milliseconds =>
            {
                ScrollMaskingTime = milliseconds;
                MaskingTimeVisibility = Visibility.Visible;
            },
            () => MaskingTimeVisibility = Visibility.Collapsed);

        await LoadExtraAsync(
            () => _service!.ReadHapticStageAsync(HapticProfileId, cancellationToken),
            state =>
            {
                MaxHapticStage = state.Max;
                SelectedHapticStage = state.Active;
                HapticStageVisibility = Visibility.Visible;
            },
            () => HapticStageVisibility = Visibility.Collapsed);

        await LoadProfileNamesAsync(cancellationToken);
    }

    private async Task LoadProfileNamesAsync(CancellationToken cancellationToken)
    {
        if (_service is null || _profileIds.Count == 0 || !BeginOperation()) return;
        try
        {
            foreach (var id in _profileIds)
            {
                var name = await _service.ReadProfileNameAsync(id, cancellationToken);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _profileNames[id] = name;
                }
            }

            OnPropertyChanged(nameof(ProfileOptions));
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            // Profile names are cosmetic: the id labels stay when the read is refused.
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.power-plan", DeviceCategory.Mouse)]
    public Task ApplyPowerPlanAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetPowerPlanAsync(
                (RazerBlePowerPlan)SelectedPowerPlanIndex, cancellationToken: cancellationToken),
            plan => SelectedPowerPlanIndex = (int)plan,
            (plan, token) => PersistMouseAsync(
                mouse => mouse.PowerPlan = (plan switch
                {
                    RazerBlePowerPlan.PowerSave => MousePowerPlan.PowerSave,
                    RazerBlePowerPlan.HighPerformance => MousePowerPlan.HighPerformance,
                    _ => MousePowerPlan.Balance,
                }).ToString(),
                token),
            token => _service!.ReadPowerPlanAsync(cancellationToken: token),
            (previous, token) => _service!.SetPowerPlanAsync(previous, cancellationToken: token),
            cancellationToken);

    /// <summary>Applies the three scroll settings the wheel card owns, in one go.</summary>
    [ProfileSetting("ble.scroll-extras", DeviceCategory.Mouse)]
    public async Task ApplyScrollExtrasAsync(CancellationToken cancellationToken = default)
    {
        if (_scrollApplyFailed || cancellationToken.IsCancellationRequested) return;
        await RunWriteAsync(
            async service =>
            {
                RazerBleScrollTactileMode? tactile = null;
                byte? acceleration = null;
                byte? masking = null;
                byte? haptic = null;
                if (TactileModeVisibility == Visibility.Visible)
                    tactile = await service.SetScrollTactileModeAsync(
                        (RazerBleScrollTactileMode)SelectedTactileModeIndex, cancellationToken);
                if (AccelerationLevelVisibility == Visibility.Visible)
                    acceleration = await service.SetScrollAccelerationLevelAsync(
                        (byte)Math.Round(ScrollAccelerationLevel), cancellationToken);
                if (MaskingTimeVisibility == Visibility.Visible)
                    masking = await service.SetScrollMaskingTimeAsync(
                        (byte)Math.Round(ScrollMaskingTime), cancellationToken);
                if (HapticStageVisibility == Visibility.Visible)
                    haptic = await service.SetHapticStageAsync(
                        (byte)Math.Round(SelectedHapticStage), HapticProfileId, cancellationToken);
                return new ScrollExtrasState(tactile, acceleration, masking, haptic);
            },
            ApplyScrollExtras,
            (state, token) => PersistMouseAsync(mouse => ApplyScrollExtrasToProfile(mouse, state), token),
            ReadScrollExtrasAsync,
            RestoreScrollExtrasAsync,
            cancellationToken);
    }

    [ProfileSetting("ble.battery-chemistry", DeviceCategory.Mouse)]
    public Task ApplyBatteryChemistryAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetBatteryChemistryAsync(
                (RazerBleBatteryChemistry)SelectedBatteryChemistryIndex, cancellationToken),
            state => SelectedBatteryChemistryIndex = (int)state,
            (state, token) => PersistMouseAsync(mouse => mouse.BatteryChemistry = (byte)state, token),
            token => _service!.ReadBatteryChemistryAsync(token),
            (previous, token) => _service!.SetBatteryChemistryAsync(previous, token),
            cancellationToken);

    [ProfileSetting("ble.smart-illumination", DeviceCategory.Mouse)]
    public Task ApplySmartIlluminationAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetSmartIlluminationAsync(
                SmartIlluminationEnabled ? (byte)1 : (byte)0,
                (byte)SelectedSmartIlluminationModeIndex,
                cancellationToken: cancellationToken),
            state =>
            {
                SmartIlluminationEnabled = state.Status != 0;
                SelectedSmartIlluminationModeIndex = state.Mode;
            },
            (state, token) => PersistMouseAsync(
                mouse => mouse.SmartIlluminationEnabled = state.Status != 0,
                token),
            token => _service!.ReadSmartIlluminationAsync(cancellationToken: token),
            (previous, token) => _service!.SetSmartIlluminationAsync(
                previous.Status, previous.Mode, cancellationToken: token),
            cancellationToken);

    [ProfileSetting("ble.snap-tap", DeviceCategory.Mouse)]
    public Task ApplySnapTapAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetSnapTapAsync(SnapTapEnabled, cancellationToken: cancellationToken),
            state => SnapTapEnabled = state.Enabled,
            (state, token) => PersistMouseAsync(mouse => mouse.SnapTapEnabled = state.Enabled, token),
            token => _service!.ReadSnapTapAsync(cancellationToken: token),
            (previous, token) => _service!.SetSnapTapAsync(
                previous.Enabled, cancellationToken: token),
            cancellationToken);

    /// <summary>Only the two timings are editable; the masks are carried over from the read.</summary>
    [ProfileSetting("ble.debounce", DeviceCategory.Mouse)]
    public Task ApplyDebounceAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetDebounceAsync(
                new RazerBleDebounceState(
                    (byte)Math.Round(DebounceMake),
                    _debounceMakeMask,
                    (byte)Math.Round(DebounceBreak),
                    _debounceBreakMask),
                cancellationToken),
            ApplyDebounce,
            (state, token) => PersistMouseAsync(mouse => mouse.DebounceMilliseconds = state.Make, token),
            token => _service!.ReadDebounceAsync(token),
            (previous, token) => _service!.SetDebounceAsync(previous, token),
            cancellationToken);

    private void ApplyDebounce(RazerBleDebounceState state)
    {
        _debounceMakeMask = state.MakeMask;
        _debounceBreakMask = state.BreakMask;
        DebounceMake = state.Make;
        DebounceBreak = state.Break;
    }

    private sealed record ScrollExtrasState(
        RazerBleScrollTactileMode? Tactile,
        byte? AccelerationLevel,
        byte? MaskingTime,
        byte? HapticStage);

    private void ApplyScrollExtras(ScrollExtrasState state)
    {
        if (state.Tactile is { } tactile) SelectedTactileModeIndex = (int)tactile;
        if (state.AccelerationLevel is { } acceleration) ScrollAccelerationLevel = acceleration;
        if (state.MaskingTime is { } masking) ScrollMaskingTime = masking;
        if (state.HapticStage is { } haptic) SelectedHapticStage = haptic;
    }

    private async Task<ScrollExtrasState> ReadScrollExtrasAsync(CancellationToken cancellationToken)
    {
        var tactile = TactileModeVisibility == Visibility.Visible
            ? await _service!.ReadScrollTactileModeAsync(cancellationToken)
            : (RazerBleScrollTactileMode?)null;
        var acceleration = AccelerationLevelVisibility == Visibility.Visible
            ? await _service!.ReadScrollAccelerationLevelAsync(cancellationToken)
            : (byte?)null;
        var masking = MaskingTimeVisibility == Visibility.Visible
            ? await _service!.ReadScrollMaskingTimeAsync(cancellationToken)
            : (byte?)null;
        var haptic = HapticStageVisibility == Visibility.Visible
            ? (await _service!.ReadHapticStageAsync(HapticProfileId, cancellationToken)).Active
            : (byte?)null;
        return new ScrollExtrasState(tactile, acceleration, masking, haptic);
    }

    private async Task RestoreScrollExtrasAsync(
        ScrollExtrasState state,
        CancellationToken cancellationToken)
    {
        if (state.Tactile is { } tactile)
        {
            try { await _service!.SetScrollTactileModeAsync(tactile, cancellationToken); }
            catch { /* The save error is the one the user must see. */ }
        }
        if (state.AccelerationLevel is { } acceleration)
        {
            try { await _service!.SetScrollAccelerationLevelAsync(acceleration, cancellationToken); }
            catch { /* The save error is the one the user must see. */ }
        }
        if (state.MaskingTime is { } masking)
        {
            try { await _service!.SetScrollMaskingTimeAsync(masking, cancellationToken); }
            catch { /* The save error is the one the user must see. */ }
        }
        if (state.HapticStage is { } haptic)
        {
            try { await _service!.SetHapticStageAsync(haptic, HapticProfileId, cancellationToken); }
            catch { /* The save error is the one the user must see. */ }
        }
    }

    private static void ApplyScrollExtrasToProfile(MouseProfileSettings mouse, ScrollExtrasState state)
    {
        if (state.Tactile is { } tactile) mouse.HapticMode = (int)tactile;
        if (state.AccelerationLevel is { } acceleration) mouse.HapticAccelerationLevel = acceleration;
        if (state.MaskingTime is { } masking) mouse.HapticMaskTime = masking;
        if (state.HapticStage is { } haptic) mouse.HapticLevel = haptic;
    }

    private async Task LoadExtraAsync<T>(Func<Task<T>> read, Action<T> apply, Action hide)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            apply(await read());
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            hide();
        }
        finally
        {
            EndOperation();
        }
    }
}
