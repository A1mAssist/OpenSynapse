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
    };

    private static HeadsetProfileSettings CloneHeadset(HeadsetProfileSettings source) => new()
    {
        VolumePercent = source.VolumePercent,
        SideTone = source.SideTone,
        GameChatBalance = source.GameChatBalance,
        AncEnabled = source.AncEnabled,
        ThxEnabled = source.ThxEnabled,
        EqPreset = source.EqPreset,
        MicrophoneVolumePercent = source.MicrophoneVolumePercent,
        MicrophoneMute = source.MicrophoneMute,
        HapticEnabled = source.HapticEnabled,
        AutoShutdownMinutes = source.AutoShutdownMinutes,
    };

    private static CoolingPadProfileSettings CloneCoolingPad(CoolingPadProfileSettings source) => new()
    {
        FanMode = source.FanMode,
        FanTargetRpm = source.FanTargetRpm,
        FanCurve = source.FanCurve?.Clone(),
    };

    private static GamepadProfileSettings CloneGamepad(GamepadProfileSettings source) => new()
    {
        PollingRateHertz = source.PollingRateHertz,
        VibrationStrengthPercent = source.VibrationStrengthPercent,
        MappingPreset = source.MappingPreset,
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
}
