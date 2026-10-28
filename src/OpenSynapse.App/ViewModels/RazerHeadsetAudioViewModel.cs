using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;
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

public sealed class RazerHeadsetThxBandViewModel : INotifyPropertyChanged
{
    private double _level;

    public RazerHeadsetThxBandViewModel(int frequency)
    {
        Label = frequency >= 1_000 ? $"{frequency / 1_000d:0.#} kHz" : $"{frequency} Hz";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; }
    public string LevelText => $"{Level:+0.0;-0.0;0.0} dB";

    public double Level
    {
        get => _level;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, -12, 12) : 0;
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

public sealed class RazerHeadsetAudioViewModel : INotifyPropertyChanged, IAsyncDisposable
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

    private static readonly (byte Value, string Key)[] MxicPresets =
    [
        (0x00, "RazerHeadsetPresetDefault"),
        (0x07, "RazerHeadsetPresetGame"),
        (0x08, "RazerHeadsetPresetMusic"),
        (0x09, "RazerHeadsetPresetMovie"),
        (0xFF, "RazerHeadsetPresetCustom"),
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
    private bool _dnd;
    private bool _autoPause;
    private bool _gestureControl;
    private bool _dolby;
    private bool _nightMode;
    private bool _voicePrompts;
    private double _micDeEsser;
    private double _micLimiterDb;
    private int _micReverbRoomModeIndex;
    private double _micReverbIntensity;
    private bool _hasDnd;
    private bool _hasAutoPause;
    private bool _hasGestureControl;
    private bool _hasDolby;
    private bool _hasNightMode;
    private bool _hasVoicePrompts;
    private bool _hasMicDeEsser;
    private bool _hasMicLimiter;
    private bool _hasMicReverb;
    private bool _hapticsEnabled;
    private double _hapticsStrength;
    private bool _hasHaptics;
    private static readonly RazerMicReverbRoomMode[] MicReverbModes =
        Enum.GetValues<RazerMicReverbRoomMode>();
    private int _selectedAncIndex;
    private double _ancLevel = 1;
    private bool _hasEqBands;
    private bool _hasBass;
    private bool _hasAnc;
    private bool _hasLighting;
    private ThxHeadsetStatus _thxStatus = new(ThxHeadsetAvailability.UnsupportedDevice, null);
    private ThxAudioState? _thxState;
    private WindowsThxHeadsetController? _thxController;
    private string _thxErrorText = string.Empty;
    private int _thxSelectedPresetIndex;
    private bool _thxSpatialEnabled;
    private bool _thxEqualizerEnabled;
    private bool _thxNormalizationEnabled;
    private int _thxNormalizationLevel;
    private bool _thxVoiceClarityEnabled;
    private int _thxVoiceClarityLevel;
    private bool _thxSidetoneEnabled;
    private int _thxSidetoneLevel;
    private double _brightness = 100;
    private int _selectedEffectIndex;
    private Color _primaryColor = Color.FromArgb(255, 0, 255, 0);
    private Color _secondaryColor = Color.FromArgb(255, 255, 0, 0);

    private static readonly RazerChromaEffect[] LightingEffects =
        [RazerChromaEffect.Off, RazerChromaEffect.Static, RazerChromaEffect.Breathing, RazerChromaEffect.Spectrum];

    private bool IsMxic => _connection.Transport == RazerHeadsetAudioTransport.Mxic64;
    private (byte Value, string Key)[] ActivePresets => IsMxic ? MxicPresets : Presets;

    public RazerHeadsetAudioViewModel(
        RazerHeadsetAudioConnection connection,
        Action<string>? diagnostic = null,
        OpenRazerKrakenViewModel? lighting = null)
    {
        _connection = connection;
        _diagnostic = diagnostic;
        Lighting = lighting;
        if (ThxHeadsetAdmission.IsSupported(connection.ProductId))
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

    public Microsoft.UI.Xaml.Visibility LightingVisibility => IsMxic || Lighting is null
        ? Microsoft.UI.Xaml.Visibility.Collapsed
        : Microsoft.UI.Xaml.Visibility.Visible;

    /// <summary>
    /// THX is an APO plus service on this headset, so the card only appears for the
    /// model whose installer we have pinned. Missing pieces get an install entry.
    /// </summary>
    public Visibility ThxVisibility => ThxHeadsetAdmission.IsSupported(_connection.ProductId)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ThxInstallerUri => ThxHeadsetAdmission.IsSupported(_connection.ProductId)
        ? ThxHeadsetAdmission.GetServiceInstallerUri(_connection.ProductId)
        : string.Empty;

    public Visibility ThxInstallerVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available
            ? Visibility.Collapsed
            : ThxVisibility;

    public string ThxGameProfilesUri =>
        ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.GameProfilesUri ?? string.Empty;

    public Visibility ThxGameProfilesVisibility =>
        ThxGameProfilesUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ThxPatcherUri =>
        ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.PatcherUri ?? string.Empty;

    public Visibility ThxPatcherVisibility =>
        ThxPatcherUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ThxStatusText => !string.IsNullOrEmpty(_thxErrorText)
        ? _thxErrorText
        : _thxStatus.ErrorCode switch
        {
            "ThxServiceMissing" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
            "ThxNativeMissing" => AppStrings.Text("RazerHeadsetThxNativeMissing"),
            "ThxDownloadFailed" or "ThxDownloadVerificationFailed" =>
                AppStrings.Text("RazerHeadsetThxDownloadFailed"),
            _ when _thxStatus.Availability == ThxHeadsetAvailability.Available =>
                AppStrings.Text("RazerHeadsetThxReady"),
            _ => string.Empty,
        };

    public Visibility ThxDownloadVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.NativeMissing ||
        _thxStatus.ErrorCode is "ThxNativeMissing"
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility ThxControlsVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available && _thxState is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool CanDownloadThxComponent =>
        !IsBusy && _thxStatus.Availability == ThxHeadsetAvailability.NativeMissing;

    public bool CanApplyThx => !IsBusy && _thxState is not null &&
        _thxStatus.Availability == ThxHeadsetAvailability.Available;

    public bool ThxSpatialEnabled
    {
        get => _thxSpatialEnabled;
        set => SetField(ref _thxSpatialEnabled, value);
    }

    public bool ThxEqualizerEnabled
    {
        get => _thxEqualizerEnabled;
        set => SetField(ref _thxEqualizerEnabled, value);
    }

    public IReadOnlyList<string> ThxPresetOptions =>
        _thxState?.Presets.Select(preset => preset.Name).ToArray() ?? [];

    public int SelectedThxPresetIndex
    {
        get => _thxSelectedPresetIndex;
        set => SetField(ref _thxSelectedPresetIndex,
            Math.Clamp(value, 0, Math.Max(0, (_thxState?.Presets.Count ?? 0) - 1)));
    }

    public ObservableCollection<RazerHeadsetThxBandViewModel> ThxBands { get; } = new();

    public Visibility ThxBandsVisibility =>
        _thxState is { EqualizerBands.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public bool ThxNormalizationEnabled
    {
        get => _thxNormalizationEnabled;
        set => SetField(ref _thxNormalizationEnabled, value);
    }

    public int ThxNormalizationLevel
    {
        get => _thxNormalizationLevel;
        set
        {
            if (!SetField(ref _thxNormalizationLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxNormalizationText));
        }
    }

    public string ThxNormalizationText => $"{ThxNormalizationLevel}%";
    public Visibility ThxNormalizationVisibility =>
        _thxState?.SoundNormalization is not null ? Visibility.Visible : Visibility.Collapsed;

    public bool ThxVoiceClarityEnabled
    {
        get => _thxVoiceClarityEnabled;
        set => SetField(ref _thxVoiceClarityEnabled, value);
    }

    public int ThxVoiceClarityLevel
    {
        get => _thxVoiceClarityLevel;
        set
        {
            if (!SetField(ref _thxVoiceClarityLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxVoiceClarityText));
        }
    }

    public string ThxVoiceClarityText => $"{ThxVoiceClarityLevel}%";
    public Visibility ThxVoiceClarityVisibility =>
        _thxState is not null ? Visibility.Visible : Visibility.Collapsed;

    public bool ThxSidetoneEnabled
    {
        get => _thxSidetoneEnabled;
        set => SetField(ref _thxSidetoneEnabled, value);
    }

    public int ThxSidetoneLevel
    {
        get => _thxSidetoneLevel;
        set
        {
            if (!SetField(ref _thxSidetoneLevel, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(ThxSidetoneText));
        }
    }

    public string ThxSidetoneText => $"{ThxSidetoneLevel}%";
    public Visibility ThxSidetoneVisibility =>
        _thxState?.Sidetone is not null ? Visibility.Visible : Visibility.Collapsed;

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

    public IReadOnlyList<string> EqPresetOptions =>
        ActivePresets.Select(preset => AppStrings.Text(preset.Key)).ToArray();

    public int SelectedEqPresetIndex
    {
        get => _selectedEqPresetIndex;
        set => SetField(ref _selectedEqPresetIndex, Math.Clamp(value, 0, ActivePresets.Length - 1));
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
    public Visibility MicVolumeVisibility => IsMxic ? Visibility.Collapsed : Visibility.Visible;

    public IReadOnlyList<string> AudioSourceOptions =>
    [
        AppStrings.Text("RazerHeadsetSourceNone"),
        AppStrings.Text("RazerHeadsetSourceSpdif"),
        AppStrings.Text("RazerHeadsetSourceAux"),
        AppStrings.Text("RazerHeadsetSourceBluetooth"),
        AppStrings.Text("RazerHeadsetSourceUsb"),
    ];

    public Visibility AudioSourceVisibility => IsMxic ? Visibility.Collapsed : Visibility.Visible;

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

    public Visibility NoiseSuppressionVisibility => IsMxic ? Visibility.Collapsed : Visibility.Visible;

    public bool Dnd { get => _dnd; set => SetField(ref _dnd, value); }
    public Visibility DndVisibility => _hasDnd ? Visibility.Visible : Visibility.Collapsed;

    public bool AutoPause { get => _autoPause; set => SetField(ref _autoPause, value); }
    public Visibility AutoPauseVisibility => _hasAutoPause ? Visibility.Visible : Visibility.Collapsed;

    public bool GestureControl { get => _gestureControl; set => SetField(ref _gestureControl, value); }
    public Visibility GestureControlVisibility => _hasGestureControl ? Visibility.Visible : Visibility.Collapsed;

    public bool Dolby { get => _dolby; set => SetField(ref _dolby, value); }
    public Visibility DolbyVisibility => _hasDolby ? Visibility.Visible : Visibility.Collapsed;

    public bool NightMode { get => _nightMode; set => SetField(ref _nightMode, value); }
    public Visibility NightModeVisibility => _hasNightMode ? Visibility.Visible : Visibility.Collapsed;

    public bool VoicePrompts { get => _voicePrompts; set => SetField(ref _voicePrompts, value); }
    public Visibility VoicePromptsVisibility => _hasVoicePrompts ? Visibility.Visible : Visibility.Collapsed;

    public double MicDeEsser
    {
        get => _micDeEsser;
        set
        {
            if (!SetField(ref _micDeEsser, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(MicDeEsserText));
        }
    }

    public string MicDeEsserText => $"{Math.Round(MicDeEsser)}";
    public Visibility MicDeEsserVisibility => _hasMicDeEsser ? Visibility.Visible : Visibility.Collapsed;

    public double MicLimiterDb
    {
        get => _micLimiterDb;
        set
        {
            if (!SetField(ref _micLimiterDb, Math.Clamp(value, -60, 0))) return;
            OnPropertyChanged(nameof(MicLimiterText));
        }
    }

    public string MicLimiterText => $"{Math.Round(MicLimiterDb)} dB";
    public Visibility MicLimiterVisibility => _hasMicLimiter ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> MicReverbOptions =>
    [
        AppStrings.Text("RazerHeadsetReverbSmall"),
        AppStrings.Text("RazerHeadsetReverbMedium"),
        AppStrings.Text("RazerHeadsetReverbLarge"),
        AppStrings.Text("RazerHeadsetReverbPlate"),
    ];

    public int SelectedMicReverbIndex
    {
        get => _micReverbRoomModeIndex;
        set => SetField(ref _micReverbRoomModeIndex, Math.Clamp(value, 0, MicReverbModes.Length - 1));
    }

    public double MicReverbIntensity
    {
        get => _micReverbIntensity;
        set
        {
            if (!SetField(ref _micReverbIntensity, Math.Clamp(value, 0, 100))) return;
            OnPropertyChanged(nameof(MicReverbIntensityText));
        }
    }

    public string MicReverbIntensityText => $"{Math.Round(MicReverbIntensity)}";
    public Visibility MicReverbVisibility => _hasMicReverb ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AdvancedAudioVisibility =>
        _hasDnd || _hasAutoPause || _hasGestureControl || _hasDolby || _hasNightMode || _hasVoicePrompts
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility MicProcessingVisibility =>
        _hasMicDeEsser || _hasMicLimiter || _hasMicReverb
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool HapticsEnabled { get => _hapticsEnabled; set => SetField(ref _hapticsEnabled, value); }

    public double HapticsStrength
    {
        get => _hapticsStrength;
        set
        {
            if (!SetField(ref _hapticsStrength, Math.Clamp(value, 0, ushort.MaxValue))) return;
            OnPropertyChanged(nameof(HapticsStrengthText));
        }
    }

    public string HapticsStrengthText => $"{Math.Round(HapticsStrength)}";
    public Visibility HapticsVisibility => _hasHaptics ? Visibility.Visible : Visibility.Collapsed;

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
            OnPropertyChanged(nameof(CanApplyThx));
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
            var state = IsMxic
                ? await ReadMxicStateAsync(cancellationToken)
                : await OpenService().ReadStateAsync(cancellationToken);
            ApplyState(state);
            if (!IsMxic)
            {
                ApplyMatrixLighting(await OpenChromaService().ReadStateAsync(cancellationToken));
            }

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
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await RefreshThxAsync(cancellationToken);
        }
    }

    public async Task InstallThxNativeAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDownloadThxComponent || !BeginOperation()) return;
        try
        {
            OperationStatusText = AppStrings.Text("RazerHeadsetThxDownloading");
            _thxStatus = await ThxHeadsetAdmission.InstallNativeComponentAsync(
                _connection.ProductId,
                cancellationToken);
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
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await RefreshThxAsync(cancellationToken);
        }
    }

    public Task ApplyThxSpatialAsync(CancellationToken cancellationToken = default) =>
        RunThxAsync(
            (controller, token) => controller.SetSpatialAsync(
                ThxAudioEndpoint.Headphones, ThxSpatialEnabled, token),
            cancellationToken);

    public Task ApplyThxPresetAsync(CancellationToken cancellationToken = default)
    {
        if (_thxState?.Presets.ElementAtOrDefault(SelectedThxPresetIndex) is not { } preset)
        {
            return Task.CompletedTask;
        }
        var gains = ThxBands.Select(band => band.Level).ToArray();
        return RunThxAsync(
            (controller, token) => controller.SelectPresetAsync(
                ThxAudioEndpoint.Headphones, preset.Id, gains, token),
            cancellationToken);
    }

    public Task ApplyThxBandsAsync(CancellationToken cancellationToken = default)
    {
        var gains = ThxBands.Select(band => band.Level).ToArray();
        return RunThxAsync(
            (controller, token) => controller.SetGainsAsync(
                ThxAudioEndpoint.Headphones, gains, token),
            cancellationToken);
    }

    public Task ApplyThxNormalizationAsync(CancellationToken cancellationToken = default) =>
        RunThxAsync(
            (controller, token) => controller.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.SoundNormalization,
                ThxNormalizationEnabled,
                ThxNormalizationLevel,
                token),
            cancellationToken);

    public Task ApplyThxVoiceClarityAsync(CancellationToken cancellationToken = default) =>
        RunThxAsync(
            (controller, token) => controller.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.VoiceClarity,
                ThxVoiceClarityEnabled,
                ThxVoiceClarityLevel,
                token),
            cancellationToken);

