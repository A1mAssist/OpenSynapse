using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

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
    private bool _hasLighting;
    private ThxHeadsetStatus _thxStatus = new(ThxHeadsetAvailability.UnsupportedDevice, null);
    private double _brightness = 100;
    private int _selectedEffectIndex;
    private Color _primaryColor = Color.FromArgb(255, 0, 255, 0);
    private Color _secondaryColor = Color.FromArgb(255, 255, 0, 0);

    private static readonly RazerChromaEffect[] LightingEffects =
        [RazerChromaEffect.Off, RazerChromaEffect.Static, RazerChromaEffect.Breathing, RazerChromaEffect.Spectrum];

    public RazerHeadsetAudioViewModel(
        RazerHeadsetAudioConnection connection,
        Action<string>? diagnostic = null,
        OpenRazerKrakenViewModel? lighting = null)
    {
        _connection = connection;
        _diagnostic = diagnostic;
        Lighting = lighting;
        if (connection.ProductId == ThxHeadsetAdmission.SupportedProductId)
        {
            _thxStatus = ThxHeadsetAdmission.Check(connection.ProductId);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Some Kraken models expose their 37-byte lighting on a second collection. Those
    /// devices get one card that carries both the audio settings and that lighting.
    /// </summary>
    public OpenRazerKrakenViewModel? Lighting { get; }

    public Microsoft.UI.Xaml.Visibility LightingVisibility => Lighting is null
        ? Microsoft.UI.Xaml.Visibility.Collapsed
        : Microsoft.UI.Xaml.Visibility.Visible;

    /// <summary>
    /// THX is an APO plus service on this headset, so the card only appears for the
    /// model whose installer we have pinned. Missing pieces get an install entry.
    /// </summary>
    public Visibility ThxVisibility => _connection.ProductId == ThxHeadsetAdmission.SupportedProductId
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ThxStatusText => _thxStatus.ErrorCode switch
    {
        "ThxServiceMissing" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
        "ThxNativeMissing" => AppStrings.Text("RazerHeadsetThxNativeMissing"),
        "ThxDownloadFailed" or "ThxDownloadVerificationFailed" =>
            AppStrings.Text("RazerHeadsetThxDownloadFailed"),
        _ when _thxStatus.Availability == ThxHeadsetAvailability.Available =>
            AppStrings.Text("RazerHeadsetThxReady"),
        _ => string.Empty,
    };

    public Visibility ThxInstallVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.ServiceMissing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility ThxDownloadVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.NativeMissing
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool CanDownloadThxComponent =>
        !IsBusy && _thxStatus.Availability == ThxHeadsetAvailability.NativeMissing;

    /// <summary>
    /// Mic-body lighting (the Seiren Emote matrix) uses the shared class 0x10 Chroma
    /// commands. Devices that do not answer keep the whole card hidden.
    /// </summary>
    public Microsoft.UI.Xaml.Visibility MatrixLightingVisibility =>
        _hasLighting ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public IReadOnlyList<string> LightingEffectOptions =>
    [
        AppStrings.Text("RazerLightingEffectOff"),
        AppStrings.Text("RazerLightingEffectStatic"),
        AppStrings.Text("RazerLightingEffectBreathing"),
        AppStrings.Text("RazerLightingEffectSpectrum"),
    ];

    public int SelectedLightingEffectIndex
    {
        get => _selectedEffectIndex;
        set
        {
            if (!SetField(ref _selectedEffectIndex, Math.Clamp(value, 0, LightingEffects.Length - 1))) return;
            OnPropertyChanged(nameof(SecondaryColorVisibility));
        }
    }

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (!SetField(ref _brightness, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(BrightnessText));
        }
    }

    public string BrightnessText => $"{Math.Round(Brightness)}%";

    public Color PrimaryColor
    {
        get => _primaryColor;
        set => SetField(ref _primaryColor, value);
    }

    public Color SecondaryColor
    {
        get => _secondaryColor;
        set => SetField(ref _secondaryColor, value);
    }

    public Microsoft.UI.Xaml.Visibility SecondaryColorVisibility =>
        LightingEffects[_selectedEffectIndex] == RazerChromaEffect.Breathing
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string Name => _connection.Name;
    public string CategoryText => AppStrings.Text(
        RazerHeadsetAudioService.GetCategory(_connection.ProductId) switch
        {
            OpenSynapse.Core.Devices.DeviceCategory.Speaker => "RazerSpeakerCategory",
            OpenSynapse.Core.Devices.DeviceCategory.Microphone => "RazerMicrophoneCategory",
            _ => "BlackSharkHeadsetCategory",
        });
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
            OnPropertyChanged(nameof(CanDownloadThxComponent));
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
            ApplyMatrixLighting(await OpenChromaService().ReadStateAsync(cancellationToken));
            ErrorText = state.Errors.Count > 0 && !HasAnyValue(state) && !_hasLighting
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
            RefreshThxStatus();
        }
    }

    public async Task InstallThxNativeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDownloadThxComponent || !BeginOperation()) return;
        try
        {
            OperationStatusText = AppStrings.Text("RazerHeadsetThxDownloading");
            _thxStatus = await ThxHeadsetAdmission.InstallNativeComponentAsync(cancellationToken);
            ErrorText = _thxStatus.ErrorCode is "ThxDownloadFailed" or "ThxDownloadVerificationFailed"
                ? AppStrings.Text("RazerHeadsetThxDownloadFailed")
                : string.Empty;
            OperationStatusText = _thxStatus.Availability == ThxHeadsetAvailability.Available
                ? AppStrings.Text("RazerHeadsetThxReady")
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
            NotifyThxStatus();
        }
    }

    private void RefreshThxStatus()
    {
        _thxStatus = ThxHeadsetAdmission.Check(_connection.ProductId);
        NotifyThxStatus();
    }

    private void NotifyThxStatus()
    {
        OnPropertyChanged(nameof(ThxStatusText));
        OnPropertyChanged(nameof(ThxInstallVisibility));
        OnPropertyChanged(nameof(ThxDownloadVisibility));
        OnPropertyChanged(nameof(CanDownloadThxComponent));
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

    public Task ApplyMatrixBrightnessAsync(CancellationToken cancellationToken = default) =>
        RunChromaWriteAsync(
            (service, token) => service.SetBrightnessAsync((byte)Math.Round(Brightness), token),
            cancellationToken);

    public Task ApplyMatrixLightingAsync(CancellationToken cancellationToken = default) =>
        RunChromaWriteAsync(async (service, token) =>
        {
            var effect = LightingEffects[SelectedLightingEffectIndex];
            var colors = effect switch
            {
                RazerChromaEffect.Static => new byte[] { PrimaryColor.R, PrimaryColor.G, PrimaryColor.B },
                RazerChromaEffect.Breathing =>
                    [PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, SecondaryColor.R, SecondaryColor.G, SecondaryColor.B],
                _ => [],
            };
            await service.SetEffectAsync(effect, colors, token);
        }, cancellationToken);

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

    private RazerChromaStandardService OpenChromaService() =>
        new(new RazerFeatureTransport(), _connection.DevicePath, TimeSpan.FromMilliseconds(20), _diagnostic);

    private async Task RunChromaWriteAsync(
        Func<RazerChromaStandardService, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return;
        try
        {
            await write(OpenChromaService(), cancellationToken);
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

    private void ApplyMatrixLighting(RazerChromaStandardState state)
    {
        if (state.BrightnessPercent is byte brightness)
        {
            Brightness = brightness;
            _hasLighting = true;
        }

        if (state.Effect is RazerChromaEffect effect && LightingEffects.Contains(effect))
        {
            SelectedLightingEffectIndex = Array.IndexOf(LightingEffects, effect);
            _hasLighting = true;
        }

        if (state.Colors.Count >= 3)
        {
            PrimaryColor = Color.FromArgb(255, state.Colors[0], state.Colors[1], state.Colors[2]);
        }

        if (state.Colors.Count >= 6)
        {
            SecondaryColor = Color.FromArgb(255, state.Colors[3], state.Colors[4], state.Colors[5]);
        }

        OnPropertyChanged(nameof(MatrixLightingVisibility));
    }
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
