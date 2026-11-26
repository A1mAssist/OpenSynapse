using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    private readonly HashSet<string> _calibrationConnections = new(StringComparer.OrdinalIgnoreCase);

    internal async Task RestoreCalibrationsAsync(IReadOnlyList<RazerHidDeviceConnection> connections,
        CancellationToken cancellationToken, bool verifySavedWireless = false)
    {
        if (_deviceService is null) return;
        var ready = connections.Where(connection => connection.IsReady &&
            connection.Definition.Category == DeviceCategory.Mouse &&
            RazerProximitySensorProfiles.TryGet(connection.Definition.ProductId, out var profile) &&
            profile.Calibration is not null).ToArray();
        var present = ready.Select(ConnectionKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (SelectedDevice is { } selected && (!present.Contains(ConnectionKey(selected.Connection)) ||
            _deviceService.IsCalibrationLinkDown(selected.Connection)))
            await selected.CancelSurfaceCalibrationAsync().ConfigureAwait(true);
        _calibrationConnections.IntersectWith(present);
        foreach (var connection in ready)
        {
            var key = ConnectionKey(connection);
            if (RazerSurfaceCalibrationSession.IsActive(connection.InstanceId) ||
                RazerSurfaceCalibrationOperations.IsBusy(RazerHidDeviceViewModel.CalibrationIdentity(connection))) continue;
            if (!verifySavedWireless && (_calibrationConnections.Contains(key) || _deviceService.IsCalibrationLinkDown(connection))) continue;
            try
            {
                if (verifySavedWireless)
                {
                    if (RazerProximitySensorProfiles.GetConnectionSupport(connection.Definition.ProductId,
                            RazerCalibrationConnection.Wireless) != RazerCalibrationSupport.Supported ||
                        RazerSurfaceCalibrationStore.Shared.Load(RazerHidDeviceViewModel.CalibrationIdentity(connection)) is null)
                        continue;
                    using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    probe.CancelAfter(TimeSpan.FromSeconds(2));
                    var linked = await _deviceService.ReadCalibrationLinkAsync(connection, probe.Token).ConfigureAwait(true);
                    if (SelectedDevice?.Connection.IsSameEndpoint(connection) == true)
                        SelectedDevice.UpdateSurfaceCalibrationAvailability();
                    if (linked == false)
                        continue;
                    key = ConnectionKey(connection);
                }
                _calibrationConnections.Add(key);
                var result = await RazerSurfaceCalibrationStore.Shared.RestoreAsync(
                    RazerHidDeviceViewModel.CalibrationIdentity(connection),
                    _deviceService.CreateSurfaceCalibrationClient(connection), cancellationToken).ConfigureAwait(true);
                if (result is not null && (!verifySavedWireless || result != RazerSurfaceCalibrationReadback.Consistent))
                    _diagnosticLog.TryWrite("surface-calibration", $"{connection.InstanceId} restore={result}");
                if (result == RazerSurfaceCalibrationReadback.Mismatch)
                    ReportOperationError(AppStrings.Text("SurfaceCalibrationRestoreFailed"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _calibrationConnections.Remove(key);
                throw;
            }
            catch (OperationCanceledException) { _calibrationConnections.Remove(key); }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite("surface-calibration", $"{connection.InstanceId} restore failed: {exception.Message}");
                ReportOperationError(AppStrings.Text("SurfaceCalibrationRestoreFailed"));
            }
        }
    }

    private string ConnectionKey(RazerHidDeviceConnection connection) =>
        $"{connection.Definition.ProductId:X4}:{connection.InstanceId}:{connection.DevicePath}:{_deviceService?.GetCalibrationConnectionGeneration(connection)}";
}