    public Task ApplyThxSidetoneAsync(CancellationToken cancellationToken = default) =>
        RunThxAsync(
            (controller, token) => controller.SetEnhancementAsync(
                ThxAudioEndpoint.Headphones,
                ThxAudioEnhancementKind.Sidetone,
                ThxSidetoneEnabled,
                ThxSidetoneLevel,
                token),
            cancellationToken);

    private WindowsThxHeadsetController ThxController =>
        _thxController ??= new WindowsThxHeadsetController(
            _connection.ProductId,
            _connection.ContainerId);

    private async Task RefreshThxAsync(CancellationToken cancellationToken)
    {
        if (!ThxHeadsetAdmission.IsSupported(_connection.ProductId))
        {
            return;
        }

        _thxStatus = ThxHeadsetAdmission.Check(_connection.ProductId);
        if (_thxStatus.Availability != ThxHeadsetAvailability.Available)
        {
            _thxState = null;
            _thxErrorText = ThxMessage(_thxStatus.ErrorCode);
            NotifyThxStatus();
            return;
        }

        try
        {
            ApplyThxStatus(await ThxController.ReadAsync(
                ThxAudioEndpoint.Headphones,
                cancellationToken).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _thxState = null;
            _thxErrorText = exception.Message;
            NotifyThxStatus();
        }
    }

    private async Task RunThxAsync(
        Func<WindowsThxHeadsetController, CancellationToken, Task<ThxAudioStatus>> action,
        CancellationToken cancellationToken)
    {
        if (!CanApplyThx || !BeginOperation()) return;
        try
        {
            var status = await action(ThxController, cancellationToken).ConfigureAwait(true);
            ApplyThxStatus(status);
            ErrorText = string.Empty;
            OperationStatusText = status.IsAvailable
                ? AppStrings.Text("BlackSharkSaved")
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _thxErrorText = exception.Message;
            OperationStatusText = string.Empty;
        }
        finally
        {
            IsBusy = false;
            NotifyThxStatus();
        }
    }

    private void ApplyThxStatus(ThxAudioStatus status)
    {
        if (status.State is { } state)
        {
            ApplyThxState(state);
            _thxStatus = new ThxHeadsetStatus(ThxHeadsetAvailability.Available, null);
            _thxErrorText = string.Empty;
        }
        else
        {
            _thxState = null;
            _thxStatus = new ThxHeadsetStatus(
                status.Error is "ThxServiceMissing"
                    ? ThxHeadsetAvailability.ServiceMissing
                    : ThxHeadsetAvailability.NativeMissing,
                status.Error);
            _thxErrorText = ThxMessage(status.Error);
        }
        NotifyThxStatus();
    }

    private void ApplyThxState(ThxAudioState state)
    {
        _thxState = state;
        _thxSpatialEnabled = state.SpatialEnabled;
        _thxEqualizerEnabled = state.EqualizerEnabled;
        _thxSelectedPresetIndex = Math.Max(0, state.Presets
            .Select((preset, index) => (preset, index))
            .FirstOrDefault(item => item.preset.Id == state.SelectedPresetId).index);

        ThxBands.Clear();
        foreach (var band in state.EqualizerBands)
        {
            ThxBands.Add(new RazerHeadsetThxBandViewModel(band.FrequencyHertz)
            {
                Level = band.GainDb,
            });
        }

        _thxNormalizationEnabled = state.SoundNormalization?.Enabled ?? false;
        _thxNormalizationLevel = state.SoundNormalization?.LevelPercent ?? 0;
        _thxVoiceClarityEnabled = state.VoiceClarity.Enabled;
        _thxVoiceClarityLevel = state.VoiceClarity.LevelPercent;
        _thxSidetoneEnabled = state.Sidetone?.Enabled ?? false;
        _thxSidetoneLevel = state.Sidetone?.LevelPercent ?? 0;
        OnPropertyChanged(string.Empty);
    }

    private static string ThxMessage(string? error) => error switch
    {
        "ThxServiceMissing" => AppStrings.Text("RazerHeadsetThxServiceMissing"),
        "ThxNativeMissing" => AppStrings.Text("RazerHeadsetThxNativeMissing"),
        "ThxContainerMissing" => AppStrings.Text("RazerHeadsetThxContainerMissing"),
        "ThxHelperMissing" or "ThxHelperStartFailed" or "ThxHelperExited" =>
            AppStrings.Text("RazerHeadsetThxHelperFailed"),
        _ when error?.StartsWith("ThxHelperFailed", StringComparison.Ordinal) == true =>
            AppStrings.Text("RazerHeadsetThxHelperFailed"),
        _ => AppStrings.Text("RazerHeadsetThxFailed"),
    };

    private void NotifyThxStatus()
    {
        OnPropertyChanged(nameof(ThxStatusText));
        OnPropertyChanged(nameof(ThxInstallerVisibility));
        OnPropertyChanged(nameof(ThxDownloadVisibility));
        OnPropertyChanged(nameof(ThxControlsVisibility));
        OnPropertyChanged(nameof(CanDownloadThxComponent));
        OnPropertyChanged(nameof(CanApplyThx));
        OnPropertyChanged(nameof(ThxPresetOptions));
        OnPropertyChanged(nameof(ThxBandsVisibility));
        OnPropertyChanged(nameof(ThxNormalizationVisibility));
        OnPropertyChanged(nameof(ThxVoiceClarityVisibility));
        OnPropertyChanged(nameof(ThxSidetoneVisibility));
    }

    public Task ApplyEqPresetAsync(CancellationToken cancellationToken = default)
    {
        var preset = ActivePresets[SelectedEqPresetIndex].Value;
        return IsMxic
            ? RunMxicWriteAsync((service, token) => service.SetEqPresetAsync(preset, token), cancellationToken)
            : RunWriteAsync((service, token) => service.SetEqPresetAsync(preset, token), cancellationToken);
    }

    public Task ApplyEqBandsAsync(CancellationToken cancellationToken = default)
    {
        var bands = EqBands.Select(band => (byte)Math.Round(band.Level)).ToArray();
        return bands.Length switch
        {
            10 when IsMxic => RunMxicWriteAsync(
                (service, token) => service.SetEqBandsAsync(bands, token), cancellationToken),
            10 or 8 when !IsMxic => RunWriteAsync(
                (service, token) => service.SetEqBandsAsync(bands, token), cancellationToken),
            _ => Task.CompletedTask,
        };
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
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync((service, token) => service.SetBassAsync((byte)Math.Round(Bass), token), cancellationToken);

    public Task ApplyMicrophoneAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? RunMxicWriteAsync((service, token) => service.SetMicMuteAsync(MicMuted, token), cancellationToken)
            : RunWriteAsync(async (service, token) =>
            {
                await service.SetMicMuteAsync(MicMuted, token);
                await service.SetMicVolumeAsync((byte)Math.Round(MicVolume), token);
            }, cancellationToken);

    public Task ApplySourceAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetAudioSourceAsync(Sources[SelectedSourceIndex], token),
                cancellationToken);

    public Task ApplyHapticsAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetHapticsAsync(
                    HapticsEnabled, (ushort)Math.Round(HapticsStrength), token),
                cancellationToken);

