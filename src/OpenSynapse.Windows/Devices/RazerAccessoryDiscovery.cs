using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerAccessoryConnection(
    ushort ProductId,
    string DevicePath,
    int ReportLength,
    byte ReportId);

/// <summary>
/// Finds the control channel for standalone accessories. A device may expose both a
/// 91-byte Chroma collection and a shorter control collection; the caller states which
/// one it needs.
/// </summary>
internal static class RazerAccessoryDiscovery
{
    internal static async Task<RazerAccessoryConnection?> FindAsync(
        ushort productId,
        int preferredReportLength,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
            0x1532,
            cancellationToken).ConfigureAwait(false);
        var candidates = interfaces
            .Where(item => item.ProductId == productId)
            .Where(item => item.FeatureReportByteLength is 65 or 91)
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenByDescending(item => item.FeatureReportByteLength == preferredReportLength)
            .ThenBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var chosen = candidates.FirstOrDefault();
        if (chosen is null)
        {
            diagnostic?.Invoke(
                $"accessory {productId:X4} found no 65/91-byte feature channel; " +
                $"enumerated={string.Join(",", interfaces.Where(item => item.ProductId == productId)
                    .Select(item => $"{item.FeatureReportByteLength}/{item.UsagePage:X4}:{item.Usage:X4}"))}");
            return null;
        }

        var length = chosen.FeatureReportByteLength;
        diagnostic?.Invoke(
            $"accessory {productId:X4} selected report={length} " +
            $"path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"access={chosen.Access}");
        return new RazerAccessoryConnection(
            productId,
            chosen.DevicePath,
            length,
            length == 65 ? (byte)0x07 : (byte)0x00);
    }
}
