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
