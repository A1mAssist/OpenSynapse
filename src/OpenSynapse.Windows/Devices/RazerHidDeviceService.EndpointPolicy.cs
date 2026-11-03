namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    internal static IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> OrderEndpointCandidates(
        ushort productId,
        IReadOnlyList<WindowsHidDiscovery.HidInterfaceDescriptor> available,
        string? cachedPath)
    {
        var ordered = cachedPath is null
            ? available
            : available.OrderByDescending(candidate =>
                string.Equals(candidate.DevicePath, cachedPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        // OpenRazer's report index is the USB interface: 025C uses 2, 0086/0088 and 00A4 use 0.
        // Prefer that collection when Windows exposes more than one 91-byte path; the probe
        // still falls back to every other path.
        var preferredInterface = productId switch
        {
            0x025C => "&mi_02",
            0x0086 or 0x0088 or 0x00A4 => "&mi_00",
            _ => null,
        };
        return cachedPath is null && preferredInterface is not null
            ? ordered.OrderByDescending(candidate =>
                candidate.DevicePath.Contains(preferredInterface, StringComparison.OrdinalIgnoreCase)).ToArray()
            : ordered;
    }
}