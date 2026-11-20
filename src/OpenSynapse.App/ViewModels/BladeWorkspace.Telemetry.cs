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
    private bool _startupStateSyncStarted;
    private bool _gameModeReadFromDevice;
    private bool _touchpadStateRead;
    private long _gameModeUserWriteAt;

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Recorded for diagnosis only: this build does not gate any mode on the SKU.
        _diagnosticLog.TryWrite(
            "blade",
            $"SystemSKU={SystemSkuReader.TryRead() ?? "<unreadable>"}");

        if (_gameModeReadFromDevice && _blade._bladeGameModeState is byte gameMode)
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

        if (_touchpadController?.GetEnabled() is bool touchpadEnabled)
        {
            try
            {
                // Synapse mirrors the Windows touchpad state onto the EC on
                // startup so the indicator does not stay stale across restarts.
                if (!await _deviceTelemetryReader.SetBladeTouchpadStatusAsync(
                        ActiveDescriptors, touchpadEnabled, cancellationToken))
                {
                    _diagnosticLog.TryWrite(
                        "device-operation",
                        $"touchpad indicator startup sync did not acknowledge enabled={touchpadEnabled}");
                }
            }
            catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
            {
                _diagnosticLog.TryWrite(
                    "device-operation",
                    $"touchpad indicator startup sync failed: {exception}");
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
        if (_blade._bladeFanRange != telemetry.BladeFanRange)
        {
            _blade._bladeFanRange = telemetry.BladeFanRange;
            _fan().RefreshBladeFanCurveEditor();
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
        if (telemetry.BladeGameMode is { } gameMode) ApplyObservedGameMode(gameMode);
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
        if (telemetry.BladeNativeDisplayMode is BladeNativeDisplayMode nativeDisplayMode &&
            BladeModelCapabilities.SupportsNativeDisplayMode(
                _blade._bladeProductId, _blade._bladeSkuHardwareConfiguration))
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
            ApplyTouchpadState();

            StatusText = telemetry.BladeKeyboardBrightness is not null ||
                         telemetry.BladePerformanceMode is not null ||
                         telemetry.BladeChargeLimitPercent is not null
                ? AppStrings.Text("Text_A98448E8")
                : AppStrings.Text("Text_874ADE24");
            QueueStartupStateSync();
        }
        // No blanket refresh on this path: it runs on every readout tick and would
        // rebuild every bound option list, closing an open combo box each time.
    }

    private void ApplyObservedGameMode(BladeGameModeTelemetry gameMode)
    {
        var hadState = _blade._bladeGameModeState is not null;
        var wasRead = _gameModeReadFromDevice;
        _gameModeReadFromDevice = true;
        if (_blade._bladeGameModeState == gameMode.GameMode)
        {
            return;
        }

        SetGameMode(gameMode);
        if (wasRead && hadState &&
            Environment.TickCount64 - _gameModeUserWriteAt > 3000)
        {
            // The Fn key changes the EC state directly; report it like a user
            // toggle so the toast still appears when the key path itself fails.
            GamingModeChangedByUser?.Invoke(gameMode.GameMode != 0);
        }
    }

    private void ApplyTouchpadState()
    {
        if (_touchpadController?.GetEnabled() is bool touchpadEnabled)
        {
            var firstRead = !_touchpadStateRead;
            var changed = !firstRead &&
                _blade._confirmedBladeTouchpadEnabled != touchpadEnabled;
            _touchpadStateRead = true;
            TouchpadEnabled = touchpadEnabled;
            _blade._confirmedBladeTouchpadEnabled = touchpadEnabled;
            TouchpadText = touchpadEnabled
                ? AppStrings.Text("Text_F55AD712")
                : AppStrings.Text("Text_7E3B0F3C");
            _blade._canSetBladeTouchpad = true;
            OnPropertyChanged(nameof(CanSetTouchpad));
            if (changed || firstRead)
            {
                // Also write on the first read: the lighting take-over can reset
                // the EC indicator after the one-shot startup sync.
                _ = SyncObservedTouchpadIndicatorAsync(ActiveDescriptors, touchpadEnabled);
            }
            if (changed)
            {
                // Mirror an Fn-key change onto the EC and report it the same way
                // as a toggle that came through our mapping runtime.
                TouchpadChangedByUser?.Invoke(touchpadEnabled);
            }
        }
        else
        {
            TouchpadText = AppStrings.Text("Text_72C29287");
            _blade._canSetBladeTouchpad = false;
            OnPropertyChanged(nameof(CanSetTouchpad));
        }
    }

    private async Task SyncObservedTouchpadIndicatorAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        bool enabled)
    {
        try
        {
            if (!await _deviceTelemetryReader.SetBladeTouchpadStatusAsync(
                    devices, enabled, CancellationToken.None))
            {
                _diagnosticLog.TryWrite(
                    "device-operation",
                    $"touchpad indicator did not follow the device state enabled={enabled}");
            }
            else
            {
                _diagnosticLog.TryWrite(
                    "blade-touchpad",
                    $"indicator synchronized enabled={enabled}");
            }
        }
        catch (Exception exception) when (RuntimeErrors.IsExpected(exception))
        {
            _diagnosticLog.TryWrite(
                "device-operation",
                $"touchpad indicator follow-up failed: {exception}");
        }
    }

    internal void RefreshTouchpadState() => ApplyTouchpadState();

    private void QueueStartupStateSync()
    {
        if (_startupStateSyncStarted || IsDisposed)
        {
            return;
        }

        _startupStateSyncStarted = true;
        _ = ObserveStartupStateSyncAsync();
    }

    private async Task ObserveStartupStateSyncAsync()
    {
        try
        {
            await InitializeAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _diagnosticLog.TryWrite("blade", $"startup state sync failed: {exception}");
        }
    }

    internal void ApplyLiveTelemetry(RazerDeviceLiveTelemetry telemetry)
    {
        if (telemetry.BladePerformanceMode is BladePerformanceMode performanceMode)
        {
            SetPerformanceMode(performanceMode);
            CanSetPerformanceMode = true;
        }
        // Game mode and the touchpad toggle only change through the Fn keys, so
        // they are not part of the fan/performance readout the device page polls.
        // Reading them here keeps a failed first read at boot from sticking.
        if (telemetry.BladeGameMode is { } gameMode)
        {
            if (_blade._bladeGameModeState != gameMode.GameMode)
            {
                _diagnosticLog.TryWrite(
                    "blade-live",
                    $"game mode {_blade._bladeGameModeState?.ToString() ?? "?"} -> {gameMode.GameMode}");
            }
            ApplyObservedGameMode(gameMode);
        }
        if (telemetry.BladeKeyboardBrightness is byte brightness)
        {
            var percent = Math.Round(brightness * 100d / 255, MidpointRounding.AwayFromZero);
            if (Math.Abs(_blade._confirmedBladeBrightnessPercent - percent) > 0.5)
            {
                _diagnosticLog.TryWrite(
                    "blade-live",
                    $"brightness {_blade._confirmedBladeBrightnessPercent}% -> {percent:0}%");
            }
            if (!_isBrightnessRestorePending())
            {
                SetBrightness(brightness);
            }
            CanSetBrightness = true;
        }
        if (ActiveDescriptors.Any(device =>
                DeviceProtocolFamilies.IsBlade(device.ProtocolFamily) &&
                device.Access == DeviceAccessState.Available))
        {
            ApplyTouchpadState();
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
            FanTargetRpmText = fanMode == BladeFanMode.Automatic
                ? "--"
                : telemetry.BladeFanTargetRpm is int targetRpm
                    ? $"{targetRpm:N0} RPM"
                    : FanTargetRpmText;
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
        _gameModeReadFromDevice = false;
        _gameModeUserWriteAt = 0;
        _touchpadStateRead = false;
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
