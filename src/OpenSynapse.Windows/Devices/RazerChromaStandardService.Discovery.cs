using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerChromaStandardService
{
    internal static IReadOnlyList<RazerChromaDeviceConnection> SelectConnections(
        IEnumerable<WindowsHidDiscovery.HidInterfaceDescriptor> interfaces) =>
        interfaces
            // The official Chroma manifest is the allowlist; curated entries keep their names.
            .Where(item => Products.ContainsKey(item.ProductId) ||
                ChromaDeviceCatalog.Resolve(item.ProductId) is not null)
            .Where(item => RazerHidDeviceCatalog.BuiltIn.Find(0x1532, item.ProductId) is null)
            // A device with its own native page owns its card; Chroma never adds a second one.
            .Where(item => RazerDeviceRegistry.BuiltIn.Find(0x1532, item.ProductId) is null)
            .Where(item => !RazerHeadsetAudioService.IsKnown(item.ProductId))
            .Where(item => !RazerKeyboardService.IsSupported(item.ProductId))
            .GroupBy(item => item.PhysicalDeviceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Access == DeviceAccessState.Available)
                .ThenBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(item => item.ProductId)
            .ThenBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
            .Select(item => Products.TryGetValue(item.ProductId, out var curated)
                ? new RazerChromaDeviceConnection(
                    item.DevicePath, item.ProductId, curated.Name, curated.Category)
                : new RazerChromaDeviceConnection(
                    item.DevicePath,
                    item.ProductId,
                    string.IsNullOrWhiteSpace(item.ProductName)
                        ? $"Razer Chroma 0x{item.ProductId:X4}"
                        : item.ProductName,
                    CategoryFor(ChromaDeviceCatalog.Resolve(item.ProductId)!.Value)))
            .ToArray();

    private static DeviceCategory CategoryFor(ChromaDeviceKind kind) => kind switch
    {
        ChromaDeviceKind.Keyboard => DeviceCategory.Keyboard,
        ChromaDeviceKind.Mouse => DeviceCategory.Mouse,
        ChromaDeviceKind.Headset => DeviceCategory.Headset,
        ChromaDeviceKind.Mousepad => DeviceCategory.MouseMat,
        ChromaDeviceKind.Keypad => DeviceCategory.Gamepad,
        _ => DeviceCategory.Accessory,
    };
}
