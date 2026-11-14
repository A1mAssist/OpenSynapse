namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    private async Task RefreshBottomDeadZoneAsync(
        byte profileId,
        CancellationToken cancellationToken)
    {
        if (_analogKeyLayout?.SupportsBottomDeadZone != true)
        {
            // 02A6/02A7/02B0 have no 0xA8; the middleware enables the command
            // only for models whose supportedFeature lists bottomDeadZone.
            _analogDeadZoneUnreadable = false;
            SetAnalogDeadZone(null);
            return;
        }

        try
        {
            var deadZone = await _service.GetAnalogKeyBottomDeadZoneAsync(
                Connection, profileId, cancellationToken);
            _analogDeadZoneUnreadable = false;
            SetAnalogDeadZone(deadZone.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotSupportedException)
        {
            _analogDeadZoneUnreadable = true;
            _analogDeadZoneReported = null;
            _analogKeyBottomDeadZone = null;
            _analogKeyBottomDeadZoneMillimeters = double.NaN;
            OnPropertyChanged(nameof(AnalogKeyBottomDeadZoneMillimeters));
            NotifyAnalogDeadZone();
        }
        catch (Exception)
        {
            // Keep the last value. A timeout is not the same as an unsupported read.
        }
    }
}
