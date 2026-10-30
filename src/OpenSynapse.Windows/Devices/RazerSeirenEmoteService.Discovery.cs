using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerSeirenEmoteConnection(
    string DevicePath,
    ushort FeatureReportByteLength);

public sealed partial class RazerSeirenEmoteService
{
    public const ushort VendorId = 0x1532;
    public const ushort ProductId = 0x0F1B;
    public const ushort MatrixUsagePage = 0x000C;
    public const ushort MatrixUsage = 0x0001;

    public static async Task<RazerSeirenEmoteConnection?> FindAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorFeatureInterfacesAsync(
            VendorId,
            RazerFeatureReport.Length,
            cancellationToken).ConfigureAwait(false);
        var chosen = SelectInterface(interfaces);
        if (chosen is null)
        {
            diagnostic?.Invoke(
                "Seiren Emote found no 91-byte matrix interface; " +
                $"enumerated={string.Join(",", interfaces.Select(item => $"{item.ProductId:X4}:0x{item.UsagePage:X4}:0x{item.Usage:X4}"))}");
            return null;
        }

        diagnostic?.Invoke(
            $"Seiren Emote selected pid={chosen.ProductId:X4} " +
            $"path={RazerFeatureTransport.DescribeDevicePath(chosen.DevicePath)} " +
            $"usage=0x{chosen.UsagePage:X4}:0x{chosen.Usage:X4} access={chosen.Access}");
        return new RazerSeirenEmoteConnection(
            chosen.DevicePath,
            chosen.FeatureReportByteLength);
    }

    public static async Task<RazerSeirenEmoteService?> ConnectAsync(
        Action<string>? diagnostic = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await FindAsync(diagnostic, cancellationToken).ConfigureAwait(false);
        return connection is null
            ? null
            : new RazerSeirenEmoteService(
                new RazerFeatureTransport(),
                connection.DevicePath);
    }

    internal static WindowsHidDiscovery.HidInterfaceDescriptor? SelectInterface(
        IEnumerable<WindowsHidDiscovery.HidInterfaceDescriptor> interfaces) =>
        interfaces
            .Where(item =>
                item.ProductId == ProductId &&
                item.UsagePage == MatrixUsagePage &&
                item.Usage == MatrixUsage &&
                item.FeatureReportByteLength == RazerFeatureReport.Length)
            .OrderByDescending(item => item.Access == DeviceAccessState.Available)
            .ThenBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
}
