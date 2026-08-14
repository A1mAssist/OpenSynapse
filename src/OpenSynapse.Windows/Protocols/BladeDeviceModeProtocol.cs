namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Product 710 device-mode reports. Logo tasks run in Normal mode (0, 0).
/// </summary>
public static class BladeDeviceModeProtocol
{
    public static byte[] CreateSetNormalRequest(byte transactionId = 0x1F) =>
        RazerFeatureReport.CreateRequest(transactionId, 0x02, 0x00, 0x04, new byte[] { 0x00, 0x00 });

    public static byte[] CreateSetSoftwareRequest(byte transactionId = 0x1F) =>
        RazerFeatureReport.CreateRequest(transactionId, 0x02, 0x00, 0x04, new byte[] { 0x03, 0x00 });
}
