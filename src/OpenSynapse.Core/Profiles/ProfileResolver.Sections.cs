namespace OpenSynapse.Core.Profiles;

public static partial class ProfileResolver
{
    private static BladeProfileSettings ResolveBlade(
        BladeProfileSettings? global,
        BladeProfileSettings? device,
        BladeProfileSettings? power)
    {
        return new BladeProfileSettings
        {
            KeyboardBrightness = First(power?.KeyboardBrightness, device?.KeyboardBrightness, global?.KeyboardBrightness),
            PerformanceMode = First(power?.PerformanceMode, device?.PerformanceMode, global?.PerformanceMode),
            FanMode = First(power?.FanMode, device?.FanMode, global?.FanMode),
            FanTargetRpm = First(power?.FanTargetRpm, device?.FanTargetRpm, global?.FanTargetRpm),
            FanCurve = (power?.FanCurve ?? device?.FanCurve ?? global?.FanCurve)?.Clone(),
            ChargeLimitPercent = First(power?.ChargeLimitPercent, device?.ChargeLimitPercent, global?.ChargeLimitPercent),
            RefreshRateHertz = First(power?.RefreshRateHertz, device?.RefreshRateHertz, global?.RefreshRateHertz),
            MaxFanMode = First(power?.MaxFanMode, device?.MaxFanMode, global?.MaxFanMode),
            CpuBoostMode = First(power?.CpuBoostMode, device?.CpuBoostMode, global?.CpuBoostMode),
            GpuBoostMode = First(power?.GpuBoostMode, device?.GpuBoostMode, global?.GpuBoostMode),
            LogoMode = First(power?.LogoMode, device?.LogoMode, global?.LogoMode),
            GamingModeEnabled = First(
                power?.GamingModeEnabled,
                device?.GamingModeEnabled,
                global?.GamingModeEnabled),
            SnapTapEnabled = First(power?.SnapTapEnabled, device?.SnapTapEnabled, global?.SnapTapEnabled),
            MappingPreset = power?.MappingPreset ?? device?.MappingPreset ?? global?.MappingPreset,
            LightingEnabled = First(power?.LightingEnabled, device?.LightingEnabled, global?.LightingEnabled),
            ChromaOverrideEnabled = First(
                power?.ChromaOverrideEnabled,
                device?.ChromaOverrideEnabled,
                global?.ChromaOverrideEnabled),
            DisplayBrightnessPercent = First(
                power?.DisplayBrightnessPercent,
                device?.DisplayBrightnessPercent,
                global?.DisplayBrightnessPercent),
            StartupAnimationEnabled = First(
                power?.StartupAnimationEnabled,
                device?.StartupAnimationEnabled,
                global?.StartupAnimationEnabled),
            AutoSleepSeconds = First(power?.AutoSleepSeconds, device?.AutoSleepSeconds, global?.AutoSleepSeconds),
            TimeToDimSeconds = First(power?.TimeToDimSeconds, device?.TimeToDimSeconds, global?.TimeToDimSeconds),
            IndicatorLedEnabled = First(
                power?.IndicatorLedEnabled,
                device?.IndicatorLedEnabled,
                global?.IndicatorLedEnabled),
            GpuCardDetectionEnabled = First(
                power?.GpuCardDetectionEnabled,
                device?.GpuCardDetectionEnabled,
                global?.GpuCardDetectionEnabled),
            NativeDisplayModeEnabled = First(
                power?.NativeDisplayModeEnabled,
                device?.NativeDisplayModeEnabled,
                global?.NativeDisplayModeEnabled),
            LocalDimmingEnabled = First(
                power?.LocalDimmingEnabled,
                device?.LocalDimmingEnabled,
                global?.LocalDimmingEnabled),
            CpuPowerBoostMode = First(
                power?.CpuPowerBoostMode,
                device?.CpuPowerBoostMode,
                global?.CpuPowerBoostMode),
            GpuCoreClockOffsetMhz = First(
                power?.GpuCoreClockOffsetMhz,
                device?.GpuCoreClockOffsetMhz,
                global?.GpuCoreClockOffsetMhz),
            GpuMemoryClockOffsetMhz = First(
                power?.GpuMemoryClockOffsetMhz,
                device?.GpuMemoryClockOffsetMhz,
                global?.GpuMemoryClockOffsetMhz),
            GpuCoreClockLimitMhz = First(
                power?.GpuCoreClockLimitMhz,
                device?.GpuCoreClockLimitMhz,
                global?.GpuCoreClockLimitMhz),
            CpuCurveOffset = First(power?.CpuCurveOffset, device?.CpuCurveOffset, global?.CpuCurveOffset),
            CpuPowerLimitWatts = First(
                power?.CpuPowerLimitWatts,
                device?.CpuPowerLimitWatts,
                global?.CpuPowerLimitWatts),
        };
    }

