using System.ComponentModel;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Presence probe for the Razer Laptop Cooling Pad (1532:0F43). The pad exposes a
/// control collection with a 91-byte feature report; it is never written to.
/// </summary>
public static class RazerCoolingPad
{
    public const ushort VendorId = 0x1532;
    public const ushort ProductId = 0x0F43;
    private const ushort ControlFeatureReportLength = 91;

    public static async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(VendorId, cancellationToken);
            return interfaces.Any(descriptor =>
                descriptor.ProductId == ProductId &&
                descriptor.FeatureReportByteLength == ControlFeatureReportLength);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // Enumeration failure counts as absent so HyperBoost stays gated.
            return false;
        }
    }
}
