using OpenSynapse.Windows.Protocols;
using Windows.Devices.Enumeration;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerNxpSerialDevice(string DevicePath, ushort ProductId, string? ContainerId,
    string? UsbParentDeviceId = null);

public static class RazerNxpSerialDiscovery
{
    internal const string InterfaceGuid = "86e0d1e0-8089-11d0-9ce4-08003e301f73";

    public static async Task<IReadOnlyList<RazerNxpSerialDevice>> FindAllAsync(
        CancellationToken cancellationToken = default)
    {
        var selector = $"System.Devices.InterfaceClassGuid:=\"{{{InterfaceGuid}}}\" AND " +
            "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var found = await DeviceInformation.FindAllAsync(selector,
                ["System.Devices.ContainerId", "System.Devices.DeviceInstanceId"])
            .AsTask(cancellationToken).ConfigureAwait(false);
        return found.Where(item => item.IsEnabled && IsNxpInterface(item.Id))
            .Select(item =>
            {
                DeviceIdParser.TryParse(item.Id, out _, out var productId);
                item.Properties.TryGetValue("System.Devices.ContainerId", out var container);
                item.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var instance);
                var identity = instance is string instanceId && !string.IsNullOrWhiteSpace(instanceId)
                    ? WindowsHidDiscovery.GetDeviceIdentity(instanceId) : default;
                return new RazerNxpSerialDevice(item.Id, productId,
                    identity.ContainerId ?? container?.ToString(), identity.UsbParentDeviceId);
            }).ToArray();
    }

    internal static bool IsNxpInterface(string path) =>
        path.Contains(InterfaceGuid, StringComparison.OrdinalIgnoreCase) &&
        DeviceIdParser.TryParse(path, out var vendorId, out var productId) && vendorId == 0x1532 &&
        RazerNxpHeadsetProtocol.IsSupported(productId);
}
