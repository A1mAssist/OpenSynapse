using System.Text.Json;

namespace OpenSynapse.Core.Profiles;

public sealed partial class ProfileDefinition
{
    internal ProfileDefinition Clone()
    {
        ApplySafeDefaults();
        var clone = new ProfileDefinition
        {
            Global = CloneSettings(Global),
            PluggedIn = ClonePower(PluggedIn),
            LowPowerAdapter = ClonePower(LowPowerAdapter),
            OnBattery = ClonePower(OnBattery),
            Shortcuts = Shortcuts.Clone(),
            LightingColorPresets = LightingColorPresets.Select(preset => preset.Clone()).ToList(),
            Macros = Macros.Select(macro => macro.Clone()).ToList(),
        };
        foreach (var (key, settings) in Devices)
        {
            clone.Devices[key] = CloneDevice(settings);
        }

        return clone;
    }

    private static ProfileSettings CloneSettings(ProfileSettings source) => new()
    {
        Blade = CloneBlade(source.Blade),
        Viper = CloneMouse(source.Viper),
        Keyboard = CloneKeyboard(source.Keyboard),
        Headset = CloneHeadset(source.Headset),
        CoolingPad = CloneCoolingPad(source.CoolingPad),
        Gamepad = CloneGamepad(source.Gamepad),
        Chroma = source.Chroma.Clone(),
        Mixer = source.Mixer.Clone(),
        Kiyo = source.Kiyo.Clone(),
        Display = CloneDisplay(source.Display),
        Lighting = CloneLighting(source.Lighting),
        LightingZones = CloneZones(source.LightingZones),
    };

