namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    private static bool NeedsHidControlInterface(ushort productId) =>
        RazerHeadsetChannelCatalog.Resolve(productId) is not
            (RazerHeadsetChannel.Unsupported or RazerHeadsetChannel.NativeDll or
                RazerHeadsetChannel.DedicatedBlackShark or RazerHeadsetChannel.AudiowiseBle);

    internal static RazerHeadsetAudioConnection? CreateUsbFallback(WindowsUsbDevice device)
    {
        if (!Products.ContainsKey(device.ProductId) || !NeedsHidControlInterface(device.ProductId))
        {
            return null;
        }

        var channel = RazerHeadsetChannelCatalog.Resolve(device.ProductId);
        return new RazerHeadsetAudioConnection(
            device.InstanceId,
            device.ProductId,
            Products[device.ProductId],
            0,
            ContainerId: device.ContainerId,
            ToTransport(channel),
            HasControlInterface: false,
            PhysicalDeviceKey: WindowsHidDiscovery.GetPhysicalDeviceKey(device.InstanceId,
                Guid.TryParse(device.ContainerId, out var container) ? container : null, device.UsbParentDeviceId),
            UsbParentDeviceId: device.UsbParentDeviceId);
    }
}
