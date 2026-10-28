namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// rzDevice25 (USB) layout-switch read. The 01:A0 tuple in the same bundle is
/// the rzDevice30 BLE variant; the Blade USB path issues 02:A4.
/// </summary>
public static class BladeKeyboardLayoutProtocol
{
    public const byte TransactionId = 0x1F;
    public const byte CommandClass = 0x02;
    public const byte GetCommandId = 0xA4;
    public const byte DataSize = 0x01;

    public static byte[] CreateGetLayoutSwitchRequest() =>
        RazerFeatureReport.CreateRequest(
            TransactionId,
            DataSize,
            CommandClass,
            GetCommandId,
            new byte[] { 0x00 });

    public static BladeKeyboardLayout ParseLayoutSwitch(ReadOnlySpan<byte> response)
    {
        var request = CreateGetLayoutSwitchRequest();
        if (!RazerFeatureReport.IsSuccessfulResponse(request, response, DataSize))
        {
            throw new InvalidOperationException(
                "Blade layout switch returned an invalid or out-of-order feature report.");
        }

        var raw = response[RazerFeatureReport.ArgumentsOffset];
        return raw switch
        {
            0x01 => BladeKeyboardLayout.Mac,
            0x02 => BladeKeyboardLayout.Windows,
            _ => throw new InvalidOperationException(
                $"Blade returned an unknown keyboard layout: 0x{raw:X2}."),
        };
    }
}

public enum BladeKeyboardLayout : byte
{
    Mac = 0x01,
    Windows = 0x02,
}
