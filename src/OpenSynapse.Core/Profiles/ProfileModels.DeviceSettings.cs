using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Profiles;

/// <summary>
/// Mouse settings shared by the Viper transport and the generic RazerHid mice.
/// The serialized property name stays "viper" so profiles written before the
/// generalisation keep loading.
/// </summary>
public sealed class MouseProfileSettings
{
    private static readonly byte[] Product184ButtonIds = [1, 2, 3, 4, 5, 9, 10, 96];

    public int? DpiX { get; set; }
    public int? DpiY { get; set; }
    public int? PollingRateHertz { get; set; }
    public int? IdleSeconds { get; set; }
    public int? LowBatteryThresholdPercent { get; set; }
    public int? AutoSleepSeconds { get; set; }
    public int? ChargeLimitPercent { get; set; }
    public byte? BatteryChemistry { get; set; }
    public string? ScrollMode { get; set; }
    public bool? ScrollFreeNotched { get; set; }
    public bool? ScrollAccelerationEnabled { get; set; }
    public int? ScrollAccelerationLevel { get; set; }
    public bool? SmartReelEnabled { get; set; }
    public int? SmartReelThreshold { get; set; }
    public int? ActiveOnboardProfileId { get; set; }
    public int? HapticScrollLevel { get; set; }
    public string? PowerPlan { get; set; }
    public bool? SmartIlluminationEnabled { get; set; }
    public int? SleepTimeoutSeconds { get; set; }
    public bool? SnapTapEnabled { get; set; }
    public int? DebounceMilliseconds { get; set; }
    public int? HapticMode { get; set; }
    public int? HapticAccelerationLevel { get; set; }
    public int? HapticMaskTime { get; set; }
    public int? HapticLevel { get; set; }
    public ViperDpiStagesProfile? DpiStages { get; set; }
    public List<ViperButtonAssignmentProfile>? ButtonAssignments { get; set; }

    internal void ApplySafeDefaults()
    {
        ValidatePercent(LowBatteryThresholdPercent, nameof(LowBatteryThresholdPercent));
        ValidatePercent(ChargeLimitPercent, nameof(ChargeLimitPercent));
        if (AutoSleepSeconds is < 0)
        {
            throw new InvalidDataException("Mouse auto-sleep seconds cannot be negative.");
        }
        if (BatteryChemistry is byte chemistry && chemistry > 2)
        {
            throw new InvalidDataException("Viper battery chemistry must be 0, 1, or 2.");
        }
        if (!string.IsNullOrWhiteSpace(ScrollMode))
        {
            if (!Enum.TryParse<MouseScrollMode>(ScrollMode, ignoreCase: true, out var scrollMode) ||
                !Enum.IsDefined(scrollMode))
            {
                throw new InvalidDataException("Mouse scroll mode is not recognized.");
            }

            ScrollMode = scrollMode.ToString();
        }
        ValidateByte(ScrollAccelerationLevel, nameof(ScrollAccelerationLevel));
        ValidateByte(SmartReelThreshold, nameof(SmartReelThreshold));
        ValidateByte(ActiveOnboardProfileId, nameof(ActiveOnboardProfileId));
        if (HapticScrollLevel is < 1 or > 6)
        {
            throw new InvalidDataException("Haptic scroll level must be between 1 and 6.");
        }
        if (!string.IsNullOrWhiteSpace(PowerPlan))
        {
            if (!Enum.TryParse<MousePowerPlan>(PowerPlan, ignoreCase: true, out var powerPlan) ||
                !Enum.IsDefined(powerPlan))
            {
                throw new InvalidDataException("Mouse power plan is not recognized.");
            }

            PowerPlan = powerPlan.ToString();
        }
        if (SleepTimeoutSeconds is < 0)
        {
            throw new InvalidDataException("Mouse sleep timeout cannot be negative.");
        }
        if (DebounceMilliseconds is < 0 or > byte.MaxValue)
        {
            throw new InvalidDataException("Mouse debounce must be between 0 and 255 ms.");
        }
        if (HapticMode is < 0 or > 2)
        {
            throw new InvalidDataException("Mouse haptic mode must be between 0 and 2.");
        }
        ValidateByte(HapticAccelerationLevel, nameof(HapticAccelerationLevel));
        ValidateByte(HapticMaskTime, nameof(HapticMaskTime));
        if (HapticLevel is < 1 or > 6)
        {
            throw new InvalidDataException("Mouse haptic level must be between 1 and 6.");
        }
        DpiStages?.ApplySafeDefaults();
        if (ButtonAssignments is null)
        {
            return;
        }
        if (ButtonAssignments.Count != 16 || ButtonAssignments.Any(assignment => assignment is null))
        {
            throw new InvalidDataException("Viper button assignments must contain exactly 16 valid assignments.");
        }

        foreach (var assignment in ButtonAssignments)
        {
            assignment.ApplySafeDefaults();
        }
        var targets = ButtonAssignments
            .Select(assignment => (assignment.ProfileId, assignment.ButtonId, assignment.Layer))
            .ToHashSet();
        if (targets.Count != ButtonAssignments.Count ||
            Product184ButtonIds.Any(buttonId =>
                !targets.Contains((1, buttonId, ViperButtonMappingLayer.Normal)) ||
                !targets.Contains((1, buttonId, ViperButtonMappingLayer.HyperShift))))
        {
            throw new InvalidDataException(
                "Viper button assignments must contain each Product 184 button and layer exactly once.");
        }
    }

