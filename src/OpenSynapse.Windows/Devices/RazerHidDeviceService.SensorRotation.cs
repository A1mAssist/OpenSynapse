using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // 00CB's catalog uses 0x1F for its middleware commands; the firmware only matches the
    // transaction it receives, so the same fixed id keeps this request on that path.
    private const byte SensorRotationTransactionId = MiddlewareTransactionId;

    public async Task<RazerHidSensorRotation> GetSensorRotationAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        var request = RazerProximitySensorProtocol.CreateGetSensorRotationRequest(
            SensorRotationTransactionId);
        var response = await ExecuteAsync(
            connection,
            new RazerHidRequest("razer_proximity_sensor_get_angle_tune", request),
            cancellationToken).ConfigureAwait(false);
        return RazerProximitySensorProtocol.ParseSensorRotation(response, request);
    }

    public Task SetSensorRotationAsync(
        RazerHidDeviceConnection connection,
        bool enabled,
        byte angle,
        CancellationToken cancellationToken = default)
    {
        var request = RazerProximitySensorProtocol.CreateSetSensorRotationRequest(
            SensorRotationTransactionId, enabled, angle);
        return ExecuteWithoutResultAsync(
            connection,
            new RazerHidRequest("razer_proximity_sensor_set_angle_tune", request),
            cancellationToken);
    }
}
