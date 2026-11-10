using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;
public sealed record RazerHeadsetAudioState(
    Version? Firmware,
    byte? BatteryPercent,
    bool? Charging,
    byte? EqPreset,
    IReadOnlyList<byte>? EqBands,
    byte? Bass,
    bool? MicMuted,
    byte? MicVolume,
    RazerAudioSource? AudioSource,
    bool? NoiseSuppression,
    RazerAncState? AncState,
    byte? AncLevel,
    IReadOnlyList<string> Errors)
{
    public RazerParametricEqBand? ParametricEq { get; init; }
    public RazerMicCompressor? MicCompressor { get; init; }
    public byte? MicDeEsser { get; init; }
    public (RazerMicReverbRoomMode RoomMode, byte Intensity)? MicReverb { get; init; }
    public sbyte? MicLimiter { get; init; }
    public bool? Dnd { get; init; }
    public (byte PromptIndex, bool Enabled)? VoicePrompts { get; init; }
    public bool? AutoPause { get; init; }
    public bool? GestureControl { get; init; }
    public bool? Dolby { get; init; }
    public bool? NightMode { get; init; }
    public RazerHapticsControlState? Haptics { get; init; }
    public bool? MicReturnSwitch { get; init; }
    public IReadOnlyList<byte>? MicBands { get; init; }
    public byte? SidetonePercent { get; init; }
    public ushort? SilenceDetectionPeriod { get; init; }
    public string? SerialNumber { get; init; }
    public byte? Edition { get; init; }
    public byte? LinkStatus { get; init; }
    public Version? DockFirmware { get; init; }
    public string? DockSerialNumber { get; init; }
    public byte? DockEdition { get; init; }
    public RazerChromaEffect? ChromaEffect { get; init; }
    public byte? ChromaBrightness { get; init; }
    public IReadOnlyList<byte>? ChromaColors { get; init; }
}

