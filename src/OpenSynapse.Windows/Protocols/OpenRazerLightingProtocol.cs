using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

internal static class OpenRazerLightingProtocol
{
    private const string StandardPrefix = "razer_chroma_standard_matrix_effect_";
    private const string ExtendedPrefix = "razer_chroma_extended_matrix_effect_";
    private const string MousePrefix = "razer_chroma_mouse_extended_matrix_effect_";

    internal static OpenRazerRequest CreateEffect(
        OpenRazerDeviceDefinition device,
        OpenRazerLightingSettings settings)
    {
        var suffix = GetEffectSuffix(settings.Effect);
        var storage = settings.Storage ?? device.DefaultStorage;
        var candidates = GetEffectBuilders(suffix);
        var selection = settings.Zone is { } requestedZone
            ? (Builder: SelectBuilder(device, candidates, storage, requestedZone), Zone: requestedZone)
            : SelectDefaultEffect(device, settings.Effect, candidates, storage);
        var (builder, zone) = selection;

        if (settings.Effect == OpenRazerLightingEffect.Custom)
        {
            var fixedStorage = builder.StartsWith(StandardPrefix, StringComparison.Ordinal)
                ? (OpenRazerStorage)device.GetRequiredFixedArgument(builder, 0)
                : OpenRazerStorage.Temporary;
            if (settings.Storage is { } requestedStorage && requestedStorage != fixedStorage)
            {
                throw new NotSupportedException(
                    $"Device 1532:{device.ProductId:X4} requires {fixedStorage} storage for the custom effect selector.");
            }
            storage = fixedStorage;
        }

        return builder.StartsWith(StandardPrefix, StringComparison.Ordinal)
            ? CreateStandard(device, builder, settings, storage)
            : builder.StartsWith(ExtendedPrefix, StringComparison.Ordinal)
                ? CreateExtended(device, builder, settings, storage, zone)
                : CreateMouse(device, builder, settings, storage, zone);
    }

    internal static IReadOnlyList<OpenRazerRequest> CreateEffectRequests(
        OpenRazerDeviceDefinition device,
        OpenRazerLightingSettings settings)
    {
        try
        {
            return [CreateEffect(device, settings)];
        }
        catch (NotSupportedException) when (CanCreateClassicEffect(device, settings))
        {
            return CreateClassicEffect(device, settings);
        }
    }

    internal static OpenRazerRequest GetBrightness(
        OpenRazerDeviceDefinition device,
        OpenRazerStorage? storage,
        OpenRazerLedZone? ledId)
    {
        var actualStorage = storage ?? device.DefaultStorage;
        string[] candidates =
        [
            "razer_chroma_extended_matrix_get_brightness",
            "razer_chroma_standard_get_led_brightness",
            "razer_chroma_misc_get_blade_brightness",
            "razer_chroma_misc_get_dock_brightness",
        ];
        var actualZone = ResolveZone(device, candidates, actualStorage, ledId);
        var builder = SelectBuilder(device, candidates, actualStorage, actualZone);
        if (builder == "razer_chroma_extended_matrix_get_brightness")
        {
            return OpenRazerStandardProtocol.Create(device,
                builder, 0x03, 0x0F, 0x84, [(byte)actualStorage, (byte)actualZone]);
        }
        if (builder == "razer_chroma_misc_get_blade_brightness")
        {
            return OpenRazerStandardProtocol.Create(device,
                builder, 0x02, 0x0E, 0x84, [0x01]);
        }
        if (builder == "razer_chroma_misc_get_dock_brightness")
        {
            return OpenRazerStandardProtocol.Create(device,
                builder, 0x01, 0x07, 0x82, []);
        }
        return OpenRazerStandardProtocol.GetLedBrightness(device, (byte)actualStorage, (byte)actualZone);
    }

