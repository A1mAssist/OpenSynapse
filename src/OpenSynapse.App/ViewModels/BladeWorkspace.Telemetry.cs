using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.UI;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Displays;
using OpenSynapse.Windows.Lifecycle;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using static OpenSynapse.App.ViewModels.DeviceUiCatalog;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_blade._bladeGameModeState is byte gameMode)
        {
            try
            {
                SetGameMode(await _deviceTelemetryReader.SetBladeGameModeAsync(
                    ActiveDescriptors,
                    gameMode != 0,
                    cancellationToken));
            }
            catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
            {
                _diagnosticLog.TryWrite(
                    "device-operation",
                    $"game mode indicator startup sync failed: {exception}");
                SetDeviceOperationError(AppStrings.FormatText("LabeledError",
                    AppStrings.Text("Text_BF98AC8A"),
                    exception.Message));
            }
        }
    }

    internal void ApplyTelemetry(RazerDeviceTelemetry telemetry)
    {
        if (telemetry.BladeKeyboardBrightness is byte brightness)
        {
            if (!_isBrightnessRestorePending())
            {
                SetBrightness(brightness);
            }
            CanSetBrightness = true;
        }
        if (telemetry.BladePerformanceMode is BladePerformanceMode performanceMode)
        {
            SetPerformanceMode(performanceMode);
            CanSetPerformanceMode = true;
        }
        if (telemetry.BladeCpuBoostMode is BladeCpuBoostMode cpuBoost)
        {
            SetCpuBoost(cpuBoost);
        }
        if (telemetry.BladeGpuBoostMode is BladeGpuBoostMode gpuBoost)
        {
            SetGpuBoost(gpuBoost);
        }
        if (telemetry.BladeMaxFanMode is BladeMaxFanMode maxFan)
        {
            SetMaxFan(maxFan);
        }
        if (telemetry.BladeLogoMode is BladeLogoMode logo)
        {
            SetLogo(logo);
            CanSetLogo = true;
        }
        else if (telemetry.BladeLogoEffectRaw is not null)
        {
            _blade.SetLogoUnavailable();
            OnPropertyChanged(nameof(LogoText));
            OnPropertyChanged(nameof(LogoIndex));
            CanSetLogo = true;
        }
        if (telemetry.BladeFanMode is BladeFanMode fanMode)
        {
            _blade._bladeTelemetryFanMode = fanMode;
            _blade._bladeTelemetryFanTargetRpm = telemetry.BladeFanTargetRpm;
            _fan().ObserveFanTelemetry(fanMode, telemetry.BladeFanTargetRpm);
            CanSetFan = true;
            FanText = fanMode switch
            {
                BladeFanMode.Automatic when telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.FormatText("AutomaticFanSpeed", cpu, gpu),
                BladeFanMode.Automatic => AppStrings.Text("Text_E3D822CF"),
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int target && telemetry.BladeCurrentFanCpuRpm is int cpu && telemetry.BladeCurrentFanGpuRpm is int gpu =>
                    AppStrings.FormatText("ManualFanCurrentSpeed", target, cpu, gpu),
                BladeFanMode.Manual when telemetry.BladeFanTargetRpm is int rpm =>
                    AppStrings.FormatText("ManualFanSpeed", rpm),
                BladeFanMode.Manual => AppStrings.Text("Text_85CC42C6"),
                _ => FanText,
            };
            FanModeText = fanMode switch
            {
                BladeFanMode.Automatic => AppStrings.Text("Text_E3D822CF"),
                BladeFanMode.Manual => AppStrings.Text("Text_71816F86"),
                _ => FanModeText,
            };
            if (fanMode == BladeFanMode.Automatic)
            {
                FanTargetRpmText = "--";
            }
            else if (telemetry.BladeFanTargetRpm is int targetRpm)
            {
                FanTargetRpmText = $"{targetRpm:N0} RPM";
            }
        }
        if (telemetry.BladeCurrentFanCpuRpm is int cpuRpm)
        {
            CurrentFanCpuRpm = cpuRpm;
            CurrentFanCpuRpmText = $"{cpuRpm:N0} RPM";
        }
        if (telemetry.BladeCurrentFanGpuRpm is int gpuRpm)
        {
            CurrentFanGpuRpm = gpuRpm;
            CurrentFanGpuRpmText = $"{gpuRpm:N0} RPM";
        }
        if (telemetry.BladeAdvancedFanCpuModeRaw is byte cpuFanMode)
        {
            AdvancedFanCpuModeRawText = FormatRawByte(cpuFanMode);
        }
        if (telemetry.BladeAdvancedFanGpuModeRaw is byte gpuFanMode)
        {
            AdvancedFanGpuModeRawText = FormatRawByte(gpuFanMode);
        }
        _blade._bladeGameModeWriteSupported = telemetry.BladeGamingModeWritable;
        OnPropertyChanged(nameof(CanSetGamingMode));
        OnPropertyChanged(nameof(CanApplyGamingMode));
        OnPropertyChanged(nameof(GamingModeVisibility));
        if (telemetry.BladeGameMode is { } gameMode)
        {
            SetGameMode(gameMode);
        }
        else if (ActiveDescriptors.FirstOrDefault(device =>
                     DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                     device.Access == DeviceAccessState.Available) is { } blade)
        {
            var enabled = ProfileResolver
                .Resolve(Profile, blade, CurrentPowerSource)
                .Blade.GamingModeEnabled == true;
            SetGameMode(new(enabled ? (byte)1 : (byte)0, 0, 0));
        }
        if (telemetry.BladeStartupAnimationEnabled is bool startupAnimationEnabled)
        {
            _blade._bladeStartupAnimationEnabled = startupAnimationEnabled;
            StartupAnimationEnabled = startupAnimationEnabled;
            StartupAnimationText = FormatOptionalState(startupAnimationEnabled);
            OnPropertyChanged(nameof(CanSetStartupAnimation));
            OnPropertyChanged(nameof(CanApplyStartupAnimation));
        }
        if (telemetry.BladeFnKeyMultimediaPrimary is bool fnKeyMultimediaPrimary)
        {
            SetFnKey(fnKeyMultimediaPrimary);
        }
        else if (_blade._bladeFnKeyMultimediaPrimary is not null)
        {
            SetFnKey(null);
        }
        SetAutoSleep(
            telemetry.BladeAutoSleepEnabled ?? _blade._bladeAutoSleepEnabled,
            telemetry.BladeAutoSleepWritable);
        SetTimeToDim(
            telemetry.BladeTimeToDim ?? _blade._bladeTimeToDim,
            telemetry.BladeTimeToDimWritable);
        SetIndicatorLed(
            telemetry.BladeIndicatorLedEnabled ?? _blade._bladeIndicatorLedEnabled,
            telemetry.BladeIndicatorLedWritable);
        SetGpuCardDetection(
            telemetry.BladeGpuCardDetection ?? _blade._bladeGpuCardDetection,
            telemetry.BladeGpuCardDetectionWritable);
        _applyFirmwareMcuVersion(telemetry.BladeMcuVersion);
        _blade._bladeSkuHardwareConfiguration = telemetry.BladeSkuHardwareConfiguration;
        if (_blade._bladeSkuHardwareConfiguration?.SupportsNativeDisplayMode == true &&
            telemetry.BladeNativeDisplayMode is BladeNativeDisplayMode nativeDisplayMode)
        {
            _blade._bladeNativeDisplayMode = nativeDisplayMode;
            NativeDisplayModeIndex = (int)nativeDisplayMode;
            OnPropertyChanged(nameof(CanSetNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
            OnPropertyChanged(nameof(NativeDisplayModeVisibility));
        }
        else
        {
            _blade._bladeNativeDisplayMode = null;
            NativeDisplayModeIndex = -1;
            OnPropertyChanged(nameof(CanSetNativeDisplayMode));
            OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
            OnPropertyChanged(nameof(NativeDisplayModeVisibility));
        }
        if (telemetry.BladeLocalDimmingEnabled is bool localDimmingEnabled)
        {
            _blade._bladeLocalDimmingEnabled = localDimmingEnabled;
            LocalDimmingEnabled = localDimmingEnabled;
            OnPropertyChanged(nameof(CanSetLocalDimming));
            OnPropertyChanged(nameof(CanApplyLocalDimming));
            OnPropertyChanged(nameof(LocalDimmingVisibility));
        }
        else
        {
            _blade._bladeLocalDimmingEnabled = null;
            LocalDimmingEnabled = false;
            OnPropertyChanged(nameof(CanSetLocalDimming));
            OnPropertyChanged(nameof(CanApplyLocalDimming));
            OnPropertyChanged(nameof(LocalDimmingVisibility));
        }
        if (telemetry.BladeCpuPowerBoostEnabled is bool cpuPowerBoostEnabled)
        {
            _blade._bladeCpuPowerBoostEnabled = cpuPowerBoostEnabled;
            CpuPowerBoostEnabled = cpuPowerBoostEnabled;
            OnPropertyChanged(nameof(CanSetCpuPowerBoost));
            OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
            OnPropertyChanged(nameof(CpuPowerBoostVisibility));
        }
        else
        {
            _blade._bladeCpuPowerBoostEnabled = null;
            CpuPowerBoostEnabled = false;
            OnPropertyChanged(nameof(CanSetCpuPowerBoost));
            OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
            OnPropertyChanged(nameof(CpuPowerBoostVisibility));
        }
        if (telemetry.BladeOneTimeFullChargeEnabled is bool oneTimeFullChargeEnabled)
        {
            _blade._bladeOneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
            OneTimeFullChargeEnabled = oneTimeFullChargeEnabled;
            OneTimeFullChargeText = FormatOptionalState(oneTimeFullChargeEnabled);
            OnPropertyChanged(nameof(CanSetOneTimeFullCharge));
            OnPropertyChanged(nameof(CanApplyOneTimeFullCharge));
        }
        if (telemetry.BladeChargeLimitPercent is int chargeLimit)
        {
            SetChargeLimit(chargeLimit);
            CanSetChargeLimit = true;
        }
        if (ActiveDescriptors.Any(device => DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) && device.Access == DeviceAccessState.Available))
        {
            if (_touchpadController?.GetEnabled() is bool touchpadEnabled)
            {
                TouchpadEnabled = touchpadEnabled;
                _blade._confirmedBladeTouchpadEnabled = touchpadEnabled;
                TouchpadText = touchpadEnabled ? AppStrings.Text("Text_F55AD712") : AppStrings.Text("Text_7E3B0F3C");
                _blade._canSetBladeTouchpad = true;
                OnPropertyChanged(nameof(CanSetTouchpad));
            }
            else
            {
                TouchpadText = AppStrings.Text("Text_72C29287");
                _blade._canSetBladeTouchpad = false;
                OnPropertyChanged(nameof(CanSetTouchpad));
            }

            StatusText = telemetry.BladeKeyboardBrightness is not null ||
                         telemetry.BladePerformanceMode is not null ||
                         telemetry.BladeChargeLimitPercent is not null
                ? AppStrings.Text("Text_A98448E8")
                : AppStrings.Text("Text_874ADE24");
        }
        // No blanket refresh on this path: it runs on every readout tick and would
        // rebuild every bound option list, closing an open combo box each time.
    }

    internal void ResetTelemetry()
    {
        StatusText = AppStrings.Text("Text_4626A505");
        BrightnessText = "--";
        BrightnessPercent = 0;
        _blade._confirmedBladeBrightnessPercent = 0;
        BrightnessSelectionText = "--";
        CanSetBrightness = false;
        PerformanceModeText = "--";
        PerformanceModeIndex = -1;
        _blade._confirmedBladePerformanceModeIndex = -1;
        OnPropertyChanged(nameof(CustomPerformanceVisibility));
        CanSetPerformanceMode = false;
        FanText = "--";
        CanSetFan = false;
        FanModeText = "--";
        FanTargetRpmText = "--";
        CurrentFanCpuRpmText = "--";
        CurrentFanGpuRpmText = "--";
        CurrentFanCpuRpm = null;
        CurrentFanGpuRpm = null;
        AdvancedFanCpuModeRawText = "--";
        AdvancedFanGpuModeRawText = "--";
        SetGameMode(null);
        _blade._bladeGameModeWriteSupported = false;
        OnPropertyChanged(nameof(CanSetGamingMode));
        OnPropertyChanged(nameof(CanApplyGamingMode));
        OnPropertyChanged(nameof(GamingModeVisibility));
        StartupAnimationText = "--";
        _blade._bladeStartupAnimationEnabled = null;
        StartupAnimationEnabled = false;
        OnPropertyChanged(nameof(CanSetStartupAnimation));
        SetAutoSleep(null, false);
        SetTimeToDim(null, false);
        SetIndicatorLed(null, false);
        SetGpuCardDetection(null, false);
        _blade._bladeFnKeyMultimediaPrimary = null;
        _blade._bladeFnKeyText = "--";
        OnPropertyChanged(nameof(FnKeyText));
        OnPropertyChanged(nameof(FnKeyMultimediaPrimary));
        OnPropertyChanged(nameof(CanSetFnKey));
        OnPropertyChanged(nameof(CanApplyFnKey));
        OnPropertyChanged(nameof(FnKeyCardVisibility));
        OnPropertyChanged(nameof(FnKeyFunctionPrimarySelected));
        _blade._bladeNativeDisplayMode = null;
        _blade._bladeSkuHardwareConfiguration = null;
        NativeDisplayModeIndex = -1;
        _blade._bladeLocalDimmingEnabled = null;
        LocalDimmingEnabled = false;
        OnPropertyChanged(nameof(CanSetNativeDisplayMode));
        OnPropertyChanged(nameof(CanApplyNativeDisplayMode));
        OnPropertyChanged(nameof(NativeDisplayModeVisibility));
        OnPropertyChanged(nameof(CanSetLocalDimming));
        OnPropertyChanged(nameof(CanApplyLocalDimming));
        OnPropertyChanged(nameof(LocalDimmingVisibility));
        _blade._bladeCpuPowerBoostEnabled = null;
        CpuPowerBoostEnabled = false;
        OnPropertyChanged(nameof(CanSetCpuPowerBoost));
        OnPropertyChanged(nameof(CanApplyCpuPowerBoost));
        OnPropertyChanged(nameof(CpuPowerBoostVisibility));
        _blade._bladeOneTimeFullChargeEnabled = null;
        OneTimeFullChargeEnabled = false;
        OneTimeFullChargeText = "--";
        ChargeLimitText = "--";
        ChargeLimitIndex = -1;
        _blade._confirmedBladeChargeLimitIndex = -1;
        CanSetChargeLimit = false;
        ClearCustomPerformance();
        LogoText = "--";
        LogoIndex = -1;
        _blade._confirmedBladeLogoIndex = -1;
        CanSetLogo = false;
        TouchpadText = "--";
        TouchpadEnabled = false;
        _blade._confirmedBladeTouchpadEnabled = false;
        _blade._canSetBladeTouchpad = false;
        OnPropertyChanged(nameof(CanSetTouchpad));
        OnPropertyChanged(nameof(CanSetCpuBoost));
        OnPropertyChanged(nameof(CanSetGpuBoost));
        OnPropertyChanged(nameof(CanSetMaxFan));
        NotifyStateChanged();
    }
}
