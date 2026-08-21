using OpenSynapse.Core.Devices;

namespace OpenSynapse.Core.Profiles;

public sealed class ProfileDocument
{
    public int Version { get; set; } = ProfileStore.CurrentVersion;
    public string ActiveProfileName { get; set; } = ProfileCatalog.DefaultProfileName;
    public Dictionary<string, ProfileDefinition> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ProfileSettings Global { get; set; } = new();
    public Dictionary<string, DeviceProfileSettings> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PowerProfileOverrides PluggedIn { get; set; } = new();
    public PowerProfileOverrides OnBattery { get; set; } = new();
    public Dictionary<string, string> ApplicationBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ProfileDocument CreateDefault()
    {
        var document = new ProfileDocument();
        document.EnsureProfileCatalog();
        return document;
    }

    public ProfileDocument Clone()
    {
        EnsureProfileCatalog();
        var clone = new ProfileDocument
        {
            Version = Version,
            ActiveProfileName = ActiveProfileName,
            ApplicationBindings = new Dictionary<string, string>(ApplicationBindings, StringComparer.OrdinalIgnoreCase),
        };
        foreach (var (name, profile) in Profiles)
        {
            clone.Profiles[name] = profile.Clone();
        }

        clone.EnsureProfileCatalog();
        return clone;
    }

    internal void ApplySafeDefaults()
    {
        Global ??= new ProfileSettings();
        Devices = ProfileDictionary.Normalize(Devices);
        PluggedIn ??= new PowerProfileOverrides();
        OnBattery ??= new PowerProfileOverrides();
        ApplicationBindings = ProfileDictionary.Normalize(ApplicationBindings);
        Global.ApplySafeDefaults();
        foreach (var settings in Devices.Values)
        {
            settings?.ApplySafeDefaults();
        }

        PluggedIn.ApplySafeDefaults();
        OnBattery.ApplySafeDefaults();
        EnsureProfileCatalog();
    }

    internal ProfileDefinition GetActiveProfileDefinition()
    {
        EnsureProfileCatalog();
        return Profiles[ActiveProfileName];
    }

    internal void EnsureProfileCatalog()
    {
        Profiles = ProfileDictionary.Normalize(Profiles);
        if (Profiles.Count == 0)
        {
            Profiles[ProfileCatalog.DefaultProfileName] = new ProfileDefinition
            {
                Global = Global,
                Devices = Devices,
                PluggedIn = PluggedIn,
                OnBattery = OnBattery,
            };
        }

        if (string.IsNullOrWhiteSpace(ActiveProfileName) ||
            !Profiles.ContainsKey(ActiveProfileName))
        {
            ActiveProfileName = Profiles.Keys.First();
        }

        foreach (var profile in Profiles.Values)
        {
            profile?.ApplySafeDefaults();
        }

        var active = Profiles[ActiveProfileName];
        Global = active.Global;
        Devices = active.Devices;
        PluggedIn = active.PluggedIn;
        OnBattery = active.OnBattery;
    }
}

public sealed class ProfileDefinition
{
    public ProfileSettings Global { get; set; } = new();
    public Dictionary<string, DeviceProfileSettings> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PowerProfileOverrides PluggedIn { get; set; } = new();
    public PowerProfileOverrides OnBattery { get; set; } = new();

    internal void ApplySafeDefaults()
    {
        Global ??= new ProfileSettings();
        Devices = ProfileDictionary.Normalize(Devices);
        PluggedIn ??= new PowerProfileOverrides();
        OnBattery ??= new PowerProfileOverrides();
        Global.ApplySafeDefaults();
        foreach (var settings in Devices.Values)
        {
            settings?.ApplySafeDefaults();
        }

        PluggedIn.ApplySafeDefaults();
        OnBattery.ApplySafeDefaults();
    }

    internal ProfileDefinition Clone()
    {
        ApplySafeDefaults();
        var clone = new ProfileDefinition
        {
            Global = new ProfileSettings
            {
                Blade = CloneBlade(Global.Blade),
                Viper = CloneViper(Global.Viper),
                Lighting = CloneLighting(Global.Lighting),
            },
            PluggedIn = ClonePower(PluggedIn),
            OnBattery = ClonePower(OnBattery),
        };
        foreach (var (key, settings) in Devices)
        {
            clone.Devices[key] = new DeviceProfileSettings
            {
                Blade = CloneBlade(settings.Blade),
                Viper = CloneViper(settings.Viper),
                Lighting = CloneLighting(settings.Lighting),
            };
        }

        return clone;
    }

    private static PowerProfileOverrides ClonePower(PowerProfileOverrides source) => new()
    {
        Blade = CloneBlade(source.Blade),
        Viper = CloneViper(source.Viper),
        Lighting = CloneLighting(source.Lighting),
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
    };

    private static ViperProfileSettings CloneViper(ViperProfileSettings source) => new()
    {
        DpiX = source.DpiX,
        DpiY = source.DpiY,
        PollingRateHertz = source.PollingRateHertz,
        IdleSeconds = source.IdleSeconds,
        DpiStages = source.DpiStages?.Clone(),
    };

    private static LightingProfile CloneLighting(LightingProfile source) => new()
    {
        Effect = source.Effect,
        Parameters = new Dictionary<string, string>(source.Parameters, StringComparer.OrdinalIgnoreCase),
    };
}

public sealed class ProfileSettings
{
    public BladeProfileSettings Blade { get; set; } = new();
    public ViperProfileSettings Viper { get; set; } = new();
    public LightingProfile Lighting { get; set; } = new();

    internal void ApplySafeDefaults()
    {
        Blade ??= new BladeProfileSettings();
        Viper ??= new ViperProfileSettings();
        Lighting ??= new LightingProfile();
        Blade.ApplySafeDefaults();
        Viper.ApplySafeDefaults();
        Lighting.ApplySafeDefaults();
    }
}

