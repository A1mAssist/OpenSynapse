using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    private readonly SynapseProductManifestService? _productManifests;

    /// <summary>
    /// The catalogue owns the full command set for its PIDs. An attached PID outside it is
    /// admitted only for mapping: the product manifest supplies the name and category, and
    /// the runtime 02:84 probe decides whether the device gets a page at all.
    /// </summary>
    internal async Task<RazerHidDeviceDefinition?> ResolveDefinitionAsync(
        ushort productId,
        string? productName,
        CancellationToken cancellationToken)
    {
        var known = _catalog.Find(RazerHidDeviceDefinition.VendorId, productId);
        if (known is not null) return known;

        // The keyboard page owns its catalogue and probes both 02:84 and 08:80.
        if (RazerKeyboardService.IsSupported(productId)) return null;

        var manifest = _productManifests is null
            ? null
            : await _productManifests.GetAsync(productId, cancellationToken).ConfigureAwait(false);
        if (manifest is { Category: not (DeviceCategory.Mouse or DeviceCategory.Keyboard) })
        {
            return null;
        }

        var name = manifest?.Name;
        if (string.IsNullOrWhiteSpace(name)) name = productName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = FormattableString.Invariant($"Razer 1532:{productId:X4}");
        }

        return RazerHidDeviceCatalog.CreateProvisional(
            productId,
            name.Trim(),
            manifest?.Category ?? DeviceCategory.Unknown);
    }

    /// <summary>
    /// Sends the middleware-only GETs once per device and caches which command families
    /// answer, so a control appears only when the firmware really understands it. The cache
    /// keeps device-watch rescans from re-sending a HID request every few seconds.
    /// </summary>
    internal async Task<IReadOnlySet<RazerHidBackendCapability>> ProbeMiddlewareCapabilitiesAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken = default)
    {
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return FrozenSet<RazerHidBackendCapability>.Empty;
        }

        var result = new HashSet<RazerHidBackendCapability>();
        if (await ProbeMappingAsync(definition, devicePath, cancellationToken).ConfigureAwait(false))
        {
            result.UnionWith(MappingCapabilities);
        }

        if (definition.Category == DeviceCategory.Mouse)
        {
            var freeNotched = RazerScrollWheelProtocol.CreateGetScrollFreeNotchedModeRequest(MiddlewareTransactionId);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "scroll.freeNotched", freeNotched, response =>
                {
                    _ = RazerScrollWheelProtocol.ParseScrollFreeNotchedMode(response, freeNotched);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
            {
                result.Add(RazerHidBackendCapability.ScrollFreeNotchedModeRead);
                result.Add(RazerHidBackendCapability.ScrollFreeNotchedModeWrite);
            }

            var accelerationLevel =
                RazerScrollWheelProtocol.CreateGetScrollVirtualAccelerationLevelRequest(MiddlewareTransactionId);
            if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                    "scroll.accelerationLevel", accelerationLevel, response =>
                {
                    _ = RazerScrollWheelProtocol.ParseScrollVirtualAccelerationLevel(response, accelerationLevel);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
            {
                result.Add(RazerHidBackendCapability.ScrollAccelerationLevelRead);
                result.Add(RazerHidBackendCapability.ScrollAccelerationLevelWrite);
            }
        }

        await ProbeOnboardProfileAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeWirelessPairingAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeKnobAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeAnalogKeyAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeOledAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);
        await ProbeHapticScrollAsync(definition, devicePath, result, cancellationToken).ConfigureAwait(false);

        if (RazerHidDeviceCatalog.IsMiddlewareMouseReadOnly(definition.ProductId))
            result.RemoveWhere(capability => capability.ToString().EndsWith("Write", StringComparison.Ordinal));
        return result.ToFrozenSet();
    }

    /// <summary>
    /// A catalogue-less device is never handed another product's write set. Only the shared
    /// mapping family is probed, because that is the page this path exists to expose.
    /// </summary>
    internal async Task<IReadOnlySet<RazerHidBackendCapability>> ProbeProvisionedMappingAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken) =>
        await ProbeMappingAsync(definition, devicePath, cancellationToken).ConfigureAwait(false)
            ? MappingCapabilities
            : FrozenSet<RazerHidBackendCapability>.Empty;

    private async Task<bool> ProbeMappingAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        CancellationToken cancellationToken)
    {
        var buttonList = RazerHidMappingProtocol.GetButtonList();
        return await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
            "mapping", buttonList.Report,
            response => RazerHidMappingProtocol.ParseButtonList(response).Any(id => id != 0),
            cancellationToken).ConfigureAwait(false);
    }
}