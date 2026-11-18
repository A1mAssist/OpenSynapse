using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // 00A4's own bundle proves LINKER/00:BF. The 0088 identity has no equivalent
    // local target-selection evidence; a sibling's linker is not its admission.
    private const ushort MouseDockProProductId = 0x00A4;
    // 00B3 is a single-device HyperPolling bridge. Its paired mouse answers through
    // the dongle's own control collection, so there is no separate child HID path.
    private const ushort HyperPollingDongleProductId = 0x00B3;

    internal async Task<IReadOnlyList<RazerWirelessDeviceConnectionStatus>> ReadLinkedStatusesAsync(
        RazerHidDeviceDefinition parentDefinition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        if (!IsLinkedDeviceParent(parentDefinition.ProductId))
        {
            return [];
        }

        // 00A4's linker answers the dock transaction; the standalone 00B3 dongle
        // uses the middleware transaction instead.
        var transactionId = parentDefinition.ProductId == HyperPollingDongleProductId
            ? MiddlewareTransactionId
            : (byte)0xE0;
        var request = RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(
            transactionId);
        try
        {
            // The linker reports busy until its wireless bank is ready. The official
            // middleware retries this read for hundreds of milliseconds; the shared
            // transport's 5 x 10 ms budget is tuned for ordinary commands.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var response = await _transport.QueryPreparedAsync(
                        devicePath, request, parentDefinition.DeviceWait, cancellationToken).ConfigureAwait(false);
                    return RazerPairingProtocol.ParseMultipleDeviceWirelessConnectionStatusV2(response, request);
                }
                catch (RazerDeviceTimeoutException exception) when (
                    exception.IsRetryableBusy && attempt < 4)
                {
                    WriteDiagnostic(parentDefinition.ProductId,
                        $"linked-device query busy; retrying ({attempt}/4)");
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or TimeoutException or
            UnauthorizedAccessException)
        {
            WriteDiagnostic(parentDefinition.ProductId,
                $"linked-device query failed: {exception.GetType().Name}: {exception.Message}");
            return [];
        }
    }

    internal async Task<IReadOnlyList<ushort>> ReadLinkedProductIdsAsync(
        RazerHidDeviceDefinition parentDefinition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        var statuses = await ReadLinkedStatusesAsync(
            parentDefinition, devicePath, cancellationToken).ConfigureAwait(false);
        // The bundle treats FFFF as empty; these ids alone do not identify
        // the target of a subsequent command on the parent's collection.
        return statuses
            .Where(status => status.ProductId != ushort.MaxValue)
            .Select(status => status.ProductId)
            .Distinct()
            .ToArray();
    }

    internal static bool IsDualLinkParent(ushort productId) =>
        productId == MouseDockProProductId;

    internal static bool IsLinkedDeviceParent(ushort productId) =>
        IsDualLinkParent(productId) || productId == HyperPollingDongleProductId;

    internal async Task<RazerHidDeviceConnection?> TryCreateHyperPollingConnectionAsync(
        ushort productId,
        string parentPath,
        string parentDeviceKey,
        CancellationToken cancellationToken)
    {
        var definition = _catalog.Find(RazerHidDeviceDefinition.VendorId, productId);
        if (definition is null || definition.Category != DeviceCategory.Mouse)
        {
            WriteDiagnostic(productId, "hyperpolling child is not a known mouse; skipped");
            return null;
        }
        if (!definition.Transactions.ContainsKey("razer_chroma_misc_get_battery_level"))
        {
            WriteDiagnostic(productId, "hyperpolling child has no battery command; skipped");
            return null;
        }

        try
        {
            var probe = RazerHidMouseProtocol.GetBattery(definition);
            var response = await _transport.QueryPreparedAsync(
                parentPath, probe.Report, definition.DeviceWait, cancellationToken).ConfigureAwait(false);
            if (!RazerFeatureReport.IsSuccessfulResponse(probe.Report, response, 2))
                throw new InvalidDataException("HyperPolling child battery response did not match the request.");
            _ = RazerHidMouseProtocol.ParseBatteryPercent(response);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or TimeoutException or
            UnauthorizedAccessException)
        {
            WriteDiagnostic(productId,
                $"hyperpolling child battery probe failed path={RazerFeatureTransport.DescribeDevicePath(parentPath)}: {exception.GetType().Name}: {exception.Message}");
            return null;
        }

        WriteDiagnostic(productId,
            $"hyperpolling child resolved path={RazerFeatureTransport.DescribeDevicePath(parentPath)}");
        return CreateConnection(
            definition,
            parentPath,
            $"{parentDeviceKey}#linked-{productId:X4}",
            RazerHidEndpointState.Resolved,
            null);
    }

    internal async Task<RazerHidDeviceConnection?> TryCreateLinkedConnectionAsync(
        ushort productId,
        IReadOnlyList<string> devicePaths,
        string parentDeviceKey,
        CancellationToken cancellationToken,
        bool allowDockLinker = false)
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

        // The native bundle selects productId/container/interface separately. 00A4
        // exposes the paired mouse over the dock's own linker collection, so probe
        // that path when Windows does not enumerate a separate child HID.
        string? linkedPath = null;
        var viaDockLinker = false;
        foreach (var devicePath in devicePaths)
        {
            if (!DeviceIdParser.TryParse(devicePath, out var vendorId, out var endpointProductId) ||
                vendorId != RazerHidDeviceDefinition.VendorId)
                continue;
            var isDockLinker = allowDockLinker && endpointProductId == MouseDockProProductId;
            if (endpointProductId != productId && !isDockLinker)
                continue;
            try
            {
                var probe = RazerHidMouseProtocol.GetBattery(definition);
                var response = await _transport.QueryPreparedForProductAsync(
                    devicePath, productId, probe.Report, definition.DeviceWait, cancellationToken).ConfigureAwait(false);
                if (!RazerFeatureReport.IsSuccessfulResponse(probe.Report, response, 2))
                    throw new InvalidDataException("Linked battery response did not match the child request.");
                _ = RazerHidMouseProtocol.ParseBatteryPercent(response);
                linkedPath = devicePath;
                viaDockLinker = isDockLinker;
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
            $"dual-link child resolved path={RazerFeatureTransport.DescribeDevicePath(linkedPath)} viaDockLinker={viaDockLinker}");

        var linkedDefinition = definition.WithDisplayName(LinkedDisplayName(definition.DisplayName));
        var extraCapabilities = viaDockLinker
            ? await ProbeMiddlewareCapabilitiesAsync(
                linkedDefinition, linkedPath, cancellationToken).ConfigureAwait(false)
            : null;
        // The child definition's command set is sent over the same dock container
        // with the child transaction bank; publish the declared controls.
        return CreateConnection(
            linkedDefinition,
            linkedPath,
            $"{parentDeviceKey}#linked-{productId:X4}",
            RazerHidEndpointState.Resolved,
            null,
            extraCapabilities);
    }

    internal async Task AddLinkedConnectionsAsync(
        List<RazerHidDeviceConnection> results,
        RazerHidDeviceDefinition dockDefinition,
        string resolvedPath,
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> candidates,
        string parentDeviceKey,
        CancellationToken cancellationToken)
    {
        var statuses = await ReadLinkedStatusesAsync(
            dockDefinition, resolvedPath, cancellationToken).ConfigureAwait(false);
        var linkedProductIds = statuses
            .Where(status => status.ProductId != ushort.MaxValue)
            // The single-device 00B3 bridge only publishes a child while its paired
            // mouse is connected. The dock keeps the previous all-status behavior.
            .Where(status => dockDefinition.ProductId != HyperPollingDongleProductId ||
                status.Status == RazerPairingProtocol.WirelessLinkConnected)
            .Select(status => status.ProductId)
            .Distinct()
            .ToArray();
        if (linkedProductIds.Length == 0)
        {
            return;
        }

        var linkedPaths = new[] { resolvedPath }
            .Concat(candidates.Select(candidate => candidate.DevicePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var linkedProductId in linkedProductIds)
        {
            var linked = dockDefinition.ProductId == HyperPollingDongleProductId
                ? await TryCreateHyperPollingConnectionAsync(
                    linkedProductId, resolvedPath, parentDeviceKey, cancellationToken).ConfigureAwait(false)
                : await TryCreateLinkedConnectionAsync(
                    linkedProductId, linkedPaths, parentDeviceKey, cancellationToken,
                    allowDockLinker: dockDefinition.ProductId == MouseDockProProductId).ConfigureAwait(false);
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
