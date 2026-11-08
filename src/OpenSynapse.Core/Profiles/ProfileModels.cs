using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Displays;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Core.Profiles;

public sealed class ProfileDocument
{
    public int Version { get; set; } = ProfileStore.CurrentVersion;
    public string ActiveProfileName { get; set; } = ProfileCatalog.DefaultProfileName;
    public Dictionary<string, ProfileDefinition> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ProfileSettings Global { get; set; } = new();
    public Dictionary<string, DeviceProfileSettings> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PowerProfileOverrides PluggedIn { get; set; } = new();
    public PowerProfileOverrides LowPowerAdapter { get; set; } = new();
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
        LowPowerAdapter ??= new PowerProfileOverrides();
        OnBattery ??= new PowerProfileOverrides();
        ApplicationBindings = ProfileDictionary.Normalize(ApplicationBindings);
        Global.ApplySafeDefaults();
        foreach (var settings in Devices.Values)
        {
            settings?.ApplySafeDefaults();
        }

        PluggedIn.ApplySafeDefaults();
        LowPowerAdapter.ApplySafeDefaults();
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
                LowPowerAdapter = LowPowerAdapter,
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
        LowPowerAdapter = active.LowPowerAdapter;
        OnBattery = active.OnBattery;
    }
}

public sealed partial class ProfileDefinition
{
    public ProfileSettings Global { get; set; } = new();
    public Dictionary<string, DeviceProfileSettings> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PowerProfileOverrides PluggedIn { get; set; } = new();
    public PowerProfileOverrides LowPowerAdapter { get; set; } = new();
    public PowerProfileOverrides OnBattery { get; set; } = new();
    public ProfileShortcutSettings Shortcuts { get; set; } = new();
    public List<LightingColorPreset> LightingColorPresets { get; set; } = [];
    public List<MacroDefinition> Macros { get; set; } = [];

    internal void ApplySafeDefaults()
    {
        Global ??= new ProfileSettings();
        Devices = ProfileDictionary.Normalize(Devices);
        PluggedIn ??= new PowerProfileOverrides();
        LowPowerAdapter ??= new PowerProfileOverrides();
        OnBattery ??= new PowerProfileOverrides();
        Shortcuts ??= new ProfileShortcutSettings();
        LightingColorPresets = LightingColorPresets?
            .Where(preset => preset is not null)
            .ToList() ?? [];
        Macros = (Macros ?? [])
            .Where(macro => macro is not null)
            .Take(64)
            .ToList();
        foreach (var macro in Macros)
        {
            macro.ApplySafeDefaults();
        }
        Global.ApplySafeDefaults();
        foreach (var settings in Devices.Values)
        {
            settings?.ApplySafeDefaults();
        }

        PluggedIn.ApplySafeDefaults();
        LowPowerAdapter.ApplySafeDefaults();
        OnBattery.ApplySafeDefaults();
        Shortcuts.ApplySafeDefaults();
    }

}

public sealed class ProfileShortcutSettings
{
    public List<BladePerformanceMode>? PerformanceCycleModes { get; set; }
    public List<int>? RefreshRateCycleHertz { get; set; }
    public MacroInput? MacroPlaybackStart { get; set; }
    public MacroInput? MacroPlaybackStop { get; set; }

