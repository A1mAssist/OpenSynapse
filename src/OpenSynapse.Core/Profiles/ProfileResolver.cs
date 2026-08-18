using OpenSynapse.Core.Devices;

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
        bool? isPluggedIn)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(device);

        var active = document.GetActiveProfileDefinition();
        var global = active.Global;
        var deviceSettings = FindDeviceSettings(active.Devices, GetDeviceKey(device));
        var powerSettings = isPluggedIn switch
        {
            true => active.PluggedIn,
            false => active.OnBattery,
            null => null,
        };

        return new ResolvedProfile(
            ResolveBlade(global?.Blade, deviceSettings?.Blade, powerSettings?.Blade),
            ResolveViper(global?.Viper, deviceSettings?.Viper, powerSettings?.Viper),
            ResolveLighting(global?.Lighting, deviceSettings?.Lighting, powerSettings?.Lighting));
    }

    public static string GetDeviceKey(DeviceDescriptor device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return $"{device.VendorId:X4}:{device.ProductId:X4}";
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
        };
    }

    private static ViperProfileSettings ResolveViper(
        ViperProfileSettings? global,
        ViperProfileSettings? device,
        ViperProfileSettings? power)
    {
        return new ViperProfileSettings
        {
            DpiX = First(power?.DpiX, device?.DpiX, global?.DpiX),
            DpiY = First(power?.DpiY, device?.DpiY, global?.DpiY),
            PollingRateHertz = First(power?.PollingRateHertz, device?.PollingRateHertz, global?.PollingRateHertz),
            IdleSeconds = First(power?.IdleSeconds, device?.IdleSeconds, global?.IdleSeconds),
            DpiStages = (power?.DpiStages ?? device?.DpiStages ?? global?.DpiStages)?.Clone(),
        };
    }

    private static LightingProfile ResolveLighting(
        LightingProfile? global,
        LightingProfile? device,
        LightingProfile? power)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CopyParameters(parameters, global?.Parameters);
        CopyParameters(parameters, device?.Parameters);
        CopyParameters(parameters, power?.Parameters);

        return new LightingProfile
        {
            Effect = ResolveEffect(global, device, power),
            Parameters = parameters,
        };
    }

    private static string ResolveEffect(
        LightingProfile? global,
        LightingProfile? device,
        LightingProfile? power)
    {
        var effect = string.IsNullOrWhiteSpace(global?.Effect) ? "off" : global.Effect;
        if (HasLightingOverride(device))
        {
            effect = device!.Effect!;
        }
        if (HasLightingOverride(power))
        {
            effect = power!.Effect!;
        }

        return effect;
    }

    private static bool HasLightingOverride(LightingProfile? lighting) =>
        lighting is not null &&
        !string.IsNullOrWhiteSpace(lighting.Effect) &&
        !StringComparer.OrdinalIgnoreCase.Equals(lighting.Effect, "off");

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
    ViperProfileSettings Viper,
    LightingProfile Lighting);