    internal static OpenRazerRequest SetBrightness(
        OpenRazerDeviceDefinition device,
        OpenRazerStorage? storage,
        OpenRazerLedZone? ledId,
        byte brightness)
    {
        var actualStorage = storage ?? device.DefaultStorage;
        string[] candidates =
        [
            "razer_chroma_extended_matrix_brightness",
            "razer_chroma_standard_set_led_brightness",
            "razer_chroma_misc_set_blade_brightness",
            "razer_chroma_misc_set_dock_brightness",
        ];
        var actualZone = ResolveZone(device, candidates, actualStorage, ledId);
        var builder = SelectBuilder(device, candidates, actualStorage, actualZone);
        if (builder == "razer_chroma_extended_matrix_brightness")
        {
            return OpenRazerStandardProtocol.Create(device,
                builder, 0x03, 0x0F, 0x04,
                [(byte)actualStorage, (byte)actualZone, brightness]);
        }
        if (builder == "razer_chroma_misc_set_blade_brightness")
        {
            return OpenRazerStandardProtocol.Create(device,
                builder, 0x02, 0x0E, 0x04, [0x01, brightness]);
        }
        if (builder == "razer_chroma_misc_set_dock_brightness")
        {
            return OpenRazerStandardProtocol.Create(device,
                builder, 0x01, 0x07, 0x02, [brightness]);
        }
        return OpenRazerStandardProtocol.SetLedBrightness(device, (byte)actualStorage, (byte)actualZone, brightness);
    }

    internal static OpenRazerRequest SetCustomRow(
        OpenRazerDeviceDefinition device,
        byte row,
        byte startColumn,
        IReadOnlyList<OpenRazerColor> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (colors.Count is < 1 or > 25 || startColumn + colors.Count - 1 > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(colors));
        }
        if (device.MatrixDimensions is { } matrix &&
            (row >= matrix.Rows || startColumn + colors.Count > matrix.Columns))
        {
            throw new ArgumentOutOfRangeException(nameof(colors), "Custom row exceeds the declared device matrix.");
        }

        var stopColumn = checked((byte)(startColumn + colors.Count - 1));
        var rgb = colors.SelectMany(color => new[] { color.Red, color.Green, color.Blue }).ToArray();
        if (device.Transactions.ContainsKey("razer_chroma_standard_matrix_set_custom_frame"))
        {
            return OpenRazerStandardProtocol.Create(device,
                "razer_chroma_standard_matrix_set_custom_frame", 0x46, 0x03, 0x0B,
                new byte[] { 0xFF, row, startColumn, stopColumn }.Concat(rgb).ToArray(),
                allowArgumentsBeyondDeclaredSize: true);
        }

