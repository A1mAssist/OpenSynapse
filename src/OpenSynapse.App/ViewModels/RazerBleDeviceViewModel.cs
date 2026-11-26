using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerBleDeviceRowViewModel(RazerBleDevice device)
    : INotifyPropertyChanged, IDeviceBatteryRow
{
    private int? _batteryPercent;
    public string BatteryText => _batteryPercent is { } value ? $"{value}%" : string.Empty;
    public bool IsBatteryAvailable => _batteryPercent is not null;
    public event PropertyChangedEventHandler? PropertyChanged;

    public RazerBleDevice Device { get; } = device;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? "Razer BLE" : Device.Name;
    public string Identity => "BLE";
    public string IconGlyph => Device.ProductId == RazerLaylaProtocol.ProductId ? "\uE7F5" : "\uE962";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    public OpenSynapse.Core.Devices.DeviceCategory HardwareCategory => Device.ProductId == RazerLaylaProtocol.ProductId
        ? DeviceCategory.Accessory : DeviceCategory.Mouse;
    public Microsoft.UI.Xaml.Media.Brush StatusBrush { get; } =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 68, 214, 44));

    /// <summary>Last value the BLE page read, reused by the tray without reconnecting.</summary>
    public int? BatteryPercent => _batteryPercent;

    /// <summary>A failed read keeps the last value so the card does not blink on a timeout.</summary>
    internal void SetBatteryPercent(int? percent)
    {
        if (percent is null || percent == _batteryPercent)
        {
            return;
        }

        _batteryPercent = percent;
        PropertyChanged?.Invoke(this, new(nameof(IsBatteryAvailable)));
        PropertyChanged?.Invoke(this, new(nameof(BatteryText)));
        PropertyChanged?.Invoke(this, new(nameof(BatteryPercent)));
    }

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class RazerBleDpiStageRowViewModel(byte index, int x, int y, int z) : INotifyPropertyChanged
{
    private double _x = x;
    private double _y = y;

    public event PropertyChangedEventHandler? PropertyChanged;

    public byte Index { get; } = index;
    public string Label => $"Stage {Index}";
    public int Z { get; } = z;

    public double X
    {
        get => _x;
        set => SetField(ref _x, Math.Clamp(value, 1d, 65_535d));
    }

    public double Y
    {
        get => _y;
        set => SetField(ref _y, Math.Clamp(value, 1d, 65_535d));
    }

    private void SetField(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Battery, firmware, polling rate, DPI, lighting and button mapping for one protocol 30
/// device. Every write is verified by the service with a read-back and a restore of the
/// previous value when the write is rejected.
/// </summary>
public sealed partial class RazerBleDeviceViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    public static readonly IReadOnlyList<int> PollingRates = [1_000, 500, 333, 250, 200, 125, 100];

    private readonly RazerBleDevice _device;
    private readonly Action<string>? _diagnostic;
    private readonly Func<ProfileDocument>? _getProfile;
    private readonly Func<CancellationToken, Task<bool>>? _saveProfile;
    private readonly Func<IReadOnlyList<MacroDefinition>>? _localMacros;
    private readonly Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
        MacroSoftwareBinding?, CancellationToken, Task<string?>>? _softwareMacroBinder;
    private readonly Func<string, MacroInput, bool, CancellationToken, Task<string?>>?
        _softwareMacroClearer;
    private readonly Func<IReadOnlyList<MacroDefinition>>? _systemMacros;
    private RazerBleDeviceService? _service;
    private RazerHidMappingViewModel? _mapping;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string? _firmware;
    private string? _serial;
    private int? _batteryPercent;
    private byte? _chargingStatus;
    private int _selectedPollingIndex = PollingRates.Count - 1;
    private double _dpiX;
    private double _dpiY;
    private Visibility _lightingVisibility = Visibility.Collapsed;
    private Visibility _mappingVisibility = Visibility.Collapsed;
    private Visibility _dpiStagesVisibility = Visibility.Collapsed;
    private Visibility _scrollVisibility = Visibility.Collapsed;
    private Visibility _scrollModeVisibility = Visibility.Collapsed;
    private Visibility _scrollSmartReelVisibility = Visibility.Collapsed;
    private Visibility _scrollSmartReelThresholdVisibility = Visibility.Collapsed;
    private Visibility _scrollAccelerationVisibility = Visibility.Collapsed;
    private Visibility _powerVisibility = Visibility.Collapsed;
    private Visibility _powerLowBatteryVisibility = Visibility.Collapsed;
    private Visibility _powerAutoSleepVisibility = Visibility.Collapsed;
    private Visibility _powerSleepTimeVisibility = Visibility.Collapsed;
    private Visibility _powerTimeToDimVisibility = Visibility.Collapsed;
    private Visibility _profilesVisibility = Visibility.Collapsed;
    private int _brightness = 50;
    private int _selectedLightingEffectIndex;
    private int _selectedLightingDirectionIndex;
    private Color _lightingColor = Color.FromArgb(255, 255, 0, 0);
    private string _lightingStatusText = string.Empty;
    private int _activeDpiStage = 1;
    private string _dpiStagesStatusText = string.Empty;
    private int _selectedScrollModeIndex;
    private bool _scrollSmartReelEnabled;
    private double _scrollSmartReelThreshold = 50;
    private bool _scrollAccelerationEnabled;
    private string _scrollStatusText = string.Empty;
    private double _lowBatteryThresholdPercent = 10;
    private bool _autoSleepEnabled = true;
    private double _sleepMinutes = 5;
    private bool _timeToDimEnabled = true;
    private double _timeToDimMinutes = 5;
    private byte _timeToDimPercentage = 20;
    private string _powerStatusText = string.Empty;
    private IReadOnlyList<byte> _profileIds = [];
    private int _selectedProfileIndex;
    private string _profileStatusText = string.Empty;
    static RazerBleDeviceViewModel()
    {
        Register("ble.polling-rate");
        Register("ble.dpi");
        Register("ble.dpi-stages");
        Register("ble.lighting");
        Register("ble.scroll");
        Register("ble.power");
        Register("ble.onboard-profile");
        Register("ble.power-plan");
        Register("ble.scroll-extras");
        Register("ble.battery-chemistry");
        Register("ble.smart-illumination");
        Register("ble.snap-tap");
        Register("ble.debounce");
    }

    public RazerBleDeviceViewModel(
        RazerBleDevice device,
        Action<string>? diagnostic = null,
        Func<ProfileDocument>? getProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfile = null,
        Func<IReadOnlyList<MacroDefinition>>? localMacros = null,
        Func<string, MacroInput, MacroTriggerMode, MacroPlaybackMode, int, bool,
            MacroSoftwareBinding?, CancellationToken, Task<string?>>? softwareMacroBinder = null,
        Func<string, MacroInput, bool, CancellationToken, Task<string?>>? softwareMacroClearer = null,
        Func<IReadOnlyList<MacroDefinition>>? systemMacros = null)
    {
        _device = device;
        _diagnostic = diagnostic;
        _getProfile = getProfile;
        _saveProfile = saveProfile;
        _localMacros = localMacros;
        _softwareMacroBinder = softwareMacroBinder;
        _softwareMacroClearer = softwareMacroClearer;
        _systemMacros = systemMacros;
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => string.IsNullOrWhiteSpace(_device.Name) ? "Razer BLE" : _device.Name;
    public string Identity => AppStrings.Text("BleIdentity");
    public string DeviceId => _device.Id;
    public string ProfileDeviceKey => $"ble:{_device.Id}";
    public bool CanEdit => !IsBusy && (IsLayla || _service is not null);

    public string FirmwareText => _firmware ?? "—";
    public string SerialText => string.IsNullOrWhiteSpace(_serial) ? "—" : _serial;
    public string BatteryText => _batteryPercent is { } percent ? $"{percent}%" : "—";
    public string ChargingText => _chargingStatus switch
    {
        1 => AppStrings.Text("BlackSharkCharging"),
        2 => AppStrings.Text("BleChargingFailed"),
        6 => AppStrings.Text("BleChargingLimit"),
        0 => AppStrings.Text("RazerHidZoneFullyCharged"),
        _ => string.Empty,
    };

    public IReadOnlyList<string> PollingRateOptions => PollingRates.Select(hertz => $"{hertz} Hz").ToArray();

    public int SelectedPollingIndex
    {
        get => _selectedPollingIndex;
        set => SetField(ref _selectedPollingIndex, value);
    }

    public double DpiX
    {
        get => _dpiX;
        set => SetField(ref _dpiX, value);
    }

    public double DpiY
    {
        get => _dpiY;
        set => SetField(ref _dpiY, value);
    }

    public Visibility LightingVisibility
    {
        get => _lightingVisibility;
        private set => SetVisibility(ref _lightingVisibility, value, nameof(LightingVisibility));
    }

    public Visibility MappingVisibility
    {
        get => _mappingVisibility;
        private set => SetVisibility(ref _mappingVisibility, value, nameof(MappingVisibility));
    }

    public Visibility DpiStagesVisibility
    {
        get => _dpiStagesVisibility;
        private set => SetVisibility(ref _dpiStagesVisibility, value, nameof(DpiStagesVisibility));
    }

    public ObservableCollection<RazerBleDpiStageRowViewModel> DpiStages { get; } = [];

    public int ActiveDpiStage
    {
        get => _activeDpiStage;
        set
        {
            if (SetField(ref _activeDpiStage, Math.Clamp(value, 1, Math.Max(1, DpiStages.Count))))
            {
                OnPropertyChanged(nameof(ActiveDpiStageValue));
            }
        }
    }

    public double ActiveDpiStageValue
    {
        get => _activeDpiStage;
        set => ActiveDpiStage = (int)Math.Round(value);
    }

    public string DpiStagesStatusText
    {
        get => _dpiStagesStatusText;
        private set => SetField(ref _dpiStagesStatusText, value);
    }

    public Visibility ScrollVisibility
    {
        get => _scrollVisibility;
        private set => SetVisibility(ref _scrollVisibility, value, nameof(ScrollVisibility));
    }

    public Visibility ScrollModeVisibility
    {
        get => _scrollModeVisibility;
        private set => SetVisibility(ref _scrollModeVisibility, value, nameof(ScrollModeVisibility));
    }

    public Visibility ScrollSmartReelVisibility
    {
        get => _scrollSmartReelVisibility;
        private set => SetVisibility(
            ref _scrollSmartReelVisibility, value, nameof(ScrollSmartReelVisibility));
    }

    public Visibility ScrollSmartReelThresholdVisibility
    {
        get => _scrollSmartReelThresholdVisibility;
        private set => SetVisibility(
            ref _scrollSmartReelThresholdVisibility, value, nameof(ScrollSmartReelThresholdVisibility));
    }

    public Visibility ScrollAccelerationVisibility
    {
        get => _scrollAccelerationVisibility;
        private set => SetVisibility(
            ref _scrollAccelerationVisibility, value, nameof(ScrollAccelerationVisibility));
    }

    public IReadOnlyList<string> ScrollModeOptions { get; } = ["Tactile", "Free spin", "Micro tactile"];

    public int SelectedScrollModeIndex
    {
        get => _selectedScrollModeIndex;
        set => SetField(ref _selectedScrollModeIndex, Math.Clamp(value, 0, 2));
    }

    public bool ScrollSmartReelEnabled
    {
        get => _scrollSmartReelEnabled;
        set => SetField(ref _scrollSmartReelEnabled, value);
    }

    public double ScrollSmartReelThreshold
    {
        get => _scrollSmartReelThreshold;
        set => SetField(ref _scrollSmartReelThreshold, Math.Clamp(value, 0d, 255d));
    }

    public bool ScrollAccelerationEnabled
    {
        get => _scrollAccelerationEnabled;
        set => SetField(ref _scrollAccelerationEnabled, value);
    }

    public string ScrollStatusText
    {
        get => _scrollStatusText;
        private set => SetField(ref _scrollStatusText, value);
    }

    public Visibility PowerVisibility
    {
        get => _powerVisibility;
        private set => SetVisibility(ref _powerVisibility, value, nameof(PowerVisibility));
    }

    public Visibility PowerLowBatteryVisibility
    {
        get => _powerLowBatteryVisibility;
        private set => SetVisibility(
            ref _powerLowBatteryVisibility, value, nameof(PowerLowBatteryVisibility));
    }

    public Visibility PowerAutoSleepVisibility
    {
        get => _powerAutoSleepVisibility;
        private set => SetVisibility(
            ref _powerAutoSleepVisibility, value, nameof(PowerAutoSleepVisibility));
    }

    public Visibility PowerSleepTimeVisibility
    {
        get => _powerSleepTimeVisibility;
        private set => SetVisibility(
            ref _powerSleepTimeVisibility, value, nameof(PowerSleepTimeVisibility));
    }

    public Visibility PowerTimeToDimVisibility
    {
        get => _powerTimeToDimVisibility;
        private set => SetVisibility(
            ref _powerTimeToDimVisibility, value, nameof(PowerTimeToDimVisibility));
    }

    public double LowBatteryThresholdPercent
    {
        get => _lowBatteryThresholdPercent;
        set => SetField(ref _lowBatteryThresholdPercent, Math.Clamp(value, 0d, 100d));
    }

    public bool AutoSleepEnabled
    {
        get => _autoSleepEnabled;
        set => SetField(ref _autoSleepEnabled, value);
    }

    public double SleepMinutes
    {
        get => _sleepMinutes;
        set => SetField(ref _sleepMinutes, Math.Clamp(value, 1d, 65_535d));
    }

    public bool TimeToDimEnabled
    {
        get => _timeToDimEnabled;
        set => SetField(ref _timeToDimEnabled, value);
    }

    public double TimeToDimMinutes
    {
        get => _timeToDimMinutes;
        set => SetField(ref _timeToDimMinutes, Math.Clamp(value, 1d, 15d));
    }

    public string PowerStatusText
    {
        get => _powerStatusText;
        private set => SetField(ref _powerStatusText, value);
    }

    public Visibility ProfilesVisibility
    {
        get => _profilesVisibility;
        private set => SetVisibility(ref _profilesVisibility, value, nameof(ProfilesVisibility));
    }

    public IReadOnlyList<string> ProfileOptions => _profileIds.Select(DescribeProfile).ToArray();

    public int SelectedProfileIndex
    {
        get => _selectedProfileIndex;
        set => SetField(
            ref _selectedProfileIndex,
            Math.Clamp(value, 0, Math.Max(0, _profileIds.Count - 1)));
    }

    public string ProfileStatusText
    {
        get => _profileStatusText;
        private set => SetField(ref _profileStatusText, value);
    }

    public int Brightness
    {
        get => _brightness;
        set
        {
            if (SetField(ref _brightness, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(BrightnessText));
            }
        }
    }

    public string BrightnessText => $"{Brightness}%";

    public IReadOnlyList<string> LightingEffectOptions { get; } =
        ["Off", "Static", "Breathing", "Spectrum", "Wave", "Reactive", "Wheel"];

    public int SelectedLightingEffectIndex
    {
        get => _selectedLightingEffectIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, LightingEffects.Length - 1);
            if (_selectedLightingEffectIndex == clamped) return;
            _selectedLightingEffectIndex = clamped;
            _selectedLightingDirectionIndex = 0;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedLightingEffect));
            OnPropertyChanged(nameof(LightingDirectionOptions));
            OnPropertyChanged(nameof(SelectedLightingDirectionIndex));
            OnPropertyChanged(nameof(LightingDirectionVisibility));
            OnPropertyChanged(nameof(LightingColorVisibility));
        }
    }

    public RazerBleLightingEffect SelectedLightingEffect => LightingEffects[_selectedLightingEffectIndex];

    public IReadOnlyList<string> LightingDirectionOptions => SelectedLightingEffect switch
    {
        RazerBleLightingEffect.Wave => ["Left", "Right"],
        RazerBleLightingEffect.Wheel => ["Clockwise", "Counter-clockwise"],
        _ => [],
    };

    public int SelectedLightingDirectionIndex
    {
        get => _selectedLightingDirectionIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, LightingDirectionOptions.Count - 1));
            if (_selectedLightingDirectionIndex == clamped) return;
            _selectedLightingDirectionIndex = clamped;
            OnPropertyChanged();
        }
    }

    public Visibility LightingDirectionVisibility =>
        SelectedLightingEffect is RazerBleLightingEffect.Wave or RazerBleLightingEffect.Wheel
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Color LightingColor
    {
        get => _lightingColor;
        set => SetField(ref _lightingColor, value);
    }

    public Visibility LightingColorVisibility =>
        SelectedLightingEffect is RazerBleLightingEffect.Static or
            RazerBleLightingEffect.Breathing or RazerBleLightingEffect.Reactive
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string LightingStatusText
    {
        get => _lightingStatusText;
        private set => SetField(ref _lightingStatusText, value);
    }

    public RazerHidMappingViewModel? Mapping => _mapping;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(CanEdit));
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }

    public async ValueTask DisposeAsync()
    {
        if (IsLayla) StopLaylaOperations();
        if (Layla is not null) { await Layla.DisposeAsync(); Layla = null; }
        if (_service is null) return;
        await Task.Run(_service.Dispose).ConfigureAwait(false);
        _service = null;
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    /// <summary>The raw percentage behind <see cref="BatteryText"/>.</summary>
    public int? BatteryPercent => _batteryPercent;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetVisibility(ref Visibility field, Visibility value, string propertyName)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

