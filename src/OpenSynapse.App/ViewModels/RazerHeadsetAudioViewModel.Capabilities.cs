using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public enum RazerHeadsetProbeState
{
    NotProbed,
    Probing,
    Ready,
    Partial,
    Failed,
    NotAvailable,
    ControlInterfaceMissing,
}

public sealed partial class RazerHeadsetAudioViewModel
{
    private enum HeadsetCapability
    {
        Firmware,
        Serial,
        Edition,
        Link,
        Battery,
        Charging,
        EqPreset,
        EqBands,
        Bass,
        MicMuted,
        MicVolume,
        AudioSource,
        NoiseSuppression,
        Anc,
        Lighting,
        Haptics,
        ParametricEq,
        MicCompressor,
        MicMonitoring,
        Dnd,
        AutoPause,
        GestureControl,
        Dolby,
        NightMode,
        VoicePrompts,
        MicDeEsser,
        MicLimiter,
        MicReverb,
        Dock,
        MicEqBands,
    }

    private static readonly HeadsetCapability[] Feature91Capabilities =
    [
        HeadsetCapability.Firmware,
        HeadsetCapability.Battery,
        HeadsetCapability.Charging,
        HeadsetCapability.EqPreset,
        HeadsetCapability.EqBands,
        HeadsetCapability.Bass,
        HeadsetCapability.MicMuted,
        HeadsetCapability.MicVolume,
        HeadsetCapability.AudioSource,
        HeadsetCapability.NoiseSuppression,
        HeadsetCapability.Anc,
        HeadsetCapability.Lighting,
        HeadsetCapability.Haptics,
        HeadsetCapability.ParametricEq,
        HeadsetCapability.MicCompressor,
        HeadsetCapability.MicMonitoring,
        HeadsetCapability.Dnd,
        HeadsetCapability.AutoPause,
        HeadsetCapability.GestureControl,
        HeadsetCapability.Dolby,
        HeadsetCapability.NightMode,
        HeadsetCapability.VoicePrompts,
        HeadsetCapability.MicDeEsser,
        HeadsetCapability.MicLimiter,
        HeadsetCapability.MicReverb,
    ];

    private static readonly HeadsetCapability[] AvneraCapabilities =
    [
        HeadsetCapability.Firmware,
        HeadsetCapability.Serial,
        HeadsetCapability.Edition,
        HeadsetCapability.Link,
        HeadsetCapability.Battery,
        HeadsetCapability.Charging,
        HeadsetCapability.MicMonitoring,
        HeadsetCapability.AutoPause,
        HeadsetCapability.Haptics,
        HeadsetCapability.Lighting,
        HeadsetCapability.Dock,
    ];

    private static readonly HeadsetCapability[] KrakenBaseCapabilities =
    [
        HeadsetCapability.Firmware,
        HeadsetCapability.Serial,
        HeadsetCapability.Anc,
    ];

    private static readonly HeadsetCapability[] MxicCapabilities =
    [
        HeadsetCapability.Firmware,
        HeadsetCapability.Battery,
        HeadsetCapability.Charging,
        HeadsetCapability.EqPreset,
        HeadsetCapability.EqBands,
        HeadsetCapability.MicMuted,
    ];

    private static readonly HeadsetCapability[] ValkyrieCapabilities =
    [
        HeadsetCapability.Battery,
        HeadsetCapability.Charging,
        HeadsetCapability.EqPreset,
        HeadsetCapability.EqBands,
        HeadsetCapability.MicMuted,
        HeadsetCapability.MicEqBands,
    ];

    private static readonly HeadsetCapability[] ValkyrieT3Capabilities =
    [
        HeadsetCapability.EqPreset,
        HeadsetCapability.EqBands,
        HeadsetCapability.MicMuted,
        HeadsetCapability.MicMonitoring,
    ];

    private static readonly HeadsetCapability[] Protocol40Capabilities =
    [
        HeadsetCapability.Battery,
        HeadsetCapability.Charging,
    ];