    internal static void ValidatePercent(int? value, string name)
    {
        if (value is < 0 or > 100)
        {
            throw new InvalidDataException($"{name} must be between 0 and 100.");
        }
    }

    private static void ValidateByte(int? value, string name)
    {
        if (value is < 0 or > byte.MaxValue)
        {
            throw new InvalidDataException($"{name} must be between 0 and 255.");
        }
    }
}

public sealed class KeyboardProfileSettings
{
    public int? PollingRateHertz { get; set; }
    public bool? GamingModeEnabled { get; set; }
    public string? SwitchType { get; set; }
    public bool? AnalogKeyEnabled { get; set; }
    public bool? SnapTapEnabled { get; set; }
    public bool? FnKeyPrimary { get; set; }
    public int? DebounceMilliseconds { get; set; }
    public int? OledBrightness { get; set; }
    public string? OledLanguage { get; set; }
    public int? KnobMode { get; set; }
    public bool? WristRestLightingEnabled { get; set; }

    internal void ApplySafeDefaults()
    {
        if (PollingRateHertz is <= 0)
        {
            throw new InvalidDataException("Keyboard polling rate must be positive.");
        }
        if (DebounceMilliseconds is < 0 or > byte.MaxValue)
        {
            throw new InvalidDataException("Keyboard debounce must be between 0 and 255 ms.");
        }
        MouseProfileSettings.ValidatePercent(OledBrightness, nameof(OledBrightness));
        if (!string.IsNullOrWhiteSpace(OledLanguage))
        {
            if (!Enum.TryParse<KeyboardOledLanguage>(OledLanguage, ignoreCase: true, out var language) ||
                !Enum.IsDefined(language))
            {
                throw new InvalidDataException("Keyboard OLED language is not recognized.");
            }

            OledLanguage = language.ToString();
        }
        if (KnobMode is < 0 or > 7)
        {
            throw new InvalidDataException("Keyboard knob mode must be between 0 and 7.");
        }
    }
}

public enum MouseScrollMode
{
    Tactile,
    FreeSpin,
    MicroTactile,
}

public enum MousePowerPlan
{
    PowerSave,
    Balance,
    HighPerformance,
}

public enum KeyboardOledLanguage
{
    English = 0,
    Chinese = 1,
    Japanese = 2,
    German = 3,
    Korean = 4,
    French = 5,
}

public sealed class HeadsetProfileSettings
{
    public int? VolumePercent { get; set; }
    public int? SideTone { get; set; }
    public int? GameChatBalance { get; set; }
    public bool? AncEnabled { get; set; }
    public bool? ThxEnabled { get; set; }
    public string? EqPreset { get; set; }
    public int? MicrophoneVolumePercent { get; set; }
    public bool? MicrophoneMute { get; set; }
    public bool? HapticEnabled { get; set; }
    public int? AutoShutdownMinutes { get; set; }

    internal void ApplySafeDefaults()
    {
        MouseProfileSettings.ValidatePercent(VolumePercent, nameof(VolumePercent));
        MouseProfileSettings.ValidatePercent(MicrophoneVolumePercent, nameof(MicrophoneVolumePercent));
        MouseProfileSettings.ValidatePercent(GameChatBalance, nameof(GameChatBalance));
        if (AutoShutdownMinutes is < 0)
        {
            throw new InvalidDataException("Headset auto-shutdown minutes cannot be negative.");
        }
    }
}

public sealed class CoolingPadProfileSettings
{
    public string? FanMode { get; set; }
    public int? FanTargetRpm { get; set; }
    public BladeFanCurveProfile? FanCurve { get; set; }

    internal void ApplySafeDefaults()
    {
        if (FanTargetRpm is < 0)
        {
            throw new InvalidDataException("Cooling pad fan target cannot be negative.");
        }
        FanCurve?.ApplySafeDefaults();
    }
}

public sealed class GamepadProfileSettings
{
    public int? PollingRateHertz { get; set; }
    public int? VibrationStrengthPercent { get; set; }
    public string? MappingPreset { get; set; }

    internal void ApplySafeDefaults()
    {
        if (PollingRateHertz is <= 0)
        {
            throw new InvalidDataException("Gamepad polling rate must be positive.");
        }
        MouseProfileSettings.ValidatePercent(VibrationStrengthPercent, nameof(VibrationStrengthPercent));
    }
}

public sealed class DisplayProfileSettings
{
    public int? BrightnessPercent { get; set; }
    public int? RefreshRateHertz { get; set; }
    public bool? LocalDimmingEnabled { get; set; }
    public bool? NativeDisplayModeEnabled { get; set; }

    internal void ApplySafeDefaults()
    {
        MouseProfileSettings.ValidatePercent(BrightnessPercent, nameof(BrightnessPercent));
        if (RefreshRateHertz is <= 0)
        {
            throw new InvalidDataException("Display refresh rate must be positive.");
        }
    }
}

public sealed class DeviceSettingValue
{
    public string? Kind { get; set; }
    public string? Value { get; set; }

    internal void ApplySafeDefaults()
    {
        Kind = string.IsNullOrWhiteSpace(Kind) ? null : Kind;
    }

    internal DeviceSettingValue Clone() => new() { Kind = Kind, Value = Value };
}
