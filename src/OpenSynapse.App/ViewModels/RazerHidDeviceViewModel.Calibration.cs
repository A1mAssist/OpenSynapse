using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private SurfaceCalibrationViewModel? _calibration;
    private bool _calibrationWasActive;

    public SurfaceCalibrationViewModel? Calibration
    {
        get
        {
            if (_calibration is null && Volatile.Read(ref _disposed) == 0 && IsReady && Connection.Definition.Category == DeviceCategory.Mouse &&
                RazerProximitySensorProfiles.TryGet(Connection.Definition.ProductId, out var profile) &&
                profile.Calibration is not null)
            {
                _calibration = new(_service.CreateSurfaceCalibrationClient(Connection),
                    CalibrationIdentity(Connection), () => IsReady && !RequiresRescan && !_service.IsCalibrationLinkDown(Connection), log: _log);
                _calibration.PropertyChanged += (_, args) =>
                {
                    if ((args.PropertyName is null or "" or nameof(SurfaceCalibrationViewModel.IsSessionActive)) &&
                        _calibrationWasActive != _calibration.IsSessionActive)
                    {
                        _calibrationWasActive = _calibration.IsSessionActive;
                        OnCapabilityChanged();
                    }
                };
            }
            return _calibration;
        }
    }

    // Without a proven cross-link identity, keep USB and receiver records independent.
    internal static RazerSurfaceCalibrationIdentity CalibrationIdentity(RazerHidDeviceConnection connection) =>
        new($"hid:{connection.InstanceId}:{connection.DevicePath}", connection.InstanceId,
            connection.Definition.ProductId, connection.DevicePath);

    internal Task LoadCalibrationAsync(CancellationToken cancellationToken = default) =>
        Calibration?.EnsureLoadedAsync(cancellationToken) ?? Task.CompletedTask;

    internal Task CancelSurfaceCalibrationAsync() => _calibration?.CancelAsync() ?? Task.CompletedTask;

    internal void UpdateSurfaceCalibrationAvailability() =>
        _calibration?.UpdateAvailability(IsReady && !RequiresRescan && !_service.IsCalibrationLinkDown(Connection),
            IsReady && !RequiresRescan && !_service.IsCalibrationLinkDown(Connection));

    private void NotifyCalibrationChanged() => UpdateSurfaceCalibrationAvailability();
}