    private HeadsetCapability[] Protocol25Capabilities
    {
        get
        {
            var capabilities = new List<HeadsetCapability>
            {
                HeadsetCapability.Firmware,
                HeadsetCapability.Serial,
                HeadsetCapability.Edition,
            };
            if (RazerProtocol25FeatureProtocol.SupportsHidEqualizer(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.EqPreset);
                capabilities.Add(HeadsetCapability.EqBands);
            }
            if (RazerProtocol25FeatureProtocol.SupportsHidBass(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.Bass);
            }
            if (RazerProtocol25FeatureProtocol.SupportsHidMicrophone(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.MicMuted);
            }
            if (RazerProtocol25FeatureProtocol.SupportsHidMicVolume(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.MicVolume);
            }
            if (RazerProtocol25FeatureProtocol.SupportsHidAudioInput(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.NoiseSuppression);
            }

            return capabilities.ToArray();
        }
    }

    private RazerHeadsetProbeState _probeState;
    private bool _hasEqPreset;

    public RazerHeadsetProbeState ProbeState => _probeState;
    public bool HasCapabilityProbe => _probeState is RazerHeadsetProbeState.Ready or RazerHeadsetProbeState.Partial;
    public int ProtocolTotalCount => SupportedCapabilities.Length;
    public int ProtocolAvailableCount => SupportedCapabilities.Count(IsCapabilityAvailable);

    public string CapabilityText => _probeState switch
    {
        RazerHeadsetProbeState.Probing => AppStrings.Text("BlackSharkCapabilityReading"),
        RazerHeadsetProbeState.Failed => AppStrings.Text("BlackSharkCapabilityReadFailed"),
        RazerHeadsetProbeState.NotAvailable => AppStrings.Text("BlackSharkComponentRequired"),
        RazerHeadsetProbeState.ControlInterfaceMissing =>
            AppStrings.Text("RazerHeadsetControlInterfaceMissing"),
        RazerHeadsetProbeState.Partial => AppStrings.FormatText(
            "ProtocolAvailableCountPartial", ProtocolAvailableCount, ProtocolTotalCount),
        _ => AppStrings.FormatText("ProtocolAvailableCount", ProtocolAvailableCount, ProtocolTotalCount),
    };

    private HeadsetCapability[] SupportedCapabilities =>
        IsProtocol40 ? Protocol40Capabilities : _connection.Transport switch
        {
            RazerHeadsetAudioTransport.AvneraFeature65 => AvneraCapabilities,
            RazerHeadsetAudioTransport.KrakenBase37 => KrakenBaseCapabilities,
            RazerHeadsetAudioTransport.Protocol25Feature65 => Protocol25Capabilities,
            RazerHeadsetAudioTransport.Mxic64 => MxicProductCapabilities,
            RazerHeadsetAudioTransport.Valkyrie64 => ValkyrieProductCapabilities,
            RazerHeadsetAudioTransport.Billie64 => ValkyrieCapabilities,
            RazerHeadsetAudioTransport.ValkyrieT3Feature64 => ValkyrieT3Capabilities,
            RazerHeadsetAudioTransport.Feature91 => Feature91Capabilities,
            _ => [],
        };

    // AudioMxicDevice proves sidetone for its six identities; only 0555 also proves ANC.
    private HeadsetCapability[] MxicProductCapabilities
    {
        get
        {
            var capabilities = new List<HeadsetCapability>(MxicCapabilities);
            if (RazerMxicHeadsetProtocol.SupportsSidetone(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.MicMonitoring);
            }
            if (RazerMxicHeadsetProtocol.SupportsAnc(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.Anc);
            }

            return capabilities.ToArray();
        }
    }

    private HeadsetCapability[] ValkyrieProductCapabilities
    {
        get
        {
            var capabilities = new List<HeadsetCapability>(ValkyrieCapabilities);
            if (RazerValkyrieHeadsetProtocol.SupportsSidetone.Contains(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.MicMonitoring);
            }
            if (RazerValkyrieHeadsetProtocol.SupportsAnc.Contains(_connection.ProductId))
            {
                capabilities.Add(HeadsetCapability.Anc);
            }

            return capabilities.ToArray();
        }
    }