    private static DeviceProfileSettings CloneDevice(DeviceProfileSettings source) => new()
    {
        Blade = CloneBlade(source.Blade),
        Viper = CloneMouse(source.Viper),
        Keyboard = CloneKeyboard(source.Keyboard),
        Headset = CloneHeadset(source.Headset),
        CoolingPad = CloneCoolingPad(source.CoolingPad),
        Gamepad = CloneGamepad(source.Gamepad),
        Chroma = source.Chroma.Clone(),
        Mixer = source.Mixer.Clone(),
        Kiyo = source.Kiyo.Clone(),
        Display = CloneDisplay(source.Display),
        Lighting = CloneLighting(source.Lighting),
        LightingZones = CloneZones(source.LightingZones),
        LightingEnabled = source.LightingEnabled,
        ChromaOverrideEnabled = source.ChromaOverrideEnabled,
        Extras = source.Extras.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase),
        ExtensionData = source.ExtensionData is null
            ? null
            : new Dictionary<string, JsonElement>(source.ExtensionData, StringComparer.OrdinalIgnoreCase),
    };

    private static PowerProfileOverrides ClonePower(PowerProfileOverrides source) => new()
    {
        Blade = CloneBlade(source.Blade),
        Viper = CloneMouse(source.Viper),
        Keyboard = CloneKeyboard(source.Keyboard),
        Headset = CloneHeadset(source.Headset),
        CoolingPad = CloneCoolingPad(source.CoolingPad),
        Gamepad = CloneGamepad(source.Gamepad),
        Chroma = source.Chroma.Clone(),
        Mixer = source.Mixer.Clone(),
        Kiyo = source.Kiyo.Clone(),
        Display = CloneDisplay(source.Display),
        Lighting = CloneLighting(source.Lighting),
        LightingZones = CloneZones(source.LightingZones),
        RazerHidLightingZones = source.RazerHidLightingZones.ToDictionary(
            pair => pair.Key,
            pair => CloneZones(pair.Value),
            StringComparer.OrdinalIgnoreCase),
        RazerHidLighting = source.RazerHidLighting.ToDictionary(
            pair => pair.Key,
            pair => CloneLighting(pair.Value),
            StringComparer.OrdinalIgnoreCase),
    };

    private static BladeProfileSettings CloneBlade(BladeProfileSettings source) => new()
    {
        KeyboardBrightness = source.KeyboardBrightness,
        PerformanceMode = source.PerformanceMode,
        FanMode = source.FanMode,
        FanTargetRpm = source.FanTargetRpm,
        FanCurve = source.FanCurve?.Clone(),
        ChargeLimitPercent = source.ChargeLimitPercent,
        RefreshRateHertz = source.RefreshRateHertz,
        MaxFanMode = source.MaxFanMode,
        CpuBoostMode = source.CpuBoostMode,
        GpuBoostMode = source.GpuBoostMode,
        LogoMode = source.LogoMode,
        GamingModeEnabled = source.GamingModeEnabled,
        SnapTapEnabled = source.SnapTapEnabled,
        MappingPreset = source.MappingPreset,
        LightingEnabled = source.LightingEnabled,
        ChromaOverrideEnabled = source.ChromaOverrideEnabled,
        DisplayBrightnessPercent = source.DisplayBrightnessPercent,
        StartupAnimationEnabled = source.StartupAnimationEnabled,
        AutoSleepSeconds = source.AutoSleepSeconds,
        TimeToDimSeconds = source.TimeToDimSeconds,
        IndicatorLedEnabled = source.IndicatorLedEnabled,
        GpuCardDetectionEnabled = source.GpuCardDetectionEnabled,
        NativeDisplayModeEnabled = source.NativeDisplayModeEnabled,
        LocalDimmingEnabled = source.LocalDimmingEnabled,
        CpuPowerBoostMode = source.CpuPowerBoostMode,
        GpuCoreClockOffsetMhz = source.GpuCoreClockOffsetMhz,
        GpuMemoryClockOffsetMhz = source.GpuMemoryClockOffsetMhz,
        GpuCoreClockLimitMhz = source.GpuCoreClockLimitMhz,
        CpuCurveOffset = source.CpuCurveOffset,
        CpuPowerLimitWatts = source.CpuPowerLimitWatts,
        GpuMode = source.GpuMode,
    };

    private static MouseProfileSettings CloneMouse(MouseProfileSettings source) => new()
    {
        DpiX = source.DpiX,
        DpiY = source.DpiY,
        PollingRateHertz = source.PollingRateHertz,
        IdleSeconds = source.IdleSeconds,
        LowBatteryThresholdPercent = source.LowBatteryThresholdPercent,
        AutoSleepSeconds = source.AutoSleepSeconds,
        ChargeLimitPercent = source.ChargeLimitPercent,
        BatteryChemistry = source.BatteryChemistry,
        ScrollMode = source.ScrollMode,
        ScrollFreeNotched = source.ScrollFreeNotched,
        ScrollAccelerationEnabled = source.ScrollAccelerationEnabled,
        ScrollAccelerationLevel = source.ScrollAccelerationLevel,
        SmartReelEnabled = source.SmartReelEnabled,
        SmartReelThreshold = source.SmartReelThreshold,
        ActiveOnboardProfileId = source.ActiveOnboardProfileId,
        HapticScrollLevel = source.HapticScrollLevel,
        PowerPlan = source.PowerPlan,
        SmartIlluminationEnabled = source.SmartIlluminationEnabled,
        SleepTimeoutSeconds = source.SleepTimeoutSeconds,
        SnapTapEnabled = source.SnapTapEnabled,
        DebounceMilliseconds = source.DebounceMilliseconds,
        HapticMode = source.HapticMode,
        HapticAccelerationLevel = source.HapticAccelerationLevel,
        HapticMaskTime = source.HapticMaskTime,
        HapticLevel = source.HapticLevel,
        DpiStages = source.DpiStages?.Clone(),
        ButtonAssignments = source.ButtonAssignments?.Select(assignment => assignment.Clone()).ToList(),
        ButtonMappings = source.ButtonMappings?.Select(mapping => mapping.Clone()).ToList(),
    };

    private static KeyboardProfileSettings CloneKeyboard(KeyboardProfileSettings source) => new()
    {
        PollingRateHertz = source.PollingRateHertz,
        GamingModeEnabled = source.GamingModeEnabled,
        SwitchType = source.SwitchType,
        AnalogKeyEnabled = source.AnalogKeyEnabled,
        SnapTapEnabled = source.SnapTapEnabled,
        FnKeyPrimary = source.FnKeyPrimary,
        DebounceMilliseconds = source.DebounceMilliseconds,
        OledBrightness = source.OledBrightness,
        OledLanguage = source.OledLanguage,
        KnobMode = source.KnobMode,
        WristRestLightingEnabled = source.WristRestLightingEnabled,
        ButtonMappings = source.ButtonMappings?.Select(mapping => mapping.Clone()).ToList(),
    };

    private static HeadsetProfileSettings CloneHeadset(HeadsetProfileSettings source) => new()
    {
        VolumePercent = source.VolumePercent,
        SideTone = source.SideTone,
        GameModeEnabled = source.GameModeEnabled,
        GameChatBalance = source.GameChatBalance,
        AncEnabled = source.AncEnabled,
        AncModeIndex = source.AncModeIndex,
        AncLevel = source.AncLevel,
        ThxEnabled = source.ThxEnabled,
        ThxSpatialEnabled = source.ThxSpatialEnabled,
        ThxEqualizerEnabled = source.ThxEqualizerEnabled,
        ThxPreset = source.ThxPreset,
        ThxBands = CloneIntMap(source.ThxBands),
        ThxNormalizationEnabled = source.ThxNormalizationEnabled,
        ThxNormalizationLevel = source.ThxNormalizationLevel,
        ThxVoiceClarityEnabled = source.ThxVoiceClarityEnabled,
        ThxVoiceClarityLevel = source.ThxVoiceClarityLevel,
        ThxBassBoostEnabled = source.ThxBassBoostEnabled,
        ThxBassBoostLevel = source.ThxBassBoostLevel,
        ThxSidetoneEnabled = source.ThxSidetoneEnabled,
        ThxSidetoneLevel = source.ThxSidetoneLevel,
        EqPreset = source.EqPreset,
        EqBands = CloneIntMap(source.EqBands),
        MicEqPreset = source.MicEqPreset,
        MicEqBands = CloneIntMap(source.MicEqBands),
        Bass = source.Bass,
        MicrophoneVolumePercent = source.MicrophoneVolumePercent,
        MicrophoneMute = source.MicrophoneMute,
        NoiseSuppression = source.NoiseSuppression,
        AudioSourceIndex = source.AudioSourceIndex,
        Dolby = source.Dolby,
        NightMode = source.NightMode,
        VoicePrompts = source.VoicePrompts,
        MicDeEsser = source.MicDeEsser,
        MicLimiterDb = source.MicLimiterDb,
        MicReverbIndex = source.MicReverbIndex,
        MicReverbIntensity = source.MicReverbIntensity,
        MicCompressorEnabled = source.MicCompressorEnabled,
        MicCompressorProfile = source.MicCompressorProfile,
        MicCompressorDb = source.MicCompressorDb,
        MicCompressorRatio = source.MicCompressorRatio,
        MicCompressorAttack = source.MicCompressorAttack,
        MicCompressorRelease = source.MicCompressorRelease,
        MicMonitoringEnabled = source.MicMonitoringEnabled,
        ParametricEqEnabled = source.ParametricEqEnabled,
        ParametricEqBandIndex = source.ParametricEqBandIndex,
        ParametricEqFrequency = source.ParametricEqFrequency,
        ParametricEqGain = source.ParametricEqGain,
        ParametricEqQ = source.ParametricEqQ,
        ParametricEqProfile = source.ParametricEqProfile,
        ParametricEqDirection = source.ParametricEqDirection,
        ParametricEqFilterType = source.ParametricEqFilterType,
        HapticEnabled = source.HapticEnabled,
        HapticsStrength = source.HapticsStrength,
        AutoPause = source.AutoPause,
        AutoShutdownMinutes = source.AutoShutdownMinutes,
        Dnd = source.Dnd,
        GestureControl = source.GestureControl,
        ChromaBrightness = source.ChromaBrightness,
        ChromaEffect = source.ChromaEffect,
        ChromaParameters = CloneStringMap(source.ChromaParameters),
    };

    private static CoolingPadProfileSettings CloneCoolingPad(CoolingPadProfileSettings source) => new()
    {
        FanMode = source.FanMode,
        FanTargetRpm = source.FanTargetRpm,
        FanCurve = source.FanCurve?.Clone(),
        LightingEnabled = source.LightingEnabled,
        LightingEffect = source.LightingEffect,
        LightingParameters = CloneStringMap(source.LightingParameters),
        PowerEnabled = source.PowerEnabled,
    };

    private static GamepadProfileSettings CloneGamepad(GamepadProfileSettings source) => new()
    {
        PollingRateHertz = source.PollingRateHertz,
        VibrationStrengthPercent = source.VibrationStrengthPercent,
        MappingPreset = source.MappingPreset,
        LeftStickDeadzone = source.LeftStickDeadzone,
        RightStickDeadzone = source.RightStickDeadzone,
        LeftStickSensitivity = source.LeftStickSensitivity,
        RightStickSensitivity = source.RightStickSensitivity,
        LeftStickCircularity = source.LeftStickCircularity,
        RightStickCircularity = source.RightStickCircularity,
        LeftStickPreventDoubleDeadzone = source.LeftStickPreventDoubleDeadzone,
        RightStickPreventDoubleDeadzone = source.RightStickPreventDoubleDeadzone,
        TriggerMode = source.TriggerMode,
        LeftTrigger = source.LeftTrigger?.Clone(),
        RightTrigger = source.RightTrigger?.Clone(),
        DpadMode = source.DpadMode,
        IgnoreDpadDiagonals = source.IgnoreDpadDiagonals,
        SocdModeIndex = source.SocdModeIndex,
        ButtonAssignments = source.ButtonAssignments?
            .Select(assignment => assignment.Clone())
            .ToList(),
        LightingEnabled = source.LightingEnabled,
        LightingEffect = source.LightingEffect,
        LightingParameters = CloneStringMap(source.LightingParameters),
    };

    private static DisplayProfileSettings CloneDisplay(DisplayProfileSettings source) => new()
    {
        BrightnessPercent = source.BrightnessPercent,
        RefreshRateHertz = source.RefreshRateHertz,
        LocalDimmingEnabled = source.LocalDimmingEnabled,
        NativeDisplayModeEnabled = source.NativeDisplayModeEnabled,
    };

    private static LightingProfile CloneLighting(LightingProfile source) => new()
    {
        Effect = source.Effect,
        Parameters = new Dictionary<string, string>(source.Parameters, StringComparer.OrdinalIgnoreCase),
    };

    private static Dictionary<string, LightingProfile> CloneZones(
        IReadOnlyDictionary<string, LightingProfile>? source) =>
        source is null
            ? new Dictionary<string, LightingProfile>(StringComparer.OrdinalIgnoreCase)
            : source.ToDictionary(
                pair => pair.Key,
                pair => CloneLighting(pair.Value),
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, int>? CloneIntMap(IReadOnlyDictionary<string, int>? source) =>
        source is null
            ? null
            : new Dictionary<string, int>(source, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string>? CloneStringMap(
        IReadOnlyDictionary<string, string>? source) =>
        source is null
            ? null
            : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
}
