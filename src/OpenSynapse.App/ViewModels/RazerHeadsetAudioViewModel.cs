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

public sealed partial class RazerHeadsetAudioViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly TimeSpan MxicReadTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan BatteryRefreshInterval = TimeSpan.FromSeconds(10);

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

    private static readonly (byte Value, string Key)[] JolietPresets =
    [
        (0x00, "RazerHeadsetPresetDefault"),
        (0x01, "RazerHeadsetPresetGame"),
        (0x02, "RazerHeadsetPresetMusic"),
        (0x03, "RazerHeadsetPresetMovie"),
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
    private bool _dnd;
    private bool _autoPause;
    private bool _gestureControl;
    private bool _dolby;
    private bool _nightMode;
    private bool _voicePrompts;
    private double _micDeEsser;
    private double _micLimiterDb;
    private bool _micHpf;
    private bool _micLimiterEnabled;
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
    private bool _hasMicHpf;
    private bool _hasMicLimiterEnabled;
    private bool _hasMicReverb;
    private bool _hapticsEnabled;
    private double _hapticsStrength;
    private bool _hasHaptics;
    private bool _hasFirmware;
    private bool _hasBattery;
    private DateTimeOffset _lastBatteryRefresh;
    private bool _hasCharging;
    private byte _sidetonePercent;
    private ushort _silenceDetectionPeriod = 15;
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

    private bool IsKrakenBase => _connection.Transport == RazerHeadsetAudioTransport.KrakenBase37;
    private bool IsAvnera65 => _connection.Transport == RazerHeadsetAudioTransport.AvneraFeature65;
    private bool IsProtocol25 => _connection.Transport == RazerHeadsetAudioTransport.Protocol25Feature65;
    private bool IsNoaBass => IsProtocol25 &&
        _connection.ProductId is 0x0532 or 0x0548 or 0x05A3;
    private bool IsT3 => _connection.Transport == RazerHeadsetAudioTransport.ValkyrieT3Feature64;
    private bool IsJolie => _connection.Transport == RazerHeadsetAudioTransport.Jolie64;
    private bool IsMxic => _connection.Transport is RazerHeadsetAudioTransport.Mxic64 or
        RazerHeadsetAudioTransport.ValkyrieT3Feature64 or RazerHeadsetAudioTransport.Jolie64;
    private bool IsMxicSidetone => IsMxic &&
        RazerMxicHeadsetProtocol.SupportsSidetone(_connection.ProductId);
    private bool IsMxicAnc => IsMxic &&
        RazerMxicHeadsetProtocol.SupportsAnc(_connection.ProductId);
    private bool IsMixer053E => _connection.Transport == RazerHeadsetAudioTransport.Mixer053E;
    private bool IsProtocol40 => RazerProtocol40HeadsetProtocol.IsSupported(_connection.ProductId);
    private bool IsMxicDnd => !IsProtocol40 && _connection.Transport is
        RazerHeadsetAudioTransport.Mxic64 or RazerHeadsetAudioTransport.ValkyrieT3Feature64;
    private bool IsValkyrie => _connection.Transport is
        RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64;
    private bool IsValkyrieSidetone => IsValkyrie &&
        RazerValkyrieHeadsetProtocol.SupportsSidetone.Contains(_connection.ProductId);
    private bool IsValkyrieAnc => IsValkyrie &&
        RazerValkyrieHeadsetProtocol.SupportsAnc.Contains(_connection.ProductId);
    private bool IsFeature91 => !IsMxic && !IsValkyrie && !IsMixer053E && !IsKrakenBase &&
        !IsAvnera65 && !IsProtocol25;
    // AudioMabelT3C (0574) uses the Mxic bundle indices 0/7/8/9/255, not the Billie 0..4 list.
    private (byte Value, string Key)[] ActivePresets =>
        IsJolie ? JolietPresets : IsT3 ? Presets[..4] : IsFeature91 || IsProtocol25 ? Presets : MxicPresets;

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

    // Noa firmware stores bass as 1..7; the shared slider stays 0..100.
    private byte ToFirmwareBass(double percent) => IsNoaBass
        ? (byte)Math.Clamp(1 + (int)Math.Round(percent * 6 / 100), 1, 7)
        : (byte)Math.Round(percent);

    private double FromFirmwareBass(byte value) => IsNoaBass
        ? Math.Clamp((value - 1) * 100d / 6, 0, 100)
        : value;

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
        if (IsMixer053E && connection.HasControlInterface)
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

    /// <summary>
    /// Reads only the battery while the page stays open. A full state read would
    /// overwrite controls the user has not applied yet.
    /// </summary>
    internal async Task RefreshBatteryStateAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy ||
            DateTimeOffset.UtcNow - _lastBatteryRefresh < BatteryRefreshInterval)
        {
            return;
        }

        _lastBatteryRefresh = DateTimeOffset.UtcNow;
        var percent = await RazerHeadsetAudioService
            .TryReadBatteryPercentAsync(_connection, _diagnostic, cancellationToken);
        ApplyBatteryPercent(percent);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!_connection.HasControlInterface)
        {
            ErrorText = AppStrings.Text("RazerHeadsetControlInterfaceMissing");
            SetProbeState(RazerHeadsetProbeState.ControlInterfaceMissing);
            return;
        }

        if (Mixer053E is { } mixer053E)
        {
            await mixer053E.LoadAsync(cancellationToken);
            return;
        }

        if (_connection.Transport == RazerHeadsetAudioTransport.NativeDll)
        {
            ErrorText = AppStrings.Text("BlackSharkComponentRequired");
            SetProbeState(RazerHeadsetProbeState.NotAvailable);
            return;
        }

        if (!BeginOperation()) return;
        SetProbeState(RazerHeadsetProbeState.Probing);
        ClearCapabilityFlags();
        try
        {
            Task<RazerHeadsetAudioState> ReadStateOnceAsync() => _connection.Transport switch
            {
                RazerHeadsetAudioTransport.AvneraFeature65 =>
                    ReadAvneraStateAsync(cancellationToken),
                RazerHeadsetAudioTransport.Mxic64 or
                    RazerHeadsetAudioTransport.ValkyrieT3Feature64 or
                    RazerHeadsetAudioTransport.Jolie64 =>
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
            if (state.BatteryPercent is not null)
            {
                _lastBatteryRefresh = DateTimeOffset.UtcNow;
            }

            var hasAnyValue = HasAnyValue(state);
            // A probe that failed while the rest of the device answered is a missing
            // read-back, not a dead device: the capability list already marks that item.
            ErrorText = state.Errors.Count > 0 && !hasAnyValue
                ? LocalizeHeadsetReadFailure(state.Errors[0])
                : string.Empty;
            SetProbeState(hasAnyValue
                ? state.Errors.Count > 0
                    ? RazerHeadsetProbeState.Partial
                    : RazerHeadsetProbeState.Ready
                : RazerHeadsetProbeState.Failed);

            // Optional extras must not decide the card state: Mako X and the 91-byte
            // headsets can answer audio while the lighting channel is busy.
            try
            {
                if ((IsFeature91 || IsJolie) && !IsSeirenEmote)
                {
                    ApplyMatrixLighting(await OpenChromaService().ReadStateAsync(cancellationToken));
                }
                await ApplyPersistedProfileAsync(PowerSourceKind.Unknown, cancellationToken);
                if (IsJolie)
                {
                    var power = await new RazerMakoXPowerService(
                        _connection.DevicePath, _diagnostic).ReadAsync(cancellationToken);
                    AutoSleepEnabled = power.Enabled;
                    SelectAutoSleepMinutes(power.Minutes);
                }
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"headset optional state failed: {exception.Message}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetProbeState(RazerHeadsetProbeState.NotProbed);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"headset read failed: {exception.Message}");
            ErrorText = LocalizeHeadsetReadFailure(exception.Message);
            SetProbeState(RazerHeadsetProbeState.Failed);
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
        if (!IsFeature91 && !IsProtocol25) return;
        var previous = NoiseSuppression;
        if (!await RunWriteAsync((service, token) => service.SetNoiseSuppressionAsync(NoiseSuppression, token), cancellationToken)) return;
        await SaveSettingAsync(previous, NoiseSuppression, value => NoiseSuppression = value,
            (settings, value) => settings.NoiseSuppression = value,
            token => OpenService().SetNoiseSuppressionAsync(previous, token), cancellationToken);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(string.Empty);
        Mixer053E?.RefreshLocalization();
    }

    internal void ApplyBatteryPercent(int? percent)
    {
        BatteryText = percent is int value ? $"{value}%" : "--";
        BatteryPercent = percent;
        _hasBattery = percent is not null;
        OnPropertyChanged(nameof(BatteryPercent));
        OnPropertyChanged(nameof(GenericStatusVisibility));
    }

    private void ApplyState(RazerHeadsetAudioState state)
    {
        if (state.Firmware is { } firmware)
        {
            FirmwareText = firmware.ToString();
            _hasFirmware = true;
        }
        ApplyBatteryPercent(state.BatteryPercent);
        if (state.Charging is bool charging)
        {
            ChargingText = AppStrings.Text(charging ? "BlackSharkCharging" : "BlackSharkNotCharging");
            _hasCharging = true;
        }
        ApplyAvneraInfo(state);
        if (state.EqPreset is byte preset)
        {
            var index = Array.FindIndex(ActivePresets, item => item.Value == preset);
            if (index >= 0)
            {
                SelectedEqPresetIndex = index;
            }

            _hasEqPreset = true;
            OnPropertyChanged(nameof(EqPresetVisibility));
        }

        if (IsJolie)
        {
            ApplyJolietEqState(state.EqPreset, state.EqBands);
        }
        else if (state.EqBands is { Count: 8 or 10 } bands)
        {
            ApplyEqBands(bands);
        }
        ApplyMicEqBands(state);

        if (state.Bass is byte bass)
        {
            Bass = FromFirmwareBass(bass);
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
        if (state.SilenceDetectionPeriod is ushort period) _silenceDetectionPeriod = period;
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

        if (state.MicHpf is bool hpf)
        {
            MicHpf = hpf;
            _hasMicHpf = true;
            OnPropertyChanged(nameof(MicHpfVisibility));
        }

        if (state.MicLimiterEnabled is bool limiterEnabled)
        {
            MicLimiterEnabled = limiterEnabled;
            _hasMicLimiterEnabled = true;
            OnPropertyChanged(nameof(MicLimiterEnabledVisibility));
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
            _hasHaptics = IsAvnera65;
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

        if (state.SidetonePercent is byte sidetone)
        {
            _sidetonePercent = sidetone;
            _hasMicMonitoring = true;
            OnPropertyChanged(nameof(MicMonitoringVisibility));
        }
        if (state.MicReturnSwitch is bool monitoring)
        {
            MicMonitoringEnabled = monitoring;
            _hasMicMonitoring = true;
            OnPropertyChanged(nameof(MicMonitoringVisibility));
        }

        OnPropertyChanged(nameof(AdvancedAudioVisibility));
        OnPropertyChanged(nameof(MicProcessingVisibility));
        OnPropertyChanged(nameof(MicDspVisibility));
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
    private static string LocalizeHeadsetReadFailure(string raw) =>
        raw.Contains("does not support seeking", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("does not support", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("no matching response", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("no matching successful ack", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("feature channel", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
        raw.Contains("busy", StringComparison.OrdinalIgnoreCase)
            ? AppStrings.Text("RazerHeadsetTransportUnavailable")
            : AppStrings.Text("RazerHeadsetReadFailed");

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