    private bool IsCapabilityAvailable(HeadsetCapability capability) => capability switch
    {
        HeadsetCapability.Firmware => _hasFirmware,
        HeadsetCapability.Serial => _hasSerial,
        HeadsetCapability.Edition => _hasEdition,
        HeadsetCapability.Link => _hasLink,
        HeadsetCapability.Battery => _hasBattery,
        HeadsetCapability.Charging => _hasCharging,
        HeadsetCapability.EqPreset => _hasEqPreset,
        HeadsetCapability.EqBands => _hasEqBands,
        HeadsetCapability.Bass => _hasBass,
        HeadsetCapability.MicMuted => _hasMicMuted,
        HeadsetCapability.MicVolume => _hasMicVolume,
        HeadsetCapability.AudioSource => _hasAudioSource,
        HeadsetCapability.NoiseSuppression => _hasNoiseSuppression,
        HeadsetCapability.Anc => _hasAnc,
        HeadsetCapability.Lighting => _hasLighting,
        HeadsetCapability.Haptics => _hasHaptics,
        HeadsetCapability.ParametricEq => _hasParametricEq,
        HeadsetCapability.MicCompressor => _hasMicCompressor,
        HeadsetCapability.MicMonitoring => _hasMicMonitoring,
        HeadsetCapability.Dnd => _hasDnd,
        HeadsetCapability.AutoPause => _hasAutoPause,
        HeadsetCapability.GestureControl => _hasGestureControl,
        HeadsetCapability.Dolby => _hasDolby,
        HeadsetCapability.NightMode => _hasNightMode,
        HeadsetCapability.VoicePrompts => _hasVoicePrompts,
        HeadsetCapability.MicDeEsser => _hasMicDeEsser,
        HeadsetCapability.MicLimiter => _hasMicLimiter,
        HeadsetCapability.MicReverb => _hasMicReverb,
        HeadsetCapability.Dock => _hasDock,
        HeadsetCapability.MicEqBands => _hasMicEqBands,
        _ => false,
    };

    private void ClearCapabilityFlags()
    {
        _hasFirmware = false;
        _hasSerial = false;
        _hasEdition = false;
        _hasLink = false;
        _hasBattery = false;
        _hasCharging = false;
        _hasEqPreset = false;
        _hasEqBands = false;
        _hasBass = false;
        _hasMicMuted = false;
        _hasMicVolume = false;
        _hasAudioSource = false;
        _hasNoiseSuppression = false;
        _hasAnc = false;
        _hasLighting = false;
        _hasHaptics = false;
        _hasParametricEq = false;
        _hasMicCompressor = false;
        _hasMicMonitoring = false;
        _hasDnd = false;
        _hasAutoPause = false;
        _hasGestureControl = false;
        _hasDolby = false;
        _hasNightMode = false;
        _hasVoicePrompts = false;
        _hasMicDeEsser = false;
        _hasMicLimiter = false;
        _hasMicReverb = false;
        _hasDock = false;
        _hasMicEqBands = false;
    }

    private void SetProbeState(RazerHeadsetProbeState state)
    {
        _probeState = state;
        OnPropertyChanged(nameof(ProbeState));
        OnPropertyChanged(nameof(HasCapabilityProbe));
        OnPropertyChanged(nameof(CapabilityText));
        OnPropertyChanged(nameof(ProtocolTotalCount));
        OnPropertyChanged(nameof(ProtocolAvailableCount));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(EqPresetVisibility));
        OnPropertyChanged(nameof(GenericStatusVisibility));
        OnPropertyChanged(nameof(LightingVisibility));
    }

    private static bool HasAnyValue(RazerHeadsetAudioState state) =>
        state.Firmware is not null || state.BatteryPercent is not null || state.Charging is not null ||
        state.EqPreset is not null || state.EqBands is not null || state.MicBands is not null ||
        state.Bass is not null || state.MicMuted is not null || state.MicVolume is not null ||
        state.AudioSource is not null || state.NoiseSuppression is not null || state.AncState is not null ||
        state.AncLevel is not null || state.ParametricEq is not null || state.MicCompressor is not null ||
        state.MicDeEsser is not null || state.MicReverb is not null || state.MicLimiter is not null ||
        state.Dnd is not null || state.VoicePrompts is not null || state.AutoPause is not null ||
        state.GestureControl is not null || state.Dolby is not null || state.NightMode is not null ||
        state.Haptics is not null || state.MicReturnSwitch is not null || state.SidetonePercent is not null ||
        state.SerialNumber is not null || state.Edition is not null || state.LinkStatus is not null ||
        state.DockFirmware is not null || state.DockSerialNumber is not null || state.DockEdition is not null ||
        state.ChromaEffect is not null || state.ChromaBrightness is not null;
}
