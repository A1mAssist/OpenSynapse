using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<IReadOnlyList<RazerHidDeviceConnection>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var interfaces = await WindowsHidDiscovery.FindVendorInterfacesAsync(
            RazerHidDeviceDefinition.VendorId,
            cancellationToken,
            LogObservedInterface).ConfigureAwait(false);
        var results = new List<RazerHidDeviceConnection>();
        foreach (var group in interfaces
            // A locally admitted Blade manifest owns the laptop controls and matrix path;
            // discovering it twice would show one physical device as two cards.
            .Where(item => RazerDeviceRegistry.BuiltIn.Find(item.VendorId, item.ProductId) is null)
            // Discovery normally carries a container id. Keep the path-derived key for the
            // empty case so two same-model devices are not merged before instance ids exist.
            .GroupBy(item => (
                item.ProductId,
                PhysicalDeviceKey: string.IsNullOrWhiteSpace(item.PhysicalDeviceKey)
                    ? WindowsHidDiscovery.GetPhysicalDeviceKey(item.DevicePath)
                    : item.PhysicalDeviceKey))
            .OrderBy(group => group.Key.ProductId)
            .ThenBy(group => group.Key.PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group.ToArray();
            var controlCollections = candidates
                .Where(candidate => candidate.FeatureReportByteLength == RazerFeatureReport.Length)
                .ToArray();
            var catalogued = _catalog.Find(RazerHidDeviceDefinition.VendorId, group.Key.ProductId);
            if (catalogued is null && controlCollections.Length == 0) continue;
            var definition = catalogued ??
                CreateProbeDefinition(
                    group.Key.ProductId,
                    candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.ProductName))
                        ?.ProductName,
                    InferCategory(candidates));
            WriteDiagnostic(definition.ProductId,
                $"scan found {candidates.Length} HID collection(s), {controlCollections.Length} with 91-byte feature reports");
            if (controlCollections.Length == 0)
            {
                WriteDiagnostic(definition.ProductId,
                    $"no 91-byte HID control collection: {DescribeRejected(candidates)}");
                results.Add(CreateConnection(definition, null, group.Key.PhysicalDeviceKey,
                    RazerHidEndpointState.RecognizedButUnresolved,
                    $"No 91-byte HID control collection ({DescribeRejected(candidates)})."));
                continue;
            }

            var available = controlCollections.Where(candidate => candidate.Access == DeviceAccessState.Available).ToArray();
            if (available.Length == 0)
            {
                WriteDiagnostic(definition.ProductId,
                    $"no usable 91-byte HID control collection: {DescribeRejected(controlCollections)}");
                results.Add(CreateConnection(definition, null, group.Key.PhysicalDeviceKey,
                    RazerHidEndpointState.BusyOrUnavailable,
                    // Name the rejected collections: a wired receiver collection can hide why.
                    $"No usable 91-byte HID control collection ({DescribeRejected(controlCollections)})."));
                continue;
            }

            var ordered = OrderEndpointCandidates(
                definition.ProductId,
                available,
                _endpointCache.TryGetValue(group.Key.PhysicalDeviceKey, out var cachedPath) ? cachedPath : null);

            var resolution = await ResolveEndpointPathAsync(
                definition, ordered.Select(candidate => candidate.DevicePath), cancellationToken).ConfigureAwait(false);
            if (resolution.Path is null)
            {
                // A just-enumerated wireless endpoint can still be settling. One delayed
                // retry lets a transiently busy collection resolve without inventing a
                // wake command or waiting for the next 10 s poll.
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                resolution = await ResolveEndpointPathAsync(
                    definition, ordered.Select(candidate => candidate.DevicePath), cancellationToken).ConfigureAwait(false);
            }
            var resolvedPath = resolution.Path;
            var lastError = resolution.Error;
            if (resolvedPath is null)
            {
                if (catalogued is null) continue;
                _endpointCache.TryRemove(group.Key.PhysicalDeviceKey, out _);
                foreach (var candidate in available)
                {
                    InvalidateMiddlewareProbeCache(candidate.DevicePath);
                }
                WriteDiagnostic(definition.ProductId, $"control channel unresolved: {lastError ?? "no side-effect-free GET answered"}");
                results.Add(CreateConnection(definition, null, group.Key.PhysicalDeviceKey,
                    RazerHidEndpointState.RecognizedButUnresolved, lastError));
            }
            else
            {
                _endpointCache[group.Key.PhysicalDeviceKey] = resolvedPath;
                var resolvedDefinition = await ResolveLeviathanDefinitionAsync(
                    definition, resolvedPath, cancellationToken).ConfigureAwait(false);
                var probedCapabilities = await ProbeMiddlewareCapabilitiesAsync(
                    resolvedDefinition, resolvedPath, cancellationToken).ConfigureAwait(false);
                var optionalCapabilities = await ProbeOptionalStateCapabilitiesAsync(
                    resolvedDefinition, resolvedPath, cancellationToken).ConfigureAwait(false);
                if (catalogued is null &&
                    !probedCapabilities.Contains(RazerHidBackendCapability.MappingRead))
                {
                    continue;
                }
                var unsupportedOptional = OptionalStateCapabilities
                    .Except(optionalCapabilities)
                    .ToFrozenSet();
                var extraCapabilities = probedCapabilities.ToHashSet();
                if (optionalCapabilities.Contains(RazerHidBackendCapability.SensorRotationRead) &&
                    optionalCapabilities.Contains(RazerHidBackendCapability.SensorRotationWrite))
                {
                    extraCapabilities.Add(RazerHidBackendCapability.SensorRotationRead);
                    extraCapabilities.Add(RazerHidBackendCapability.SensorRotationWrite);
                }
                results.Add(CreateConnection(resolvedDefinition, resolvedPath, group.Key.PhysicalDeviceKey,
                    RazerHidEndpointState.Resolved, null,
                    extraCapabilities.Count > 0 ? extraCapabilities.ToFrozenSet() : null,
                    unsupportedOptional));

                await AddLinkedConnectionsAsync(results, resolvedDefinition, resolvedPath, ordered,
                    group.Key.PhysicalDeviceKey, cancellationToken).ConfigureAwait(false);
            }
        }
        RemoveDuplicateLinkedConnections(results);
        return results;
    }


    private static DeviceCategory InferCategory(
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> candidates)
    {
        var name = candidates
            .Select(candidate => candidate.ProductName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        if (name.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("keypad", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("tartarus", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("orbweaver", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceCategory.Keyboard;
        }

        if (candidates.Any(candidate => candidate.UsagePage == 0x01 && candidate.Usage == 0x06))
        {
            return DeviceCategory.Keyboard;
        }

        return DeviceCategory.Mouse;
    }

    internal static RazerHidDeviceDefinition CreateProbeDefinition(
        ushort productId,
        string? displayName,
        DeviceCategory category)
    {
        var resolvedCategory = category is DeviceCategory.Mouse or DeviceCategory.Keyboard
            ? category
            : DeviceCategory.Mouse;
        return new RazerHidDeviceDefinition(
            productId,
            string.IsNullOrWhiteSpace(displayName)
                ? FormattableString.Invariant($"Razer 1532:{productId:X4}")
                : displayName.Trim(),
            resolvedCategory,
            new HashSet<string>(StringComparer.Ordinal),
            maximumDpi: null,
            pollingRates: Array.Empty<int>(),
            matrixDimensions: null,
            deviceWait: TimeSpan.FromMilliseconds(100),
            transactions: new Dictionary<string, byte>(StringComparer.Ordinal),
            builderArgumentPrefixes:
                new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(StringComparer.Ordinal),
            defaultStorage: RazerHidStorage.Temporary,
            defaultLedZone: RazerHidLedZone.All,
            commandSequences:
                new Dictionary<string, IReadOnlyList<RazerHidCommandStep>>(StringComparer.Ordinal));
    }
}
