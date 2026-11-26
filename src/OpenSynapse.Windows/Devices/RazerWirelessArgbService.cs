using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerWirelessArgbConnection(string DevicePath, ushort ProductId);

/// <summary>
/// Sends the 381-byte GlitterWireless ARGB reports directly to the device's
/// feature collection when that collection is available.
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
        RazerChromaDeviceConnection connection,
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
            RazerHidDeviceDefinition.VendorId,
            cancellationToken).ConfigureAwait(false);
        var candidate = SelectInterface(interfaces, connection);
        if (candidate is null)
        {
            diagnostic?.Invoke(
                $"wireless-argb 1532:{connection.ProductId:X4} found no matching {RazerWirelessArgbProtocol.ReportLength}-byte feature collection; " +
                $"seen={string.Join(",", interfaces.Where(item => item.ProductId == connection.ProductId)
                    .Select(item => $"{item.FeatureReportByteLength}/{item.Access}"))}");
            return null;
        }

        diagnostic?.Invoke(
            $"wireless-argb 1532:{connection.ProductId:X4} path={RazerFeatureTransport.DescribeDevicePath(candidate.DevicePath)}");
        return new RazerWirelessArgbConnection(candidate.DevicePath, connection.ProductId);
    }

    internal static WindowsHidDiscovery.HidInterfaceDescriptor? SelectInterface(
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> interfaces,
        RazerChromaDeviceConnection connection)
    {
        var control = interfaces.FirstOrDefault(item =>
            item.ProductId == connection.ProductId &&
            string.Equals(item.DevicePath, connection.DevicePath, StringComparison.OrdinalIgnoreCase));
        if (control is null) return null;

        return interfaces
            .Where(item =>
                item.ProductId == connection.ProductId &&
                string.Equals(item.PhysicalDeviceKey, control.PhysicalDeviceKey, StringComparison.OrdinalIgnoreCase) &&
                item.FeatureReportByteLength == RazerWirelessArgbProtocol.ReportLength &&
                item.Access == DeviceAccessState.Available)
            .OrderBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
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
