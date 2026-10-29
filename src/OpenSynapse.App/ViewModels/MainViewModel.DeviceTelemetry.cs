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
        if (telemetry.BladeKeyboardBrightness is byte brightness)
        {
            if (Volatile.Read(ref _displayBrightnessRestorePending) == 0)
            {
                SetBladeBrightness(brightness);
            }
            CanSetBladeBrightness = true;
        }
        if (telemetry.BladePerformanceMode is BladePerformanceMode performanceMode)
        {
            SetBladePerformanceMode(performanceMode);
            CanSetBladePerformanceMode = true;
        }
        if (telemetry.BladeCpuBoostMode is BladeCpuBoostMode cpuBoost)
        {
            SetBladeCpuBoost(cpuBoost);
        }
        if (telemetry.BladeGpuBoostMode is BladeGpuBoostMode gpuBoost)
        {
            SetBladeGpuBoost(gpuBoost);
        }
        if (telemetry.BladeMaxFanMode is BladeMaxFanMode maxFan)
        {
            SetBladeMaxFan(maxFan);
        }
        if (telemetry.BladeLogoMode is BladeLogoMode logo)
        {
            SetBladeLogo(logo);
            CanSetBladeLogo = true;
        }
        if (telemetry.BladeFanMode is BladeFanMode fanMode)
        {
            CanSetBladeFan = true;
            BladeFanText = fanMode switch
            {
                BladeFanMode.Automatic when telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.FormatText("AutomaticFanSpeed", cpu, gpu),
                BladeFanMode.Automatic => AppStrings.Text("Text_E3D822CF"),
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int target && telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.FormatText("ManualFanCurrentSpeed", target, cpu, gpu),
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int rpm =>
                    AppStrings.FormatText("ManualFanSpeed", rpm),
                BladeFanMode.Manual => AppStrings.Text("Text_85CC42C6"),
                _ => BladeFanText,
            };
            BladeFanModeText = fanMode switch
            {
                BladeFanMode.Automatic => AppStrings.Text("Text_E3D822CF"),
                BladeFanMode.Manual => AppStrings.Text("Text_71816F86"),
                _ => BladeFanModeText,
            };
            if (fanMode == BladeFanMode.Automatic)
            {
                BladeFanTargetRpmText = "--";
            }
            else if (telemetry.BladeFanTargetRpm is int targetRpm)
            {
                BladeFanTargetRpmText = $"{targetRpm:N0} RPM";
            }
        }
        if (telemetry.BladeCurrentFanCpuRpm is int cpuRpm)
        {
            BladeCurrentFanCpuRpmText = $"{cpuRpm:N0} RPM";
        }
        if (telemetry.BladeCurrentFanGpuRpm is int gpuRpm)
        {
            BladeCurrentFanGpuRpmText = $"{gpuRpm:N0} RPM";
        }
        if (telemetry.BladeAdvancedFanCpuModeRaw is byte cpuFanMode)
        {
            BladeAdvancedFanCpuModeRawText = FormatRawByte(cpuFanMode);
        }
        if (telemetry.BladeAdvancedFanGpuModeRaw is byte gpuFanMode)
        {
            BladeAdvancedFanGpuModeRawText = FormatRawByte(gpuFanMode);
        }
        if (telemetry.BladeGameMode is { } gameMode)
        {
            SetBladeGameMode(gameMode);
        }
        else if (_deviceDescriptors.FirstOrDefault(device =>
                     DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                     device.Access == DeviceAccessState.Available) is { } blade)
        {
            var enabled = ProfileResolver
                .Resolve(_profile, blade, CurrentPowerSource)
                .Blade.GamingModeEnabled == true;
            SetBladeGameMode(new(enabled ? (byte)1 : (byte)0, 0, 0));
        }
        if (telemetry.BladeStartupAnimationEnabled is bool startupAnimationEnabled)
        {
            _blade._bladeStartupAnimationEnabled = startupAnimationEnabled;
            BladeStartupAnimationEnabled = startupAnimationEnabled;
            BladeStartupAnimationText = FormatOptionalState(startupAnimationEnabled);
            OnPropertyChanged(nameof(CanSetBladeStartupAnimation));
            OnPropertyChanged(nameof(CanApplyBladeStartupAnimation));
        }
        if (telemetry.BladeFnKeyMultimediaPrimary is bool fnKeyMultimediaPrimary)
        {
            SetBladeFnKey(fnKeyMultimediaPrimary);
        }
        ApplyFirmwareMcuVersion(telemetry.BladeMcuVersion);
        _blade._bladeSkuHardwareConfiguration = telemetry.BladeSkuHardwareConfiguration;
        if (_blade._bladeSkuHardwareConfiguration?.SupportsNativeDisplayMode == true &&
            telemetry.BladeNativeDisplayMode is BladeNativeDisplayMode nativeDisplayMode)
        {
            _blade._bladeNativeDisplayMode = nativeDisplayMode;
            BladeNativeDisplayModeIndex = (int)nativeDisplayMode;
            OnPropertyChanged(nameof(CanSetBladeNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyBladeNativeDisplayMode));
            OnPropertyChanged(nameof(BladeNativeDisplayModeVisibility));
        }
        else
        {
            _blade._bladeNativeDisplayMode = null;
            BladeNativeDisplayModeIndex = -1;
            OnPropertyChanged(nameof(CanSetBladeNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyBladeNativeDisplayMode));
            OnPropertyChanged(nameof(BladeNativeDisplayModeVisibility));
        }
        if (telemetry.BladeLocalDimmingEnabled is bool localDimmingEnabled)
        {
            _blade._bladeLocalDimmingEnabled = localDimmingEnabled;
            BladeLocalDimmingEnabled = localDimmingEnabled;
            OnPropertyChanged(nameof(CanSetBladeLocalDimming));
            OnPropertyChanged(nameof(CanApplyBladeLocalDimming));
            OnPropertyChanged(nameof(BladeLocalDimmingVisibility));
        }
        else
        {
            _blade._bladeLocalDimmingEnabled = null;
            BladeLocalDimmingEnabled = false;
            OnPropertyChanged(nameof(CanSetBladeLocalDimming));
            OnPropertyChanged(nameof(CanApplyBladeLocalDimming));
            OnPropertyChanged(nameof(BladeLocalDimmingVisibility));
        }
        if (telemetry.BladeCpuPowerBoostEnabled is bool cpuPowerBoostEnabled)
        {
            _blade._bladeCpuPowerBoostEnabled = cpuPowerBoostEnabled;
            BladeCpuPowerBoostEnabled = cpuPowerBoostEnabled;
            OnPropertyChanged(nameof(CanSetBladeCpuPowerBoost));
            OnPropertyChanged(nameof(CanApplyBladeCpuPowerBoost));
            OnPropertyChanged(nameof(BladeCpuPowerBoostVisibility));
        }
        else
        {
            _blade._bladeCpuPowerBoostEnabled = null;
            BladeCpuPowerBoostEnabled = false;
            OnPropertyChanged(nameof(CanSetBladeCpuPowerBoost));
            OnPropertyChanged(nameof(CanApplyBladeCpuPowerBoost));
            OnPropertyChanged(nameof(BladeCpuPowerBoostVisibility));
        }
        if (telemetry.BladeOneTimeFullChargeEnabled is bool oneTimeFullChargeEnabled)
        {
            _blade._bladeOneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
            BladeOneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
            BladeOneTimeFullChargeText = FormatOptionalState(oneTimeFullChargeEnabled);
            OnPropertyChanged(nameof(CanSetBladeOneTimeFullCharge));
            OnPropertyChanged(nameof(CanApplyBladeOneTimeFullCharge));
        }
        if (telemetry.BladeChargeLimitPercent is int chargeLimit)
        {
            SetBladeChargeLimit(chargeLimit);
            CanSetBladeChargeLimit = true;
        }
        if (_deviceDescriptors.Any(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) && device.Access == DeviceAccessState.Available))
        {
            if (_touchpadController?.GetEnabled() is bool touchpadEnabled)
            {
                BladeTouchpadEnabled = touchpadEnabled;
                _blade._confirmedBladeTouchpadEnabled = touchpadEnabled;
                BladeTouchpadText = touchpadEnabled ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
                _blade._canSetBladeTouchpad = true;
                OnPropertyChanged(nameof(CanSetBladeTouchpad));
            }
            else
            {
                BladeTouchpadText = AppStrings.Text("Text_72C29287");
                _blade._canSetBladeTouchpad = false;
                OnPropertyChanged(nameof(CanSetBladeTouchpad));
            }

            BladeStatusText = telemetry.BladeKeyboardBrightness is not null ||
                              telemetry.BladePerformanceMode is not null ||
                              telemetry.BladeChargeLimitPercent is not null
                ? AppStrings.Text("Text_A98448E8")
                : AppStrings.Text("Text_874ADE24");
        }
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

    private void SetBladeBrightness(byte brightness, bool confirm = true)
    {
        var percent = Math.Round(brightness * 100d / 255, MidpointRounding.AwayFromZero);
        BladeBrightnessText = $"{percent:0}%";
        lock (_bladeBrightnessGate)
        {
            if (_desiredBladeBrightness is null)
            {
                BladeBrightnessPercent = percent;
                if (confirm)
                {
                    _blade._confirmedBladeBrightnessPercent = percent;
                }
            }
        }
    }

    private void SetBladePerformanceMode(BladePerformanceMode mode, bool confirm = true)
    {
        _blade.SetPerformanceMode(mode, confirm);
        OnPropertyChanged(nameof(BladePerformanceModeText));
        OnPropertyChanged(nameof(BladePerformanceModeIndex));
        OnPropertyChanged(nameof(BladeCpuBoostText));
        OnPropertyChanged(nameof(BladeCpuBoostIndex));
        OnPropertyChanged(nameof(BladeGpuBoostText));
        OnPropertyChanged(nameof(BladeGpuBoostIndex));
        OnPropertyChanged(nameof(BladeMaxFanText));
        OnPropertyChanged(nameof(BladeMaxFanEnabled));
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
        OnPropertyChanged(nameof(BladeCustomPerformanceVisibility));
    }

    private void SetBladeGameMode(BladeGameModeTelemetry? gameMode)
    {
        _blade.SetGameMode(gameMode);
        OnPropertyChanged(nameof(BladeGameModeText));
        OnPropertyChanged(nameof(BladeGameModeEnabled));
        OnPropertyChanged(nameof(CanSetBladeGamingMode));
        OnPropertyChanged(nameof(CanApplyBladeGamingMode));
    }

    private void SetBladeFnKey(bool multimediaPrimary)
    {
        _blade.SetFnKey(multimediaPrimary);
        OnPropertyChanged(nameof(BladeFnKeyText));
        OnPropertyChanged(nameof(BladeFnKeyMultimediaPrimary));
        OnPropertyChanged(nameof(CanSetBladeFnKey));
        OnPropertyChanged(nameof(CanApplyBladeFnKey));
    }

    private void SetBladeChargeLimit(int percent)
    {
        _blade.SetChargeLimit(percent);
        OnPropertyChanged(nameof(BladeChargeLimitText));
        OnPropertyChanged(nameof(BladeChargeLimitIndex));
        OnPropertyChanged(nameof(CanSetBladeOneTimeFullCharge));
        OnPropertyChanged(nameof(CanApplyBladeOneTimeFullCharge));
    }

    private bool IsBladeCustomMode => _blade.IsCustomMode;

    private void ClearBladeCustomPerformance()
    {
        _blade.ClearCustomPerformance();
        OnPropertyChanged(nameof(BladeCpuBoostText));
        OnPropertyChanged(nameof(BladeCpuBoostIndex));
        OnPropertyChanged(nameof(BladeGpuBoostText));
        OnPropertyChanged(nameof(BladeGpuBoostIndex));
        OnPropertyChanged(nameof(BladeMaxFanText));
        OnPropertyChanged(nameof(BladeMaxFanEnabled));
        Fan.RaiseCurveStateChanged();
    }

    private void SetBladeCpuBoost(BladeCpuBoostMode mode)
    {
        _blade.SetCpuBoost(mode);
        OnPropertyChanged(nameof(BladeCpuBoostText));
        OnPropertyChanged(nameof(BladeCpuBoostIndex));
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
    }

    private void SetBladeGpuBoost(BladeGpuBoostMode mode)
    {
        _blade.SetGpuBoost(mode);
        OnPropertyChanged(nameof(BladeGpuBoostText));
        OnPropertyChanged(nameof(BladeGpuBoostIndex));
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
    }

    private void SetBladeMaxFan(BladeMaxFanMode mode)
    {
        _blade.SetMaxFan(mode);
        OnPropertyChanged(nameof(BladeMaxFanText));
        OnPropertyChanged(nameof(BladeMaxFanEnabled));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
    }

    private void SetBladeLogo(BladeLogoMode mode, bool confirm = true)
    {
        _blade.SetLogo(mode, confirm);
        OnPropertyChanged(nameof(BladeLogoText));
        OnPropertyChanged(nameof(BladeLogoIndex));
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
        BladeStatusText = AppStrings.Text("Text_4626A505");
        BladeBrightnessText = "--";
        BladeBrightnessPercent = 0;
        _blade._confirmedBladeBrightnessPercent = 0;
        BladeBrightnessSelectionText = "--";
        CanSetBladeBrightness = false;
        BladePerformanceModeText = "--";
        BladePerformanceModeIndex = -1;
        _blade._confirmedBladePerformanceModeIndex = -1;
        OnPropertyChanged(nameof(BladeCustomPerformanceVisibility));
        CanSetBladePerformanceMode = false;
        BladeFanText = "--";
        CanSetBladeFan = false;
        BladeFanModeText = "--";
        BladeFanTargetRpmText = "--";
        BladeCurrentFanCpuRpmText = "--";
        BladeCurrentFanGpuRpmText = "--";
        BladeAdvancedFanCpuModeRawText = "--";
        BladeAdvancedFanGpuModeRawText = "--";
        SetBladeGameMode(null);
        BladeStartupAnimationText = "--";
        _blade._bladeStartupAnimationEnabled = null;
        BladeStartupAnimationEnabled = false;
        _blade._bladeFnKeyMultimediaPrimary = null;
        _blade._bladeFnKeyText = "--";
        _firmwareVersions = _firmwareVersions with { Mcu = "--" };
        ApplyFirmwareTexts();
        OnPropertyChanged(nameof(BladeFnKeyText));
        OnPropertyChanged(nameof(BladeFnKeyMultimediaPrimary));
        OnPropertyChanged(nameof(CanSetBladeFnKey));
        OnPropertyChanged(nameof(CanApplyBladeFnKey));
        _blade._bladeNativeDisplayMode = null;
        _blade._bladeSkuHardwareConfiguration = null;
        BladeNativeDisplayModeIndex = -1;
        _blade._bladeLocalDimmingEnabled = null;
        BladeLocalDimmingEnabled = false;
        OnPropertyChanged(nameof(CanSetBladeNativeDisplayMode));
        OnPropertyChanged(nameof(CanApplyBladeNativeDisplayMode));
        OnPropertyChanged(nameof(BladeNativeDisplayModeVisibility));
        OnPropertyChanged(nameof(CanSetBladeLocalDimming));
        OnPropertyChanged(nameof(CanApplyBladeLocalDimming));
        OnPropertyChanged(nameof(BladeLocalDimmingVisibility));
        _blade._bladeCpuPowerBoostEnabled = null;
        BladeCpuPowerBoostEnabled = false;
        OnPropertyChanged(nameof(CanSetBladeCpuPowerBoost));
        OnPropertyChanged(nameof(CanApplyBladeCpuPowerBoost));
        OnPropertyChanged(nameof(BladeCpuPowerBoostVisibility));
        _blade._bladeOneTimeFullChargeEnabled = null;
        BladeOneTimeFullChargeEnabled = false;
        BladeOneTimeFullChargeText = "--";
        BladeChargeLimitText = "--";
        BladeChargeLimitIndex = -1;
        _blade._confirmedBladeChargeLimitIndex = -1;
        CanSetBladeChargeLimit = false;
        ClearBladeCustomPerformance();
        BladeLogoText = "--";
        BladeLogoIndex = -1;
        _blade._confirmedBladeLogoIndex = -1;
        CanSetBladeLogo = false;
        BladeTouchpadText = "--";
        BladeTouchpadEnabled = false;
        _blade._confirmedBladeTouchpadEnabled = false;
        _blade._canSetBladeTouchpad = false;
        OnPropertyChanged(nameof(CanSetBladeTouchpad));
        OnPropertyChanged(nameof(CanSetBladeCpuBoost));
        OnPropertyChanged(nameof(CanSetBladeGpuBoost));
        OnPropertyChanged(nameof(CanSetBladeMaxFan));
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




