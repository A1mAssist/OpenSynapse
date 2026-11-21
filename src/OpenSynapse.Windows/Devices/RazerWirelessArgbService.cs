using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerWirelessArgbConnection(string DevicePath, ushort ProductId);

/// <summary>
/// Sends the 381-byte GlitterWireless ARGB reports directly to the device's
/// feature collection. Unlike the 0F1F controller this path needs no filter driver.
/// </summary>
internal sealed class RazerWirelessArgbService
{
    private readonly RazerHidHidReportTransport _transport = new();
    private readonly Action<string>? _diagnostic;

    internal RazerWirelessArgbService(Action<string>? diagnostic = null)
    {
        _diagnostic = diagnostic;
    }

    internal static async Task<RazerWirelessArgbConnection?> FindAsync(
        ushort productId,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
            RazerHidDeviceDefinition.VendorId,
            cancellationToken).ConfigureAwait(false);
        var candidate = interfaces
            .Where(item =>
                item.ProductId == productId &&
                item.FeatureReportByteLength == RazerWirelessArgbProtocol.ReportLength &&
                item.Access == DeviceAccessState.Available)
            .OrderBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (candidate is null)
        {
            diagnostic?.Invoke(
                $"wireless-argb 1532:{productId:X4} found no {RazerWirelessArgbProtocol.ReportLength}-byte feature collection; " +
                $"seen={string.Join(",", interfaces.Where(item => item.ProductId == productId)
                    .Select(item => $"{item.FeatureReportByteLength}/{item.Access}"))}");
            return null;
        }

        diagnostic?.Invoke(
            $"wireless-argb 1532:{productId:X4} path={RazerFeatureTransport.DescribeDevicePath(candidate.DevicePath)}");
        return new RazerWirelessArgbConnection(candidate.DevicePath, productId);
    }

    internal async Task SetPortColorAsync(
        RazerWirelessArgbConnection connection,
        byte port,
        RazerHidColor color,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var frame = RazerWirelessArgbProtocol.Create(port, [color]);
        _diagnostic?.Invoke(
            $"wireless-argb 1532:{connection.ProductId:X4} port={port} " +
            $"region={frame[0]} report={frame.Length}");
        await _transport.SendFeatureAsync(connection.DevicePath, frame, cancellationToken)
            .ConfigureAwait(false);
    }
}
