using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Resolves only devices that passed discovery and may still be probed. The lookup
/// order is the runtime contract; keep it stable when new Blade families are added.
/// </summary>
internal sealed class RazerDeviceResolver
{
    private static readonly string[] BladeFamilies =
    [
        "blade-710",
        "blade-710-chroma",
        "blade-671",
        "blade-597",
    ];

    private readonly RazerDeviceRegistry _registry;

    public RazerDeviceResolver(RazerDeviceRegistry registry) =>
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public ResolvedRazerDevice? FindReadyBlade(IReadOnlyList<DeviceDescriptor> devices)
    {
        foreach (var family in BladeFamilies)
        {
            if (FindReadyDevice(devices, family) is { } blade)
            {
                return blade;
            }
        }

        return null;
    }

    public ResolvedRazerDevice? FindReadyDevice(
        IReadOnlyList<DeviceDescriptor> devices,
        string protocolFamily)
    {
        foreach (var device in devices)
        {
            if (device.Access != DeviceAccessState.Available ||
                device.Capability != DeviceCapabilityState.PendingValidation)
            {
                continue;
            }

            var manifest = _registry.Find(device.VendorId, device.ProductId);
            if (manifest?.ProtocolFamily == protocolFamily)
            {
                return new ResolvedRazerDevice(device, manifest);
            }
        }

        return null;
    }
}
