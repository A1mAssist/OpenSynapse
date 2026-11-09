using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task SetSmartTrackingAsync(
        RazerHidDeviceConnection connection,
        bool asymmetric,
        int trackingDistance,
        CancellationToken cancellationToken = default)
    {
        var classId = RazerProximitySensorProtocol.DefaultProximityClassId;
        var sensorId = RazerProximitySensorProtocol.AvagoProximitySensorId;
        var distance = (byte)Math.Clamp(trackingDistance - 1, 0, 2);

        await ExecuteWithoutResultAsync(
            connection,
            new RazerHidRequest(
                "razer_proximity_sensor_set_state",
                RazerProximitySensorProtocol.CreateSetProximitySensorStateRequest(
                    MiddlewareTransactionId, classId, sensorId, 0x01)),
            cancellationToken).ConfigureAwait(false);

        if (asymmetric)
        {
            // shortcut: one tracking-distance value drives both lift-off and landing
            // distances; split them if real-device testing shows they differ.
            await ExecuteWithoutResultAsync(
                connection,
                new RazerHidRequest(
                    "razer_proximity_sensor_set_lift_setting",
                    RazerProximitySensorProtocol.CreateSetProximitySensorLiftSettingRequest(
                        MiddlewareTransactionId,
                        classId,
                        sensorId,
                        RazerProximitySensorProtocol.PresetCalibrationHysteresisMode,
                        0x00)),
                cancellationToken).ConfigureAwait(false);
            await ExecuteWithoutResultAsync(
                connection,
                new RazerHidRequest(
                    "razer_proximity_sensor_set_configuration",
                    RazerProximitySensorProtocol.CreateSetProximitySensorConfigurationRequest(
                        MiddlewareTransactionId,
                        classId,
                        sensorId,
                        new[] { distance, distance })),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteWithoutResultAsync(
            connection,
            new RazerHidRequest(
                "razer_proximity_sensor_set_lift_setting",
                RazerProximitySensorProtocol.CreateSetProximitySensorLiftSettingRequest(
                    MiddlewareTransactionId,
                    classId,
                    sensorId,
                    RazerProximitySensorProtocol.UniversalLiftCutOffMode,
                    distance)),
            cancellationToken).ConfigureAwait(false);
    }
}
