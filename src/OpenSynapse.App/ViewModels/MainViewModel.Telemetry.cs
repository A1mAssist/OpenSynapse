using System.ComponentModel;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task RefreshPerformanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await Task.Run(
                () => _performanceMonitor.SampleAsync(cancellationToken).AsTask(),
                cancellationToken);
            _systemTelemetry.Apply(snapshot);
            _performanceErrorText = snapshot.ErrorMessage ?? string.Empty;
            UpdateErrorText();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedRuntimeException(exception))
        {
            SetPerformanceUnavailable(exception.Message);
        }
    }

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
                     device.ProtocolFamily == DeviceProtocolFamilies.Blade &&
                     device.Access == DeviceAccessState.Available) is { } blade)
        {
            var enabled = ProfileResolver
                .Resolve(_profile, blade, _powerSourceProvider.IsPluggedIn)
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
        if (telemetry.BladeNativeDisplayMode is BladeNativeDisplayMode nativeDisplayMode)
        {
            BladeNativeDisplayModeText = nativeDisplayMode == BladeNativeDisplayMode.Uhd ? "UHD" : "FHD";
        }
        if (telemetry.BladeSkuHardwareConfiguration is { } sku)
        {
            BladeSkuHardwareText =
                $"0x{sku.Raw:X2} · DDS {FormatState(sku.Dds)} · MiniLED {FormatState(sku.MiniLedResolution)} · Battery {FormatState(sku.IllegalBatterySupport)}";
            if (!sku.MiniLedResolution)
            {
                BladeLocalDimmingText = AppStrings.Text("Text_BA8E994B");
            }
            else if (telemetry.BladeLocalDimmingEnabled is bool localDimmingEnabled)
            {
                BladeLocalDimmingText = FormatOptionalState(localDimmingEnabled);
            }
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
        if (_deviceDescriptors.Any(device => device.ProtocolFamily == DeviceProtocolFamilies.Blade && device.Access == DeviceAccessState.Available))
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
            var chemistry = ProfileResolver.Resolve(
                _profile,
                viper,
                _powerSourceProvider.IsPluggedIn).Viper.BatteryChemistry;
            ViperBatteryChemistryIndex = chemistry is byte value ? value : -1;
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
            ViperLowBatteryThresholdText = ViperLowBatteryThresholdProtocol.Format(raw);
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

    private void RefreshInternalDisplay(bool? powerState, bool applyProfile)
    {
        if (_internalDisplayController is null)
        {
            return;
        }

        try
        {
            var snapshot = _internalDisplayController.Read();
            if (applyProfile)
            {
                var blade = _deviceDescriptors.FirstOrDefault(
                    device => device.ProtocolFamily == DeviceProtocolFamilies.Blade) ?? new DeviceDescriptor(
                        "internal-display",
                        "Windows internal display",
                        0,
                        0,
                        DeviceAccessState.Available,
                        DeviceCapabilityState.Unsupported,
                        0,
                        0,
                        0,
                        DeviceProtocolFamilies.Blade);
                var requested = ProfileResolver.Resolve(_profile, blade, powerState).Blade.RefreshRateHertz;
                if (requested is int hertz && hertz != snapshot.RefreshRateHertz)
                {
                    if (!snapshot.SupportedRefreshRates.Contains(hertz))
                    {
                        throw new InvalidOperationException(AppStrings.FormatText("UnsupportedDisplayRefreshRate",
                            hertz,
                            snapshot.Width,
                            snapshot.Height));
                    }

                    snapshot = _internalDisplayController.SetRefreshRate(hertz);
                }
            }

            ApplyInternalDisplaySnapshot(snapshot);
            SetDisplayError(string.Empty);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentOutOfRangeException)
        {
            SetInternalDisplayUnavailable();
            SetDisplayError(AppStrings.FormatText("DisplayRefreshRateError", exception.Message));
        }
    }

    private void ApplyInternalDisplaySnapshot(InternalDisplaySnapshot snapshot)
    {
        InternalDisplayResolutionText = $"{snapshot.Width} x {snapshot.Height}";
        InternalDisplayRefreshRateText = $"{snapshot.RefreshRateHertz} Hz";
        InternalDisplayRefreshRates = snapshot.SupportedRefreshRates;
        InternalDisplayRefreshRateHertz = snapshot.RefreshRateHertz;
        _confirmedInternalDisplayRefreshRateHertz = snapshot.RefreshRateHertz;
        CanSetInternalDisplayRefreshRate = snapshot.CanSetRefreshRate;
    }

    private void SetInternalDisplayUnavailable()
    {
        CanSetInternalDisplayRefreshRate = false;
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
            }
        }
        if (confirm)
        {
            _blade._confirmedBladeBrightnessPercent = percent;
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

    private void SetBladeLogo(BladeLogoMode mode)
    {
        _blade.SetLogo(mode);
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
        BladeNativeDisplayModeText = "--";
        BladeSkuHardwareText = "--";
        BladeLocalDimmingText = "--";
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
            nameof(ViperBatteryText), nameof(ViperPollingRateText), nameof(ViperPollingRateIndex),
            nameof(ViperBatteryChemistryIndex),
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

    public string CpuName => _systemTelemetry.CpuName;
    public string CpuValue => _systemTelemetry.CpuValue;
    public double CpuPercent => _systemTelemetry.CpuPercent;
    public string CpuTemperatureText => _systemTelemetry.CpuTemperatureText;
    public string CpuPowerText => _systemTelemetry.CpuPowerText;
    public string CpuClockText => _systemTelemetry.CpuClockText;
    public string GpuName => _systemTelemetry.GpuName;
    public string GpuValue => _systemTelemetry.GpuValue;
    public double GpuPercent => _systemTelemetry.GpuPercent;
    public string GpuTemperatureText => _systemTelemetry.GpuTemperatureText;
    public string GpuPowerText => _systemTelemetry.GpuPowerText;
    public string GpuClockText => _systemTelemetry.GpuClockText;
    public string GpuMemoryLabel => _systemTelemetry.GpuMemoryLabel;
    public string GpuMemoryText => _systemTelemetry.GpuMemoryText;
    public string MemoryValue => _systemTelemetry.MemoryValue;
    public string MemoryDetail => _systemTelemetry.MemoryDetail;
    public double MemoryPercent => _systemTelemetry.MemoryPercent;
    public string StorageValue => _systemTelemetry.StorageValue;
    public string StorageDetail => _systemTelemetry.StorageDetail;
    public double StoragePercent => _systemTelemetry.StoragePercent;
}
