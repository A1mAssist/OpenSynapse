using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerBleDeviceRowViewModel(RazerBleDevice device) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public RazerBleDevice Device { get; } = device;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? "Razer BLE" : Device.Name;
    public string Identity => "BLE";
    public string IconGlyph => "\uE962";
    public string Status => AppStrings.Text("BlackSharkStatusConnected");
    // shortcut: protocol 30 is the middleware's mouse class; revisit when a BLE keyboard appears.
    public OpenSynapse.Core.Devices.DeviceCategory HardwareCategory => OpenSynapse.Core.Devices.DeviceCategory.Mouse;
    public Microsoft.UI.Xaml.Media.Brush StatusBrush { get; } =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 68, 214, 44));

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
    private RazerBleDeviceService? _service;
    private RazerHidMappingViewModel? _mapping;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string? _firmware;
    private string? _serial;
    private int? _batteryPercent;
    private byte? _chargingStatus;
    private static readonly RazerBleLightingEffect[] LightingEffects =
    [
        RazerBleLightingEffect.Off,
        RazerBleLightingEffect.Static,
        RazerBleLightingEffect.Breathing,
        RazerBleLightingEffect.Spectrum,
        RazerBleLightingEffect.Wave,
        RazerBleLightingEffect.Reactive,
        RazerBleLightingEffect.Wheel,
    ];

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
        Func<CancellationToken, Task<bool>>? saveProfile = null)
    {
        _device = device;
        _diagnostic = diagnostic;
        _getProfile = getProfile;
        _saveProfile = saveProfile;
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => string.IsNullOrWhiteSpace(_device.Name) ? "Razer BLE" : _device.Name;
    public string Identity => AppStrings.Text("BleIdentity");
    public string DeviceId => _device.Id;
    public string ProfileDeviceKey => $"ble:{_device.Id}";
    public bool CanEdit => !IsBusy && _service is not null;

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

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!BeginOperation()) return;
        try
        {
            _service ??= await RazerBleDeviceService.OpenAsync(_device, _diagnostic, cancellationToken);
            Apply(await _service.ReadStateAsync(cancellationToken));
            OperationStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            EndOperation();
        }

        await LoadLightingAsync(cancellationToken);
        await LoadMappingAsync(cancellationToken);
        await LoadDpiStagesAsync(cancellationToken);
        await LoadScrollAsync(cancellationToken);
        await LoadPowerAsync(cancellationToken);
        await LoadProfilesAsync(cancellationToken);
        await LoadExtrasAsync(cancellationToken);
    }

    [ProfileSetting("ble.polling-rate", DeviceCategory.Mouse)]
    public Task ApplyPollingRateAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetPollingRateAsync(PollingRates[SelectedPollingIndex], cancellationToken),
            applied => SelectedPollingIndex = Math.Max(0, PollingRates.ToList().IndexOf(applied)),
            (applied, token) => PersistMouseAsync(mouse => mouse.PollingRateHertz = applied, token),
            async token => (await _service!.ReadStateAsync(token)).PollingHertz
                ?? throw new InvalidOperationException("The device did not report a polling rate."),
            (previous, token) => _service!.SetPollingRateAsync(previous, token),
            cancellationToken);

    [ProfileSetting("ble.dpi", DeviceCategory.Mouse)]
    public Task ApplyDpiAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetDpiAsync((int)Math.Round(DpiX), (int)Math.Round(DpiY), cancellationToken),
            applied =>
            {
                DpiX = applied.X;
                DpiY = applied.Y;
            },
            (applied, token) => PersistMouseAsync(
                mouse =>
                {
                    mouse.DpiX = applied.X;
                    mouse.DpiY = applied.Y;
                },
                token),
            async token => (await _service!.ReadStateAsync(token)).Dpi is { } dpi
                ? (dpi.X, dpi.Y)
                : throw new InvalidOperationException("The device did not report DPI."),
            (previous, token) => _service!.SetDpiAsync(previous.X, previous.Y, token),
            cancellationToken);

    public async Task LoadLightingAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            ApplyLighting(await _service.ReadLightingAsync(cancellationToken));
            LightingStatusText = string.Empty;
            LightingVisibility = Visibility.Visible;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            LightingVisibility = Visibility.Collapsed;
            LightingStatusText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.lighting", DeviceCategory.Mouse)]
    public Task ApplyLightingAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            async service =>
            {
                var brightness = await service.SetBrightnessAsync(Brightness, cancellationToken);
                var (flags, rate, colors) = BuildLightingPayload(
                    SelectedLightingEffect, LightingColor, SelectedLightingDirectionIndex);
                var state = await service.SetEffectAsync(
                    SelectedLightingEffect, flags, rate, colors, cancellationToken);
                return state with { BrightnessPercent = brightness };
            },
            state =>
            {
                ApplyLighting(state);
            },
            async (state, token) =>
            {
                var saved = await PersistLightingAsync(BuildLightingProfile(), token);
                LightingStatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
                return saved;
            },
            token => _service!.ReadLightingAsync(token),
            RestoreLightingAsync,
            cancellationToken);

    public async Task LoadMappingAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            if (_mapping is null)
            {
                _mapping = new RazerHidMappingViewModel(
                    new RazerBleMappingTransport(_service),
                    canWrite: true,
                    log: _diagnostic,
                    mappingProfileSaver: PersistMappingProfileAsync);
                OnPropertyChanged(nameof(Mapping));
            }

            await _mapping.ReadAsync(cancellationToken).ConfigureAwait(true);
            // A read failure means the device advertised mapping but did not answer this
            // time; keep the card visible so the error is actionable instead of hiding it.
            MappingVisibility = _mapping.Rows.Count > 0 || _mapping.HasError
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            MappingVisibility = Visibility.Collapsed;
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task PersistMappingProfileAsync(
        IReadOnlyList<ButtonMappingProfile> mappings,
        CancellationToken cancellationToken)
    {
        if (mappings.Count == 0)
        {
            return;
        }

        var mode = mappings[0].Mode ?? 0;
        var saved = await PersistMouseAsync(mouse =>
        {
            mouse.ButtonMappings = RazerHidMappingActions.MergeButtonMappings(
                mouse.ButtonMappings, mappings, mode);
        }, cancellationToken).ConfigureAwait(true);
        if (!saved)
        {
            throw new IOException("BLE mapping could not be saved to the profile.");
        }
    }

    [NotProfileScoped]
    public async Task ApplyMappingAsync(CancellationToken cancellationToken = default)
    {
        if (_mapping is null) return;
        await _mapping.ApplyAsync(cancellationToken).ConfigureAwait(true);
        if (_mapping.HasError)
        {
            ErrorText = _mapping.ErrorText;
        }
        else
        {
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
    }

    public async Task LoadDpiStagesAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            ApplyDpiStages(await _service.ReadDpiStagesAsync(cancellationToken));
            DpiStagesStatusText = string.Empty;
            DpiStagesVisibility = Visibility.Visible;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            DpiStages.Clear();
            ActiveDpiStage = 1;
            DpiStagesVisibility = Visibility.Collapsed;
            DpiStagesStatusText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.dpi-stages", DeviceCategory.Mouse)]
    public Task ApplyDpiStagesAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            async service =>
            {
                var state = new RazerBleDpiStages(
                    (byte)ActiveDpiStage,
                    DpiStages.Select(stage => new RazerBleDpiStage(
                        stage.Index,
                        (int)Math.Round(stage.X),
                        (int)Math.Round(stage.Y),
                        stage.Z)).ToArray());
                return await service.SetDpiStagesAsync(state, cancellationToken);
            },
            state =>
            {
                ApplyDpiStages(state);
            },
            async (state, token) =>
            {
                var saved = await PersistMouseAsync(
                    mouse => ApplyDpiStagesToProfile(mouse, state), token);
                DpiStagesStatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
                return saved;
            },
            token => _service!.ReadDpiStagesAsync(token),
            (previous, token) => _service!.SetDpiStagesAsync(previous, token),
            cancellationToken);

    public async Task LoadScrollAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            ApplyScroll(await _service.ReadScrollAsync(cancellationToken));
            ScrollStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ScrollVisibility = Visibility.Collapsed;
            ScrollStatusText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    private bool _scrollApplyFailed;
    [ProfileSetting("ble.scroll", DeviceCategory.Mouse)]
    public async Task ApplyScrollAsync(CancellationToken cancellationToken = default) =>
        _scrollApplyFailed = !await ApplyScrollCoreAsync(cancellationToken);

    private Task<bool> ApplyScrollCoreAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetScrollAsync(
                ScrollModeVisibility == Visibility.Visible ? (byte)SelectedScrollModeIndex : null,
                ScrollSmartReelVisibility == Visibility.Visible ? ScrollSmartReelEnabled : null,
                ScrollSmartReelThresholdVisibility == Visibility.Visible
                    ? (byte)Math.Round(ScrollSmartReelThreshold)
                    : null,
                ScrollAccelerationVisibility == Visibility.Visible
                    ? ScrollAccelerationEnabled
                    : null,
                cancellationToken),
            state =>
            {
                ApplyScroll(state);
            },
            async (state, token) =>
            {
                var saved = await PersistMouseAsync(
                    mouse => ApplyScrollToProfile(mouse, state), token);
                ScrollStatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
                return saved;
            },
            token => _service!.ReadScrollAsync(token),
            (previous, token) => _service!.SetScrollAsync(
                previous.Mode,
                previous.SmartReel,
                previous.SmartReelThreshold,
                previous.Acceleration,
                token),
            cancellationToken);

    public async Task LoadPowerAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            ApplyPower(await _service.ReadPowerAsync(cancellationToken));
            PowerStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            PowerVisibility = Visibility.Collapsed;
            PowerStatusText = exception.Message;
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.power", DeviceCategory.Mouse)]
    public Task ApplyPowerAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetPowerAsync(
                PowerLowBatteryVisibility == Visibility.Visible
                    ? (int)Math.Round(LowBatteryThresholdPercent)
                    : null,
                PowerAutoSleepVisibility == Visibility.Visible ? AutoSleepEnabled : null,
                PowerSleepTimeVisibility == Visibility.Visible
                    ? (int)Math.Round(SleepMinutes)
                    : null,
                PowerTimeToDimVisibility == Visibility.Visible
                    ? new RazerBleTimeToDimState(
                        TimeToDimEnabled,
                        (int)Math.Round(TimeToDimMinutes * 60d),
                        _timeToDimPercentage)
                    : null,
                cancellationToken),
            state =>
            {
                ApplyPower(state);
            },
            async (state, token) =>
            {
                var saved = await PersistMouseAsync(
                    mouse => ApplyPowerToProfile(mouse, state), token);
                PowerStatusText = saved ? AppStrings.Text("BlackSharkSaved") : string.Empty;
                return saved;
            },
            token => _service!.ReadPowerAsync(token),
            (previous, token) => _service!.SetPowerAsync(
                previous.LowBatteryPercent,
                previous.AutoSleep,
                previous.SleepMinutes,
                previous.TimeToDim,
                token),
            cancellationToken);

    public async Task LoadProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || !BeginOperation()) return;
        try
        {
            var state = await _service.ReadProfilesAsync(cancellationToken);
            _profileIds = state.Ids;
            SelectedProfileIndex = state.ActiveId is { } active
                ? Math.Max(0, state.Ids.ToList().IndexOf(active))
                : 0;
            ProfilesVisibility = state.Ids.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ProfileStatusText = state.ActiveId is { } id
                ? $"Active: Profile {id}"
                : string.Empty;
            OnPropertyChanged(nameof(ProfileOptions));
            OnPropertyChanged(nameof(SelectedProfileIndex));
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _profileIds = [];
            ProfilesVisibility = Visibility.Collapsed;
            ProfileStatusText = exception.Message;
            OnPropertyChanged(nameof(ProfileOptions));
            OnPropertyChanged(nameof(SelectedProfileIndex));
        }
        finally
        {
            EndOperation();
        }
    }

    [ProfileSetting("ble.onboard-profile", DeviceCategory.Mouse)]
    public async Task ApplyProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || _profileIds.Count == 0 || !BeginOperation()) return;
        var profileId = _profileIds[Math.Clamp(SelectedProfileIndex, 0, _profileIds.Count - 1)];
        RazerBleProfileState? previous = null;
        try
        {
            previous = await _service.ReadProfilesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            EndOperation();
            return;
        }
        catch
        {
            // The active profile is still writable; only the rollback target is lost.
        }

        var applied = false;
        try
        {
            await _service.SetActiveProfileAsync(profileId, cancellationToken);
            ProfileStatusText = AppStrings.Text("BlackSharkSaved");
            if (!await PersistMouseAsync(mouse => mouse.ActiveOnboardProfileId = profileId, cancellationToken))
            {
                await RestorePreviousProfileAsync();
                ProfileStatusText = string.Empty;
                ErrorText = AppStrings.Text("ViperSettingAppliedProfileSaveFailed");
                return;
            }

            applied = true;
        }
        catch (OperationCanceledException)
        {
            await RestorePreviousProfileAsync();
            ProfileStatusText = string.Empty;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            await RestorePreviousProfileAsync();
            ProfileStatusText = string.Empty;
            ErrorText = exception.Message;
        }
        finally
        {
            EndOperation();
        }

        if (!applied) return;
        await LoadDpiStagesAsync(cancellationToken);
        await LoadScrollAsync(cancellationToken);
        await LoadPowerAsync(cancellationToken);
        await LoadLightingAsync(cancellationToken);
        await LoadMappingAsync(cancellationToken);

        async Task RestorePreviousProfileAsync()
        {
            if (previous?.ActiveId is not { } previousId) return;
            SelectedProfileIndex = Math.Max(0, _profileIds.ToList().IndexOf(previousId));
            try
            {
                await _service.SetActiveProfileAsync(previousId, CancellationToken.None);
            }
            catch
            {
                // The save failure is the error the user must see.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_service is null) return;
        await Task.Run(_service.Dispose).ConfigureAwait(false);
        _service = null;
    }

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private void ApplyLighting(RazerBleLightingState state)
    {
        Brightness = state.BrightnessPercent;
        var effectIndex = Array.IndexOf(LightingEffects, (RazerBleLightingEffect)state.EffectId);
        if (effectIndex >= 0)
        {
            SelectedLightingEffectIndex = effectIndex;
        }

        if (state.Direction is 1 or 2 && SelectedLightingEffect == RazerBleLightingEffect.Wave)
        {
            SelectedLightingDirectionIndex = state.Direction == 2 ? 0 : 1;
        }
        else if (state.Direction is 1 or 2 && SelectedLightingEffect == RazerBleLightingEffect.Wheel)
        {
            SelectedLightingDirectionIndex = state.Direction - 1;
        }

        if (state.Colors.Count >= 3)
        {
            LightingColor = Color.FromArgb(255, state.Colors[0], state.Colors[1], state.Colors[2]);
        }

        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(SelectedLightingEffectIndex));
        OnPropertyChanged(nameof(SelectedLightingEffect));
        OnPropertyChanged(nameof(LightingDirectionOptions));
        OnPropertyChanged(nameof(SelectedLightingDirectionIndex));
        OnPropertyChanged(nameof(LightingDirectionVisibility));
        OnPropertyChanged(nameof(LightingColor));
        OnPropertyChanged(nameof(LightingColorVisibility));
    }

    private static (byte Flags, byte Rate, byte[] Colors) BuildLightingPayload(
        RazerBleLightingEffect effect,
        Color color,
        int directionIndex)
    {
        byte[] rgb = [color.R, color.G, color.B];
        return effect switch
        {
            RazerBleLightingEffect.Off => (0, 0, []),
            RazerBleLightingEffect.Static => (0, 0, rgb),
            RazerBleLightingEffect.Breathing => (1, 0, rgb),
            RazerBleLightingEffect.Spectrum => (0, 0, []),
            RazerBleLightingEffect.Wave => ((byte)(directionIndex == 0 ? 2 : 1), 40, []),
            RazerBleLightingEffect.Reactive => (0, 1, rgb),
            RazerBleLightingEffect.Wheel => ((byte)(directionIndex + 1), 0, []),
            _ => (0, 0, []),
        };
    }

    private void ApplyDpiStages(RazerBleDpiStages state)
    {
        DpiStages.Clear();
        foreach (var stage in state.Stages)
        {
            DpiStages.Add(new RazerBleDpiStageRowViewModel(stage.Index, stage.X, stage.Y, stage.Z));
        }

        ActiveDpiStage = state.ActiveStage;
    }

    private void ApplyScroll(RazerBleScrollState state)
    {
        if (state.Mode is { } mode && mode <= 2)
        {
            SelectedScrollModeIndex = mode;
        }
        if (state.SmartReel is { } smartReel)
        {
            ScrollSmartReelEnabled = smartReel;
        }
        if (state.SmartReelThreshold is { } threshold)
        {
            ScrollSmartReelThreshold = threshold;
        }
        if (state.Acceleration is { } acceleration)
        {
            ScrollAccelerationEnabled = acceleration;
        }

        ScrollModeVisibility = state.Mode is { } readMode && readMode <= 2
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScrollSmartReelVisibility = state.SmartReel is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ScrollSmartReelThresholdVisibility = state.SmartReelThreshold is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ScrollAccelerationVisibility = state.Acceleration is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        ScrollVisibility = state.Mode is not null || state.SmartReel is not null ||
            state.SmartReelThreshold is not null || state.Acceleration is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyPower(RazerBlePowerState state)
    {
        if (state.LowBatteryPercent is { } lowBattery)
        {
            LowBatteryThresholdPercent = lowBattery;
        }
        if (state.AutoSleep is { } autoSleep)
        {
            AutoSleepEnabled = autoSleep;
        }
        if (state.SleepMinutes is { } sleepMinutes)
        {
            SleepMinutes = sleepMinutes;
        }
        if (state.TimeToDim is { } timeToDim)
        {
            TimeToDimEnabled = timeToDim.Enabled;
            TimeToDimMinutes = Math.Clamp(timeToDim.Seconds / 60d, 1d, 15d);
            _timeToDimPercentage = timeToDim.Percentage is >= 1 ? timeToDim.Percentage : (byte)20; // Synapse uses 20%.
        }

        PowerLowBatteryVisibility = state.LowBatteryPercent is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        PowerAutoSleepVisibility = state.AutoSleep is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        PowerSleepTimeVisibility = state.SleepMinutes is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        PowerTimeToDimVisibility = state.TimeToDim is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        PowerVisibility = state.LowBatteryPercent is not null || state.AutoSleep is not null ||
            state.SleepMinutes is not null || state.TimeToDim is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Apply(RazerBleDeviceState state)
    {
        _firmware = state.Firmware?.ToVersionString();
        _serial = state.SerialNumber;
        _batteryPercent = state.BatteryPercent;
        _chargingStatus = state.ChargingStatus;
        if (state.PollingHertz is { } hertz)
        {
            _selectedPollingIndex = Math.Max(0, PollingRates.ToList().IndexOf(hertz));
        }
        if (state.Dpi is { } dpi)
        {
            _dpiX = dpi.X;
            _dpiY = dpi.Y;
        }

        OnPropertyChanged(nameof(FirmwareText));
        OnPropertyChanged(nameof(SerialText));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ChargingText));
        OnPropertyChanged(nameof(SelectedPollingIndex));
        OnPropertyChanged(nameof(DpiX));
        OnPropertyChanged(nameof(DpiY));
    }

    private bool BeginOperation()
    {
        if (IsBusy) return false;
        ErrorText = string.Empty;
        OperationStatusText = string.Empty;
        IsBusy = true;
        return true;
    }

    private void EndOperation() => IsBusy = false;

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or InvalidOperationException or ArgumentOutOfRangeException or
            NotSupportedException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or
            TimeoutException or OperationCanceledException;

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

