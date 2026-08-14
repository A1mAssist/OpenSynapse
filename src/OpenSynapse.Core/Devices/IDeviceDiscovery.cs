namespace OpenSynapse.Core.Devices;

public interface IDeviceDiscovery
{
    ValueTask<DeviceSnapshot> DiscoverAsync(CancellationToken cancellationToken = default);
}