        var builder = device.Transactions.ContainsKey("razer_chroma_extended_matrix_set_custom_frame2")
            ? "razer_chroma_extended_matrix_set_custom_frame2"
            : "razer_chroma_extended_matrix_set_custom_frame";
        var arguments = new byte[] { 0x00, 0x00, row, startColumn, stopColumn }.Concat(rgb).ToArray();
        var dataSize = builder.EndsWith('2') ? checked((byte)arguments.Length) : (byte)0x47;
        return OpenRazerStandardProtocol.Create(device, builder, dataSize, 0x0F, 0x03, arguments,
            allowArgumentsBeyondDeclaredSize: arguments.Length > dataSize);
    }

    private static OpenRazerRequest CreateStandard(
        OpenRazerDeviceDefinition device,
        string builder,
        OpenRazerLightingSettings value,
        OpenRazerStorage storage)
    {
        var speed = Math.Clamp(value.Speed, (byte)1, (byte)4);
        var arguments = value.Effect switch
        {
            OpenRazerLightingEffect.Off => new byte[] { 0x00 },
            OpenRazerLightingEffect.Wave => [0x01, Math.Clamp(value.Direction, (byte)1, (byte)2)],
            OpenRazerLightingEffect.Spectrum => [0x04],
            OpenRazerLightingEffect.Reactive => [0x02, speed, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            OpenRazerLightingEffect.Static => [0x06, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            OpenRazerLightingEffect.StarlightRandom => [0x19, 0x03, Math.Clamp(value.Speed, (byte)1, (byte)3)],
            OpenRazerLightingEffect.StarlightSingle =>
                [0x19, 0x01, Math.Clamp(value.Speed, (byte)1, (byte)3), value.Primary.Red, value.Primary.Green, value.Primary.Blue, 0, 0, 0],
            OpenRazerLightingEffect.StarlightDual =>
                [0x19, 0x02, Math.Clamp(value.Speed, (byte)1, (byte)3), value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            OpenRazerLightingEffect.BreathingRandom => [0x03, 0x03, 0, 0, 0, 0, 0, 0],
            OpenRazerLightingEffect.BreathingSingle =>
                [0x03, 0x01, value.Primary.Red, value.Primary.Green, value.Primary.Blue, 0, 0, 0],
            OpenRazerLightingEffect.BreathingDual =>
                [0x03, 0x02, value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            OpenRazerLightingEffect.Custom => [0x05, (byte)storage],
            _ => throw new NotSupportedException($"Standard lighting does not support {value.Effect}."),
        };
        var declaredSize = value.Effect is OpenRazerLightingEffect.StarlightRandom or
            OpenRazerLightingEffect.StarlightSingle or OpenRazerLightingEffect.StarlightDual
            ? (byte)1
            : checked((byte)arguments.Length);
        return OpenRazerStandardProtocol.Create(device, builder, declaredSize, 0x03, 0x0A, arguments,
            allowArgumentsBeyondDeclaredSize: arguments.Length > declaredSize);
    }

    private static OpenRazerRequest CreateExtended(
        OpenRazerDeviceDefinition device,
        string builder,
        OpenRazerLightingSettings value,
        OpenRazerStorage storage,
        OpenRazerLedZone zone)
    {
        var prefix = new byte[] { (byte)storage, (byte)zone };
        byte[] tail = value.Effect switch
        {
            OpenRazerLightingEffect.Off => [0x00, 0, 0, 0],
            OpenRazerLightingEffect.Static => [0x01, 0, 0, 1, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            OpenRazerLightingEffect.Wave => [0x04, Math.Clamp(value.Direction, (byte)0, (byte)2), 0x28, 0],
            OpenRazerLightingEffect.Spectrum => [0x03, 0, 0, 0],
            OpenRazerLightingEffect.Wheel => [0x0A, Math.Clamp(value.Direction, (byte)1, (byte)2), 0x28, 0],
            OpenRazerLightingEffect.Reactive =>
                [0x05, 0, Math.Clamp(value.Speed, (byte)1, (byte)4), 1, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            OpenRazerLightingEffect.BreathingRandom => [0x02, 0, 0, 0],
            OpenRazerLightingEffect.BreathingSingle =>
                [0x02, 1, 0, 1, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            OpenRazerLightingEffect.BreathingDual =>
                [0x02, 2, 0, 2, value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            OpenRazerLightingEffect.StarlightRandom =>
                [0x07, 0, Math.Clamp(value.Speed, (byte)1, (byte)3), 0],
            OpenRazerLightingEffect.StarlightSingle =>
                [0x07, 0, Math.Clamp(value.Speed, (byte)1, (byte)3), 1, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            OpenRazerLightingEffect.StarlightDual =>
                [0x07, 0, Math.Clamp(value.Speed, (byte)1, (byte)3), 2, value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            OpenRazerLightingEffect.Custom => [0x08, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            _ => throw new NotSupportedException($"Extended lighting does not support {value.Effect}."),
        };
        var arguments = value.Effect == OpenRazerLightingEffect.Custom
            ? new byte[] { 0x00, 0x00 }.Concat(tail).ToArray()
            : prefix.Concat(tail).ToArray();
        return OpenRazerStandardProtocol.Create(device, builder, checked((byte)arguments.Length), 0x0F, 0x02, arguments);
    }

    private static OpenRazerRequest CreateMouse(
        OpenRazerDeviceDefinition device,
        string builder,
        OpenRazerLightingSettings value,
        OpenRazerStorage storage,
        OpenRazerLedZone zone)
    {
        byte[] tail = value.Effect switch
        {
            OpenRazerLightingEffect.Off => [0x00],
            OpenRazerLightingEffect.Static => [0x06, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            OpenRazerLightingEffect.Spectrum => [0x04],
            OpenRazerLightingEffect.Reactive =>
                [0x02, Math.Clamp(value.Speed, (byte)1, (byte)4), value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            OpenRazerLightingEffect.BreathingRandom => [0x03, 0x03, 0, 0, 0, 0, 0, 0],
            OpenRazerLightingEffect.BreathingSingle =>
                [0x03, 0x01, value.Primary.Red, value.Primary.Green, value.Primary.Blue, 0, 0, 0],
            OpenRazerLightingEffect.BreathingDual =>
                [0x03, 0x02, value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            _ => throw new NotSupportedException($"Mouse lighting does not support {value.Effect}."),
        };
        var arguments = new byte[] { (byte)storage, (byte)zone }.Concat(tail).ToArray();
        return OpenRazerStandardProtocol.Create(device, builder, checked((byte)arguments.Length), 0x03, 0x0D, arguments);
    }

    private static string SelectBuilder(
        OpenRazerDeviceDefinition device,
        IReadOnlyList<string> candidates,
        OpenRazerStorage storage,
        OpenRazerLedZone zone)
    {
        var scored = candidates
            .Where(device.Transactions.ContainsKey)
            .Select(builder => (Builder: builder, Score: ScoreBuilder(device, builder, storage, zone)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ToArray();
        if (scored.Length == 0 || (scored.Length > 1 && scored[0].Score == scored[1].Score))
        {
            throw new NotSupportedException(
                $"Device 1532:{device.ProductId:X4} does not resolve this lighting operation to one protocol family.");
        }
        return scored[0].Builder;
    }

    private static int ScoreBuilder(
        OpenRazerDeviceDefinition device,
        string builder,
        OpenRazerStorage storage,
        OpenRazerLedZone zone)
    {
        if (device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes))
        {
            if (prefixes.Any(prefix => prefix.Length >= 2 &&
                prefix.Span[0] == (byte)storage && prefix.Span[1] == (byte)zone))
            {
                return 4;
            }
            if (prefixes.Any(prefix => prefix.Length == 1 && prefix.Span[0] == (byte)storage))
            {
                return 3;
            }
            return 0;
        }
        if (builder.StartsWith(StandardPrefix, StringComparison.Ordinal))
        {
            return zone == device.DefaultLedZone ? 2 : 0;
        }
        var effectFamily = builder.StartsWith(ExtendedPrefix, StringComparison.Ordinal)
            ? ExtendedPrefix
            : builder.StartsWith(MousePrefix, StringComparison.Ordinal)
                ? MousePrefix
                : null;
        if (effectFamily is not null)
        {
            if (builder.EndsWith("custom_frame", StringComparison.Ordinal) &&
                zone == device.DefaultLedZone)
            {
                return 2;
            }
            var familyPrefixes = device.BuilderArgumentPrefixes
                .Where(item => item.Key.StartsWith(effectFamily, StringComparison.Ordinal))
                .SelectMany(item => item.Value)
                .ToArray();
            var familyZones = familyPrefixes
                .Where(prefix => prefix.Length >= 2 && prefix.Span[0] == (byte)storage &&
                    Enum.IsDefined((OpenRazerLedZone)prefix.Span[1]))
                .Select(prefix => (OpenRazerLedZone)prefix.Span[1])
                .Concat(familyPrefixes.Any(prefix => prefix.Length == 1 && prefix.Span[0] == (byte)storage)
                    ? [device.DefaultLedZone]
                    : [])
                .Distinct()
                .ToArray();
            return familyZones.Contains(zone) ? 2 : 0;
        }
        if (builder.Contains("_blade_brightness", StringComparison.Ordinal) &&
            device.Category == OpenSynapse.Core.Devices.DeviceCategory.Laptop)
        {
            return zone == device.DefaultLedZone ? 2 : 0;
        }
        if (builder.Contains("_dock_brightness", StringComparison.Ordinal))
        {
            return zone == device.DefaultLedZone ? 2 : 0;
        }
        return 1;
    }

    internal static OpenRazerLedZone ResolveEffectZone(
        OpenRazerDeviceDefinition device,
        OpenRazerLightingEffect effect,
        OpenRazerStorage? storage = null,
        OpenRazerLedZone? zone = null)
    {
        try
        {
            return zone is { } requestedZone
                ? ResolveZone(device, GetEffectBuilders(GetEffectSuffix(effect)),
                    storage ?? device.DefaultStorage, requestedZone)
                : SelectDefaultEffect(device, effect, GetEffectBuilders(GetEffectSuffix(effect)),
                    storage ?? device.DefaultStorage).Zone;
        }
        catch (NotSupportedException)
        {
            var settings = new OpenRazerLightingSettings(effect, Storage: storage, Zone: zone);
            if (!CanCreateClassicEffect(device, settings))
            {
                throw;
            }
            return zone ?? device.DefaultLedZone;
        }
    }

    internal static OpenRazerLedZone ResolveBrightnessZone(
        OpenRazerDeviceDefinition device,
        bool write,
        OpenRazerStorage? storage = null,
        OpenRazerLedZone? zone = null) =>
        ResolveZone(device,
            write
                ? ["razer_chroma_extended_matrix_brightness", "razer_chroma_standard_set_led_brightness",
                    "razer_chroma_misc_set_blade_brightness", "razer_chroma_misc_set_dock_brightness"]
                : ["razer_chroma_extended_matrix_get_brightness", "razer_chroma_standard_get_led_brightness",
                    "razer_chroma_misc_get_blade_brightness", "razer_chroma_misc_get_dock_brightness"],
            storage ?? device.DefaultStorage, zone);

    private static OpenRazerLedZone ResolveZone(
        OpenRazerDeviceDefinition device,
        IReadOnlyList<string> candidates,
        OpenRazerStorage storage,
        OpenRazerLedZone? requested)
    {
        if (requested is { } explicitZone)
        {
            _ = SelectBuilder(device, candidates, storage, explicitZone);
            return explicitZone;
        }

        var zones = new List<OpenRazerLedZone> { device.DefaultLedZone };
        foreach (var builder in candidates)
        {
            if (!device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes))
            {
                continue;
            }
            foreach (var prefix in prefixes)
            {
                if (prefix.Length >= 2 && prefix.Span[0] == (byte)storage &&
                    Enum.IsDefined((OpenRazerLedZone)prefix.Span[1]))
                {
                    var zone = (OpenRazerLedZone)prefix.Span[1];
                    if (!zones.Contains(zone))
                    {
                        zones.Add(zone);
                    }
                }
            }
        }

        foreach (var zone in zones)
        {
            try
            {
                _ = SelectBuilder(device, candidates, storage, zone);
                return zone;
            }
            catch (NotSupportedException)
            {
            }
        }
        throw new NotSupportedException(
            $"Device 1532:{device.ProductId:X4} has no supported zone for this lighting operation.");
    }

    private static string GetEffectSuffix(OpenRazerLightingEffect effect) => effect switch
    {
        OpenRazerLightingEffect.Off => "none",
        OpenRazerLightingEffect.Static => "static",
        OpenRazerLightingEffect.Spectrum => "spectrum",
        OpenRazerLightingEffect.Wave => "wave",
        OpenRazerLightingEffect.Reactive => "reactive",
        OpenRazerLightingEffect.Blinking => "blinking",
        OpenRazerLightingEffect.BreathingRandom => "breathing_random",
        OpenRazerLightingEffect.BreathingSingle => "breathing_single",
        OpenRazerLightingEffect.BreathingDual => "breathing_dual",
        OpenRazerLightingEffect.StarlightRandom => "starlight_random",
        OpenRazerLightingEffect.StarlightSingle => "starlight_single",
        OpenRazerLightingEffect.StarlightDual => "starlight_dual",
        OpenRazerLightingEffect.Wheel => "wheel",
        OpenRazerLightingEffect.Custom => "custom_frame",
        _ => throw new NotSupportedException($"{effect} is not supported by the standard OpenRazer lighting protocol."),
    };

    private static string[] GetEffectBuilders(string suffix) =>
        [StandardPrefix + suffix, ExtendedPrefix + suffix, MousePrefix + suffix];

    private static (string Builder, OpenRazerLedZone Zone) SelectDefaultEffect(
        OpenRazerDeviceDefinition device,
        OpenRazerLightingEffect effect,
        IReadOnlyList<string> candidates,
        OpenRazerStorage storage)
    {
        if (!device.HasSourceCapability(GetGenericEffectCapability(effect)))
        {
            throw new NotSupportedException(
                $"Device 1532:{device.ProductId:X4} does not expose {effect} as a device-wide effect.");
        }

        var priority = new[] { candidates[0], candidates[2], candidates[1] };
        foreach (var builder in priority.Where(device.Transactions.ContainsKey))
        {
            foreach (var zone in GetBuilderZones(device, builder, storage))
            {
                return (builder, zone);
            }
        }
        throw new NotSupportedException(
            $"Device 1532:{device.ProductId:X4} has no unambiguous default builder for {effect}.");
    }

    private static IReadOnlyList<OpenRazerLedZone> GetBuilderZones(
        OpenRazerDeviceDefinition device,
        string builder,
        OpenRazerStorage storage)
    {
        if (builder.StartsWith(StandardPrefix, StringComparison.Ordinal))
        {
            return [device.DefaultLedZone];
        }
        if (device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes))
        {
            var exact = prefixes
                .Where(prefix => prefix.Length >= 2 && prefix.Span[0] == (byte)storage &&
                    Enum.IsDefined((OpenRazerLedZone)prefix.Span[1]))
                .Select(prefix => (OpenRazerLedZone)prefix.Span[1])
                .Distinct()
                .ToArray();
            if (exact.Length > 0)
            {
                return exact;
            }
            if (prefixes.Any(prefix => prefix.Length == 1 && prefix.Span[0] == (byte)storage))
            {
                return [device.DefaultLedZone];
            }
        }

        var family = builder.StartsWith(MousePrefix, StringComparison.Ordinal)
            ? MousePrefix
            : builder.StartsWith(ExtendedPrefix, StringComparison.Ordinal)
                ? ExtendedPrefix
                : null;
        if (family is null)
        {
            return [];
        }
        if (builder.EndsWith("custom_frame", StringComparison.Ordinal))
        {
            // The extended custom-frame selector has fixed wire arguments 00 00;
            // the physical target remains the device's declared matrix zone.
            return [device.DefaultLedZone];
        }
        var siblingPrefixes = device.BuilderArgumentPrefixes
            .Where(item => item.Key.StartsWith(family, StringComparison.Ordinal))
            .SelectMany(item => item.Value)
            .ToArray();
        var siblingZones = siblingPrefixes
            .Where(prefix => prefix.Length >= 2 && prefix.Span[0] == (byte)storage &&
                Enum.IsDefined((OpenRazerLedZone)prefix.Span[1]))
            .Select(prefix => (OpenRazerLedZone)prefix.Span[1])
            .Distinct()
            .ToList();
        if (siblingPrefixes.Any(prefix => prefix.Length == 1 && prefix.Span[0] == (byte)storage) &&
            !siblingZones.Contains(device.DefaultLedZone))
        {
            siblingZones.Add(device.DefaultLedZone);
        }
        return siblingZones;
    }

    private static string GetGenericEffectCapability(OpenRazerLightingEffect effect) => effect switch
    {
        OpenRazerLightingEffect.Off => "set_none_effect",
        OpenRazerLightingEffect.Static => "set_static_effect",
        OpenRazerLightingEffect.Spectrum => "set_spectrum_effect",
        OpenRazerLightingEffect.Wave => "set_wave_effect",
        OpenRazerLightingEffect.Reactive => "set_reactive_effect",
        OpenRazerLightingEffect.Blinking => "set_blinking_effect",
        OpenRazerLightingEffect.BreathingRandom => "set_breath_random_effect",
        OpenRazerLightingEffect.BreathingSingle => "set_breath_single_effect",
        OpenRazerLightingEffect.BreathingDual => "set_breath_dual_effect",
        OpenRazerLightingEffect.StarlightRandom => "set_starlight_random_effect",
        OpenRazerLightingEffect.StarlightSingle => "set_starlight_single_effect",
        OpenRazerLightingEffect.StarlightDual => "set_starlight_dual_effect",
        OpenRazerLightingEffect.Wheel => "set_wheel_effect",
        OpenRazerLightingEffect.Custom => "set_custom_effect",
        _ => throw new ArgumentOutOfRangeException(nameof(effect)),
    };

    private static bool CanCreateClassicEffect(
        OpenRazerDeviceDefinition device,
        OpenRazerLightingSettings settings)
    {
        if (settings.Effect == OpenRazerLightingEffect.BreathingTriple)
        {
            return false;
        }
        if (!device.HasSourceCapability(GetGenericEffectCapability(settings.Effect)))
        {
            return false;
        }
        var storage = settings.Storage ?? device.DefaultStorage;
        var zone = settings.Zone ?? device.DefaultLedZone;
        var builders = settings.Effect switch
        {
            OpenRazerLightingEffect.Off => new[] { "razer_chroma_standard_set_led_state" },
            OpenRazerLightingEffect.Spectrum =>
                ["razer_chroma_standard_set_led_state", "razer_chroma_standard_set_led_effect"],
            OpenRazerLightingEffect.Static or OpenRazerLightingEffect.BreathingSingle or OpenRazerLightingEffect.Blinking =>
                ["razer_chroma_standard_set_led_state", "razer_chroma_standard_set_led_rgb",
                    "razer_chroma_standard_set_led_effect"],
            _ => [],
        };
        return builders.Length > 0 && builders.All(builder =>
            device.Transactions.ContainsKey(builder) &&
            device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes) &&
            prefixes.Any(prefix => prefix.Length >= 1 && prefix.Span[0] == (byte)storage &&
                (prefix.Length == 1 || prefix.Span[1] == (byte)zone)));
    }

    private static IReadOnlyList<OpenRazerRequest> CreateClassicEffect(
        OpenRazerDeviceDefinition device,
        OpenRazerLightingSettings settings)
    {
        var storage = (byte)(settings.Storage ?? device.DefaultStorage);
        var zone = (byte)(settings.Zone ?? device.DefaultLedZone);
        if (settings.Effect == OpenRazerLightingEffect.Off)
        {
            return [OpenRazerStandardProtocol.SetLedState(device, storage, zone, false)];
        }

        var requests = new List<OpenRazerRequest>
        {
            OpenRazerStandardProtocol.SetLedState(device, storage, zone, true),
        };
        if (settings.Effect is OpenRazerLightingEffect.Static or
            OpenRazerLightingEffect.BreathingSingle or OpenRazerLightingEffect.Blinking)
        {
            requests.Add(OpenRazerStandardProtocol.SetLedColor(device, storage, zone, settings.Primary));
        }
        requests.Add(OpenRazerStandardProtocol.SetLedEffect(device, storage, zone, settings.Effect switch
        {
            OpenRazerLightingEffect.Static => OpenRazerClassicLedEffect.Static,
            OpenRazerLightingEffect.BreathingSingle => OpenRazerClassicLedEffect.Breathing,
            OpenRazerLightingEffect.Blinking => OpenRazerClassicLedEffect.Blinking,
            OpenRazerLightingEffect.Spectrum => OpenRazerClassicLedEffect.Spectrum,
            _ => throw new NotSupportedException(),
        }));
        return requests;
    }
}