    internal void ApplySafeDefaults()
    {
        ValidateNonEmpty(PerformanceCycleModes, nameof(PerformanceCycleModes));
        ValidateNonEmpty(RefreshRateCycleHertz, nameof(RefreshRateCycleHertz));
        MacroPlaybackStart = NormalizeMacroShortcut(MacroPlaybackStart);
        MacroPlaybackStop = NormalizeMacroShortcut(MacroPlaybackStop);
        if (PerformanceCycleModes?.Any(mode => mode is not (
                BladePerformanceMode.Balanced or
                BladePerformanceMode.Gaming or
                BladePerformanceMode.Performance or
                BladePerformanceMode.Custom or
                BladePerformanceMode.Silent or
                BladePerformanceMode.BatterySaver or
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
            MacroPlaybackStart = MacroPlaybackStart?.Clone(),
            MacroPlaybackStop = MacroPlaybackStop?.Clone(),
        };
    }

    private static MacroInput? NormalizeMacroShortcut(MacroInput? input) =>
        input is not null &&
        !input.IsModifierKey &&
        input.IsValid(allowPrimaryMouseButton: false, allowWheel: false)
            ? input.Clone()
            : null;

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
    [JsonPropertyName("viper")]
    public MouseProfileSettings Viper { get; set; } = new();
    public KeyboardProfileSettings Keyboard { get; set; } = new();
    public HeadsetProfileSettings Headset { get; set; } = new();
    public CoolingPadProfileSettings CoolingPad { get; set; } = new();
    public GamepadProfileSettings Gamepad { get; set; } = new();
    public ChromaProfileSettings Chroma { get; set; } = new();
    public MixerProfileSettings Mixer { get; set; } = new();
    public KiyoProfileSettings Kiyo { get; set; } = new();
    public DisplayProfileSettings Display { get; set; } = new();
    public LightingProfile Lighting { get; set; } = new();
    public Dictionary<string, LightingProfile> LightingZones { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    internal void ApplySafeDefaults()
    {
        Blade ??= new BladeProfileSettings();
        Viper ??= new MouseProfileSettings();
        Keyboard ??= new KeyboardProfileSettings();
        Headset ??= new HeadsetProfileSettings();
        CoolingPad ??= new CoolingPadProfileSettings();
        Gamepad ??= new GamepadProfileSettings();
        Chroma ??= new ChromaProfileSettings();
        Mixer ??= new MixerProfileSettings();
        Kiyo ??= new KiyoProfileSettings();
        Display ??= new DisplayProfileSettings();
        Lighting ??= new LightingProfile();
        LightingZones = ProfileDictionary.Normalize(LightingZones);
        Blade.ApplySafeDefaults();
        Viper.ApplySafeDefaults();
        Keyboard.ApplySafeDefaults();
        Headset.ApplySafeDefaults();
        CoolingPad.ApplySafeDefaults();
        Gamepad.ApplySafeDefaults();
        Chroma.ApplySafeDefaults();
        Mixer.ApplySafeDefaults();
        Kiyo.ApplySafeDefaults();
        Display.ApplySafeDefaults();
        Lighting.ApplySafeDefaults();
        foreach (var zone in LightingZones.Values)
        {
            zone.ApplySafeDefaults();
        }
    }
}

public sealed class DeviceProfileSettings
{
    public BladeProfileSettings Blade { get; set; } = new();
    [JsonPropertyName("viper")]
    public MouseProfileSettings Viper { get; set; } = new();
    public KeyboardProfileSettings Keyboard { get; set; } = new();
    public HeadsetProfileSettings Headset { get; set; } = new();
    public CoolingPadProfileSettings CoolingPad { get; set; } = new();
    public GamepadProfileSettings Gamepad { get; set; } = new();
    public ChromaProfileSettings Chroma { get; set; } = new();
    public MixerProfileSettings Mixer { get; set; } = new();
    public KiyoProfileSettings Kiyo { get; set; } = new();
    public DisplayProfileSettings Display { get; set; } = new();
    public LightingProfile Lighting { get; set; } = new();
    public Dictionary<string, LightingProfile> LightingZones { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DeviceSettingValue> Extras { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    public bool? LightingEnabled { get; set; }
    public bool? ChromaOverrideEnabled { get; set; }

    internal void ApplySafeDefaults()
    {
        Blade ??= new BladeProfileSettings();
        Viper ??= new MouseProfileSettings();
        Keyboard ??= new KeyboardProfileSettings();
        Headset ??= new HeadsetProfileSettings();
        CoolingPad ??= new CoolingPadProfileSettings();
        Gamepad ??= new GamepadProfileSettings();
        Chroma ??= new ChromaProfileSettings();
        Mixer ??= new MixerProfileSettings();
        Kiyo ??= new KiyoProfileSettings();
        Display ??= new DisplayProfileSettings();
        Lighting ??= new LightingProfile();
        LightingZones = ProfileDictionary.Normalize(LightingZones);
        Extras = ProfileDictionary.Normalize(Extras);
        Blade.ApplySafeDefaults();
        Viper.ApplySafeDefaults();
        Keyboard.ApplySafeDefaults();
        Headset.ApplySafeDefaults();
        CoolingPad.ApplySafeDefaults();
        Gamepad.ApplySafeDefaults();
        Chroma.ApplySafeDefaults();
        Mixer.ApplySafeDefaults();
        Kiyo.ApplySafeDefaults();
        Display.ApplySafeDefaults();
        Lighting.ApplySafeDefaults();
        foreach (var zone in LightingZones.Values)
        {
            zone.ApplySafeDefaults();
        }
        foreach (var value in Extras.Values)
        {
            value.ApplySafeDefaults();
        }
    }
}

public sealed class PowerProfileOverrides
{
    public BladeProfileSettings Blade { get; set; } = new();
    [JsonPropertyName("viper")]
    public MouseProfileSettings Viper { get; set; } = new();
    public KeyboardProfileSettings Keyboard { get; set; } = new();
    public HeadsetProfileSettings Headset { get; set; } = new();
    public CoolingPadProfileSettings CoolingPad { get; set; } = new();
    public GamepadProfileSettings Gamepad { get; set; } = new();
    public ChromaProfileSettings Chroma { get; set; } = new();
    public MixerProfileSettings Mixer { get; set; } = new();
    public KiyoProfileSettings Kiyo { get; set; } = new();
    public DisplayProfileSettings Display { get; set; } = new();
    public LightingProfile Lighting { get; set; } = new();
    public Dictionary<string, LightingProfile> LightingZones { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    // RazerHid devices key their power-state zones by device so two mice do not
    // share a per-zone override.
    public Dictionary<string, Dictionary<string, LightingProfile>> RazerHidLightingZones { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LightingProfile> RazerHidLighting { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    // Profiles written before the RazerHid rename used this key; merge it once and save only the new key.
    [JsonPropertyName("openRazerLighting"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, LightingProfile>? LegacyRazerHidLighting { get; set; }

    internal void ApplySafeDefaults()
    {
        Blade ??= new BladeProfileSettings();
        Viper ??= new MouseProfileSettings();
        Keyboard ??= new KeyboardProfileSettings();
        Headset ??= new HeadsetProfileSettings();
        CoolingPad ??= new CoolingPadProfileSettings();
        Gamepad ??= new GamepadProfileSettings();
        Chroma ??= new ChromaProfileSettings();
        Mixer ??= new MixerProfileSettings();
        Kiyo ??= new KiyoProfileSettings();
        Display ??= new DisplayProfileSettings();
        Lighting ??= new LightingProfile();
        LightingZones = ProfileDictionary.Normalize(LightingZones);
        RazerHidLightingZones = NormalizeRazerHidLightingZones(RazerHidLightingZones);
        RazerHidLighting = ProfileDictionary.Normalize(RazerHidLighting);
        foreach (var (key, value) in ProfileDictionary.Normalize(LegacyRazerHidLighting))
        {
            RazerHidLighting.TryAdd(key, value);
        }
        LegacyRazerHidLighting = null;
        Blade.ApplySafeDefaults();
        Viper.ApplySafeDefaults();
        Keyboard.ApplySafeDefaults();
        Headset.ApplySafeDefaults();
        CoolingPad.ApplySafeDefaults();
        Gamepad.ApplySafeDefaults();
        Chroma.ApplySafeDefaults();
        Mixer.ApplySafeDefaults();
        Kiyo.ApplySafeDefaults();
        Display.ApplySafeDefaults();
        Lighting.ApplySafeDefaults();
        foreach (var zone in LightingZones.Values)
        {
            zone.ApplySafeDefaults();
        }
        foreach (var zones in RazerHidLightingZones.Values)
        {
            foreach (var zone in zones.Values)
            {
                zone.ApplySafeDefaults();
            }
        }
    }

    private static Dictionary<string, Dictionary<string, LightingProfile>> NormalizeRazerHidLightingZones(
        IReadOnlyDictionary<string, Dictionary<string, LightingProfile>>? source)
    {
        var result = new Dictionary<string, Dictionary<string, LightingProfile>>(
            StringComparer.OrdinalIgnoreCase);
        if (source is null)
        {
            return result;
        }

        foreach (var (deviceKey, zones) in source)
        {
            if (!string.IsNullOrWhiteSpace(deviceKey))
            {
                result[deviceKey] = ProfileDictionary.Normalize(zones);
            }
        }

        return result;
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
    // Empty means inherit the global/device profile; "off" is an explicit setting.
    public string Effect { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    internal void ApplySafeDefaults()
    {
        Parameters = ProfileDictionary.Normalize(Parameters);
    }
}

public sealed class LightingColorPreset
{
    public string Name { get; set; } = string.Empty;
    public string Primary { get; set; } = "#FFFFFF";
    public string Secondary { get; set; } = "#FFFFFF";
    public string Tertiary { get; set; } = "#FFFFFF";

    internal LightingColorPreset Clone() => new()
    {
        Name = Name,
        Primary = Primary,
        Secondary = Secondary,
        Tertiary = Tertiary,
    };
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
