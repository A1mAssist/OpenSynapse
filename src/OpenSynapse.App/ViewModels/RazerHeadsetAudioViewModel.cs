using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed class RazerHeadsetEqBandViewModel : INotifyPropertyChanged
{
    private double _level;

    public RazerHeadsetEqBandViewModel(
        int frequency,
        double minimum,
        double maximum,
        double step,
        bool decibels)
    {
        Label = frequency >= 1_000 ? $"{frequency / 1_000d:0.#} kHz" : $"{frequency} Hz";
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        Decibels = decibels;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Label { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public bool Decibels { get; }
    public string LevelText => Decibels ? $"{Level:+0.#;-0.#;0} dB" : $"{Math.Round(Level)}";

    public double Level
    {
        get => _level;
        set
        {
            var normalized = double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : Minimum;
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

public sealed partial class RazerHeadsetAudioViewModel : INotifyPropertyChanged, IAsyncDisposable
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
    private byte _micCompressorProfile;
    private bool _micCompressorEnabled;
    private double _micCompressorDb;
    private double _micCompressorRatio;
    private double _micCompressorAttack;
    private double _micCompressorRelease;
    private bool _hasMicCompressor;
    private bool _micMonitoringEnabled;
    private bool _hasMicMonitoring;
    private int _parametricEqBandIndex;
    private bool _parametricEqEnabled;
    private double _parametricEqFrequency;
    private double _parametricEqGain;
    private double _parametricEqQ;
    private byte _parametricEqProfile;
    private byte _parametricEqDirection;
    private byte _parametricEqFilterType;
    private bool _hasParametricEq;
    private static readonly RazerMicReverbRoomMode[] MicReverbModes =
        Enum.GetValues<RazerMicReverbRoomMode>();
    private int _selectedAncIndex;
    private double _ancLevel = 1;
    private bool _hasEqBands;
    private bool _hasMicMuted;
    private bool _hasMicVolume;
    private bool _hasAudioSource;
    private bool _hasNoiseSuppression;
    private bool _hasBass;
    private bool _hasAnc;
    private bool _hasLighting;
    private bool _capabilityProbed;
    private ThxHeadsetStatus _thxStatus = new(ThxHeadsetAvailability.UnsupportedDevice, null);
    private ThxAudioState? _thxState;
    private WindowsThxHeadsetController? _thxController;
    private string _thxErrorText = string.Empty;
    private int _thxSelectedPresetIndex;
    private bool _thxSpatialEnabled;
    private bool _thxEqualizerEnabled;
    private double _brightness = 100;
    private int _selectedEffectIndex;
    private Color _primaryColor = Color.FromArgb(255, 0, 255, 0);
    private Color _secondaryColor = Color.FromArgb(255, 255, 0, 0);

    private static readonly RazerChromaEffect[] LightingEffects =
        [RazerChromaEffect.Off, RazerChromaEffect.Static, RazerChromaEffect.Breathing, RazerChromaEffect.Spectrum];

    private bool IsKrakenBase => _connection.Transport == RazerHeadsetAudioTransport.KrakenBase37;
    private bool IsT3 => _connection.Transport == RazerHeadsetAudioTransport.ValkyrieT3Feature64;
    private bool IsMxic => _connection.Transport is RazerHeadsetAudioTransport.Mxic64 or RazerHeadsetAudioTransport.ValkyrieT3Feature64;
    private bool IsMixer053E => _connection.Transport == RazerHeadsetAudioTransport.Mixer053E;
    private bool IsProtocol40 => RazerProtocol40HeadsetProtocol.IsSupported(_connection.ProductId);
    private bool IsValkyrie => _connection.Transport is
        RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64;
    private bool IsFeature91 => !IsMxic && !IsValkyrie && !IsMixer053E && !IsKrakenBase;
    private (byte Value, string Key)[] ActivePresets => IsT3 ? Presets[..4] : IsFeature91 ? Presets : MxicPresets;

    private static bool SupportsS3Baseline(ushort productId) =>
        productId is 0x0521 or 0x0549;

    // Synapse 3 shipped the KrystalT2 family (Kraken V3 / HyperSense) with this
    // Game curve. Synapse 4 changed the same preset to [-3,-3,-4,0,5,5,4,1,0,-1],
    // which is +7 dB at 500 Hz and -3 dB at 4-16 kHz versus the curve below.
    private static readonly byte[] Synapse3GameBands =
    [
        0xFB, 0xFB, 0xFC, 0xFD, 0xFE, 0x02, 0x04, 0x04, 0x03, 0x02,
    ];

    private static readonly double[] FlatThxBands = new double[10];

    private byte ToFirmwareBand(double level) => _connection.Transport switch
    {
        RazerHeadsetAudioTransport.Mxic64 => RazerHeadsetEqScale.ToStep(level),
        RazerHeadsetAudioTransport.ValkyrieT3Feature64 => RazerValkyrieHeadsetProtocol.EncodeEqBandLevel((sbyte)Math.Round(level)),
        RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
            RazerValkyrieHeadsetProtocol.EncodeEqBandLevel((sbyte)Math.Round(level)),
        _ => (byte)Math.Round(level),
    };

    private double FromFirmwareBand(byte level) => _connection.Transport switch
    {
        RazerHeadsetAudioTransport.Mxic64 => RazerHeadsetEqScale.ToDecibels(level),
        RazerHeadsetAudioTransport.ValkyrieT3Feature64 => RazerValkyrieHeadsetProtocol.DecodeEqBandLevel(level),
        RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
            RazerValkyrieHeadsetProtocol.DecodeEqBandLevel(level),
        _ => level,
    };

    public RazerHeadsetAudioViewModel(
        RazerHeadsetAudioConnection connection,
        Action<string>? diagnostic = null,
        RazerHidKrakenViewModel? lighting = null,
        Func<ProfileDocument>? getActiveProfile = null,
        Func<CancellationToken, Task<bool>>? saveProfileAsync = null)
    {
        _connection = connection;
        _diagnostic = diagnostic;
        Lighting = lighting;
        _getActiveProfile = getActiveProfile;
        _saveProfileAsync = saveProfileAsync;
        if (IsMixer053E)
        {
            Mixer053E = new RazerMixer053EViewModel(
                connection.DevicePath,
                diagnostic,
                getActiveProfile,
                saveProfileAsync);
        }

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
    public RazerHidKrakenViewModel? Lighting { get; }

    public RazerMixer053EViewModel? Mixer053E { get; }

    public Visibility Mixer053EVisibility => Mixer053E is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility GenericStatusVisibility => IsMixer053E ? Visibility.Collapsed : Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility LightingVisibility =>
        (IsFeature91 || IsKrakenBase) && Lighting is not null
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>
    /// THX is an APO plus service on this headset, so the card only appears for the
    /// model whose installer we have pinned. Missing pieces get an install entry.
    /// </summary>
    public Visibility ThxVisibility => ThxHeadsetAdmission.IsSupported(_connection.ProductId)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ThxInstallerVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available
            ? Visibility.Collapsed
            : ThxVisibility;

    public bool CanInstallThx =>
        !IsBusy && ThxVisibility == Visibility.Visible &&
        _thxStatus.Availability != ThxHeadsetAvailability.Available;

    public string ThxGameProfilesUri =>
        ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.GameProfilesUri ?? string.Empty;

    public Visibility ThxGameProfilesVisibility =>
        ThxGameProfilesUri.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string ThxPatcherUri =>
        ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.PatcherUri ?? string.Empty;

    /// <summary>The pinned official installer, for a manual retry when the silent run fails.</summary>
    public string ThxInstallerUri =>
        ThxHeadsetAdmission.GetProfile(_connection.ProductId)?.InstallerUri ?? string.Empty;

    public Visibility ThxInstallerLinkVisibility =>
        ThxVisibility == Visibility.Visible &&
        ThxInstallerUri.Length > 0 &&
        (_thxErrorText.Length > 0 ||
         _thxStatus.ErrorCode is "ThxDownloadFailed" or "ThxDownloadVerificationFailed" ||
         _thxStatus.ErrorCode?.StartsWith("ThxInstaller", StringComparison.Ordinal) == true)
            ? Visibility.Visible
            : Visibility.Collapsed;

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
            _ when _thxStatus.ErrorCode?.StartsWith("ThxInstaller", StringComparison.Ordinal) == true =>
                AppStrings.Text("RazerHeadsetThxDownloadFailed"),
            _ when _thxStatus.Availability == ThxHeadsetAvailability.Available =>
                AppStrings.Text("RazerHeadsetThxReady"),
            _ => string.Empty,
        };

    public Visibility ThxControlsVisibility =>
        _thxStatus.Availability == ThxHeadsetAvailability.Available && _thxState is not null
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <summary>Synapse 3 baseline applies only to the two affected Kraken models.</summary>
    public Visibility ThxS3BaselineVisibility =>
        SupportsS3Baseline(_connection.ProductId) ? Visibility.Visible : Visibility.Collapsed;

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
        _thxState?.Presets.Select(preset => preset.Id == "default"
            ? AppStrings.Text("ThxPresetDefault")
            : preset.DisplayName).ToArray() ?? [];

    public int SelectedThxPresetIndex
    {
        get => _thxSelectedPresetIndex;
        set => SetField(ref _thxSelectedPresetIndex,
            Math.Clamp(value, 0, Math.Max(0, (_thxState?.Presets.Count ?? 0) - 1)));
    }

    public ObservableCollection<RazerHeadsetThxBandViewModel> ThxBands { get; } = new();

    public Visibility ThxBandsVisibility =>
        _thxState is { EqualizerBands.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

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
    public bool HasCapabilityProbe => _capabilityProbed;
    public int ProtocolTotalCount => 20;
    public int ProtocolAvailableCount =>
        (_hasEqBands ? 1 : 0) +
        (_hasBass ? 1 : 0) +
        (_hasMicMuted || _hasMicVolume ? 1 : 0) +
        (_hasAudioSource ? 1 : 0) +
        (_hasNoiseSuppression ? 1 : 0) +
        (_hasAnc ? 1 : 0) +
        (_hasLighting ? 1 : 0) +
        (_hasHaptics ? 1 : 0) +
        (_hasParametricEq ? 1 : 0) +
        (_hasMicCompressor ? 1 : 0) +
        (_hasMicMonitoring ? 1 : 0) +
        (_hasDnd ? 1 : 0) +
        (_hasAutoPause ? 1 : 0) +
        (_hasGestureControl ? 1 : 0) +
        (_hasDolby ? 1 : 0) +
        (_hasNightMode ? 1 : 0) +
        (_hasVoicePrompts ? 1 : 0) +
        (_hasMicDeEsser ? 1 : 0) +
        (_hasMicLimiter ? 1 : 0) +
        (_hasMicReverb ? 1 : 0);
    public string CapabilityText => !_capabilityProbed
        ? AppStrings.Text("BlackSharkStatusConnected")
        : AppStrings.FormatText("ProtocolAvailableCount", ProtocolAvailableCount, ProtocolTotalCount);

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

    public Visibility EqPresetVisibility => IsMixer053E || IsProtocol40 || IsKrakenBase
        ? Visibility.Collapsed
        : Visibility.Visible;

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

    public Visibility MicCardVisibility =>
        IsProtocol40
            ? Visibility.Collapsed
            : _hasMicMuted || _hasMicVolume ? Visibility.Visible : Visibility.Collapsed;

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
    public Visibility MicVolumeVisibility =>
        IsFeature91 && _hasMicVolume ? Visibility.Visible : Visibility.Collapsed;

    public IReadOnlyList<string> AudioSourceOptions =>
    [
        AppStrings.Text("RazerHeadsetSourceNone"),
        AppStrings.Text("RazerHeadsetSourceSpdif"),
        AppStrings.Text("RazerHeadsetSourceAux"),
        AppStrings.Text("RazerHeadsetSourceBluetooth"),
        AppStrings.Text("RazerHeadsetSourceUsb"),
    ];

    public Visibility AudioSourceVisibility =>
        IsFeature91 && _hasAudioSource ? Visibility.Visible : Visibility.Collapsed;

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

    public Visibility NoiseSuppressionVisibility =>
        IsFeature91 && _hasNoiseSuppression ? Visibility.Visible : Visibility.Collapsed;

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

    public bool MicCompressorEnabled
    {
        get => _micCompressorEnabled;
        set => SetField(ref _micCompressorEnabled, value);
    }

    public double MicCompressorDb
    {
        get => _micCompressorDb;
        set => SetField(ref _micCompressorDb, Math.Clamp(value, -128, 0));
    }

    // The middleware stores ratio, attack and release as raw 16-bit values and never
    // defines a scale, so the UI exposes them unchanged instead of inventing units.
    public double MicCompressorRatio
    {
        get => _micCompressorRatio;
        set => SetField(ref _micCompressorRatio, Math.Clamp(value, 0, ushort.MaxValue));
    }

    public double MicCompressorAttack
    {
        get => _micCompressorAttack;
        set => SetField(ref _micCompressorAttack, Math.Clamp(value, 0, ushort.MaxValue));
    }

    public double MicCompressorRelease
    {
        get => _micCompressorRelease;
        set => SetField(ref _micCompressorRelease, Math.Clamp(value, 0, ushort.MaxValue));
    }

    public Visibility MicCompressorVisibility =>
        _hasMicCompressor ? Visibility.Visible : Visibility.Collapsed;

    public bool MicMonitoringEnabled
    {
        get => _micMonitoringEnabled;
        set => SetField(ref _micMonitoringEnabled, value);
    }

    public Visibility MicMonitoringVisibility =>
        _hasMicMonitoring ? Visibility.Visible : Visibility.Collapsed;

    public int ParametricEqBandIndex
    {
        get => _parametricEqBandIndex;
        set => SetField(ref _parametricEqBandIndex, Math.Clamp(value, 0, 9));
    }

    public bool ParametricEqEnabled
    {
        get => _parametricEqEnabled;
        set => SetField(ref _parametricEqEnabled, value);
    }

    public double ParametricEqFrequency
    {
        get => _parametricEqFrequency;
        set => SetField(ref _parametricEqFrequency, Math.Clamp(value, 0, ushort.MaxValue));
    }

    public double ParametricEqGain
    {
        get => _parametricEqGain;
        set => SetField(ref _parametricEqGain, Math.Clamp(value, -128, 127));
    }

    public double ParametricEqQ
    {
        get => _parametricEqQ;
        set => SetField(ref _parametricEqQ, Math.Clamp(value, 0, 255));
    }

    public Visibility ParametricEqVisibility =>
        _hasParametricEq ? Visibility.Visible : Visibility.Collapsed;

    private RazerAncState[] DeviceAncStates => IsKrakenBase
        ? [RazerAncState.Off, RazerAncState.On]
        : AncStates;

    public IReadOnlyList<string> AncOptions =>
        DeviceAncStates.Select(state => AppStrings.Text(state switch
        {
            RazerAncState.Off => "RazerHeadsetAncOff",
            RazerAncState.On => "RazerHeadsetAncOn",
            _ => "RazerHeadsetAncAmbient",
        })).ToArray();

    public int SelectedAncIndex
    {
        get => _selectedAncIndex;
        set
        {
            if (!SetField(ref _selectedAncIndex, Math.Clamp(value, 0, DeviceAncStates.Length - 1))) return;
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
    public Visibility AncLevelVisibility => !IsKrakenBase && _selectedAncIndex == 1
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility AncVisibility => _hasAnc ? Visibility.Visible : Visibility.Collapsed;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanInstallThx));
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
        if (Mixer053E is { } mixer053E)
        {
            await mixer053E.LoadAsync(cancellationToken);
            return;
        }

        if (!BeginOperation()) return;
        try
        {
            Task<RazerHeadsetAudioState> ReadStateOnceAsync() => _connection.Transport switch
            {
                RazerHeadsetAudioTransport.Mxic64 or RazerHeadsetAudioTransport.ValkyrieT3Feature64 =>
                    ReadMxicStateAsync(cancellationToken),
                RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
                    ReadValkyrieStateAsync(cancellationToken),
                _ => OpenService().ReadStateAsync(cancellationToken),
            };

            var state = await ReadStateOnceAsync();
            if (state.Errors.Count > 0 && !HasAnyValue(state))
            {
                // A wireless headset that just woke up drops the first probe batch; one
                // short retry clears the transient case without hiding a dead device.
                await Task.Delay(250, cancellationToken);
                state = await ReadStateOnceAsync();
            }
            ApplyState(state);
            if (IsFeature91 && !IsSeirenEmote)
            {
                ApplyMatrixLighting(await OpenChromaService().ReadStateAsync(cancellationToken));
            }
            await ApplyPersistedProfileAsync(PowerSourceKind.Unknown, cancellationToken);

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

        _capabilityProbed = true;
        OnPropertyChanged(nameof(HasCapabilityProbe));
        OnPropertyChanged(nameof(ProtocolAvailableCount));
        OnPropertyChanged(nameof(CapabilityText));
    }

    [ProfileSetting("headset.thx.spatial", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxSpatialAsync(CancellationToken cancellationToken = default)
    {
        var previous = ThxSpatialEnabled;
        if (!await RunThxAsync(
                (controller, token) => controller.SetSpatialAsync(
                    ThxAudioEndpoint.Headphones, ThxSpatialEnabled, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.ThxSpatialEnabled = ThxSpatialEnabled,
            settings => settings.ThxSpatialEnabled = previous,
            () => ThxSpatialEnabled = previous,
            async token => await ThxController.SetSpatialAsync(
                ThxAudioEndpoint.Headphones, previous, token),
            cancellationToken);
    }

    [ProfileSetting("headset.thx.preset", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxPresetAsync(CancellationToken cancellationToken = default)
    {
        if (_thxState?.Presets.ElementAtOrDefault(SelectedThxPresetIndex) is not { } preset)
        {
            return;
        }
        var previousIndex = SelectedThxPresetIndex;
        var previousPreset = _thxState.Presets.ElementAtOrDefault(previousIndex);
        var previousGains = ThxBands.Select(band => band.Level).ToArray();
        var gains = preset.Id == "default"
            ? Enumerable.Repeat(0d, ThxBands.Count).ToArray()
            : ThxBands.Select(band => band.Level).ToArray();
        if (!await RunThxAsync(
                (controller, token) => controller.SelectPresetAsync(
                    ThxAudioEndpoint.Headphones, preset.Id, gains, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.ThxPreset = preset.Id,
            settings => settings.ThxPreset = previousPreset?.Id,
            () => SelectedThxPresetIndex = previousIndex,
            async token =>
            {
                if (previousPreset is not null)
                {
                    await ThxController.SelectPresetAsync(
                        ThxAudioEndpoint.Headphones, previousPreset.Id, previousGains, token);
                }
            },
            cancellationToken);
    }

    [ProfileSetting("headset.thx.bands", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyThxBandsAsync(CancellationToken cancellationToken = default)
    {
        var previous = ThxBands.Select(band => band.Level).ToArray();
        var gains = ThxBands.Select(band => band.Level).ToArray();
        if (!await RunThxAsync(
                (controller, token) => controller.SetGainsAsync(
                    ThxAudioEndpoint.Headphones, gains, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.ThxBands = ToBandMap(ThxBands.Select(band => band.Level)),
            settings => settings.ThxBands = ToBandMap(previous),
            () =>
            {
                for (var index = 0; index < previous.Length && index < ThxBands.Count; index++)
                {
                    ThxBands[index].Level = previous[index];
                }
            },
            async token => await ThxController.SetGainsAsync(
                ThxAudioEndpoint.Headphones, previous, token),
            cancellationToken);
    }

    /// <summary>
    /// Synapse 3 ran these headsets with VirtualSurround off and its Game EQ on
    /// the headset firmware. Synapse 4 keeps spatial on for the Kitty and uses a
    /// different Game curve, so restore both halves of the old baseline.
    /// </summary>
    [NotProfileScoped]
    public async Task ApplyS3BaselineAsync(CancellationToken cancellationToken = default)
    {
        if (!SupportsS3Baseline(_connection.ProductId) || !CanApplyThx) return;

        ThxSpatialEnabled = false;
        await ApplyThxSpatialAsync(cancellationToken);
        if (!CanApplyThx || ThxSpatialEnabled) return;

        var customPresetId = _thxState?.Presets
            .FirstOrDefault(preset =>
                preset.Id.Equals("Custom", StringComparison.OrdinalIgnoreCase) ||
                preset.DisplayName.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            ?.Id ?? "Custom";
        await RunThxAsync(
            (controller, token) => controller.SelectPresetAsync(
                ThxAudioEndpoint.Headphones, customPresetId, FlatThxBands, token),
            cancellationToken);

        // 0549 and the other 91-byte THX headsets expose the ten-band firmware EQ.
        // 0521 (KrakenBase37) has no EQ writer here; its Synapse 3 fix is spatial off.
        if (IsFeature91 && CanApplyThx)
        {
            var customIndex = Array.FindIndex(
                ActivePresets, preset => preset.Key == "RazerHeadsetPresetCustom");
            if (customIndex >= 0)
            {
                var customPreset = ActivePresets[customIndex].Value;
                await RunWriteAsync(
                    (service, token) => service.SetCustomEqAsync(
                        customPreset, Synapse3GameBands, token),
                    cancellationToken);
            }
        }
    }

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

    private async Task<bool> RunThxAsync(
        Func<WindowsThxHeadsetController, CancellationToken, Task<ThxAudioStatus>> action,
        CancellationToken cancellationToken)
    {
        if (!CanApplyThx || !BeginOperation()) return false;
        try
        {
            var status = await action(ThxController, cancellationToken).ConfigureAwait(true);
            ApplyThxStatus(status);
            ErrorText = string.Empty;
            OperationStatusText = status.IsAvailable
                ? AppStrings.Text("BlackSharkSaved")
                : string.Empty;
            return status.IsAvailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _thxErrorText = exception.Message;
            OperationStatusText = string.Empty;
            return false;
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
        _thxBassBoostEnabled = state.BassBoost?.Enabled ?? false;
        _thxBassBoostLevel = state.BassBoost?.LevelPercent ?? 0;
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
        OnPropertyChanged(nameof(ThxInstallerLinkVisibility));
        OnPropertyChanged(nameof(ThxControlsVisibility));
        OnPropertyChanged(nameof(CanInstallThx));
        OnPropertyChanged(nameof(CanApplyThx));
        OnPropertyChanged(nameof(ThxPresetOptions));
        OnPropertyChanged(nameof(ThxBandsVisibility));
        OnPropertyChanged(nameof(ThxNormalizationVisibility));
        OnPropertyChanged(nameof(ThxVoiceClarityVisibility));
        OnPropertyChanged(nameof(ThxBassBoostVisibility));
        OnPropertyChanged(nameof(ThxSidetoneVisibility));
    }

    public async Task LoadParametricEqAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91 || !BeginOperation()) return;
        try
        {
            var band = await OpenService()
                .GetParametricEqAsync((byte)ParametricEqBandIndex, cancellationToken);
            _parametricEqProfile = band.Profile;
            _parametricEqDirection = band.Direction;
            _parametricEqFilterType = band.FilterType;
            ParametricEqEnabled = band.Enabled;
            ParametricEqFrequency = band.Frequency;
            ParametricEqGain = band.GainTenths;
            ParametricEqQ = band.QFactorTenths;
            _hasParametricEq = true;
            OnPropertyChanged(nameof(ParametricEqVisibility));
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

    [ProfileSetting("headset.noise-suppression", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyNoiseSuppressionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = NoiseSuppression;
        if (!await RunWriteAsync((service, token) => service.SetNoiseSuppressionAsync(NoiseSuppression, token), cancellationToken)) return;
        await SaveSettingAsync(previous, NoiseSuppression, value => NoiseSuppression = value,
            (settings, value) => settings.NoiseSuppression = value,
            token => OpenService().SetNoiseSuppressionAsync(previous, token), cancellationToken);
    }

    [ProfileSetting("headset.anc", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyAncAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91 && !IsKrakenBase) return;
        var previousIndex = SelectedAncIndex;
        var previousLevel = AncLevel;
        if (!await RunWriteAsync(
                (service, token) => service.SetAncAsync(
                    DeviceAncStates[SelectedAncIndex], (byte)Math.Round(AncLevel), token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.AncModeIndex = SelectedAncIndex;
                settings.AncEnabled = DeviceAncStates[SelectedAncIndex] != RazerAncState.Off;
                settings.AncLevel = (int)Math.Round(AncLevel);
            },
            settings =>
            {
                settings.AncModeIndex = previousIndex;
                settings.AncEnabled = DeviceAncStates[previousIndex] != RazerAncState.Off;
                settings.AncLevel = (int)Math.Round(previousLevel);
            },
            () =>
            {
                SelectedAncIndex = previousIndex;
                AncLevel = previousLevel;
            },
            token => OpenService().SetAncAsync(
                DeviceAncStates[previousIndex], (byte)Math.Round(previousLevel), token),
            cancellationToken);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(string.Empty);
        Mixer053E?.RefreshLocalization();
    }

    public async ValueTask DisposeAsync()
    {
        var controller = _thxController;
        _thxController = null;
        if (controller is not null)
        {
            await controller.DisposeAsync();
        }

        if (Mixer053E is { } mixer053E)
        {
            await mixer053E.DisposeAsync();
        }

    }

    private RazerHeadsetAudioService OpenService() =>
        new(new RazerFeatureTransport(), _connection.DevicePath,
            IsKrakenBase ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(20),
            _diagnostic, _connection.Transport);

    private RazerMxicHeadsetService OpenMxicService() =>
        new(_connection.ProductId, _connection.DevicePath, _diagnostic);

    private async Task<RazerHeadsetAudioState> ReadMxicStateAsync(CancellationToken cancellationToken)
    {
        await using var service = OpenMxicService();
        return await service.ReadStateAsync(cancellationToken);
    }

    private RazerValkyrieHeadsetService OpenValkyrieService() =>
        new(_connection.ProductId, _connection.DevicePath, _diagnostic);

    private async Task<RazerHeadsetAudioState> ReadValkyrieStateAsync(CancellationToken cancellationToken)
    {
        await using var service = OpenValkyrieService();
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
    private Task<bool> RunWriteAsync(
        Func<RazerHeadsetAudioService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(token => write(OpenService(), token), cancellationToken);

    private Task<bool> RunMxicWriteAsync(
        Func<RazerMxicHeadsetService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(async token =>
        {
            await using var service = OpenMxicService();
            await write(service, token);
        }, cancellationToken);

    private Task<bool> RunValkyrieWriteAsync(
        Func<RazerValkyrieHeadsetService, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        RunWriteCoreAsync(async token =>
        {
            await using var service = OpenValkyrieService();
            await write(service, token);
        }, cancellationToken);

    private async Task<bool> RunWriteCoreAsync(
        Func<CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        if (!BeginOperation()) return false;
        try
        {
            await write(cancellationToken);
            ErrorText = string.Empty;
            OperationStatusText = AppStrings.Text("BlackSharkSaved");
            return true;
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

        return false;
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
            var (minimum, maximum, step) = _connection.Transport switch
            {
                RazerHeadsetAudioTransport.ValkyrieT3Feature64 => (-6d, 6d, 1d),
                RazerHeadsetAudioTransport.Mxic64 =>
                    (RazerHeadsetEqScale.MinimumDb, RazerHeadsetEqScale.MaximumDb, RazerHeadsetEqScale.StepDb),
                RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
                    (-12d, 12d, 1d),
                _ => (0d, 100d, 1d),
            };
            EqBands.Clear();
            for (var index = 0; index < bands.Count; index++)
            {
                EqBands.Add(new RazerHeadsetEqBandViewModel(
                    frequencies[index],
                    minimum,
                    maximum,
                    step,
                    decibels: _connection.Transport != RazerHeadsetAudioTransport.Feature91)
                {
                    Level = FromFirmwareBand(bands[index]),
                });
            }

            _hasEqBands = true;
            OnPropertyChanged(nameof(EqBandsVisibility));
        }
        ApplyMicEqBands(state);

        if (state.Bass is byte bass)
        {
            Bass = bass;
            _hasBass = true;
            OnPropertyChanged(nameof(BassVisibility));
        }

        if (state.MicMuted is bool muted)
        {
            MicMuted = muted;
            _hasMicMuted = !IsT3;
            OnPropertyChanged(nameof(MicCardVisibility));
        }
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
            _hasHaptics = false; // No independently verified per-product 91-byte write channel.
            OnPropertyChanged(nameof(HapticsVisibility));
        }

        if (state.MicCompressor is { } compressor)
        {
            _micCompressorProfile = compressor.Profile;
            MicCompressorEnabled = compressor.Enabled;
            MicCompressorDb = compressor.Db;
            MicCompressorRatio = compressor.RatioTenths;
            MicCompressorAttack = compressor.AttackTime;
            MicCompressorRelease = compressor.ReleaseTime;
            _hasMicCompressor = true;
            OnPropertyChanged(nameof(MicCompressorVisibility));
        }

        if (state.MicReturnSwitch is bool monitoring)
        {
            MicMonitoringEnabled = monitoring;
            _hasMicMonitoring = true;
            OnPropertyChanged(nameof(MicMonitoringVisibility));
        }

        OnPropertyChanged(nameof(AdvancedAudioVisibility));
        OnPropertyChanged(nameof(MicProcessingVisibility));
        if (state.MicVolume is byte volume)
        {
            MicVolume = volume;
            _hasMicVolume = true;
            OnPropertyChanged(nameof(MicVolumeVisibility));
            OnPropertyChanged(nameof(MicCardVisibility));
        }
        if (state.AudioSource is RazerAudioSource source)
        {
            var index = Array.IndexOf(Sources, source);
            if (index >= 0)
            {
                SelectedSourceIndex = index;
            }

            _hasAudioSource = true;
            OnPropertyChanged(nameof(AudioSourceVisibility));
        }
        if (state.NoiseSuppression is bool noise)
        {
            NoiseSuppression = noise;
            _hasNoiseSuppression = true;
            OnPropertyChanged(nameof(NoiseSuppressionVisibility));
        }
        if (state.AncState is RazerAncState anc)
        {
            var index = Array.IndexOf(DeviceAncStates, anc);
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
        state.Firmware is not null || state.AncState is not null ||
        state.BatteryPercent is not null || state.EqPreset is not null || state.EqBands is not null ||
        state.MicBands is not null ||
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
