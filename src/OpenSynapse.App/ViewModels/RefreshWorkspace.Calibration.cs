namespace OpenSynapse.App.ViewModels;

public sealed partial class RefreshWorkspace
{
    private long _lastCalibrationCheck;

    private async Task MaintainSurfaceCalibrationsAsync(CancellationToken token)
    {
        var now = Environment.TickCount64;
        if (_lastCalibrationCheck != 0 && now - _lastCalibrationCheck < 10_000) return;
        _lastCalibrationCheck = now;
        // Check saved results even if an off/on cycle happened entirely between two ticks.
        await _razerHid.RestoreCalibrationsAsync(_razerHid.CurrentConnections, token, verifySavedWireless: true);
        await _viper.VerifySavedCalibrationAsync(token);
    }
}
