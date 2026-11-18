namespace OpenSynapse.Windows.Protocols;

internal static class RazerHidKeyNames
{
    /// <summary>USB HID usage name, or null when the usage is not a keyboard key.</summary>
    internal static string? Describe(byte hid) =>
        RazerHidMappingActions.Keys.FirstOrDefault(key => key.Hid == hid).Name;
}
