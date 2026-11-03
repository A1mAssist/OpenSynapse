using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // 00A4's own bundle proves LINKER/00:BF. The 0088 identity has no equivalent
    // local target-selection evidence; a sibling's linker is not its admission.
    private const ushort MouseDockProProductId = 0x00A4;

    internal async Task<IReadOnlyList<ushort>> ReadLinkedProductIdsAsync(
        RazerHidDeviceDefinition parentDefinition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        if (!IsDualLinkParent(parentDefinition.ProductId))
        {
            return [];
        }

        var request = RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(
            0xE0);
        try
        {
            var response = await _transport.QueryPreparedAsync(
                devicePath, request, parentDefinition.DeviceWait, cancellationToken).ConfigureAwait(false);
            return RazerPairingProtocol
                .ParseMultipleDeviceWirelessConnectionStatusV2(response, request)
                // The bundle treats FFFF as empty; these ids alone do not identify
                // the target of a subsequent command on the parent's collection.
                .Where(status => status.ProductId != ushort.MaxValue)
                .Select(status => status.ProductId)
                .Distinct()
                .ToArray();
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or TimeoutException or
            UnauthorizedAccessException)
        {
            WriteDiagnostic(parentDefinition.ProductId,
                $"dual-link query failed: {exception.GetType().Name}: {exception.Message}");
            return [];
        }
    }

    internal static bool IsDualLinkParent(ushort productId) =>
        productId == MouseDockProProductId;

    internal async Task<RazerHidDeviceConnection?> TryCreateLinkedConnectionAsync(
        ushort productId,
        IReadOnlyList<string> devicePaths,
        string parentDeviceKey,
        CancellationToken cancellationToken)
    {
        var definition = _catalog.Find(RazerHidDeviceDefinition.VendorId, productId);
        if (definition is null || definition.Category != DeviceCategory.Mouse)
        {
            WriteDiagnostic(productId, "dual-link child is not a known mouse; skipped");
            return null;
        }
        if (!definition.Transactions.ContainsKey("razer_chroma_misc_get_battery_level"))
        {
            WriteDiagnostic(productId, "dual-link child has no battery command; skipped");
            return null;
        }

        // The native bundle selects productId/container/interface separately. Until
        // that bridge is proved here, a parent battery reply cannot identify a child.
        string? linkedPath = null;
        foreach (var devicePath in devicePaths)
        {
            if (!DeviceIdParser.TryParse(devicePath, out var vendorId, out var endpointProductId) ||
                vendorId != RazerHidDeviceDefinition.VendorId || endpointProductId != productId)
                continue;
            try
            {
                var probe = RazerHidMouseProtocol.GetBattery(definition);
                var response = await _transport.QueryPreparedAsync(
                    devicePath, probe.Report, definition.DeviceWait, cancellationToken).ConfigureAwait(false);
                if (!RazerFeatureReport.IsSuccessfulResponse(probe.Report, response, 2))
                    throw new InvalidDataException("Linked battery response did not match the child request.");
                _ = RazerHidMouseProtocol.ParseBatteryPercent(response);
                linkedPath = devicePath;
                break;
            }
            catch (Exception exception) when (exception is Win32Exception or IOException or
                InvalidOperationException or NotSupportedException or InvalidDataException or TimeoutException or
                UnauthorizedAccessException)
            {
                WriteDiagnostic(productId,
                    $"dual-link battery probe failed path={RazerFeatureTransport.DescribeDevicePath(devicePath)}: {exception.GetType().Name}: {exception.Message}");
            }
        }
        if (linkedPath is null)
        {
            return null;
        }
        WriteDiagnostic(productId,
            $"dual-link child resolved path={RazerFeatureTransport.DescribeDevicePath(linkedPath)}");

        var linkedDefinition = definition.WithDisplayName(LinkedDisplayName(definition.DisplayName));
        // Only battery has answered on this path. Normal discovery can independently
        // admit the direct endpoint; this fallback must not publish inherited writes.
        return new RazerHidDeviceConnection(
            linkedDefinition,
            linkedPath,
            $"{parentDeviceKey}#linked-{productId:X4}",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.BatteryRead },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(),
            null);
    }

    internal async Task AddLinkedConnectionsAsync(
        List<RazerHidDeviceConnection> results,
        RazerHidDeviceDefinition dockDefinition,
        string resolvedPath,
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> candidates,
        string parentDeviceKey,
        CancellationToken cancellationToken)
    {
        var linkedProductIds = await ReadLinkedProductIdsAsync(
            dockDefinition, resolvedPath, cancellationToken).ConfigureAwait(false);
        if (linkedProductIds.Count == 0)
        {
            return;
        }

        var linkedPaths = new[] { resolvedPath }
            .Concat(candidates.Select(candidate => candidate.DevicePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var linkedProductId in linkedProductIds)
        {
            var linked = await TryCreateLinkedConnectionAsync(
                linkedProductId, linkedPaths, parentDeviceKey, cancellationToken).ConfigureAwait(false);
            if (linked is not null)
            {
                results.Add(linked);
            }
        }
    }

    internal static void RemoveDuplicateLinkedConnections(List<RazerHidDeviceConnection> connections)
    {
        var directlyResolved = connections
            .Where(connection => connection.IsReady &&
                !connection.InstanceId.Contains("#linked-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        connections.RemoveAll(connection =>
        {
            var marker = connection.InstanceId.LastIndexOf("#linked-", StringComparison.OrdinalIgnoreCase);
            return marker >= 0 && directlyResolved.Any(direct =>
                direct.Definition.ProductId == connection.Definition.ProductId &&
                string.Equals(direct.InstanceId, connection.InstanceId[..marker], StringComparison.OrdinalIgnoreCase));
        });
    }

    private static string LinkedDisplayName(string displayName) =>
        displayName
            .Replace(" (Wired)", string.Empty, StringComparison.Ordinal)
            .Replace(" (Wireless)", string.Empty, StringComparison.Ordinal);
}