    public async Task ApplyAdvancedAsync(CancellationToken cancellationToken = default)
    {
        await ApplyDndAsync(cancellationToken);
        await ApplyAutoPauseAsync(cancellationToken);
        await ApplyGestureControlAsync(cancellationToken);
        await ApplyDolbyAsync(cancellationToken);
        await ApplyNightModeAsync(cancellationToken);
        await ApplyVoicePromptsAsync(cancellationToken);
    }

    public async Task ApplyMicProcessingAsync(CancellationToken cancellationToken = default)
    {
        await ApplyMicDeEsserAsync(cancellationToken);
        await ApplyMicLimiterAsync(cancellationToken);
        await ApplyMicReverbAsync(cancellationToken);
    }

    public Task ApplyDndAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync((service, token) => service.SetAudioDndAsync(Dnd, token), cancellationToken);

    public Task ApplyAutoPauseAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync((service, token) => service.SetAudioAutoPauseAsync(AutoPause, token), cancellationToken);

    public Task ApplyGestureControlAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetAudioGestureControlAsync(GestureControl, token),
                cancellationToken);

    public Task ApplyDolbyAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync((service, token) => service.SetDolbyStateAsync(Dolby, token), cancellationToken);

    public Task ApplyNightModeAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync((service, token) => service.SetEqNightModeAsync(NightMode, token), cancellationToken);

    public Task ApplyVoicePromptsAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetAudioVoicePromptsAsync(
                    VoicePrompts, cancellationToken: token),
                cancellationToken);

    public Task ApplyMicDeEsserAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetMicDeEsserAsync(
                    (byte)Math.Round(MicDeEsser), token),
                cancellationToken);

    public Task ApplyMicLimiterAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetMicLimiterAsync(
                    (sbyte)Math.Round(MicLimiterDb), token),
                cancellationToken);

    public Task ApplyMicReverbAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetMicReverbAsync(
                    MicReverbModes[SelectedMicReverbIndex],
                    (byte)Math.Round(MicReverbIntensity),
                    token),
                cancellationToken);

    public Task ApplyNoiseSuppressionAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync((service, token) => service.SetNoiseSuppressionAsync(NoiseSuppression, token), cancellationToken);

    public Task ApplyAncAsync(CancellationToken cancellationToken = default) =>
        IsMxic
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetAncAsync(AncStates[SelectedAncIndex], (byte)Math.Round(AncLevel), token),
                cancellationToken);

    public void RefreshLocalization() => OnPropertyChanged(string.Empty);

    public async ValueTask DisposeAsync()
    {
        var controller = _thxController;
        _thxController = null;
        if (controller is not null)
        {
            await controller.DisposeAsync();
        }
    }

    private RazerHeadsetAudioService OpenService() =>
        new(new RazerFeatureTransport(), _connection.DevicePath, TimeSpan.FromMilliseconds(20), _diagnostic);

    private RazerMxicHeadsetService OpenMxicService() =>
        new(_connection.ProductId, _connection.DevicePath, _diagnostic);

    private async Task<RazerHeadsetAudioState> ReadMxicStateAsync(CancellationToken cancellationToken)
    {
        await using var service = OpenMxicService();
        return await service.ReadStateAsync(cancellationToken);
    }

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
    private Task RunWriteAsync(
        Func<RazerHeadsetAudioService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(token => write(OpenService(), token), cancellationToken);

    private Task RunMxicWriteAsync(
        Func<RazerMxicHeadsetService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(async token =>
        {
            await using var service = OpenMxicService();
            await write(service, token);
        }, cancellationToken);

    private async Task RunWriteCoreAsync(
        Func<CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return;
        try
        {
            await write(cancellationToken);
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
            var index = Array.FindIndex(ActivePresets, item => item.Value == preset);
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
        if (state.Dnd is bool dnd) { Dnd = dnd; _hasDnd = true; OnPropertyChanged(nameof(DndVisibility)); }
        if (state.AutoPause is bool autoPause)
        {
            AutoPause = autoPause;
            _hasAutoPause = true;
            OnPropertyChanged(nameof(AutoPauseVisibility));
        }

        if (state.GestureControl is bool gesture)
        {
            GestureControl = gesture;
            _hasGestureControl = true;
            OnPropertyChanged(nameof(GestureControlVisibility));
        }

        if (state.Dolby is bool dolby) { Dolby = dolby; _hasDolby = true; OnPropertyChanged(nameof(DolbyVisibility)); }
        if (state.NightMode is bool night)
        {
            NightMode = night;
            _hasNightMode = true;
            OnPropertyChanged(nameof(NightModeVisibility));
        }

        if (state.VoicePrompts is { } prompts)
        {
            VoicePrompts = prompts.Enabled;
            _hasVoicePrompts = true;
            OnPropertyChanged(nameof(VoicePromptsVisibility));
        }

        if (state.MicDeEsser is byte deEsser)
        {
            MicDeEsser = deEsser;
            _hasMicDeEsser = true;
            OnPropertyChanged(nameof(MicDeEsserVisibility));
        }

        if (state.MicLimiter is sbyte limiter)
        {
            MicLimiterDb = limiter;
            _hasMicLimiter = true;
            OnPropertyChanged(nameof(MicLimiterVisibility));
        }

        if (state.MicReverb is { } reverb)
        {
            var index = Array.IndexOf(MicReverbModes, reverb.RoomMode);
            if (index >= 0) SelectedMicReverbIndex = index;
            MicReverbIntensity = reverb.Intensity;
            _hasMicReverb = true;
            OnPropertyChanged(nameof(MicReverbVisibility));
        }

        if (state.Haptics is { } haptics)
        {
            HapticsEnabled = haptics.Enabled;
            HapticsStrength = haptics.Strength;
            _hasHaptics = true;
            OnPropertyChanged(nameof(HapticsVisibility));
        }

        OnPropertyChanged(nameof(AdvancedAudioVisibility));
        OnPropertyChanged(nameof(MicProcessingVisibility));
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
        exception is IOException or InvalidDataException or InvalidOperationException or ArgumentOutOfRangeException or
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
