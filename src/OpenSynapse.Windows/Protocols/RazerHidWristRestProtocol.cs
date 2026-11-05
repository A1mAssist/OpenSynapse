using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// The BlackWidow V4 Pro wrist rest is 20 underglow LEDs on row 7 of the
/// keyboard's 8x23 device matrix. Razer's 653 Chroma Studio manifest maps those
/// LEDs to device row 7, columns 0-19; the main keyboard keys occupy rows 0-6.
/// </summary>
internal static class RazerHidWristRestProtocol
{
    private const ushort BlackWidowV4ProProductId = 0x028D;
    internal const byte Row = 7;
    internal const byte Columns = 20;

    internal static bool Supports(RazerHidDeviceDefinition device) =>
        device.ProductId == BlackWidowV4ProProductId &&
        device.MatrixDimensions is { Rows: > Row, Columns: >= Columns };

    internal static RazerHidRequest CreateSetColorRequest(
        RazerHidDeviceDefinition device,
        RazerHidColor color)
    {
        if (!Supports(device))
        {
            throw new NotSupportedException(
                $"Device 1532:{device.ProductId:X4} does not expose the BlackWidow V4 Pro wrist rest.");
        }

        return RazerHidLightingProtocol.SetCustomRow(
            device,
            Row,
            0,
            Enumerable.Repeat(color, Columns).ToArray());
    }
}
