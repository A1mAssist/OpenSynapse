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
    public ProfileShortcutSettings Shortcuts { get; set; } = new();

    internal void ApplySafeDefaults()
    {
        Global ??= new ProfileSettings();
        Devices = ProfileDictionary.Normalize(Devices);
        PluggedIn ??= new PowerProfileOverrides();
        OnBattery ??= new PowerProfileOverrides();
        Shortcuts ??= new ProfileShortcutSettings();
        Global.ApplySafeDefaults();
        foreach (var settings in Devices.Values)
        {
            settings?.ApplySafeDefaults();
        }

        PluggedIn.ApplySafeDefaults();
        OnBattery.ApplySafeDefaults();
        Shortcuts.ApplySafeDefaults();
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
            Shortcuts = Shortcuts.Clone(),
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
        SnapTapEnabled = source.SnapTapEnabled,
        MappingPreset = source.MappingPreset,
    };

    private static ViperProfileSettings CloneViper(ViperProfileSettings source) => new()
    {
        DpiX = source.DpiX,
        DpiY = source.DpiY,
        PollingRateHertz = source.PollingRateHertz,
        IdleSeconds = source.IdleSeconds,
        BatteryChemistry = source.BatteryChemistry,
        DpiStages = source.DpiStages?.Clone(),
        ButtonAssignments = source.ButtonAssignments?.Select(assignment => assignment.Clone()).ToList(),
    };

    private static LightingProfile CloneLighting(LightingProfile source) => new()
    {
        Effect = source.Effect,
        Parameters = new Dictionary<string, string>(source.Parameters, StringComparer.OrdinalIgnoreCase),
    };
}

public sealed class ProfileShortcutSettings
{
    public List<BladePerformanceMode>? PerformanceCycleModes { get; set; }
    public List<int>? RefreshRateCycleHertz { get; set; }

    internal void ApplySafeDefaults()
    {
        ValidateNonEmpty(PerformanceCycleModes, nameof(PerformanceCycleModes));
        ValidateNonEmpty(RefreshRateCycleHertz, nameof(RefreshRateCycleHertz));
        if (PerformanceCycleModes?.Any(mode => mode is not (
                BladePerformanceMode.Balanced or
                BladePerformanceMode.Performance or
                BladePerformanceMode.Custom or
                BladePerformanceMode.Silent or
                BladePerformanceMode.Hyperboost)) == true)
        {
            throw new InvalidDataException("Performance shortcut cycle contains an invalid mode.");
        }
        if (RefreshRateCycleHertz?.Any(hertz => hertz <= 0) == true)
        {
            throw new InvalidDataException("Refresh-rate shortcut cycle contains an invalid value.");
        }
        if (PerformanceCycleModes?.Distinct().Count() != PerformanceCycleModes?.Count ||
            RefreshRateCycleHertz?.Distinct().Count() != RefreshRateCycleHertz?.Count)
        {
            throw new InvalidDataException("Shortcut cycles cannot contain duplicate values.");
        }
    }

    internal ProfileShortcutSettings Clone()
    {
        ApplySafeDefaults();
        return new()
        {
            PerformanceCycleModes = PerformanceCycleModes?.ToList(),
            RefreshRateCycleHertz = RefreshRateCycleHertz?.ToList(),
        };
    }

    private static void ValidateNonEmpty<T>(IReadOnlyCollection<T>? values, string name)
    {
        if (values is { Count: 0 })
        {
            throw new InvalidDataException($"{name} must contain at least one value when configured.");
        }
    }
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
    public const string Product710DefaultMappingPreset = "product710-default";

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
    public bool? SnapTapEnabled { get; set; }
    public string? MappingPreset { get; set; }

    internal void ApplySafeDefaults()
    {
        FanCurve?.ApplySafeDefaults();
        if (MappingPreset is not null &&
            !StringComparer.Ordinal.Equals(MappingPreset, Product710DefaultMappingPreset))
        {
            throw new InvalidDataException($"Unsupported Blade mapping preset: {MappingPreset}.");
        }
    }
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
    private static readonly byte[] Product184ButtonIds = [1, 2, 3, 4, 5, 9, 10, 96];

    public int? DpiX { get; set; }
    public int? DpiY { get; set; }
    public int? PollingRateHertz { get; set; }
    public int? IdleSeconds { get; set; }
    public byte? BatteryChemistry { get; set; }
    public ViperDpiStagesProfile? DpiStages { get; set; }
    public List<ViperButtonAssignmentProfile>? ButtonAssignments { get; set; }

    internal void ApplySafeDefaults()
    {
        if (BatteryChemistry is byte chemistry && chemistry > 2)
        {
            throw new InvalidDataException("Viper battery chemistry must be 0, 1, or 2.");
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
}

public sealed class ViperButtonAssignmentProfile
{
    public byte ProfileId { get; set; }
    public byte ButtonId { get; set; }
    public ViperButtonMappingLayer Layer { get; set; }
    public ViperButtonMappingFunction Function { get; set; }
    public List<byte> FunctionData { get; set; } = [];

    internal void ApplySafeDefaults()
    {
        FunctionData ??= [];
        if (!Enum.IsDefined(Layer) || !Enum.IsDefined(Function))
        {
            throw new InvalidDataException("Viper button assignment contains an invalid layer or function.");
        }
    }

    internal ViperButtonAssignmentProfile Clone()
    {
        ApplySafeDefaults();
        return new()
        {
            ProfileId = ProfileId,
            ButtonId = ButtonId,
            Layer = Layer,
            Function = Function,
            FunctionData = FunctionData.ToList(),
        };
    }
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