public sealed class DeviceProfileSettings
{
    public BladeProfileSettings Blade { get; set; } = new();
    public ViperProfileSettings Viper { get; set; } = new();
    public LightingProfile Lighting { get; set; } = new();

    internal void ApplySafeDefaults()
    {
        Blade ??= new BladeProfileSettings();
        Viper ??= new ViperProfileSettings();
        Lighting ??= new LightingProfile();
        Blade.ApplySafeDefaults();
        Viper.ApplySafeDefaults();
        Lighting.ApplySafeDefaults();
    }
}

public sealed class PowerProfileOverrides
{
    public BladeProfileSettings Blade { get; set; } = new();
    public ViperProfileSettings Viper { get; set; } = new();
    public LightingProfile Lighting { get; set; } = new();

    internal void ApplySafeDefaults()
    {
        Blade ??= new BladeProfileSettings();
        Viper ??= new ViperProfileSettings();
        Lighting ??= new LightingProfile();
        Blade.ApplySafeDefaults();
        Viper.ApplySafeDefaults();
        Lighting.ApplySafeDefaults();
    }
}

public sealed class BladeProfileSettings
{
    public byte? KeyboardBrightness { get; set; }
    public byte? PerformanceMode { get; set; }
    public byte? FanMode { get; set; }
    public int? FanTargetRpm { get; set; }
    public BladeFanCurveProfile? FanCurve { get; set; }
    public int? ChargeLimitPercent { get; set; }
    public int? RefreshRateHertz { get; set; }
    public byte? MaxFanMode { get; set; }
    public byte? CpuBoostMode { get; set; }
    public byte? GpuBoostMode { get; set; }
    public byte? LogoMode { get; set; }
    public bool? GamingModeEnabled { get; set; }

    internal void ApplySafeDefaults() => FanCurve?.ApplySafeDefaults();
}

public sealed class BladeFanCurveProfile
{
    public BladeFanCurveTemperatureMode TemperatureMode { get; set; }
    public List<BladeFanCurvePoint> CpuPoints { get; set; } = [];
    public List<BladeFanCurvePoint> GpuPoints { get; set; } = [];
    public int MinimumCpuTemperatureCelsius { get; set; } =
        BladeFanCurve.DefaultMinimumCpuTemperatureCelsius;
    public int MinimumGpuTemperatureCelsius { get; set; } =
        BladeFanCurve.DefaultMinimumGpuTemperatureCelsius;
    public int MinimumFanSpeedRpm { get; set; } = BladeFanCurve.DefaultMinimumFanSpeedRpm;

    public BladeFanCurve CreateCurve() => new(
        TemperatureMode,
        CpuPoints,
        GpuPoints,
        MinimumCpuTemperatureCelsius,
        MinimumGpuTemperatureCelsius,
        MinimumFanSpeedRpm);

    public static BladeFanCurveProfile FromCurve(BladeFanCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return new()
        {
            TemperatureMode = curve.TemperatureMode,
            CpuPoints = curve.CpuPoints.ToList(),
            GpuPoints = curve.GpuPoints.ToList(),
            MinimumCpuTemperatureCelsius = curve.MinimumCpuTemperatureCelsius,
            MinimumGpuTemperatureCelsius = curve.MinimumGpuTemperatureCelsius,
            MinimumFanSpeedRpm = curve.MinimumFanSpeedRpm,
        };
    }

    internal BladeFanCurveProfile Clone()
    {
        ApplySafeDefaults();
        return new()
        {
            TemperatureMode = TemperatureMode,
            CpuPoints = CpuPoints.ToList(),
            GpuPoints = GpuPoints.ToList(),
            MinimumCpuTemperatureCelsius = MinimumCpuTemperatureCelsius,
            MinimumGpuTemperatureCelsius = MinimumGpuTemperatureCelsius,
            MinimumFanSpeedRpm = MinimumFanSpeedRpm,
        };
    }

    internal void ApplySafeDefaults()
    {
        CpuPoints ??= [];
        GpuPoints ??= [];
    }
}

public sealed class ViperProfileSettings
{
    public int? DpiX { get; set; }
    public int? DpiY { get; set; }
    public int? PollingRateHertz { get; set; }
    public int? IdleSeconds { get; set; }
    public ViperDpiStagesProfile? DpiStages { get; set; }

    internal void ApplySafeDefaults() => DpiStages?.ApplySafeDefaults();
}

public sealed class ViperDpiStagesProfile
{
    public byte ActiveStage { get; set; }
    public List<ViperDpiStageProfile> Stages { get; set; } = [];

    internal void ApplySafeDefaults() =>
        Stages = Stages?.Where(stage => stage is not null).ToList() ?? [];

    internal ViperDpiStagesProfile Clone()
    {
        ApplySafeDefaults();
        return new ViperDpiStagesProfile
        {
            ActiveStage = ActiveStage,
            Stages = Stages.Select(stage => stage.Clone()).ToList(),
        };
    }
}

public sealed class ViperDpiStageProfile
{
    public byte Number { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    internal ViperDpiStageProfile Clone() => new() { Number = Number, X = X, Y = Y };
}

public sealed class LightingProfile
{
    public string Effect { get; set; } = "off";
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal void ApplySafeDefaults()
    {
        if (string.IsNullOrWhiteSpace(Effect))
        {
            Effect = "off";
        }

        Parameters = ProfileDictionary.Normalize(Parameters);
    }
}

internal static class ProfileDictionary
{
    public static Dictionary<string, TValue> Normalize<TValue>(
        IReadOnlyDictionary<string, TValue>? source)
        where TValue : class
    {
        var result = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var (key, value) in source)
        {
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
