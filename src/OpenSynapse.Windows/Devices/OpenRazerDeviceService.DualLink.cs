using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class OpenRazerDeviceService
{
    // The Mouse Dock Pro exposes the paired mouse as a second product id on the
    // dock's own HID collection. Razor's 25-protocol bundle calls this "dual link":
    // the dock declares isDualLinkDevice/LINKER and asks 00:BF for the child ids.
    private const ushort MouseDockProProductId = 0x00A4;

    internal async Task<IReadOnlyList<ushort>> ReadLinkedProductIdsAsync(
        OpenRazerDeviceDefinition dockDefinition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        if (dockDefinition.ProductId != MouseDockProProductId)
        {
            return [];
        }

        var request = RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(
            MiddlewareTransactionId);
        try
        {
            var response = await _transport.QueryPreparedAsync(
                devicePath, request, dockDefinition.DeviceWait, cancellationToken).ConfigureAwait(false);
            return RazerPairingProtocol
                .ParseMultipleDeviceWirelessConnectionStatusV2(response, request)
                // The bundle treats 0xFFFF as the empty slot and does not interpret the
                // status byte; the battery probe below is what proves the child answers.
                .Where(status => status.ProductId != ushort.MaxValue)
                .Select(status => status.ProductId)
                .Distinct()
                .ToArray();
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or NotSupportedException or InvalidDataException or TimeoutException or
            UnauthorizedAccessException)
        {
            WriteDiagnostic(dockDefinition.ProductId,
                $"dual-link query failed: {exception.GetType().Name}: {exception.Message}");
            return [];
        }
    }

    internal async Task<OpenRazerDeviceConnection?> TryCreateLinkedConnectionAsync(
        ushort productId,
        IReadOnlyList<string> devicePaths,
        string parentDeviceKey,
        CancellationToken cancellationToken)
    {
        var definition = _catalog.Find(OpenRazerDeviceDefinition.VendorId, productId);
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

        // The dock may forward mouse commands on its own collection or expose a
        // second collection in the same container. Try every candidate, because
        // only the battery read proves which one reaches the child. A card that
        // can never read anything is worse than no card.
        string? linkedPath = null;
        foreach (var devicePath in devicePaths)
        {
            try
            {
                var probe = OpenRazerMouseProtocol.GetBattery(definition);
                var response = await _transport.QueryPreparedAsync(
                    devicePath, probe.Report, definition.DeviceWait, cancellationToken).ConfigureAwait(false);
                _ = OpenRazerMouseProtocol.ParseBatteryPercent(response);
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
        return CreateConnection(
            linkedDefinition,
            linkedPath,
            $"{parentDeviceKey}#linked-{productId:X4}",
            OpenRazerEndpointState.Resolved,
            null);
    }

    internal async Task AddLinkedConnectionsAsync(
        List<OpenRazerDeviceConnection> results,
        OpenRazerDeviceDefinition dockDefinition,
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

    internal static void RemoveDuplicateLinkedConnections(List<OpenRazerDeviceConnection> connections)
    {
        var directlyResolvedProductIds = connections
            .Where(connection => connection.IsReady &&
                !connection.InstanceId.Contains("#linked-", StringComparison.Ordinal))
            .Select(connection => connection.Definition.ProductId)
            .ToHashSet();
        connections.RemoveAll(connection =>
            connection.InstanceId.Contains("#linked-", StringComparison.Ordinal) &&
            directlyResolvedProductIds.Contains(connection.Definition.ProductId));
    }

    private static string LinkedDisplayName(string displayName) =>
        displayName
            .Replace(" (Wired)", string.Empty, StringComparison.Ordinal)
            .Replace(" (Wireless)", string.Empty, StringComparison.Ordinal);
}
