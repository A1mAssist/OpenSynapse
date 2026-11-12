namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    private static bool NeedsHidControlInterface(ushort productId) =>
        RazerHeadsetChannelCatalog.Resolve(productId) is not
            (RazerHeadsetChannel.Unsupported or RazerHeadsetChannel.NativeDll);

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
            ContainerId: null,
            ToTransport(channel),
            HasControlInterface: false);
    }
}
