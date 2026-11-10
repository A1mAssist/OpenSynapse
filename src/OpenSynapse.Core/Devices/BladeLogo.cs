namespace OpenSynapse.Core.Devices;

/// <summary>
/// Writable Logo states. A read can return another firmware effect; the reader
/// keeps that raw byte so an unsupported mode does not hide the whole control.
/// </summary>
public enum BladeLogoMode
{
    Off,
    Static,
    Breathing,
}
