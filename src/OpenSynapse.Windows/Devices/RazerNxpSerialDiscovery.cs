using OpenSynapse.Windows.Protocols;
using Windows.Devices.Enumeration;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerNxpSerialDevice(string DevicePath, ushort ProductId, string? ContainerId,
    string? UsbParentDeviceId = null);

public static class RazerNxpSerialDiscovery
{
    internal const string InterfaceGuid = "86e0d1e0-8089-11d0-9ce4-08003e301f73";

    public static async Task<IReadOnlyList<RazerNxpSerialDevice>> FindAllAsync(
        CancellationToken cancellationToken = default,
        Action<string>? diagnostic = null)
    {
        var selector = $"System.Devices.InterfaceClassGuid:=\"{{{InterfaceGuid}}}\" AND " +
            "System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var found = await DeviceInformation.FindAllAsync(selector,
                ["System.Devices.ContainerId", "System.Devices.DeviceInstanceId"])
            .AsTask(cancellationToken).ConfigureAwait(false);
        var admitted = new List<RazerNxpSerialDevice>();
        foreach (var item in found)
        {
            if (!item.IsEnabled) continue;
            if (!DeviceIdParser.TryParse(item.Id, out var vendorId, out var productId) ||
                vendorId != 0x1532)
            {
                // A COM port that is not a Razer interface is noise; only record the
                // ones that claim the vendor id but expose no parseable product id.
                if (item.Id.Contains("vid_1532", StringComparison.OrdinalIgnoreCase))
                    diagnostic?.Invoke($"headset-audio serial port rejected (no pid): {item.Id}");
                continue;
            }
            if (!RazerNxpHeadsetProtocol.IsSupported(productId))
            {
                diagnostic?.Invoke($"headset-audio serial port ignored pid={productId:X4}: {item.Id}");
                continue;
            }
            item.Properties.TryGetValue("System.Devices.ContainerId", out var container);
            item.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var instance);
            var identity = instance is string instanceId && !string.IsNullOrWhiteSpace(instanceId)
                ? WindowsHidDiscovery.GetDeviceIdentity(instanceId) : default;
            diagnostic?.Invoke($"headset-audio serial port admitted pid={productId:X4}: {item.Id}");
            admitted.Add(new RazerNxpSerialDevice(item.Id, productId,
                identity.ContainerId ?? container?.ToString(), identity.UsbParentDeviceId));
        }
        return admitted;
    }

    internal static bool IsNxpInterface(string path) =>
        path.Contains(InterfaceGuid, StringComparison.OrdinalIgnoreCase) &&
        DeviceIdParser.TryParse(path, out var vendorId, out var productId) && vendorId == 0x1532 &&
        RazerNxpHeadsetProtocol.IsSupported(productId);
}
