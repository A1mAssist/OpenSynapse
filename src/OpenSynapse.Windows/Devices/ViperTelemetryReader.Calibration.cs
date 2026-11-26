using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerDeviceTelemetryReader
{
    public IRazerSurfaceCalibrationClient CreateViperCalibrationClient(DeviceDescriptor device) =>
        _viperReader.CreateCalibrationClient(device);

    internal Task<bool?> ReadViperCalibrationLinkAsync(DeviceDescriptor device, CancellationToken token) =>
        _viperReader.ReadCalibrationLinkAsync(device, token);
}

internal sealed partial class ViperTelemetryReader
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CalibrationGates =
        new(StringComparer.OrdinalIgnoreCase);

    internal async Task<bool?> ReadCalibrationLinkAsync(DeviceDescriptor descriptor, CancellationToken token)
    {
        var device = _resolver.FindReadyDevice([descriptor], DeviceProtocolFamilies.Viper)
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        var response = await _client.QueryAsync(device, CapabilityIds.WirelessLinkStatusGet, token).ConfigureAwait(false);
        var statuses = RazerPairingProtocol.ParseMultipleDeviceWirelessConnectionStatusV2(response,
            RazerCapabilityClient.CreateRequest(device, CapabilityIds.WirelessLinkStatusGet));
        var status = statuses.FirstOrDefault(item => item.ProductId == descriptor.ProductId);
        return status.ProductId != descriptor.ProductId ? null : status.Status == RazerPairingProtocol.WirelessLinkConnected;
    }

    public IRazerSurfaceCalibrationClient CreateCalibrationClient(DeviceDescriptor descriptor)
    {
        if (descriptor.ProductId != 0x00B8 || descriptor.ProtocolFamily != DeviceProtocolFamilies.Viper)
            throw new ArgumentException("The descriptor does not belong to the dedicated Viper channel.", nameof(descriptor));
        var device = _resolver.FindReadyDevice([descriptor], DeviceProtocolFamilies.Viper)
            ?? throw new InvalidOperationException("The Viper control channel is unavailable.");
        if (!RazerProximitySensorProfiles.TryGet(descriptor.ProductId, out var profile) || profile.Calibration is null)
            throw new NotSupportedException("This Viper has no confirmed calibration product data.");
        var gate = CalibrationGates.GetOrAdd(descriptor.Id, static _ => new SemaphoreSlim(1, 1));
        return new RazerSurfaceCalibrationClient(profile, (report, token) =>
        {
            var capability = (report[7], report[8]) switch
            {
                (0x0B, 0x83) => CapabilityIds.SurfaceStateGet,
                (0x0B, 0x03) => CapabilityIds.SurfaceStateSet,
                (0x0B, 0x85) => CapabilityIds.SurfaceConfigurationGet,
                (0x0B, 0x05) => CapabilityIds.SurfaceConfigurationSet,
                (0x0B, 0x8B) => CapabilityIds.SurfaceLiftGet,
                (0x0B, 0x0B) => CapabilityIds.SurfaceLiftSet,
                _ => throw new NotSupportedException("The Viper manifest does not admit this calibration command."),
            };
            return _client.QueryBuiltRequestAsync(device, capability, report, token);
        }, async (action, token) =>
        {
            if (!await gate.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false))
                throw new RazerDeviceTimeoutException("The Viper calibration operation queue timed out.", retryableBusy: false);
            try { await action(token).ConfigureAwait(false); }
            finally { gate.Release(); }
        });
    }
}
