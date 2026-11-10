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

