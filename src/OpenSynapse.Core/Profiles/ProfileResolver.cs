using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Core.Profiles;

/// <summary>
/// Resolves one profile document for one device and power state.
/// This class only combines persisted values; hardware capability checks belong
/// to the coordinator that applies the resolved values.
/// </summary>
public static partial class ProfileResolver
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
        => ResolveWithDeviceKey(document, device, powerSource, GetDeviceKey(device));

    /// <summary>
    /// Resolves a peripheral that stores its settings under a transport identity
    /// such as a serial number instead of the shared VID:PID key.
    /// </summary>
    public static ResolvedProfile ResolveWithDeviceKey(
        ProfileDocument document,
        DeviceDescriptor device,
        PowerSourceKind powerSource,
        string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);

        var active = document.GetActiveProfileDefinition();
        var global = active.Global;
        var deviceSettings = FindDeviceSettings(active.Devices, deviceKey);
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
            ResolveChroma(global?.Chroma, deviceSettings?.Chroma, powerSettings?.Chroma),
            ResolveMixer(global?.Mixer, deviceSettings?.Mixer, powerSettings?.Mixer),
            ResolveKiyo(global?.Kiyo, deviceSettings?.Kiyo, powerSettings?.Kiyo),
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

    /// <summary>
    /// Resolves a headset stored under a transport-specific key such as
    /// <c>ble:{id}</c> or a serial number. The shared resolver keys only on VID:PID,
    /// which would make two Bluetooth headsets share one settings entry.
    /// </summary>
    public static HeadsetProfileSettings ResolveHeadset(
        ProfileDocument document,
        PowerSourceKind powerSource,
        string deviceKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);

        var active = document.GetActiveProfileDefinition();
        var powerSettings = powerSource switch
        {
            PowerSourceKind.FullPowerAdapter => active.PluggedIn,
            PowerSourceKind.LowPowerAdapter => active.LowPowerAdapter,
            PowerSourceKind.Battery => active.OnBattery,
            _ => null,
        };
        return ResolveHeadset(
            active.Global.Headset,
            FindDeviceSettings(active.Devices, deviceKey)?.Headset,
            powerSettings?.Headset);
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

    private static HeadsetProfileSettings ResolveHeadset(
        HeadsetProfileSettings? global,
        HeadsetProfileSettings? device,
        HeadsetProfileSettings? power) => new()
    {
        VolumePercent = First(power?.VolumePercent, device?.VolumePercent, global?.VolumePercent),
        SideTone = First(power?.SideTone, device?.SideTone, global?.SideTone),
        GameModeEnabled = First(
            power?.GameModeEnabled,
            device?.GameModeEnabled,
            global?.GameModeEnabled),
        GameChatBalance = First(power?.GameChatBalance, device?.GameChatBalance, global?.GameChatBalance),
        AncEnabled = First(power?.AncEnabled, device?.AncEnabled, global?.AncEnabled),
        AncModeIndex = First(power?.AncModeIndex, device?.AncModeIndex, global?.AncModeIndex),
        AncLevel = First(power?.AncLevel, device?.AncLevel, global?.AncLevel),
        ThxEnabled = First(power?.ThxEnabled, device?.ThxEnabled, global?.ThxEnabled),
        ThxSpatialEnabled = First(
            power?.ThxSpatialEnabled,
            device?.ThxSpatialEnabled,
            global?.ThxSpatialEnabled),
        ThxEqualizerEnabled = First(
            power?.ThxEqualizerEnabled,
            device?.ThxEqualizerEnabled,
            global?.ThxEqualizerEnabled),
        ThxPreset = power?.ThxPreset ?? device?.ThxPreset ?? global?.ThxPreset,
        ThxBands = CloneMap(power?.ThxBands ?? device?.ThxBands ?? global?.ThxBands),
        ThxNormalizationEnabled = First(
            power?.ThxNormalizationEnabled,
            device?.ThxNormalizationEnabled,
            global?.ThxNormalizationEnabled),
        ThxNormalizationLevel = First(
            power?.ThxNormalizationLevel,
            device?.ThxNormalizationLevel,
            global?.ThxNormalizationLevel),
        ThxVoiceClarityEnabled = First(
            power?.ThxVoiceClarityEnabled,
            device?.ThxVoiceClarityEnabled,
            global?.ThxVoiceClarityEnabled),
        ThxVoiceClarityLevel = First(
            power?.ThxVoiceClarityLevel,
            device?.ThxVoiceClarityLevel,
            global?.ThxVoiceClarityLevel),
        ThxBassBoostEnabled = First(
            power?.ThxBassBoostEnabled,
            device?.ThxBassBoostEnabled,
            global?.ThxBassBoostEnabled),
        ThxBassBoostLevel = First(
            power?.ThxBassBoostLevel,
            device?.ThxBassBoostLevel,
            global?.ThxBassBoostLevel),
        ThxSidetoneEnabled = First(
            power?.ThxSidetoneEnabled,
            device?.ThxSidetoneEnabled,
            global?.ThxSidetoneEnabled),
        ThxSidetoneLevel = First(
            power?.ThxSidetoneLevel,
            device?.ThxSidetoneLevel,
            global?.ThxSidetoneLevel),
        EqPreset = power?.EqPreset ?? device?.EqPreset ?? global?.EqPreset,
        EqBands = CloneMap(power?.EqBands ?? device?.EqBands ?? global?.EqBands),
        MicEqPreset = power?.MicEqPreset ?? device?.MicEqPreset ?? global?.MicEqPreset,
        MicEqBands = CloneMap(power?.MicEqBands ?? device?.MicEqBands ?? global?.MicEqBands),
        Bass = First(power?.Bass, device?.Bass, global?.Bass),
        MicrophoneVolumePercent = First(
            power?.MicrophoneVolumePercent,
            device?.MicrophoneVolumePercent,
            global?.MicrophoneVolumePercent),
        MicrophoneMute = First(power?.MicrophoneMute, device?.MicrophoneMute, global?.MicrophoneMute),
        NoiseSuppression = First(power?.NoiseSuppression, device?.NoiseSuppression, global?.NoiseSuppression),
        AudioSourceIndex = First(power?.AudioSourceIndex, device?.AudioSourceIndex, global?.AudioSourceIndex),
        Dolby = First(power?.Dolby, device?.Dolby, global?.Dolby),
        NightMode = First(power?.NightMode, device?.NightMode, global?.NightMode),
        VoicePrompts = First(power?.VoicePrompts, device?.VoicePrompts, global?.VoicePrompts),
        MicDeEsser = First(power?.MicDeEsser, device?.MicDeEsser, global?.MicDeEsser),
        MicLimiterDb = First(power?.MicLimiterDb, device?.MicLimiterDb, global?.MicLimiterDb),
        MicReverbIndex = First(power?.MicReverbIndex, device?.MicReverbIndex, global?.MicReverbIndex),
        MicReverbIntensity = First(
            power?.MicReverbIntensity,
            device?.MicReverbIntensity,
            global?.MicReverbIntensity),
        MicCompressorEnabled = First(
            power?.MicCompressorEnabled,
            device?.MicCompressorEnabled,
            global?.MicCompressorEnabled),
        MicCompressorProfile = First(
            power?.MicCompressorProfile,
            device?.MicCompressorProfile,
            global?.MicCompressorProfile),
        MicCompressorDb = First(power?.MicCompressorDb, device?.MicCompressorDb, global?.MicCompressorDb),
        MicCompressorRatio = First(
            power?.MicCompressorRatio,
            device?.MicCompressorRatio,
            global?.MicCompressorRatio),
        MicCompressorAttack = First(
            power?.MicCompressorAttack,
            device?.MicCompressorAttack,
            global?.MicCompressorAttack),
        MicCompressorRelease = First(
            power?.MicCompressorRelease,
            device?.MicCompressorRelease,
            global?.MicCompressorRelease),
        MicMonitoringEnabled = First(
            power?.MicMonitoringEnabled,
            device?.MicMonitoringEnabled,
            global?.MicMonitoringEnabled),
        ParametricEqEnabled = First(
            power?.ParametricEqEnabled,
            device?.ParametricEqEnabled,
            global?.ParametricEqEnabled),
        ParametricEqBandIndex = First(
            power?.ParametricEqBandIndex,
            device?.ParametricEqBandIndex,
            global?.ParametricEqBandIndex),
        ParametricEqFrequency = First(
            power?.ParametricEqFrequency,
            device?.ParametricEqFrequency,
            global?.ParametricEqFrequency),
        ParametricEqGain = First(
            power?.ParametricEqGain,
            device?.ParametricEqGain,
            global?.ParametricEqGain),
        ParametricEqQ = First(power?.ParametricEqQ, device?.ParametricEqQ, global?.ParametricEqQ),
        ParametricEqProfile = First(
            power?.ParametricEqProfile,
            device?.ParametricEqProfile,
            global?.ParametricEqProfile),
        ParametricEqDirection = First(
            power?.ParametricEqDirection,
            device?.ParametricEqDirection,
            global?.ParametricEqDirection),
        ParametricEqFilterType = First(
            power?.ParametricEqFilterType,
            device?.ParametricEqFilterType,
            global?.ParametricEqFilterType),
        HapticEnabled = First(power?.HapticEnabled, device?.HapticEnabled, global?.HapticEnabled),
        HapticsStrength = First(power?.HapticsStrength, device?.HapticsStrength, global?.HapticsStrength),
        AutoPause = First(power?.AutoPause, device?.AutoPause, global?.AutoPause),
        AutoShutdownMinutes = First(
            power?.AutoShutdownMinutes,
            device?.AutoShutdownMinutes,
            global?.AutoShutdownMinutes),
        Dnd = First(power?.Dnd, device?.Dnd, global?.Dnd),
        GestureControl = First(power?.GestureControl, device?.GestureControl, global?.GestureControl),
        ChromaBrightness = First(power?.ChromaBrightness, device?.ChromaBrightness, global?.ChromaBrightness),
        ChromaEffect = power?.ChromaEffect ?? device?.ChromaEffect ?? global?.ChromaEffect,
        ChromaParameters = CloneMap(power?.ChromaParameters ?? device?.ChromaParameters ?? global?.ChromaParameters),
    };

    private static CoolingPadProfileSettings ResolveCoolingPad(
        CoolingPadProfileSettings? global,
        CoolingPadProfileSettings? device,
        CoolingPadProfileSettings? power) => new()
    {
        FanMode = power?.FanMode ?? device?.FanMode ?? global?.FanMode,
        FanTargetRpm = First(power?.FanTargetRpm, device?.FanTargetRpm, global?.FanTargetRpm),
        FanCurve = (power?.FanCurve ?? device?.FanCurve ?? global?.FanCurve)?.Clone(),
        LightingEnabled = First(power?.LightingEnabled, device?.LightingEnabled, global?.LightingEnabled),
        LightingEffect = power?.LightingEffect ?? device?.LightingEffect ?? global?.LightingEffect,
        LightingParameters = CloneMap(
            power?.LightingParameters ?? device?.LightingParameters ?? global?.LightingParameters),
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

    private static List<ButtonMappingProfile>? CloneMappings(
        IReadOnlyCollection<ButtonMappingProfile>? source) =>
        source?.Select(mapping => mapping.Clone()).ToList();

    private static Dictionary<string, TValue>? CloneMap<TValue>(
        IReadOnlyDictionary<string, TValue>? source) =>
        source is null
            ? null
            : new Dictionary<string, TValue>(source, StringComparer.OrdinalIgnoreCase);
}

public sealed record ResolvedProfile(
    BladeProfileSettings Blade,
    MouseProfileSettings Viper,
    LightingProfile Lighting,
    KeyboardProfileSettings Keyboard,
    HeadsetProfileSettings Headset,
    CoolingPadProfileSettings CoolingPad,
    GamepadProfileSettings Gamepad,
    ChromaProfileSettings Chroma,
    MixerProfileSettings Mixer,
    KiyoProfileSettings Kiyo,
    DisplayProfileSettings Display,
    IReadOnlyDictionary<string, LightingProfile> LightingZones);
