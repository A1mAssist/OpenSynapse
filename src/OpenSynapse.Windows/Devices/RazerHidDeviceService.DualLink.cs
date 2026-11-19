using System.Collections.Concurrent;
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

    // A host that rejects 0xBF stays rejected until its HID path changes; rescanning
    // all 42 hosts every 10 s must not cost one timeout each.
    private readonly ConcurrentDictionary<string, bool> _linkedStatusUnsupported =
        new(StringComparer.OrdinalIgnoreCase);

    internal async Task<IReadOnlyList<RazerWirelessDeviceConnectionStatus>> ReadLinkedStatusesAsync(
        RazerHidDeviceDefinition parentDefinition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        if (!IsLinkedDeviceParent(parentDefinition.ProductId))
        {
            return [];
        }

        var cacheKey = $"{devicePath}|{parentDefinition.ProductId:X4}";
        if (_linkedStatusUnsupported.ContainsKey(cacheKey))
        {
            return [];
        }

        // 00A4's linker answers the dock transaction. Every other host starts on the
        // middleware bank and the transport rewrites linker commands to its own bank.
        var transactionId = parentDefinition.ProductId == MouseDockProProductId
            ? (byte)0xE0
            : MiddlewareTransactionId;
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
                    var statuses = RazerPairingProtocol.ParseMultipleDeviceWirelessConnectionStatusV2(
                        response, request);
                    WriteDiagnostic(parentDefinition.ProductId,
                        $"linked-device status list: count={statuses.Count} entries=" +
                        $"[{string.Join(", ", statuses.Select(status => $"{status.Status}:{status.ProductId:X4}"))}]");
                    return statuses;
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
        catch (NotSupportedException exception)
        {
            _linkedStatusUnsupported.TryAdd(cacheKey, true);
            WriteDiagnostic(parentDefinition.ProductId,
                $"linked-device query unsupported: {exception.Message}");
            return [];
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or InvalidDataException or TimeoutException or
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
            .Where(status => status.ProductId != parentDefinition.ProductId)
            .Select(status => status.ProductId)
            .Distinct()
            .ToArray();
    }

    internal static bool IsDualLinkParent(ushort productId) =>
        productId == MouseDockProProductId;

    internal static bool IsLinkedDeviceParent(ushort productId) =>
        RazerDualDongleCatalog.CanHost(productId);

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
        RazerWirelessDeviceConnectionStatus status,
        ushort parentProductId,
        IReadOnlyList<string> devicePaths,
        string parentDeviceKey,
        CancellationToken cancellationToken,
        bool allowDockLinker = false)
    {
        var productId = status.ProductId;
        var definition = _catalog.Find(RazerHidDeviceDefinition.VendorId, productId)
            ?? CreateProbeDefinition(
                productId,
                RazerDualDongleCatalog.DisplayName(productId),
                RazerDualDongleCatalog.CategoryOf(productId));
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            WriteDiagnostic(productId, "linked child is not a known mouse or keyboard; skipped");
            return null;
        }

        var linkedDefinition = definition.WithDisplayName(LinkedDisplayName(definition.DisplayName));
        // status 0 means paired but asleep or powered off. The link is gone, so probing
        // would fail; keep the row so the pairing stays visible.
        if (status.Status != RazerPairingProtocol.WirelessLinkConnected)
        {
            return CreateConnection(
                linkedDefinition,
                devicePaths.FirstOrDefault(),
                $"{parentDeviceKey}#linked-{productId:X4}",
                RazerHidEndpointState.RecognizedButUnresolved,
                "Paired but not connected.");
        }

        var probes = BuildLinkedProbes(definition);
        if (probes.Count == 0)
        {
            return CreateConnection(
                linkedDefinition,
                devicePaths.FirstOrDefault(),
                $"{parentDeviceKey}#linked-{productId:X4}",
                RazerHidEndpointState.RecognizedButUnresolved,
                "The paired device has no battery or firmware command.");
        }

        // The native bundle selects productId/container/interface separately. 00A4
        // exposes the paired mouse over the dock's own linker collection, so probe
        // that path when Windows does not enumerate a separate child HID.
        string? linkedPath = null;
        var viaDockLinker = false;
        string? lastError = null;
        foreach (var devicePath in devicePaths)
        {
            if (!DeviceIdParser.TryParse(devicePath, out var vendorId, out var endpointProductId) ||
                vendorId != RazerHidDeviceDefinition.VendorId)
                continue;
            var isDockLinker = allowDockLinker && endpointProductId == parentProductId;
            if (endpointProductId != productId && !isDockLinker)
                continue;
            foreach (var probe in probes)
            {
                try
                {
                    var response = await _transport.QueryPreparedForProductAsync(
                        devicePath, productId, probe.Report, definition.DeviceWait, cancellationToken)
                        .ConfigureAwait(false);
                    if (!RazerFeatureReport.IsSuccessfulResponse(probe.Report, response, 2))
                        throw new InvalidDataException("Linked child response did not match the request.");
                    if (probe.BuilderName == LinkedBatteryProbe)
                    {
                        _ = RazerHidMouseProtocol.ParseBatteryPercent(response);
                    }
                    linkedPath = devicePath;
                    viaDockLinker = isDockLinker;
                    break;
                }
                catch (Exception exception) when (exception is Win32Exception or IOException or
                    InvalidOperationException or NotSupportedException or InvalidDataException or TimeoutException or
                    UnauthorizedAccessException)
                {
                    lastError = $"{exception.GetType().Name}: {exception.Message}";
                    WriteDiagnostic(productId,
                        $"linked child probe failed path={RazerFeatureTransport.DescribeDevicePath(devicePath)}: {lastError}");
                }
            }
            if (linkedPath is not null)
                break;
        }
        if (linkedPath is null)
        {
            return CreateConnection(
                linkedDefinition,
                devicePaths.FirstOrDefault(),
                $"{parentDeviceKey}#linked-{productId:X4}",
                RazerHidEndpointState.RecognizedButUnresolved,
                lastError ?? "The paired device did not answer a probe.");
        }
        WriteDiagnostic(productId,
            $"linked child resolved pid={productId:X4} path={RazerFeatureTransport.DescribeDevicePath(linkedPath)} viaDockLinker={viaDockLinker}");

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

    private static IReadOnlyList<RazerHidRequest> BuildLinkedProbes(RazerHidDeviceDefinition definition)
    {
        var hasBattery = definition.Transactions.ContainsKey(LinkedBatteryProbe);
        var hasFirmware = definition.Transactions.ContainsKey(LinkedFirmwareProbe);
        var probes = new List<RazerHidRequest>(2);
        if (definition.Category == DeviceCategory.Keyboard)
        {
            if (hasFirmware)
                probes.Add(RazerHidStandardProtocol.GetFirmware(definition));
            if (hasBattery)
                probes.Add(RazerHidMouseProtocol.GetBattery(definition));
        }
        else
        {
            if (hasBattery)
                probes.Add(RazerHidMouseProtocol.GetBattery(definition));
            if (hasFirmware)
                probes.Add(RazerHidStandardProtocol.GetFirmware(definition));
        }

        return probes;
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
        var linkedStatuses = statuses
            .Where(status => status.ProductId != ushort.MaxValue)
            .Where(status => status.ProductId != dockDefinition.ProductId)
            // The single-device 00B3 bridge only publishes a child while its paired
            // mouse is connected. The dock keeps the previous all-status behavior.
            .Where(status => dockDefinition.ProductId != HyperPollingDongleProductId ||
                status.Status == RazerPairingProtocol.WirelessLinkConnected)
            .GroupBy(status => status.ProductId)
            .Select(group => group.OrderByDescending(status => status.Status).First())
            .ToArray();
        if (linkedStatuses.Length == 0)
        {
            return;
        }

        var linkedPaths = new[] { resolvedPath }
            .Concat(candidates.Select(candidate => candidate.DevicePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var status in linkedStatuses)
        {
            var definition = _catalog.Find(RazerHidDeviceDefinition.VendorId, status.ProductId);
            var linked = dockDefinition.ProductId == HyperPollingDongleProductId &&
                definition?.Category == DeviceCategory.Mouse
                ? await TryCreateHyperPollingConnectionAsync(
                    status.ProductId, resolvedPath, parentDeviceKey, cancellationToken).ConfigureAwait(false)
                : await TryCreateLinkedConnectionAsync(
                    status, dockDefinition.ProductId, linkedPaths, parentDeviceKey,
                    cancellationToken, allowDockLinker: true).ConfigureAwait(false);
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

    private const string LinkedBatteryProbe = "razer_chroma_misc_get_battery_level";
    private const string LinkedFirmwareProbe = "razer_chroma_standard_get_firmware_version";
}