    private static MouseProfileSettings ResolveMouse(
        MouseProfileSettings? global,
        MouseProfileSettings? device,
        MouseProfileSettings? power)
    {
        return new MouseProfileSettings
        {
            DpiX = First(power?.DpiX, device?.DpiX, global?.DpiX),
            DpiY = First(power?.DpiY, device?.DpiY, global?.DpiY),
            PollingRateHertz = First(power?.PollingRateHertz, device?.PollingRateHertz, global?.PollingRateHertz),
            IdleSeconds = First(power?.IdleSeconds, device?.IdleSeconds, global?.IdleSeconds),
            LowBatteryThresholdPercent = First(
                power?.LowBatteryThresholdPercent,
                device?.LowBatteryThresholdPercent,
                global?.LowBatteryThresholdPercent),
            AutoSleepSeconds = First(power?.AutoSleepSeconds, device?.AutoSleepSeconds, global?.AutoSleepSeconds),
            ChargeLimitPercent = First(power?.ChargeLimitPercent, device?.ChargeLimitPercent, global?.ChargeLimitPercent),
            BatteryChemistry = First(power?.BatteryChemistry, device?.BatteryChemistry, global?.BatteryChemistry),
            ScrollMode = power?.ScrollMode ?? device?.ScrollMode ?? global?.ScrollMode,
            ScrollFreeNotched = First(power?.ScrollFreeNotched, device?.ScrollFreeNotched, global?.ScrollFreeNotched),
            ScrollAccelerationEnabled = First(
                power?.ScrollAccelerationEnabled,
                device?.ScrollAccelerationEnabled,
                global?.ScrollAccelerationEnabled),
            ScrollAccelerationLevel = First(
                power?.ScrollAccelerationLevel,
                device?.ScrollAccelerationLevel,
                global?.ScrollAccelerationLevel),
            SmartReelEnabled = First(power?.SmartReelEnabled, device?.SmartReelEnabled, global?.SmartReelEnabled),
            SmartReelThreshold = First(
                power?.SmartReelThreshold,
                device?.SmartReelThreshold,
                global?.SmartReelThreshold),
            ActiveOnboardProfileId = First(
                power?.ActiveOnboardProfileId,
                device?.ActiveOnboardProfileId,
                global?.ActiveOnboardProfileId),
            HapticScrollLevel = First(
                power?.HapticScrollLevel,
                device?.HapticScrollLevel,
                global?.HapticScrollLevel),
            PowerPlan = power?.PowerPlan ?? device?.PowerPlan ?? global?.PowerPlan,
            SmartIlluminationEnabled = First(
                power?.SmartIlluminationEnabled,
                device?.SmartIlluminationEnabled,
                global?.SmartIlluminationEnabled),
            SleepTimeoutSeconds = First(
                power?.SleepTimeoutSeconds,
                device?.SleepTimeoutSeconds,
                global?.SleepTimeoutSeconds),
            SnapTapEnabled = First(power?.SnapTapEnabled, device?.SnapTapEnabled, global?.SnapTapEnabled),
            DebounceMilliseconds = First(
                power?.DebounceMilliseconds,
                device?.DebounceMilliseconds,
                global?.DebounceMilliseconds),
            HapticMode = First(power?.HapticMode, device?.HapticMode, global?.HapticMode),
            HapticAccelerationLevel = First(
                power?.HapticAccelerationLevel,
                device?.HapticAccelerationLevel,
                global?.HapticAccelerationLevel),
            HapticMaskTime = First(power?.HapticMaskTime, device?.HapticMaskTime, global?.HapticMaskTime),
            HapticLevel = First(power?.HapticLevel, device?.HapticLevel, global?.HapticLevel),
            DpiStages = (power?.DpiStages ?? device?.DpiStages ?? global?.DpiStages)?.Clone(),
            ButtonAssignments = (power?.ButtonAssignments ??
                device?.ButtonAssignments ??
                global?.ButtonAssignments)?.Select(assignment => assignment.Clone()).ToList(),
            ButtonMappings = CloneMappings(power?.ButtonMappings ?? device?.ButtonMappings ?? global?.ButtonMappings),
        };
    }

    private static KeyboardProfileSettings ResolveKeyboard(
        KeyboardProfileSettings? global,
        KeyboardProfileSettings? device,
        KeyboardProfileSettings? power) => new()
    {
        PollingRateHertz = First(power?.PollingRateHertz, device?.PollingRateHertz, global?.PollingRateHertz),
        GamingModeEnabled = First(power?.GamingModeEnabled, device?.GamingModeEnabled, global?.GamingModeEnabled),
        SwitchType = power?.SwitchType ?? device?.SwitchType ?? global?.SwitchType,
        AnalogKeyEnabled = First(power?.AnalogKeyEnabled, device?.AnalogKeyEnabled, global?.AnalogKeyEnabled),
        SnapTapEnabled = First(power?.SnapTapEnabled, device?.SnapTapEnabled, global?.SnapTapEnabled),
        FnKeyPrimary = First(power?.FnKeyPrimary, device?.FnKeyPrimary, global?.FnKeyPrimary),
        DebounceMilliseconds = First(
            power?.DebounceMilliseconds,
            device?.DebounceMilliseconds,
            global?.DebounceMilliseconds),
        OledBrightness = First(power?.OledBrightness, device?.OledBrightness, global?.OledBrightness),
        OledLanguage = power?.OledLanguage ?? device?.OledLanguage ?? global?.OledLanguage,
        KnobMode = First(power?.KnobMode, device?.KnobMode, global?.KnobMode),
        WristRestLightingEnabled = First(
            power?.WristRestLightingEnabled,
            device?.WristRestLightingEnabled,
            global?.WristRestLightingEnabled),
        ButtonMappings = CloneMappings(power?.ButtonMappings ?? device?.ButtonMappings ?? global?.ButtonMappings),
    };
}
