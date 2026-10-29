using OpenSynapse.Core.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private void ApplyDeviceTelemetry(RazerDeviceTelemetry telemetry)
    {
        _lastDeviceTelemetry = telemetry;
        Blade.ApplyTelemetry(telemetry);
        Viper.ApplyTelemetry(telemetry);

        DeviceTelemetryTimeText = AppStrings.FormatText("HardwareQueryTime",
            telemetry.CapturedAt.ToLocalTime());
    }

    private void ResetDeviceTelemetry()
    {
        Blade.ResetTelemetry();
        Firmware.ResetMcu();
        Viper.ResetTelemetry();
        DeviceTelemetryTimeText = AppStrings.Text("Text_4E7B2B0B");
    }

}




