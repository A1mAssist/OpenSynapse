using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class ViperWorkspace
{
    private SurfaceCalibrationViewModel? _calibration;
    private string? _calibrationConnection;
    private bool _calibrationRefreshing;
    private bool _calibrationDisposed;

    public SurfaceCalibrationViewModel? Calibration => _calibration;

    internal async Task RefreshCalibrationAsync(CancellationToken cancellationToken)
    {
        if (_calibrationRefreshing || _calibrationDisposed || _calibration?.IsBusy == true) return;
        _calibrationRefreshing = true;
        try
        {
            var descriptor = _deviceDescriptors().FirstOrDefault(device =>
                device.ProtocolFamily == DeviceProtocolFamilies.Viper &&
                device.Access == DeviceAccessState.Available);
            if (descriptor is null || _wirelessLinkDown)
            {
                _calibrationConnection = null;
                _calibration?.UpdateAvailability(false, false);
                return;
            }
            if (StringComparer.OrdinalIgnoreCase.Equals(_calibrationConnection, descriptor.Id)) return;
            if (_deviceTelemetryReader is not RazerDeviceTelemetryReader reader) return;
            if (_calibration is not null) await _calibration.DisposeAsync().ConfigureAwait(true);
            _calibration = null;
            OnPropertyChanged(nameof(Calibration));
            _calibrationConnection = descriptor.Id;
            var client = reader.CreateViperCalibrationClient(descriptor);
            var instanceId = descriptor.InstanceId ?? descriptor.Id;
            var identity = new RazerSurfaceCalibrationIdentity($"viper:{instanceId}:{descriptor.Id}", instanceId,
                descriptor.ProductId, descriptor.Id);
            try
            {
                var result = await RazerSurfaceCalibrationStore.Shared.RestoreAsync(identity, client, cancellationToken).ConfigureAwait(true);
                if (result == RazerSurfaceCalibrationReadback.Mismatch)
                    StatusText = AppStrings.Text("SurfaceCalibrationRestoreFailed");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { StatusText = AppStrings.Text("SurfaceCalibrationRestoreFailed"); }
            if (_calibrationDisposed) return;
            cancellationToken.ThrowIfCancellationRequested();
            _calibration = new(client, identity, () => !_wirelessLinkDown,
                connection: RazerCalibrationConnection.Wireless);
            OnPropertyChanged(nameof(Calibration));
            await _calibration.EnsureLoadedAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _calibrationConnection = null;
        }
        catch (Exception)
        {
            if (_calibration is null) _calibrationConnection = null;
            StatusText = AppStrings.Text("SurfaceCalibrationReadFailed");
        }
        finally { _calibrationRefreshing = false; }
    }

    internal Task CancelCalibrationAsync() => _calibration?.CancelAsync() ?? Task.CompletedTask;

    internal async Task VerifySavedCalibrationAsync(CancellationToken cancellationToken)
    {
        if (_calibrationDisposed || _calibrationRefreshing || _calibration?.IsSessionActive == true ||
            _deviceTelemetryReader is not RazerDeviceTelemetryReader reader) return;
        var descriptor = _deviceDescriptors().FirstOrDefault(device =>
            device.ProtocolFamily == DeviceProtocolFamilies.Viper && device.Access == DeviceAccessState.Available);
        if (descriptor is null) return;
        var instanceId = descriptor.InstanceId ?? descriptor.Id;
        var identity = new RazerSurfaceCalibrationIdentity($"viper:{instanceId}:{descriptor.Id}", instanceId,
            descriptor.ProductId, descriptor.Id);
        if (RazerSurfaceCalibrationOperations.IsBusy(identity)) return;
        try
        {
            if (RazerSurfaceCalibrationStore.Shared.Load(identity) is null) return;
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probe.CancelAfter(TimeSpan.FromSeconds(2));
            var linked = await reader.ReadViperCalibrationLinkAsync(descriptor, probe.Token).ConfigureAwait(true);
            if (linked is { } isLinked) _wirelessLinkDown = !isLinked;
            if (linked == false)
            {
                _calibrationConnection = null;
                _calibration?.UpdateAvailability(false, false);
                return;
            }
            var result = await RazerSurfaceCalibrationStore.Shared.RestoreAsync(identity,
                reader.CreateViperCalibrationClient(descriptor), cancellationToken).ConfigureAwait(true);
            _calibration?.UpdateAvailability(true, true);
            if (result == RazerSurfaceCalibrationReadback.Mismatch)
                StatusText = AppStrings.Text("SurfaceCalibrationRestoreFailed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { StatusText = AppStrings.Text("SurfaceCalibrationRestoreFailed"); }
    }

    internal async ValueTask DisposeCalibrationAsync()
    {
        _calibrationDisposed = true;
        if (_calibration is not null) await _calibration.DisposeAsync().ConfigureAwait(true);
        _calibration = null;
        _calibrationConnection = null;
    }
}
