using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task SetSmartTrackingAsync(
        RazerHidDeviceConnection connection,
        RazerSmartTrackingMode mode,
        int trackingDistance,
        int liftOffDistance,
        int landingDistance,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var (classId, sensorId) = ResolveProximitySensor(connection.Definition.ProductId);
        var distance = (byte)Math.Clamp(trackingDistance - 1, 0, 2);

        await ExecuteWithoutResultAsync(
            connection,
            new RazerHidRequest(
                "razer_proximity_sensor_set_state",
                RazerProximitySensorProtocol.CreateSetProximitySensorStateRequest(
                    MiddlewareTransactionId, classId, sensorId, 0x01)),
            cancellationToken).ConfigureAwait(false);

        if (mode == RazerSmartTrackingMode.Adjustable)
        {
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
                        new[]
                        {
                            (byte)Math.Clamp(liftOffDistance - 1, 0, 255),
                            (byte)Math.Clamp(landingDistance - 1, 0, 255),
                        })),
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

    public async Task<RazerSmartTrackingState> GetSmartTrackingAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        var (classId, sensorId) = ResolveProximitySensor(connection.Definition.ProductId);
        var liftReport = RazerProximitySensorProtocol.CreateGetProximitySensorLiftSettingRequest(
            MiddlewareTransactionId, classId, sensorId);
        var liftResponse = await ExecuteAsync(
                connection,
                new RazerHidRequest("razer_proximity_sensor_get_lift_setting", liftReport),
                cancellationToken)
            .ConfigureAwait(false);
        var lift = RazerProximitySensorProtocol.ParseLiftSetting(liftResponse, liftReport);
        if (lift.LiftMode != RazerProximitySensorProtocol.PresetCalibrationHysteresisMode)
        {
            return new RazerSmartTrackingState(
                RazerSmartTrackingMode.Adaptive,
                lift.LiftHeight + 1,
                0,
                0);
        }

        var configurationReport =
            RazerProximitySensorProtocol.CreateGetProximitySensorConfigurationRequest(
                MiddlewareTransactionId, classId, sensorId);
        var configurationResponse = await ExecuteAsync(
                connection,
                new RazerHidRequest("razer_proximity_sensor_get_configuration", configurationReport),
                cancellationToken)
            .ConfigureAwait(false);
        var configuration = RazerProximitySensorProtocol.ParseConfiguration(
            configurationResponse, configurationReport);
        if (configuration.Parameters.Count < 2)
        {
            throw new InvalidDataException(
                "The proximity sensor configuration did not include lift-off and landing distances.");
        }

        return new RazerSmartTrackingState(
            RazerSmartTrackingMode.Adjustable,
            0,
            configuration.Parameters[0] + 1,
            configuration.Parameters[1] + 1);
    }

    private static (byte ClassId, byte SensorId) ResolveProximitySensor(ushort productId) =>
        RazerProximitySensorProfiles.TryGet(productId, out var profile)
            ? (profile.ClassId, profile.SensorId)
            : (RazerProximitySensorProtocol.DefaultProximityClassId,
                RazerProximitySensorProtocol.AvagoProximitySensorId);
}
