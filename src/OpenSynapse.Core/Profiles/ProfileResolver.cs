using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Profiles;

/// <summary>
/// Resolves one profile document for one device and power state.
/// This class only combines persisted values; hardware capability checks belong
/// to the coordinator that applies the resolved values.
/// </summary>
public static class ProfileResolver
{
    public static ResolvedProfile Resolve(
        ProfileDocument document,
        DeviceDescriptor device,
        bool? isPluggedIn) =>
        Resolve(document, device, isPluggedIn switch
        {
            true => PowerSourceKind.FullPowerAdapter,
            false => PowerSourceKind.Battery,
            _ => PowerSourceKind.Unknown,
        });

    public static ResolvedProfile Resolve(
        ProfileDocument document,
        DeviceDescriptor device,
        PowerSourceKind powerSource)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(device);

        var active = document.GetActiveProfileDefinition();
        var global = active.Global;
        var deviceSettings = FindDeviceSettings(active.Devices, GetDeviceKey(device));
        var powerSettings = powerSource switch
        {
            PowerSourceKind.FullPowerAdapter => active.PluggedIn,
            PowerSourceKind.LowPowerAdapter => active.LowPowerAdapter,
            PowerSourceKind.Battery => active.OnBattery,
            _ => null,
        };

        return new ResolvedProfile(
            ResolveBlade(global?.Blade, deviceSettings?.Blade, powerSettings?.Blade),
            ResolveMouse(global?.Viper, deviceSettings?.Viper, powerSettings?.Viper),
            ResolveLighting(global?.Lighting, deviceSettings?.Lighting, powerSettings?.Lighting),
            ResolveKeyboard(global?.Keyboard, deviceSettings?.Keyboard, powerSettings?.Keyboard),
            ResolveHeadset(global?.Headset, deviceSettings?.Headset, powerSettings?.Headset),
            ResolveCoolingPad(global?.CoolingPad, deviceSettings?.CoolingPad, powerSettings?.CoolingPad),
            ResolveGamepad(global?.Gamepad, deviceSettings?.Gamepad, powerSettings?.Gamepad),
            ResolveDisplay(global?.Display, deviceSettings?.Display, powerSettings?.Display),
            ResolveLightingZones(
                global?.LightingZones,
                deviceSettings?.LightingZones,
                powerSettings?.LightingZones));
    }

    public static string GetDeviceKey(DeviceDescriptor device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return $"{device.VendorId:X4}:{device.ProductId:X4}";
    }

    public static LightingProfile? ResolveRazerHidLighting(
        ProfileDocument document,
        DeviceDescriptor device,
        bool? isPluggedIn) =>
        ResolveRazerHidLighting(document, device, isPluggedIn switch
        {
            true => PowerSourceKind.FullPowerAdapter,
            false => PowerSourceKind.Battery,
            _ => PowerSourceKind.Unknown,
        });

    public static LightingProfile? ResolveRazerHidLighting(
        ProfileDocument document,
        DeviceDescriptor device,
        PowerSourceKind powerSource)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(device);

        var active = document.GetActiveProfileDefinition();
        var key = GetDeviceKey(device);
        var globalLighting = active.Global.Lighting;
        var deviceLighting = FindDeviceSettings(active.Devices, key)?.Lighting;
        var powerLighting = powerSource switch
        {
            PowerSourceKind.FullPowerAdapter => FindLighting(active.PluggedIn.RazerHidLighting, key),
            PowerSourceKind.LowPowerAdapter => FindLighting(active.LowPowerAdapter.RazerHidLighting, key),
            PowerSourceKind.Battery => FindLighting(active.OnBattery.RazerHidLighting, key),
            _ => null,
        };

        if (!HasLightingSettings(globalLighting) &&
            !HasLightingSettings(deviceLighting) &&
            !HasLightingSettings(powerLighting))
        {
            return null;
        }

        return MergeLighting(globalLighting, deviceLighting, powerLighting);
    }

    /// <summary>
    /// Resolves one lighting zone for a RazerHid device. Zone storage is
    /// power &gt; device &gt; global; the old single-profile entries are read as a
    /// fallback so a profile written before multi-zone support keeps its effect.
    /// </summary>
    public static LightingProfile? ResolveRazerHidLighting(
        ProfileDocument document,
        DeviceDescriptor device,
        PowerSourceKind powerSource,
        string zoneKey,
        string defaultZoneKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultZoneKey);

        var active = document.GetActiveProfileDefinition();
        var key = GetDeviceKey(device);
        var deviceSettings = FindDeviceSettings(active.Devices, key);
        var powerSettings = powerSource switch
        {
            PowerSourceKind.FullPowerAdapter => active.PluggedIn,
            PowerSourceKind.LowPowerAdapter => active.LowPowerAdapter,
            PowerSourceKind.Battery => active.OnBattery,
            _ => null,
        };

        IReadOnlyDictionary<string, LightingProfile>? powerZones = null;
        if (powerSettings is not null &&
            powerSettings.RazerHidLightingZones.TryGetValue(key, out var zones))
        {
            powerZones = zones;
        }

        var globalZone = FindLighting(active.Global.LightingZones, zoneKey);
        var deviceZone = FindLighting(deviceSettings?.LightingZones, zoneKey);
        var powerZone = FindLighting(powerZones, zoneKey);
        if (HasLightingSettings(globalZone) || HasLightingSettings(deviceZone) || HasLightingSettings(powerZone))
        {
            return MergeLighting(globalZone, deviceZone, powerZone);
        }

        // Use the raw layer entry for the zone parameter: MergeLighting clears
        // parameters for an "off" profile, which would otherwise lose the zone
        // that the old single-zone format stored.
        LightingProfile? legacySource = null;
        if (powerSettings is not null)
        {
            legacySource = FindLighting(powerSettings.RazerHidLighting, key);
        }
        if (!HasLightingSettings(legacySource) && HasLightingSettings(deviceSettings?.Lighting))
        {
            legacySource = deviceSettings!.Lighting;
        }
        if (!HasLightingSettings(legacySource) && HasLightingSettings(active.Global.Lighting))
        {
            legacySource = active.Global.Lighting;
        }
        if (!HasLightingSettings(legacySource))
        {
            return null;
        }

        if (legacySource!.Parameters.TryGetValue("zone", out var legacyZone))
        {
            return StringComparer.OrdinalIgnoreCase.Equals(legacyZone, zoneKey)
                ? ResolveRazerHidLighting(document, device, powerSource)
                : null;
        }

        return StringComparer.OrdinalIgnoreCase.Equals(zoneKey, defaultZoneKey)
            ? ResolveRazerHidLighting(document, device, powerSource)
            : null;
    }

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
    };

    private static HeadsetProfileSettings ResolveHeadset(
        HeadsetProfileSettings? global,
        HeadsetProfileSettings? device,
        HeadsetProfileSettings? power) => new()
    {
        VolumePercent = First(power?.VolumePercent, device?.VolumePercent, global?.VolumePercent),
        SideTone = First(power?.SideTone, device?.SideTone, global?.SideTone),
        GameChatBalance = First(power?.GameChatBalance, device?.GameChatBalance, global?.GameChatBalance),
        AncEnabled = First(power?.AncEnabled, device?.AncEnabled, global?.AncEnabled),
        ThxEnabled = First(power?.ThxEnabled, device?.ThxEnabled, global?.ThxEnabled),
        EqPreset = power?.EqPreset ?? device?.EqPreset ?? global?.EqPreset,
        MicrophoneVolumePercent = First(
            power?.MicrophoneVolumePercent,
            device?.MicrophoneVolumePercent,
            global?.MicrophoneVolumePercent),
        MicrophoneMute = First(power?.MicrophoneMute, device?.MicrophoneMute, global?.MicrophoneMute),
        HapticEnabled = First(power?.HapticEnabled, device?.HapticEnabled, global?.HapticEnabled),
        AutoShutdownMinutes = First(
            power?.AutoShutdownMinutes,
            device?.AutoShutdownMinutes,
            global?.AutoShutdownMinutes),
    };

    private static CoolingPadProfileSettings ResolveCoolingPad(
        CoolingPadProfileSettings? global,
        CoolingPadProfileSettings? device,
        CoolingPadProfileSettings? power) => new()
    {
        FanMode = power?.FanMode ?? device?.FanMode ?? global?.FanMode,
        FanTargetRpm = First(power?.FanTargetRpm, device?.FanTargetRpm, global?.FanTargetRpm),
        FanCurve = (power?.FanCurve ?? device?.FanCurve ?? global?.FanCurve)?.Clone(),
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
    };

    private static DisplayProfileSettings ResolveDisplay(
        DisplayProfileSettings? global,
        DisplayProfileSettings? device,
        DisplayProfileSettings? power) => new()
    {
        BrightnessPercent = First(power?.BrightnessPercent, device?.BrightnessPercent, global?.BrightnessPercent),
        RefreshRateHertz = First(power?.RefreshRateHertz, device?.RefreshRateHertz, global?.RefreshRateHertz),
        LocalDimmingEnabled = First(
            power?.LocalDimmingEnabled,
            device?.LocalDimmingEnabled,
            global?.LocalDimmingEnabled),
        NativeDisplayModeEnabled = First(
            power?.NativeDisplayModeEnabled,
            device?.NativeDisplayModeEnabled,
            global?.NativeDisplayModeEnabled),
    };

    private static Dictionary<string, LightingProfile> ResolveLightingZones(
        IReadOnlyDictionary<string, LightingProfile>? global,
        IReadOnlyDictionary<string, LightingProfile>? device,
        IReadOnlyDictionary<string, LightingProfile>? power)
    {
        var result = new Dictionary<string, LightingProfile>(StringComparer.OrdinalIgnoreCase);
        MergeLightingZones(result, global);
        MergeLightingZones(result, device);
        MergeLightingZones(result, power);
        return result;
    }

    private static void MergeLightingZones(
        IDictionary<string, LightingProfile> destination,
        IReadOnlyDictionary<string, LightingProfile>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var (zone, lighting) in source)
        {
            if (string.IsNullOrWhiteSpace(zone) || lighting is null)
            {
                continue;
            }
            destination[zone] = destination.TryGetValue(zone, out var current)
                ? MergeLighting(current, lighting, null)
                : MergeLighting(null, lighting, null);
        }
    }

    private static LightingProfile ResolveLighting(
        LightingProfile? global,
        LightingProfile? device,
        LightingProfile? power) =>
        MergeLighting(global, device, power, defaultEffect: "off");

    private static bool HasLightingOverride(LightingProfile? lighting) =>
        lighting is not null &&
        !string.IsNullOrWhiteSpace(lighting.Effect);

    private static bool HasLightingSettings(LightingProfile? lighting) =>
        lighting is not null &&
        (!string.IsNullOrWhiteSpace(lighting.Effect) || lighting.Parameters.Count > 0);

    private static LightingProfile? FindLighting(
        IReadOnlyDictionary<string, LightingProfile>? values,
        string key)
    {
        if (values is null)
        {
            return null;
        }

        return values.FirstOrDefault(pair =>
            StringComparer.OrdinalIgnoreCase.Equals(pair.Key, key)).Value;
    }

    private static LightingProfile MergeLighting(
        LightingProfile? global,
        LightingProfile? device,
        LightingProfile? power,
        string defaultEffect = "")
    {
        var result = new LightingProfile { Effect = defaultEffect };
        ApplyLightingLayer(result, global);
        ApplyLightingLayer(result, device);
        ApplyLightingLayer(result, power);
        if (StringComparer.OrdinalIgnoreCase.Equals(result.Effect, "off"))
        {
            result.Parameters.Clear();
        }

        return result;
    }

    private static void ApplyLightingLayer(LightingProfile destination, LightingProfile? source)
    {
        if (source is null)
        {
            return;
        }

        if (HasLightingOverride(source))
        {
            destination.Effect = source.Effect;
            destination.Parameters.Clear();
        }
        CopyParameters(destination.Parameters, source.Parameters);
    }

    private static void CopyParameters(
        IDictionary<string, string> destination,
        IReadOnlyDictionary<string, string>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var pair in source)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            {
                destination[pair.Key] = pair.Value;
            }
        }
    }

    private static DeviceProfileSettings? FindDeviceSettings(
        IReadOnlyDictionary<string, DeviceProfileSettings>? devices,
        string key)
    {
        if (devices is null)
        {
            return null;
        }

        if (devices.TryGetValue(key, out var exact))
        {
            return exact;
        }

        foreach (var pair in devices)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(pair.Key, key))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static T? First<T>(T? power, T? device, T? global)
        where T : struct => power ?? device ?? global;
}

public sealed record ResolvedProfile(
    BladeProfileSettings Blade,
    MouseProfileSettings Viper,
    LightingProfile Lighting,
    KeyboardProfileSettings Keyboard,
    HeadsetProfileSettings Headset,
    CoolingPadProfileSettings CoolingPad,
    GamepadProfileSettings Gamepad,
    DisplayProfileSettings Display,
    IReadOnlyDictionary<string, LightingProfile> LightingZones);
