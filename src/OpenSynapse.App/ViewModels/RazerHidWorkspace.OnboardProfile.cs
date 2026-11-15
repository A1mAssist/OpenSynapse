using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidWorkspace
{
    internal async Task ResetActiveOnboardProfileAsync(
        RazerHidDeviceRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (_deviceService is null)
        {
            return;
        }

        var productId = row.Connection.Definition.ProductId;
        if (!row.Connection.IsReady)
        {
            throw new InvalidOperationException("The device is not ready.");
        }

        var profileId = await _deviceService
            .GetActiveOnboardProfileIdAsync(row.Connection, cancellationToken);
        if (profileId == 0)
        {
            _diagnosticLog.TryWrite(
                "razer-onboard-reset",
                $"pid={productId:X4} profile=0 result=no-active-profile");
            throw new InvalidOperationException("The active onboard profile id is unavailable.");
        }

        try
        {
            await _deviceService.ResetOnboardProfileAsync(
                row.Connection, profileId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _diagnosticLog.TryWrite(
                "razer-onboard-reset",
                $"pid={productId:X4} profile={profileId} result=failed " +
                $"error={exception.GetType().Name}: {exception.Message}");
            throw;
        }

        if (SelectedDevice is { } selected &&
            StringComparer.OrdinalIgnoreCase.Equals(
                selected.InstanceId, row.Connection.InstanceId))
        {
            try
            {
                await selected.LoadOnboardProfilesAsync(cancellationToken);
                await selected.LoadMappingAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _diagnosticLog.TryWrite(
                    "razer-onboard-reset",
                    $"pid={productId:X4} profile={profileId} result=applied refresh_failed " +
                    $"error={exception.GetType().Name}: {exception.Message}");
            }
        }

        _diagnosticLog.TryWrite(
            "razer-onboard-reset",
            $"pid={productId:X4} profile={profileId} result=applied");
    }
}
