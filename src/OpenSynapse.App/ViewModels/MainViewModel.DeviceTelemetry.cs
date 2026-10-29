using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;
using OpenSynapse.Windows.Sensors;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private void ApplyDeviceTelemetry(RazerDeviceTelemetry telemetry)
    {
        _lastDeviceTelemetry = telemetry;
        Blade.ApplyTelemetry(telemetry);
        if (telemetry.ViperBatteryPercent is int battery)
        {
            ViperBatteryText = $"{battery}%";
        }
        var viper = _deviceDescriptors.FirstOrDefault(device =>
            device.ProtocolFamily == DeviceProtocolFamilies.Viper &&
            device.Access == DeviceAccessState.Available);
        if (viper is not null)
        {
            if (telemetry.ViperBatteryPercent is not null)
            {
                Telemetry.NotifyBatteryObserved(new DeviceBatteryReading(
                    $"viper:{viper.Id}", viper.Name, telemetry.ViperBatteryPercent, null));
            }
            var chemistry = ProfileResolver.Resolve(
                _profile,
                viper,
                CurrentPowerSource).Viper.BatteryChemistry;
            ViperBatteryChemistryIndex = chemistry is byte value ? value : -1;
            _viper._confirmedViperBatteryChemistryIndex = ViperBatteryChemistryIndex;
            OnPropertyChanged(nameof(ViperBatteryChemistryText));
        }
        if (telemetry.ViperPollingRateHertz is int pollingRate)
        {
            ViperPollingRateText = $"{pollingRate} Hz";
            ViperPollingRateIndex = pollingRate switch { 125 => 0, 500 => 1, _ => 2 };
            _viper._confirmedViperPollingRateIndex = ViperPollingRateIndex;
            CanSetViperPollingRate = true;
        }
        if (telemetry.ViperDpiX is int dpiX && telemetry.ViperDpiY is int dpiY)
        {
            ViperDpiText = $"{dpiX} × {dpiY}";
            ViperDpiXValue = dpiX;
            ViperDpiYValue = dpiY;
            _viper._confirmedViperDpiXValue = dpiX;
            _viper._confirmedViperDpiYValue = dpiY;
            CanSetViperDpi = true;
        }
        if (telemetry.ViperIdleSeconds is int idleSeconds)
        {
            ViperIdleText = FormatDuration(idleSeconds);
            ViperIdleMinutesValue = idleSeconds / 60d;
            _viper._confirmedViperIdleMinutesValue = ViperIdleMinutesValue;
            CanSetViperIdle = true;
        }
        if (telemetry.ViperDpiStages is { } stages)
        {
            SetViperDpiStages(stages);
            CanSetViperDpiStages = true;
        }
        if (telemetry.ViperLowBatteryThresholdRaw is byte raw)
        {
            ViperLowBatteryThresholdPercent = ViperLowBatteryThresholdProtocol.ToPercent(raw);
            ViperLowBatteryThresholdText = $"{ViperLowBatteryThresholdPercent}%";
            _viper._confirmedViperLowBatteryThresholdPercent = ViperLowBatteryThresholdPercent;
            CanSetViperLowBatteryThreshold = true;
        }
        if (_deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Viper && device.Access == DeviceAccessState.Available))
        {
            CanSetViperBatteryChemistry = true;
            _viper._canReadViperButtonMappings = true;
            OnPropertyChanged(nameof(CanReadViperButtonMappings));
            ViperStatusText = telemetry.ViperBatteryPercent is not null ||
                              telemetry.ViperPollingRateHertz is not null ||
                              telemetry.ViperDpiX is not null ||
                              telemetry.ViperIdleSeconds is not null
                ? AppStrings.Text("Text_7EEF96A9")
                : AppStrings.Text("Text_2E25FD9A");
        }

        DeviceTelemetryTimeText = AppStrings.FormatText("HardwareQueryTime",
            telemetry.CapturedAt.ToLocalTime());
    }

    private void SetViperDpiStages(ViperDpiStagesTelemetry stages, bool confirm = true)
    {
        _viper.SetDpiStages(stages, confirm);
        OnPropertyChanged(nameof(ViperDpiStageCount));
        OnPropertyChanged(nameof(ViperActiveDpiStage));
        OnPropertyChanged(nameof(ViperDpiStagesText));
    }

    private void ResizeViperDpiStages(int count)
    {
        _viper.ResizeDpiStages(count);
        OnPropertyChanged(nameof(ViperDpiStageCount));
        OnPropertyChanged(nameof(ViperActiveDpiStage));
    }

    private void RestoreViperDpiStages()
    {
        _viper.RestoreDpiStages();
        OnPropertyChanged(nameof(ViperDpiStageCount));
        OnPropertyChanged(nameof(ViperActiveDpiStage));
        OnPropertyChanged(nameof(ViperDpiStagesText));
    }

    private void ResetDeviceTelemetry()
    {
        Blade.ResetTelemetry();
        _firmwareVersions = _firmwareVersions with { Mcu = "--" };
        ApplyFirmwareTexts();
        ResetViperTelemetry();
        DeviceTelemetryTimeText = AppStrings.Text("Text_4E7B2B0B");
    }

    private void ResetViperTelemetry()
    {
        _viper.Reset();
        foreach (var propertyName in new[]
        {
            nameof(ViperStatusText), nameof(ViperDpiStagesText), nameof(ViperLowBatteryThresholdText),
            nameof(ViperLowBatteryThresholdPercent), nameof(CanSetViperLowBatteryThreshold),
            nameof(ViperBatteryText), nameof(ViperPollingRateText), nameof(ViperPollingRateIndex),
            nameof(ViperBatteryChemistryIndex),
            nameof(ViperBatteryChemistryText),
            nameof(CanSetViperBatteryChemistry),
            nameof(CanSetViperPollingRate), nameof(ViperDpiText), nameof(ViperDpiXValue),
            nameof(ViperDpiYValue), nameof(CanSetViperDpi), nameof(ViperIdleText),
            nameof(ViperIdleMinutesValue), nameof(CanSetViperIdle), nameof(ViperDpiStageCount),
            nameof(ViperActiveDpiStage), nameof(CanSetViperDpiStages), nameof(VisibleViperButtonAssignments),
            nameof(ViperButtonMappingsText), nameof(CanReadViperButtonMappings), nameof(CanSetViperButtonMappings),
        })
        {
            OnPropertyChanged(propertyName);
        }
    }

}




