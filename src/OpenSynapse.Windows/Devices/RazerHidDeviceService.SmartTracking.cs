using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public Task SetSmartTrackingAsync(RazerHidDeviceConnection connection, RazerSmartTrackingMode mode,
        int trackingDistance, int liftOffDistance, int landingDistance, CancellationToken cancellationToken = default)
    {
        if (mode is not (RazerSmartTrackingMode.Adaptive or RazerSmartTrackingMode.Adjustable))
            throw new ArgumentOutOfRangeException(nameof(mode));
        ThrowIfCalibrationWriteConflicts(connection);
        return CreateSurfaceCalibrationClient(connection).ApplyAsync(new()
        {
            Mode = mode == RazerSmartTrackingMode.Adaptive ? RazerSurfaceCalibrationMode.Adaptive : RazerSurfaceCalibrationMode.Adjustable,
            TrackingDistance = trackingDistance,
            LiftOffDistance = liftOffDistance,
            LandingDistance = landingDistance,
        }, cancellationToken);
    }

    public Task<RazerSmartTrackingState> GetSmartTrackingAsync(RazerHidDeviceConnection connection, CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(connection, async token =>
        {
            var profile = ResolveCalibrationProfile(connection);
            var report = RazerProximitySensorProtocol.CreateGetProximitySensorLiftSettingRequest(
                MiddlewareTransactionId, profile.ClassId, profile.SensorId);
            var response = await ExecuteAsync(connection, new RazerHidRequest("razer_proximity_sensor_get_lift_setting", report), token).ConfigureAwait(false);
            var lift = RazerProximitySensorProtocol.ParseLiftSetting(response, report);
            if (lift.LiftMode == 1 && profile.Kind is RazerProximityCalibrationKind.Orca5 or RazerProximityCalibrationKind.Orca5Plus or RazerProximityCalibrationKind.SmartPixart)
                return new RazerSmartTrackingState(RazerSmartTrackingMode.Adaptive, lift.LiftHeight + 1, 0, 0);
            if (lift.LiftMode != 4 || profile.Kind is not (RazerProximityCalibrationKind.Orca5 or RazerProximityCalibrationKind.Orca5Plus))
                return new RazerSmartTrackingState(RazerSmartTrackingMode.Unknown, 0, 0, 0) { RawLiftMode = lift.LiftMode };
            report = RazerProximitySensorProtocol.CreateGetProximitySensorConfigurationRequest(MiddlewareTransactionId, profile.ClassId, profile.SensorId);
            response = await ExecuteAsync(connection, new RazerHidRequest("razer_proximity_sensor_get_configuration", report), token).ConfigureAwait(false);
            var data = RazerProximitySensorProtocol.ParseConfiguration(response, report);
            return new RazerSmartTrackingState(RazerSmartTrackingMode.Adjustable, 0, data.Parameters[0] + 1, data.Parameters[1] + 1);
        }, cancellationToken);
}
