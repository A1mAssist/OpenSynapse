using System.Collections.Frozen;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    internal static IReadOnlySet<RazerHidLedZone> GetSupportedLedZones(RazerHidDeviceDefinition device)
    {
        return GetLightingZoneCapabilities(device).Keys.ToFrozenSet();
    }

    internal static IReadOnlyDictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities> GetLightingZoneCapabilities(
        RazerHidDeviceDefinition device)
    {
        var zones = new HashSet<RazerHidLedZone> { device.DefaultLedZone };
        foreach (var (builder, prefixes) in device.BuilderArgumentPrefixes)
        {
            if (!builder.Contains("matrix_effect", StringComparison.Ordinal) &&
                !builder.Contains("brightness", StringComparison.Ordinal) &&
                !builder.Contains("_led_", StringComparison.Ordinal))
            {
                continue;
            }
            foreach (var prefix in prefixes)
            {
                if (prefix.Length >= 2 && Enum.IsDefined((RazerHidLedZone)prefix.Span[1]))
                {
                    zones.Add((RazerHidLedZone)prefix.Span[1]);
                }
            }
        }

        foreach (var capability in device.SourceCapabilities)
        {
            if (TryGetSourceZone(capability, out var zone))
            {
                zones.Add(zone);
            }
        }

        var output = new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>();
        var effectsByZone = GetLightingEffectsByZone(device);
        foreach (var zone in zones.Order())
        {
            var effects = effectsByZone.GetValueOrDefault(zone) ?? FrozenSet<RazerHidLightingEffect>.Empty;
            var canReadBrightness = HasSourceBrightness(device, zone, write: false) &&
                CanBuild(() => RazerHidLightingProtocol.GetBrightness(
                device, device.DefaultStorage, zone));
            var canWriteBrightness = (HasSourceBrightness(device, zone, write: true) ||
                    zone == RazerHidLedZone.All &&
                    effects.Any(effect => effect != RazerHidLightingEffect.Custom) &&
                    HasAnyBrightnessWriter(device)) &&
                CanBuild(() => RazerHidLightingProtocol.SetBrightness(
                device, device.DefaultStorage, zone, 0));
            var canReadState = device.HasSourceCapability("get_logo_active") && zone == RazerHidLedZone.Logo &&
                CanUseClassicBuilder(device, "razer_chroma_standard_get_led_state", zone);
            var canWriteState = device.Transactions.ContainsKey("razer_chroma_standard_set_led_state") &&
                HasSourceStateWrite(device, zone) &&
                SupportsStorageAndZone(device, "razer_chroma_standard_set_led_state",
                    device.DefaultStorage, zone);
            var canReadEffect = zone == RazerHidLedZone.Macro &&
                device.HasSourceCapability("get_macro_effect") &&
                CanUseClassicBuilder(device, "razer_chroma_standard_get_led_effect", zone);
            var canWriteEffect = effects.Count > 0 &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_effect", zone);
            var canReadColor = false;
            var canWriteColor = effects.Any(effect => effect is RazerHidLightingEffect.Static or
                RazerHidLightingEffect.BreathingSingle or RazerHidLightingEffect.Blinking) &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_rgb", zone);
            var canWriteBlinking = effects.Contains(RazerHidLightingEffect.Blinking) &&
                CanUseClassicBuilder(device, "razer_chroma_standard_set_led_blinking", zone);
            if (canReadBrightness || canWriteBrightness || canReadState || canWriteState ||
                canReadEffect || canWriteEffect || canReadColor || canWriteColor || canWriteBlinking ||
                effects.Count > 0)
            {
                output.Add(zone, new RazerHidLightingZoneCapabilities(
                    zone, canReadBrightness, canWriteBrightness, canReadState, canWriteState,
                    canReadEffect, canWriteEffect, canReadColor, canWriteColor, canWriteBlinking,
                    effects));
            }
        }
        return output.ToFrozenDictionary();
    }

    private static IReadOnlyDictionary<RazerHidLedZone, IReadOnlySet<RazerHidLightingEffect>> GetLightingEffectsByZone(
        RazerHidDeviceDefinition device)
    {
        var effects = new Dictionary<RazerHidLedZone, HashSet<RazerHidLightingEffect>>();
        foreach (var effect in Enum.GetValues<RazerHidLightingEffect>())
        {
            if (RazerHidLightingEffects.IsSoftware(effect))
            {
                continue;
            }
            try
            {
                var zone = RazerHidLightingProtocol.ResolveEffectZone(device, effect);
                Add(zone, effect);
            }
            catch (NotSupportedException)
            {
            }
        }

        foreach (var capability in device.SourceCapabilities)
        {
            if (!TryGetSourceZone(capability, out var zone) ||
                !TryGetSourceEffect(capability, out var effect) ||
                !CanBuild(() => RazerHidLightingProtocol.CreateEffectRequests(device,
                    new RazerHidLightingSettings(effect, Zone: zone))))
            {
                continue;
            }
            Add(zone, effect);
        }

        return effects.ToDictionary(
            item => item.Key,
            item => (IReadOnlySet<RazerHidLightingEffect>)item.Value.ToFrozenSet())
            .ToFrozenDictionary();

        void Add(RazerHidLedZone zone, RazerHidLightingEffect effect)
        {
            if (!effects.TryGetValue(zone, out var zoneEffects))
            {
                zoneEffects = [];
                effects.Add(zone, zoneEffects);
            }
            zoneEffects.Add(effect);
        }
    }

    private static bool TryGetSourceEffect(string capability, out RazerHidLightingEffect effect)
    {
        foreach (var sourceZone in SourceZones)
        {
            var prefix = $"set_{sourceZone.Name}_";
            if (!capability.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var suffix = capability[prefix.Length..];
            var parsed = suffix switch
            {
                "none" => RazerHidLightingEffect.Off,
                "static" => RazerHidLightingEffect.Static,
                "spectrum" => RazerHidLightingEffect.Spectrum,
                "wave" => RazerHidLightingEffect.Wave,
                "reactive" => RazerHidLightingEffect.Reactive,
                "breath_random" => RazerHidLightingEffect.BreathingRandom,
                "breath_mono" or "breath_single" => RazerHidLightingEffect.BreathingSingle,
                "breath_dual" => RazerHidLightingEffect.BreathingDual,
                _ => (RazerHidLightingEffect?)null,
            };
            if (parsed is { } value)
            {
                effect = value;
                return true;
            }
        }
        effect = default;
        return false;
    }

    private static bool TryGetSourceZone(string capability, out RazerHidLedZone zone)
    {
        foreach (var candidate in SourceZones)
        {
            if (capability.StartsWith($"get_{candidate.Name}_", StringComparison.Ordinal) ||
                capability.StartsWith($"set_{candidate.Name}_", StringComparison.Ordinal))
            {
                zone = candidate.Zone;
                return true;
            }
        }
        zone = default;
        return false;
    }

    private static bool HasSourceStateWrite(RazerHidDeviceDefinition device, RazerHidLedZone zone)
    {
        var name = SourceZones.SingleOrDefault(candidate => candidate.Zone == zone).Name;
        if (name is null)
        {
            return false;
        }
        return device.HasSourceCapability($"set_{name}_active") ||
            device.HasSourceCapability($"set_{name}_on");
    }

    private static bool HasSourceBrightness(RazerHidDeviceDefinition device, RazerHidLedZone zone, bool write)
    {
        var operation = write ? "set" : "get";
        // RazerHid adds get/set_brightness at runtime and routes them to the backlight zone.
        if (zone == device.DefaultLedZone &&
            (device.HasSourceCapability($"{operation}_brightness") ||
             device.Category == DeviceCategory.Keyboard && zone == RazerHidLedZone.Backlight))
        {
            // Keyboards get get/set_brightness injected at runtime; mice must not
            // inherit a backlight zone they never declared.
            return true;
        }
        var name = SourceZones.SingleOrDefault(candidate => candidate.Zone == zone).Name;
        return name is not null && device.HasSourceCapability($"{operation}_{name}_brightness");
    }

    private static bool HasAnyBrightnessWriter(RazerHidDeviceDefinition device) =>
        device.Transactions.ContainsKey("razer_chroma_extended_matrix_brightness") ||
        device.Transactions.ContainsKey("razer_chroma_standard_set_led_brightness") ||
        device.Transactions.ContainsKey("razer_chroma_misc_set_blade_brightness") ||
        device.Transactions.ContainsKey("razer_chroma_misc_set_dock_brightness");

    private static bool SupportsStorageAndZone(
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidStorage storage,
        RazerHidLedZone zone)
    {
        return device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes) &&
            prefixes.Any(prefix =>
                prefix.Length >= 2 && prefix.Span[0] == (byte)storage && prefix.Span[1] == (byte)zone ||
                prefix.Length == 1 && prefix.Span[0] == (byte)storage);
    }

    private static bool CanUseClassicBuilder(
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidLedZone zone) =>
        device.Transactions.ContainsKey(builder) &&
        device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes) &&
        prefixes.Any(prefix => prefix.Length == 1 || prefix.Length >= 2 && prefix.Span[1] == (byte)zone);

    private static bool CanBuild(Func<object> create)
    {
        try
        {
            _ = create();
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static readonly (string Name, RazerHidLedZone Zone)[] SourceZones =
    [
        ("logo", RazerHidLedZone.Logo),
        ("scroll", RazerHidLedZone.ScrollWheel),
        ("backlight", RazerHidLedZone.Backlight),
        ("left", RazerHidLedZone.LeftSide),
        ("right", RazerHidLedZone.RightSide),
        ("charging", RazerHidLedZone.Charging),
        ("fast_charging", RazerHidLedZone.FastCharging),
        ("fully_charged", RazerHidLedZone.FullyCharged),
    ];
}
