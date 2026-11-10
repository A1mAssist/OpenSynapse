namespace OpenSynapse.Core.Devices;

/// <summary>
/// Writable Logo states. A read can return any Chroma effect id; the reader
/// keeps that raw byte so the current effect does not hide the whole control.
/// </summary>
public enum BladeLogoMode
{
    Off,
    Static,
    Breathing,
}

/// <summary>
/// Chroma effect ids returned by the Blade Logo effect read. The writable
/// subset is <see cref="BladeLogoMode"/>; other values are read-only state.
/// </summary>
public enum BladeLogoEffect
{
    Static,
    Breathing,
    Spectrum,
    Wave,
    Reactive,
    Ripple,
    Starlight,
    Fire,
    Ambient,
    AudioMeter,
    Wheel,
    BatteryLevel,
    Lamborghini,
    Radiate,
    Tidal,
    CustomFrame,
    Destiny2,
    ExtendedBreathing,
}
