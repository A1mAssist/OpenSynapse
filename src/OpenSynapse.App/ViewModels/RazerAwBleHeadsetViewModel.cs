using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.UI;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>Device-list row for a paired AudioAWKittyBLE headset.</summary>
public sealed class RazerAwBleHeadsetRowViewModel(RazerBleDevice device) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public RazerBleDevice Device { get; } = device;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? "Razer Audio BLE" : Device.Name;
    public string Identity => "BLE";
    public string IconGlyph => "\uE7F6";
    public DeviceCategory HardwareCategory => DeviceCategory.Headset;
    public string Status => AppStrings.Text("BlackSharkStatusConnected");

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

/// <summary>
/// Battery and audio controls for the AudioAWKittyBLE headsets. Each control is
/// probed by its bundle getter and hidden when the headset does not answer.
/// </summary>
public sealed partial class RazerAwBleHeadsetViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly (byte Value, string Key)[] EqPresets =
    [
        (0, "AwBleHeadsetEqDefault"),
        (1, "AwBleHeadsetEqAmplified"),
        (2, "AwBleHeadsetEqVocal"),
        (3, "AwBleHeadsetEqBass"),
        (4, "AwBleHeadsetEqClarity"),
        (5, "AwBleHeadsetEqTreble"),
        (6, "AwBleHeadsetEqFps"),
        (7, "AwBleHeadsetEqGame"),
        (8, "AwBleHeadsetEqMusic"),
        (9, "AwBleHeadsetEqMovie"),
        (10, "AwBleHeadsetEqApex"),
        (11, "AwBleHeadsetEqCsgo"),
        (12, "AwBleHeadsetEqValorant"),
        (13, "AwBleHeadsetEqFortnite"),
        (254, "AwBleHeadsetEqCustom2"),
        (255, "AwBleHeadsetEqCustom1"),
    ];

    private static readonly (byte Value, string Key)[] MicEqPresets =
    [
        (0, "AwBleHeadsetMicEqDefault"),
        (1, "AwBleHeadsetMicEqBoost"),
        (2, "AwBleHeadsetMicEqBroadcast"),
        (3, "AwBleHeadsetMicEqConference"),
        (255, "AwBleHeadsetMicEqCustom"),
    ];

    private static readonly int[] CustomEqFrequencies =
        [31, 63, 125, 250, 500, 1_000, 2_000, 4_000, 8_000, 16_000];

    private static readonly int[] MicCustomEqFrequencies =
        [250, 750, 1_000, 1_200, 1_500, 2_300, 3_000, 4_000, 5_000, 7_000];

    private static readonly (RazerAwChromaEffect Value, string Key)[] ChromaEffects =
    [
        (RazerAwChromaEffect.Off, "AwBleHeadsetChromaOff"),
        (RazerAwChromaEffect.Static, "AwBleHeadsetChromaStatic"),
        (RazerAwChromaEffect.Breathing, "AwBleHeadsetChromaBreathing"),
        (RazerAwChromaEffect.Spectrum, "AwBleHeadsetChromaSpectrum"),
        (RazerAwChromaEffect.AudioMeter, "AwBleHeadsetChromaAudioMeter"),
    ];

    private readonly Func<CancellationToken, Task<RazerAwBleHeadsetService?>> _connect;
    private readonly RazerBleDevice _device;
    private RazerAwBleHeadsetService? _service;
    private bool _isBusy;
    private string _batteryText = "--";
    private string _statusText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _errorText = string.Empty;
    private double _volume;
    private double _sidetone;
    private bool _gameMode;
    private int _selectedEqPresetIndex;
    private bool _customEqEnabled;
    private int _selectedAutoPowerOffIndex;
    private double _chromaBrightness;
    private int _selectedChromaEffectIndex;
    private Color _chromaPrimaryColor = Color.FromArgb(255, 0, 255, 0);
    private Color _chromaSecondaryColor = Color.FromArgb(255, 0, 0, 255);
    private double _bassLevel;
    private double _hapticsLevel;
    private int _selectedMicEqPresetIndex;
    private bool _micCustomEqEnabled;
    private string _firmwareText = "—";
    private string _serialText = "—";
    private string _connectionText = "—";
    private string _chargingText = "—";
    private Visibility _controlsVisibility = Visibility.Collapsed;
    private Visibility _volumeVisibility = Visibility.Collapsed;
    private Visibility _sidetoneVisibility = Visibility.Collapsed;
    private Visibility _gameModeVisibility = Visibility.Collapsed;
    private Visibility _eqPresetVisibility = Visibility.Collapsed;
    private Visibility _autoPowerOffVisibility = Visibility.Collapsed;
    private Visibility _chromaBrightnessVisibility = Visibility.Collapsed;
    private Visibility _chromaEffectVisibility = Visibility.Collapsed;
    private Visibility _chromaPrimaryColorVisibility = Visibility.Collapsed;
    private Visibility _chromaSecondaryColorVisibility = Visibility.Collapsed;
    private Visibility _bassVisibility = Visibility.Collapsed;
    private Visibility _hapticsVisibility = Visibility.Collapsed;
    private Visibility _micEqVisibility = Visibility.Collapsed;
    private Visibility _micCustomEqVisibility = Visibility.Collapsed;
    private Visibility _customEqVisibility = Visibility.Collapsed;
    private Visibility _firmwareVisibility = Visibility.Collapsed;
    private Visibility _serialVisibility = Visibility.Collapsed;
    private Visibility _connectionVisibility = Visibility.Collapsed;
    private Visibility _chargingVisibility = Visibility.Collapsed;

    public RazerAwBleHeadsetViewModel(RazerBleDevice device, Action<string>? diagnostic = null)
        : this(device, cancellationToken => ConnectAsync(device, diagnostic, cancellationToken))
    {
    }

    internal RazerAwBleHeadsetViewModel(
        RazerBleDevice device,
        Func<CancellationToken, Task<RazerAwBleHeadsetService?>> connect)
    {
        _device = device;
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RazerBleDevice Device => _device;
    public string Name => string.IsNullOrWhiteSpace(_device.Name) ? "Razer Audio BLE" : _device.Name;
    public string Identity => "BLE";
    public string BatteryText => _batteryText;
    public string StatusText => _statusText;
    public string OperationStatusText
    {
        get => _operationStatusText;
        private set => SetField(ref _operationStatusText, value);
    }
    public string ErrorText => _errorText;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsBusy => _isBusy;
    public bool CanRefresh => !_isBusy;
    public bool CanEdit => !_isBusy && _service is not null;
    public bool CanApply => CanEdit;

    public IReadOnlyList<string> EqPresetOptions =>
        EqPresets.Select(preset => AppStrings.Text(preset.Key)).ToArray();

    public double Volume
    {
        get => _volume;
        set
        {
            if (SetField(ref _volume, Math.Clamp(value, 0d, 100d)))
            {
                OnPropertyChanged(nameof(VolumeText));
            }
        }
    }

    public string VolumeText => AppStrings.FormatText("AwBleHeadsetPercent", (int)Math.Round(Volume));

    public double Sidetone
    {
        get => _sidetone;
        set
        {
            if (SetField(ref _sidetone, Math.Clamp(value, 0d, 100d)))
            {
                OnPropertyChanged(nameof(SidetoneText));
            }
        }
    }

    public string SidetoneText => AppStrings.FormatText("AwBleHeadsetPercent", (int)Math.Round(Sidetone));

    public bool GameMode
    {
        get => _gameMode;
        set => SetField(ref _gameMode, value);
    }

    public int SelectedEqPresetIndex
    {
        get => _selectedEqPresetIndex;
        set => SetField(ref _selectedEqPresetIndex, Math.Clamp(value, 0, EqPresets.Length - 1));
    }

    public IReadOnlyList<string> AutoPowerOffOptions =>
        RazerAwBleProtocol.AutoPowerOffMinuteOptions
            .Select(minutes => minutes == 0
                ? AppStrings.Text("AwBleHeadsetAutoPowerOffOff")
                : AppStrings.FormatText("AwBleHeadsetAutoPowerOffMinutes", minutes))
            .ToArray();

    public int SelectedAutoPowerOffIndex
    {
        get => _selectedAutoPowerOffIndex;
        set => SetField(
            ref _selectedAutoPowerOffIndex,
            Math.Clamp(value, 0, RazerAwBleProtocol.AutoPowerOffMinuteOptions.Length - 1));
    }

    public bool CustomEqEnabled
    {
        get => _customEqEnabled;
        set => SetField(ref _customEqEnabled, value);
    }

    public double BassLevel
    {
        get => _bassLevel;
        set
        {
            if (SetField(ref _bassLevel, Math.Clamp(value, 0d, 100d)))
            {
                OnPropertyChanged(nameof(BassLevelText));
            }
        }
    }

    public string BassLevelText =>
        AppStrings.FormatText("AwBleHeadsetPercent", (int)Math.Round(BassLevel));

    public double HapticsLevel
    {
        get => _hapticsLevel;
        set
        {
            if (SetField(
                    ref _hapticsLevel,
                    Math.Clamp(value, RazerAwBleProtocol.HapticsMinLevel, RazerAwBleProtocol.HapticsMaxLevel)))
            {
                OnPropertyChanged(nameof(HapticsLevelText));
            }
        }
    }

    public string HapticsLevelText =>
        AppStrings.FormatText("AwBleHeadsetHapticsValue", (int)Math.Round(HapticsLevel));

    public IReadOnlyList<string> MicEqPresetOptions =>
        MicEqPresets.Select(preset => AppStrings.Text(preset.Key)).ToArray();

    public int SelectedMicEqPresetIndex
    {
        get => _selectedMicEqPresetIndex;
        set => SetField(ref _selectedMicEqPresetIndex, Math.Clamp(value, 0, MicEqPresets.Length - 1));
    }

    public bool MicCustomEqEnabled
    {
        get => _micCustomEqEnabled;
        set => SetField(ref _micCustomEqEnabled, value);
    }

    public ObservableCollection<RazerAwBleEqBandViewModel> CustomEqBands { get; } = [];

    public ObservableCollection<RazerAwBleEqBandViewModel> MicCustomEqBands { get; } = [];

    public string FirmwareText
    {
        get => _firmwareText;
        private set => SetField(ref _firmwareText, value);
    }

    public string SerialText
    {
        get => _serialText;
        private set => SetField(ref _serialText, value);
    }

    public string ConnectionText
    {
        get => _connectionText;
        private set => SetField(ref _connectionText, value);
    }

    public string ChargingText
    {
        get => _chargingText;
        private set => SetField(ref _chargingText, value);
    }

    public Visibility ControlsVisibility
    {
        get => _controlsVisibility;
        private set => SetVisibility(ref _controlsVisibility, value, nameof(ControlsVisibility));
    }

    public Visibility VolumeVisibility
    {
        get => _volumeVisibility;
        private set => SetVisibility(ref _volumeVisibility, value, nameof(VolumeVisibility));
    }

    public Visibility SidetoneVisibility
    {
        get => _sidetoneVisibility;
        private set => SetVisibility(ref _sidetoneVisibility, value, nameof(SidetoneVisibility));
    }

    public Visibility GameModeVisibility
    {
        get => _gameModeVisibility;
        private set => SetVisibility(ref _gameModeVisibility, value, nameof(GameModeVisibility));
    }

    public Visibility EqPresetVisibility
    {
        get => _eqPresetVisibility;
        private set => SetVisibility(ref _eqPresetVisibility, value, nameof(EqPresetVisibility));
    }

    public Visibility AutoPowerOffVisibility
    {
        get => _autoPowerOffVisibility;
        private set => SetVisibility(ref _autoPowerOffVisibility, value, nameof(AutoPowerOffVisibility));
    }

    public Visibility BassVisibility
    {
        get => _bassVisibility;
        private set => SetVisibility(ref _bassVisibility, value, nameof(BassVisibility));
    }

    public Visibility HapticsVisibility
    {
        get => _hapticsVisibility;
        private set => SetVisibility(ref _hapticsVisibility, value, nameof(HapticsVisibility));
    }

    public Visibility MicEqVisibility
    {
        get => _micEqVisibility;
        private set => SetVisibility(ref _micEqVisibility, value, nameof(MicEqVisibility));
    }

    public Visibility MicCustomEqVisibility
    {
        get => _micCustomEqVisibility;
        private set => SetVisibility(ref _micCustomEqVisibility, value, nameof(MicCustomEqVisibility));
    }

    public Visibility CustomEqVisibility
    {
        get => _customEqVisibility;
        private set => SetVisibility(ref _customEqVisibility, value, nameof(CustomEqVisibility));
    }

    public Visibility FirmwareVisibility
    {
        get => _firmwareVisibility;
        private set => SetVisibility(ref _firmwareVisibility, value, nameof(FirmwareVisibility));
    }

    public Visibility SerialVisibility
    {
        get => _serialVisibility;
        private set => SetVisibility(ref _serialVisibility, value, nameof(SerialVisibility));
    }

    public Visibility ConnectionVisibility
    {
        get => _connectionVisibility;
        private set => SetVisibility(ref _connectionVisibility, value, nameof(ConnectionVisibility));
    }

    public Visibility ChargingVisibility
    {
        get => _chargingVisibility;
        private set => SetVisibility(ref _chargingVisibility, value, nameof(ChargingVisibility));
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        _isBusy = true;
        NotifyBusy();
        OperationStatusText = string.Empty;
        try
        {
            _service ??= await _connect(cancellationToken).ConfigureAwait(false);
            if (_service is null)
            {
                SetError(AppStrings.FormatText("AwBleHeadsetErrorRead", "not connected"));
                HideControls();
                return;
            }

            var state = await _service.ReadBatteryAsync(cancellationToken).ConfigureAwait(false);
            _batteryText = FormatBattery(state);
            _statusText = AppStrings.FormatText("AwBleHeadsetStatusRead", _batteryText);
            OnPropertyChanged(nameof(BatteryText));
            OnPropertyChanged(nameof(StatusText));
            SetError(string.Empty);

            await LoadInfoAsync(cancellationToken).ConfigureAwait(false);
            await LoadControlsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _service?.Dispose();
            _service = null;
            HideControls();
            SetError(AppStrings.FormatText("AwBleHeadsetErrorRead", exception.Message));
        }
        finally
        {
            _isBusy = false;
            NotifyBusy();
        }
    }

    public Task ApplyVolumeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetVolumeAsync((int)Math.Round(Volume), cancellationToken),
            applied => Volume = applied);

    public Task ApplySidetoneAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetSidetoneAsync((int)Math.Round(Sidetone), cancellationToken),
            applied => Sidetone = applied);

    public Task ApplyGameModeAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetGameModeAsync(GameMode, cancellationToken),
            applied => GameMode = applied);

    public Task ApplyEqPresetAsync(CancellationToken cancellationToken = default)
    {
        var preset = EqPresets[Math.Clamp(SelectedEqPresetIndex, 0, EqPresets.Length - 1)].Value;
        return RunWriteAsync(
            service => service.SetEqPresetAsync(preset, cancellationToken),
            applied => SelectedEqPresetIndex = IndexOfPreset(applied));
    }

    public Task ApplyAutoPowerOffAsync(CancellationToken cancellationToken = default)
    {
        var minutes = RazerAwBleProtocol.AutoPowerOffMinuteOptions[SelectedAutoPowerOffIndex];
        return RunWriteAsync(
            service => service.SetAutoPowerOffMinutesAsync(minutes, cancellationToken),
            applied => SelectedAutoPowerOffIndex = Array.IndexOf(
                RazerAwBleProtocol.AutoPowerOffMinuteOptions, applied));
    }

    public Task ApplyBassAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetBassAsync((int)Math.Round(BassLevel), cancellationToken),
            applied => BassLevel = applied);

    public Task ApplyHapticsAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            service => service.SetHapticsAsync((int)Math.Round(HapticsLevel), cancellationToken),
            applied => HapticsLevel = applied);

    public Task ApplyMicEqPresetAsync(CancellationToken cancellationToken = default)
    {
        var preset = MicEqPresets[
            Math.Clamp(SelectedMicEqPresetIndex, 0, MicEqPresets.Length - 1)].Value;
        return RunWriteAsync(
            service => service.SetMicEqPresetAsync(preset, cancellationToken),
            applied => SelectedMicEqPresetIndex = IndexOfMicPreset(applied));
    }

    public async ValueTask DisposeAsync()
    {
        _service?.Dispose();
        _service = null;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task RunWriteAsync<T>(
        Func<RazerAwBleHeadsetService, Task<T>> write,
        Action<T> apply)
    {
        if (_service is null || _isBusy)
        {
            return;
        }

        _isBusy = true;
        NotifyBusy();
        try
        {
            apply(await write(_service).ConfigureAwait(false));
            OperationStatusText = AppStrings.Text("AwBleHeadsetSaved");
            SetError(string.Empty);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            OperationStatusText = string.Empty;
            SetError(AppStrings.FormatText("AwBleHeadsetErrorWrite", exception.Message));
        }
        finally
        {
            _isBusy = false;
            NotifyBusy();
        }
    }

    private static async Task<RazerAwBleHeadsetService?> ConnectAsync(
        RazerBleDevice device,
        Action<string>? diagnostic,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RazerAwBleHeadsetService.ConnectAsync(device.Id, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            diagnostic?.Invoke($"aw ble connect failed: {exception.Message}");
            return null;
        }
    }

    private static string FormatBattery(RazerAwBatteryState state) =>
        state.HeadsetPercent is { } level ? $"{level}%" :
        state.RightEarbudPercent is { } right && state.LeftEarbudPercent is { } left
            ? AppStrings.FormatText("AwBleHeadsetEarbudBattery", right, left)
            : "--";

    private void SetError(string error)
    {
        _errorText = error;
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
    }

    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanApply));
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidDataException or InvalidOperationException or
            NotSupportedException or UnauthorizedAccessException or ArgumentException;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetVisibility(ref Visibility field, Visibility value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));
}
