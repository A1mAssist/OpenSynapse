using System.Globalization;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerBleProfileApplier
{
    private static bool TryParseScrollMode(string? value, out byte mode)
    {
        mode = 0;
        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.TryParse<MouseScrollMode>(value, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return false;
        }

        mode = parsed switch
        {
            MouseScrollMode.Tactile => 0,
            MouseScrollMode.FreeSpin => 1,
            MouseScrollMode.MicroTactile => 2,
            _ => 0,
        };
        return true;
    }

    private static bool TryParsePowerPlan(string? value, out RazerBlePowerPlan plan)
    {
        plan = RazerBlePowerPlan.Balance;
        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.TryParse<MousePowerPlan>(value, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return false;
        }

        plan = parsed switch
        {
            MousePowerPlan.PowerSave => RazerBlePowerPlan.PowerSave,
            MousePowerPlan.Balance => RazerBlePowerPlan.Balance,
            MousePowerPlan.HighPerformance => RazerBlePowerPlan.HighPerformance,
            _ => RazerBlePowerPlan.Balance,
        };
        return true;
    }

    private sealed record LightingDesired(
        RazerBleLightingEffect Effect,
        byte Flags,
        byte Rate,
        int Brightness,
        byte[] Colors,
        byte Direction);

    private static bool TryBuildLighting(LightingProfile profile, out LightingDesired desired)
    {
        desired = null!;
        if (!Enum.TryParse<RazerBleLightingEffect>(profile.Effect, ignoreCase: true, out var effect) ||
            !Enum.IsDefined(effect))
        {
            return false;
        }

        var parameters = profile.Parameters;
        var brightness = parameters.TryGetValue("brightness", out var rawBrightness) &&
            int.TryParse(rawBrightness, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBrightness)
                ? Math.Clamp(parsedBrightness, 0, 100)
                : 100;
        var directionIndex = parameters.TryGetValue("direction", out var rawDirection) &&
            int.TryParse(rawDirection, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDirection)
                ? Math.Clamp(parsedDirection, 0, 1)
                : 0;
        var color = parameters.TryGetValue("color", out var rawColor) && rawColor.Length == 6
            ? rawColor
            : "FF0000";
        var colors = new byte[3];
        for (var index = 0; index < 3; index++)
        {
            if (!byte.TryParse(
                color.AsSpan(index * 2, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out colors[index]))
            {
                return false;
            }
        }

        var (flags, rate) = effect switch
        {
            RazerBleLightingEffect.Breathing => ((byte)1, (byte)0),
            RazerBleLightingEffect.Wave => ((byte)(directionIndex == 0 ? 2 : 1), (byte)40),
            RazerBleLightingEffect.Reactive => ((byte)0, (byte)1),
            RazerBleLightingEffect.Wheel => ((byte)(directionIndex + 1), (byte)0),
            _ => ((byte)0, (byte)0),
        };
        var direction = effect switch
        {
            RazerBleLightingEffect.Wave => (byte)(directionIndex == 0 ? 2 : 1),
            RazerBleLightingEffect.Wheel => (byte)(directionIndex + 1),
            _ => (byte)0,
        };

        desired = new LightingDesired(
            effect,
            flags,
            rate,
            brightness,
            effect is RazerBleLightingEffect.Off or RazerBleLightingEffect.Spectrum or
                RazerBleLightingEffect.Wave or RazerBleLightingEffect.Wheel
                ? []
                : colors,
            direction);
        return true;
    }

    private static bool LightingEquals(RazerBleLightingState current, LightingDesired desired) =>
        current.EffectId == (byte)desired.Effect &&
        current.BrightnessPercent == desired.Brightness &&
        (desired.Effect is not (RazerBleLightingEffect.Wave or RazerBleLightingEffect.Wheel) ||
            current.Direction == desired.Direction) &&
        (desired.Colors.Length == 0 ||
            (current.Colors.Count >= 3 &&
                current.Colors[0] == desired.Colors[0] &&
                current.Colors[1] == desired.Colors[1] &&
                current.Colors[2] == desired.Colors[2]));

    private static MouseProfileSettings ResolveMouse(
        MouseProfileSettings? global,
        MouseProfileSettings? device,
        MouseProfileSettings? power) => new()
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
        SmartReelThreshold = First(power?.SmartReelThreshold, device?.SmartReelThreshold, global?.SmartReelThreshold),
        ActiveOnboardProfileId = First(
            power?.ActiveOnboardProfileId,
            device?.ActiveOnboardProfileId,
            global?.ActiveOnboardProfileId),
        HapticScrollLevel = First(power?.HapticScrollLevel, device?.HapticScrollLevel, global?.HapticScrollLevel),
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
        DpiStages = power?.DpiStages ?? device?.DpiStages ?? global?.DpiStages,
    };

    private static T? First<T>(T? power, T? device, T? global)
        where T : struct => power ?? device ?? global;
}
