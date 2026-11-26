namespace OpenSynapse.Windows.Devices;

public sealed record RazerHeadsetEqualizerState(byte? Preset, IReadOnlyList<byte>? Bands);
