using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHeadsetEqBandViewModel : INotifyPropertyChanged
{
    private double _level;

    public RazerHeadsetEqBandViewModel(int frequency)
    {
        Label = frequency >= 1_000 ? $"{frequency / 1_000d:0.#} kHz" : $"{frequency} Hz";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; }
    public string LevelText => $"{Math.Round(Level)}";

    public double Level
    {
        get => _level;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
            if (Math.Abs(_level - normalized) < 0.01)
            {
                return;
            }

            _level = normalized;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LevelText)));
        }
    }
}

public sealed class RazerHeadsetAudioViewModel : INotifyPropertyChanged
{
    private static readonly (byte Value, string Key)[] Presets =
    [
        (0x00, "RazerHeadsetPresetDefault"),
        (0x01, "RazerHeadsetPresetGame"),
        (0x02, "RazerHeadsetPresetMusic"),
        (0x03, "RazerHeadsetPresetMovie"),
        (0x04, "RazerHeadsetPresetTreble"),
        (0x05, "RazerHeadsetPresetMicBoost"),
        (0x06, "RazerHeadsetPresetPodcast"),
        (0x10, "RazerHeadsetPresetCustom"),
    ];

    private static readonly RazerAudioSource[] Sources =
        [RazerAudioSource.None, RazerAudioSource.Spdif, RazerAudioSource.Aux, RazerAudioSource.Bluetooth, RazerAudioSource.Usb];

    private static readonly RazerAncState[] AncStates =
        [RazerAncState.Off, RazerAncState.On, RazerAncState.Ambient];

    private readonly RazerHeadsetAudioConnection _connection;
    private readonly Action<string>? _diagnostic;
    private bool _isBusy;
    private string _errorText = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _firmwareText = "--";
    private string _batteryText = "--";
    private string _chargingText = "--";
    private int _selectedEqPresetIndex;
    private double _bass;
    private bool _micMuted;
    private double _micVolume;
    private int _selectedSourceIndex;
    private bool _noiseSuppression;
    private int _selectedAncIndex;
    private double _ancLevel = 1;
    private bool _hasEqBands;
    private bool _hasBass;
    private bool _hasAnc;

    public RazerHeadsetAudioViewModel(
        RazerHeadsetAudioConnection connection,
        Action<string>? diagnostic = null)
    {
        _connection = connection;
        _diagnostic = diagnostic;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => _connection.Name;
    public string CategoryText => AppStrings.Text(
        RazerHeadsetAudioService.GetCategory(_connection.ProductId) == OpenSynapse.Core.Devices.DeviceCategory.Speaker
            ? "RazerSpeakerCategory"
            : "BlackSharkHeadsetCategory");
    public string Identity => $"VID_1532 / PID_{_connection.ProductId:X4}";
    public string DevicePath => _connection.DevicePath;
    public string StatusText => AppStrings.Text("BlackSharkStatusConnected");

    public string FirmwareText { get => _firmwareText; private set => SetField(ref _firmwareText, value); }
    public string BatteryText { get => _batteryText; private set => SetField(ref _batteryText, value); }
    public string ChargingText { get => _chargingText; private set => SetField(ref _chargingText, value); }

    public IReadOnlyList<string> EqPresetOptions => Presets.Select(preset => AppStrings.Text(preset.Key)).ToArray();

    public int SelectedEqPresetIndex
    {
        get => _selectedEqPresetIndex;
        set => SetField(ref _selectedEqPresetIndex, Math.Clamp(value, 0, Presets.Length - 1));
    }

    public ObservableCollection<RazerHeadsetEqBandViewModel> EqBands { get; } = new();

    public Visibility EqBandsVisibility => _hasEqBands ? Visibility.Visible : Visibility.Collapsed;

    public double Bass
    {
        get => _bass;
        set
        {
            if (!SetField(ref _bass, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(BassText));
        }
    }

    public string BassText => $"{Math.Round(Bass)}";

    public Visibility BassVisibility => _hasBass ? Visibility.Visible : Visibility.Collapsed;

    public bool MicMuted
    {
        get => _micMuted;
        set => SetField(ref _micMuted, value);
    }

    public double MicVolume
    {
        get => _micVolume;
        set
        {
            if (!SetField(ref _micVolume, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(MicVolumeText));
        }
    }

    public string MicVolumeText => $"{Math.Round(MicVolume)}";

    public IReadOnlyList<string> AudioSourceOptions =>
    [
        AppStrings.Text("RazerHeadsetSourceNone"),
        AppStrings.Text("RazerHeadsetSourceSpdif"),
        AppStrings.Text("RazerHeadsetSourceAux"),
        AppStrings.Text("RazerHeadsetSourceBluetooth"),
        AppStrings.Text("RazerHeadsetSourceUsb"),
    ];

    public int SelectedSourceIndex
    {
        get => _selectedSourceIndex;
        set => SetField(ref _selectedSourceIndex, Math.Clamp(value, 0, Sources.Length - 1));
    }

    public bool NoiseSuppression
    {
        get => _noiseSuppression;
        set => SetField(ref _noiseSuppression, value);
    }

    public IReadOnlyList<string> AncOptions =>
    [
        AppStrings.Text("RazerHeadsetAncOff"),
        AppStrings.Text("RazerHeadsetAncOn"),
        AppStrings.Text("RazerHeadsetAncAmbient"),
    ];

    public int SelectedAncIndex
    {
        get => _selectedAncIndex;
        set
        {
            if (!SetField(ref _selectedAncIndex, Math.Clamp(value, 0, AncStates.Length - 1))) return;
            OnPropertyChanged(nameof(AncLevelVisibility));
        }
    }

    public double AncLevel
    {
        get => _ancLevel;
        set
        {
            if (!SetField(ref _ancLevel, Math.Clamp(value, 0, 10))) return;
            OnPropertyChanged(nameof(AncLevelText));
        }
    }

    public string AncLevelText => $"{Math.Round(AncLevel)} / 10";
    public Visibility AncLevelVisibility => _selectedAncIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AncVisibility => _hasAnc ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
        }
    }

    public bool CanEdit => !IsBusy;
    public string ErrorText { get => _errorText; private set => SetField(ref _errorText, value); }
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
            var state = await OpenService().ReadStateAsync(cancellationToken);
            ApplyState(state);
            ErrorText = state.Errors.Count > 0 && !HasAnyValue(state)
                ? AppStrings.Text("RazerHeadsetReadFailed")
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task ApplyEqPresetAsync(CancellationToken cancellationToken = default)
    {
        var preset = Presets[SelectedEqPresetIndex].Value;
        return RunWriteAsync((service, token) => service.SetEqPresetAsync(preset, token), cancellationToken);
    }

    public Task ApplyEqBandsAsync(CancellationToken cancellationToken = default)
    {
        var bands = EqBands.Select(band => (byte)Math.Round(band.Level)).ToArray();
        return bands.Length is 8 or 10
            ? RunWriteAsync((service, token) => service.SetEqBandsAsync(bands, token), cancellationToken)
            : Task.CompletedTask;
    }

    public Task ApplyBassAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync((service, token) => service.SetBassAsync((byte)Math.Round(Bass), token), cancellationToken);

    public Task ApplyMicrophoneAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(async (service, token) =>
        {
            await service.SetMicMuteAsync(MicMuted, token);
            await service.SetMicVolumeAsync((byte)Math.Round(MicVolume), token);
        }, cancellationToken);

    public Task ApplySourceAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            (service, token) => service.SetAudioSourceAsync(Sources[SelectedSourceIndex], token),
            cancellationToken);

    public Task ApplyNoiseSuppressionAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync((service, token) => service.SetNoiseSuppressionAsync(NoiseSuppression, token), cancellationToken);

    public Task ApplyAncAsync(CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            (service, token) => service.SetAncAsync(AncStates[SelectedAncIndex], (byte)Math.Round(AncLevel), token),
            cancellationToken);

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    private RazerHeadsetAudioService OpenService() =>
        new(new RazerFeatureTransport(), _connection.DevicePath, TimeSpan.FromMilliseconds(20), _diagnostic);

    private async Task RunWriteAsync(
        Func<RazerHeadsetAudioService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return;
        try
        {
            await write(OpenService(), cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            ErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyState(RazerHeadsetAudioState state)
    {
        if (state.Firmware is { } firmware) FirmwareText = firmware.ToString();
        if (state.BatteryPercent is byte battery) BatteryText = $"{battery}%";
        if (state.Charging is bool charging)
        {
            ChargingText = AppStrings.Text(charging ? "BlackSharkCharging" : "BlackSharkNotCharging");
        }

        if (state.EqPreset is byte preset)
        {
            var index = Array.FindIndex(Presets, item => item.Value == preset);
            if (index >= 0)
            {
                SelectedEqPresetIndex = index;
            }
        }

        if (state.EqBands is { Count: 8 or 10 } bands)
        {
            var frequencies = bands.Count == 10
                ? RazerHeadsetAudioProtocol.TenBandFrequencies
                : RazerHeadsetAudioProtocol.EightBandFrequencies;
            EqBands.Clear();
            for (var index = 0; index < bands.Count; index++)
            {
                EqBands.Add(new RazerHeadsetEqBandViewModel(frequencies[index]) { Level = bands[index] });
            }

            _hasEqBands = true;
            OnPropertyChanged(nameof(EqBandsVisibility));
        }

        if (state.Bass is byte bass)
        {
            Bass = bass;
            _hasBass = true;
            OnPropertyChanged(nameof(BassVisibility));
        }

        if (state.MicMuted is bool muted) MicMuted = muted;
        if (state.MicVolume is byte volume) MicVolume = volume;
        if (state.AudioSource is RazerAudioSource source)
        {
            var index = Array.IndexOf(Sources, source);
            if (index >= 0)
            {
                SelectedSourceIndex = index;
            }
        }
        if (state.NoiseSuppression is bool noise) NoiseSuppression = noise;
        if (state.AncState is RazerAncState anc)
        {
            var index = Array.IndexOf(AncStates, anc);
            if (index >= 0)
            {
                SelectedAncIndex = index;
            }

            _hasAnc = true;
            OnPropertyChanged(nameof(AncVisibility));
        }
        if (state.AncLevel is byte level) AncLevel = level;
    }

    private static bool HasAnyValue(RazerHeadsetAudioState state) =>
        state.BatteryPercent is not null || state.EqPreset is not null || state.EqBands is not null ||
        state.Bass is not null || state.MicMuted is not null || state.AudioSource is not null;

    private bool BeginOperation()
    {
        if (IsBusy) return false;
        ErrorText = string.Empty;
        OperationStatusText = string.Empty;
        IsBusy = true;
        return true;
    }

    private static bool IsExpectedException(Exception exception) =>
        exception is IOException or InvalidOperationException or ArgumentOutOfRangeException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(ErrorText)) OnPropertyChanged(nameof(HasError));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
