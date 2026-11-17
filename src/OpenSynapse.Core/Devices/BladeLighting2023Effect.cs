namespace OpenSynapse.Core.Devices;

/// <summary>
/// The effect byte returned by the Blade 2023 selector report. The firmware-effect
/// generation admits only Off/Breathing/Spectrum/Wave; matrix models also report
/// Static here, but their colors are painted through the matrix channel.
/// </summary>
public enum BladeLighting2023Effect : byte
{
    Off = 0x00,
    Static = 0x01,
    Breathing = 0x02,
    Spectrum = 0x03,
    Wave = 0x04,
}
