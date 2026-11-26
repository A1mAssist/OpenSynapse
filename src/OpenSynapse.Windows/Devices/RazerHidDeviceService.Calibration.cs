using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public RazerSurfaceCalibrationClient CreateSurfaceCalibrationClient(RazerHidDeviceConnection connection)
    {
        RequireReady(connection);
        return new(ResolveCalibrationProfile(connection),
            (report, token) => ExecuteAsync(connection,
                new RazerHidRequest($"razer_calibration_{report[7]:X2}_{report[8]:X2}", report), token),
            (action, token) => RunExclusiveAsync(connection, action, token));
    }

    private static RazerProximitySensorProfile ResolveCalibrationProfile(RazerHidDeviceConnection connection) =>
        RazerProximitySensorProfiles.TryGet(connection.Definition.ProductId, out var profile)
            ? profile : throw new NotSupportedException("This product has no confirmed surface calibration profile.");

    private static void ThrowIfCalibrationWriteConflicts(RazerHidDeviceConnection connection)
    {
        if (RazerSurfaceCalibrationSession.IsActive(connection.InstanceId) &&
            !RazerSurfaceCalibrationSession.IsCurrentSession(connection.InstanceId))
            throw new InvalidOperationException("Finish or cancel this device's surface calibration before changing its settings.");
    }
}
