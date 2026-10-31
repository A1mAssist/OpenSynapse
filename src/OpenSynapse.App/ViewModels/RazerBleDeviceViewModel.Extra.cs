using Microsoft.UI.Xaml;
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
    }

    public Task ApplyPowerPlanAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetPowerPlanAsync(
                (RazerBlePowerPlan)SelectedPowerPlanIndex, cancellationToken: cancellationToken),
            plan => SelectedPowerPlanIndex = (int)plan);

    /// <summary>Applies the three scroll settings the wheel card owns, in one go.</summary>
    public async Task ApplyScrollExtrasAsync(CancellationToken cancellationToken = default)
    {
        if (TactileModeVisibility == Visibility.Visible)
        {
            await RunWriteAsync(
                service => service.SetScrollTactileModeAsync(
                    (RazerBleScrollTactileMode)SelectedTactileModeIndex, cancellationToken),
                mode => SelectedTactileModeIndex = (int)mode);
        }

        if (AccelerationLevelVisibility == Visibility.Visible)
        {
            await RunWriteAsync(
                service => service.SetScrollAccelerationLevelAsync(
                    (byte)Math.Round(ScrollAccelerationLevel), cancellationToken),
                level => ScrollAccelerationLevel = level);
        }

        if (MaskingTimeVisibility == Visibility.Visible)
        {
            await RunWriteAsync(
                service => service.SetScrollMaskingTimeAsync(
                    (byte)Math.Round(ScrollMaskingTime), cancellationToken),
                milliseconds => ScrollMaskingTime = milliseconds);
        }

        if (HapticStageVisibility == Visibility.Visible)
        {
            var profileId = HapticProfileId;
            await RunWriteAsync(
                service => service.SetHapticStageAsync(
                    (byte)Math.Round(SelectedHapticStage), profileId, cancellationToken),
                stage => SelectedHapticStage = stage);
        }
    }

    public Task ApplyBatteryChemistryAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetBatteryChemistryAsync(
                (RazerBleBatteryChemistry)SelectedBatteryChemistryIndex, cancellationToken),
            state => SelectedBatteryChemistryIndex = (int)state);

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
            });

    public Task ApplySnapTapAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetSnapTapAsync(SnapTapEnabled, cancellationToken: cancellationToken),
            state => SnapTapEnabled = state.Enabled);

    /// <summary>Only the two timings are editable; the masks are carried over from the read.</summary>
    public Task ApplyDebounceAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetDebounceAsync(
                new RazerBleDebounceState(
                    (byte)Math.Round(DebounceMake),
                    _debounceMakeMask,
                    (byte)Math.Round(DebounceBreak),
                    _debounceBreakMask),
                cancellationToken),
            ApplyDebounce);

    private void ApplyDebounce(RazerBleDebounceState state)
    {
        _debounceMakeMask = state.MakeMask;
        _debounceBreakMask = state.BreakMask;
        DebounceMake = state.Make;
        DebounceBreak = state.Break;
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
