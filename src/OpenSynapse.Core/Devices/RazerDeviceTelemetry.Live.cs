namespace OpenSynapse.Core.Devices;

public partial interface IRazerDeviceTelemetryReader
{
    ValueTask<RazerDeviceLiveTelemetry> ReadLiveAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RazerDeviceLiveTelemetry(
            null, null, null, null, null, null, false, [], DateTimeOffset.UtcNow));
}
