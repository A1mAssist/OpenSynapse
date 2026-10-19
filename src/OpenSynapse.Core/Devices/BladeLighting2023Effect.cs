namespace OpenSynapse.Core.Devices;

/// <summary>
/// The firmware-rendered uniform effects the Blade 16 2023 keyboard admits. This model
/// has no per-key matrix report, so effect RGB is stored without a readable copy.
/// </summary>
public enum BladeLighting2023Effect : byte
{
    Off = 0x00,
    Breathing = 0x02,
    Spectrum = 0x03,
    Wave = 0x04,
}
