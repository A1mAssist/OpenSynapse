using OpenSynapse.Windows.Protocols;
using Windows.Devices.Enumeration;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerNxpSerialDevice(string DevicePath, ushort ProductId, string? ContainerId,
    string? UsbParentDeviceId = null);

public static class RazerNxpSerialDiscovery
{
    internal const string InterfaceGuid = "86e0d1e0-8089-11d0-9ce4-08003e301f73";

    internal static async Task<IReadOnlyList<RazerNxpSerialDevice>> FindAllAsync(
        CancellationToken cancellationToken = default,
        Action<string>? diagnostic = null,
        IReadOnlyList<WindowsUsbDevice>? presentDevices = null)
    {
        var selector = $"System.Devices.InterfaceClassGuid:=\"{{{InterfaceGuid}}}\" AND " +
            "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var found = await DeviceInformation.FindAllAsync(selector,
                ["System.Devices.ContainerId", "System.Devices.DeviceInstanceId"])
            .AsTask(cancellationToken).ConfigureAwait(false);
        var nxpUsb = presentDevices?
            .Where(item => RazerNxpHeadsetProtocol.IsSupported(item.ProductId)).ToArray() ?? [];
        var admitted = new List<RazerNxpSerialDevice>();
        foreach (var item in found)
        {
            if (!item.IsEnabled) continue;
            item.Properties.TryGetValue("System.Devices.ContainerId", out var container);
            item.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var instance);
            var identity = instance is string instanceId && !string.IsNullOrWhiteSpace(instanceId)
                ? WindowsHidDiscovery.GetDeviceIdentity(instanceId) : default;
            var containerId = identity.ContainerId ?? container?.ToString();

            ushort productId;
            if (DeviceIdParser.TryParse(item.Id, out var vendorId, out var parsed) && vendorId == 0x1532)
            {
                if (!RazerNxpHeadsetProtocol.IsSupported(parsed))
                {
                    diagnostic?.Invoke($"headset-audio serial port ignored pid={parsed:X4}: {item.Id}");
                    continue;
                }
                productId = parsed;
            }
            else if (TryAdoptByContainer(nxpUsb, containerId, identity.UsbParentDeviceId, out productId))
            {
                // The Razer NXP bridge can enumerate as a SWD/ROOT port whose id carries
                // no VID/PID; adopt it only when its container is a present 052C/052D.
                diagnostic?.Invoke($"headset-audio serial port adopted pid={productId:X4} by container: {item.Id}");
            }
            else
            {
                if (item.Id.Contains("vid_1532", StringComparison.OrdinalIgnoreCase))
                    diagnostic?.Invoke($"headset-audio serial port rejected (no pid): {item.Id}");
                continue;
            }
            diagnostic?.Invoke($"headset-audio serial port admitted pid={productId:X4}: {item.Id}");
            admitted.Add(new RazerNxpSerialDevice(item.Id, productId,
                identity.ContainerId ?? container?.ToString(), identity.UsbParentDeviceId));
        }
        return admitted;
    }

    private static bool TryAdoptByContainer(
        IReadOnlyList<WindowsUsbDevice> present,
        string? containerId,
        string? usbParentDeviceId,
        out ushort productId)
    {
        productId = 0;
        foreach (var device in present)
        {
            if (SameIdentity(containerId, device.ContainerId) ||
                SameIdentity(usbParentDeviceId, device.UsbParentDeviceId) ||
                SameIdentity(usbParentDeviceId, device.InstanceId))
            {
                productId = device.ProductId;
                return true;
            }
        }
        return false;
    }

    private static bool SameIdentity(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        StringComparer.OrdinalIgnoreCase.Equals(Normalize(left), Normalize(right));

    // Windows reports a container as "{guid}" from CM and as a bare guid from
    // DeviceInformation, so compare the value without braces.
    private static string Normalize(string value) => value.Trim().Trim('{', '}').ToLowerInvariant();

    internal static bool IsNxpInterface(string path) =>
        path.Contains(InterfaceGuid, StringComparison.OrdinalIgnoreCase) &&
        (!DeviceIdParser.TryParse(path, out var vendorId, out var productId) ||
         vendorId == 0x1532 && RazerNxpHeadsetProtocol.IsSupported(productId));
}
