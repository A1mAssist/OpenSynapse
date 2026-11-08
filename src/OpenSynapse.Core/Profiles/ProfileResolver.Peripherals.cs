namespace OpenSynapse.Core.Profiles;

public static partial class ProfileResolver
{
    private static ChromaProfileSettings ResolveChroma(
        ChromaProfileSettings? global,
        ChromaProfileSettings? device,
        ChromaProfileSettings? power) => new()
    {
        BrightnessPercent = First(
            power?.BrightnessPercent,
            device?.BrightnessPercent,
            global?.BrightnessPercent),
        Effect = power?.Effect ?? device?.Effect ?? global?.Effect,
        PrimaryColor = power?.PrimaryColor ?? device?.PrimaryColor ?? global?.PrimaryColor,
        SecondaryColor = power?.SecondaryColor ?? device?.SecondaryColor ?? global?.SecondaryColor,
        ArgbFrames = CloneMap(power?.ArgbFrames ?? device?.ArgbFrames ?? global?.ArgbFrames),
    };

    private static MixerProfileSettings ResolveMixer(
        MixerProfileSettings? global,
        MixerProfileSettings? device,
        MixerProfileSettings? power) => new()
    {
        ChannelMutes = (power?.ChannelMutes ?? device?.ChannelMutes ?? global?.ChannelMutes)?.ToList(),
        MicMute = First(power?.MicMute, device?.MicMute, global?.MicMute),
        BrightnessPercent = First(
            power?.BrightnessPercent,
            device?.BrightnessPercent,
            global?.BrightnessPercent),
    };

    private static KiyoProfileSettings ResolveKiyo(
        KiyoProfileSettings? global,
        KiyoProfileSettings? device,
        KiyoProfileSettings? power) => new()
    {
        WhiteBalanceAuto = First(
            power?.WhiteBalanceAuto,
            device?.WhiteBalanceAuto,
            global?.WhiteBalanceAuto),
        WhiteBalanceTemperature = First(
            power?.WhiteBalanceTemperature,
            device?.WhiteBalanceTemperature,
            global?.WhiteBalanceTemperature),
    };

    private static GamepadProfileSettings ResolveGamepad(
        GamepadProfileSettings? global,
        GamepadProfileSettings? device,
        GamepadProfileSettings? power) => new()
    {
        PollingRateHertz = First(power?.PollingRateHertz, device?.PollingRateHertz, global?.PollingRateHertz),
        VibrationStrengthPercent = First(
            power?.VibrationStrengthPercent,
            device?.VibrationStrengthPercent,
            global?.VibrationStrengthPercent),
        MappingPreset = power?.MappingPreset ?? device?.MappingPreset ?? global?.MappingPreset,
        LeftStickDeadzone = First(power?.LeftStickDeadzone, device?.LeftStickDeadzone, global?.LeftStickDeadzone),
        RightStickDeadzone = First(
            power?.RightStickDeadzone,
            device?.RightStickDeadzone,
            global?.RightStickDeadzone),
        LeftStickSensitivity = First(
            power?.LeftStickSensitivity,
            device?.LeftStickSensitivity,
            global?.LeftStickSensitivity),
        RightStickSensitivity = First(
            power?.RightStickSensitivity,
            device?.RightStickSensitivity,
            global?.RightStickSensitivity),
        LeftStickCircularity =
            power?.LeftStickCircularity ?? device?.LeftStickCircularity ?? global?.LeftStickCircularity,
        RightStickCircularity =
            power?.RightStickCircularity ?? device?.RightStickCircularity ?? global?.RightStickCircularity,
        LeftStickPreventDoubleDeadzone = First(
            power?.LeftStickPreventDoubleDeadzone,
            device?.LeftStickPreventDoubleDeadzone,
            global?.LeftStickPreventDoubleDeadzone),
        RightStickPreventDoubleDeadzone = First(
            power?.RightStickPreventDoubleDeadzone,
            device?.RightStickPreventDoubleDeadzone,
            global?.RightStickPreventDoubleDeadzone),
        TriggerMode = power?.TriggerMode ?? device?.TriggerMode ?? global?.TriggerMode,
        LeftTrigger = (power?.LeftTrigger ?? device?.LeftTrigger ?? global?.LeftTrigger)?.Clone(),
        RightTrigger = (power?.RightTrigger ?? device?.RightTrigger ?? global?.RightTrigger)?.Clone(),
        DpadMode = power?.DpadMode ?? device?.DpadMode ?? global?.DpadMode,
        IgnoreDpadDiagonals = First(
            power?.IgnoreDpadDiagonals,
            device?.IgnoreDpadDiagonals,
            global?.IgnoreDpadDiagonals),
        SocdModeIndex = First(power?.SocdModeIndex, device?.SocdModeIndex, global?.SocdModeIndex),
        ButtonAssignments = (power?.ButtonAssignments ??
                device?.ButtonAssignments ??
                global?.ButtonAssignments)
            ?.Select(assignment => assignment.Clone())
            .ToList(),
        LightingEnabled = First(power?.LightingEnabled, device?.LightingEnabled, global?.LightingEnabled),
        LightingEffect = power?.LightingEffect ?? device?.LightingEffect ?? global?.LightingEffect,
        LightingParameters = CloneMap(
            power?.LightingParameters ?? device?.LightingParameters ?? global?.LightingParameters),
    };
}
