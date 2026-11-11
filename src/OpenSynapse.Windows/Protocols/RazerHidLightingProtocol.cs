using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

internal static class RazerHidLightingProtocol
{
    private const string StandardPrefix = "razer_chroma_standard_matrix_effect_";
    private const string ExtendedPrefix = "razer_chroma_extended_matrix_effect_";
    private const string MousePrefix = "razer_chroma_mouse_extended_matrix_effect_";

    internal static RazerHidRequest CreateEffect(
        RazerHidDeviceDefinition device,
        RazerHidLightingSettings settings)
    {
        var suffix = GetEffectSuffix(settings.Effect);
        var storage = settings.Storage ?? device.DefaultStorage;
        var candidates = GetEffectBuilders(suffix);
        (string Builder, RazerHidLedZone Zone) selection;
        if (settings.Effect == RazerHidLightingEffect.Custom && settings.Zone is { } customZone)
        {
            selection = SelectDefaultEffect(device, settings.Effect, candidates, storage);
            if (selection.Zone != customZone)
            {
                throw new NotSupportedException(
                    $"Device 1532:{device.ProductId:X4} does not expose the custom selector on zone {customZone}.");
            }
        }
        else if (settings.Zone is { } requestedZone)
        {
            selection = (SelectBuilder(device, candidates, storage, requestedZone), requestedZone);
        }
        else
        {
            selection = SelectDefaultEffect(device, settings.Effect, candidates, storage);
        }
        var (builder, zone) = selection;

        if (settings.Effect == RazerHidLightingEffect.Custom)
        {
            var fixedStorage = builder.StartsWith(StandardPrefix, StringComparison.Ordinal)
                ? (RazerHidStorage)device.GetRequiredFixedArgument(builder, 0)
                : RazerHidStorage.Temporary;
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

    internal static IReadOnlyList<RazerHidRequest> CreateEffectRequests(
        RazerHidDeviceDefinition device,
        RazerHidLightingSettings settings)
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

    internal static RazerHidRequest GetBrightness(
        RazerHidDeviceDefinition device,
        RazerHidStorage? storage,
        RazerHidLedZone? ledId)
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
            return RazerHidStandardProtocol.Create(device,
                builder, 0x03, 0x0F, 0x84, [(byte)actualStorage, (byte)actualZone]);
        }
        if (builder == "razer_chroma_misc_get_blade_brightness")
        {
            return RazerHidStandardProtocol.Create(device,
                builder, 0x02, 0x0E, 0x84, [0x01]);
        }
        if (builder == "razer_chroma_misc_get_dock_brightness")
        {
            return RazerHidStandardProtocol.Create(device,
                builder, 0x01, 0x07, 0x82, []);
        }
        return RazerHidStandardProtocol.GetLedBrightness(device, (byte)actualStorage, (byte)actualZone);
    }

    internal static RazerHidRequest SetBrightness(
        RazerHidDeviceDefinition device,
        RazerHidStorage? storage,
        RazerHidLedZone? ledId,
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
            return RazerHidStandardProtocol.Create(device,
                builder, 0x03, 0x0F, 0x04,
                [(byte)actualStorage, (byte)actualZone, brightness]);
        }
        if (builder == "razer_chroma_misc_set_blade_brightness")
        {
            return RazerHidStandardProtocol.Create(device,
                builder, 0x02, 0x0E, 0x04, [0x01, brightness]);
        }
        if (builder == "razer_chroma_misc_set_dock_brightness")
        {
            return RazerHidStandardProtocol.Create(device,
                builder, 0x01, 0x07, 0x02, [brightness]);
        }
        return RazerHidStandardProtocol.SetLedBrightness(device, (byte)actualStorage, (byte)actualZone, brightness);
    }

    internal static RazerHidRequest SetCustomRow(
        RazerHidDeviceDefinition device,
        byte row,
        byte startColumn,
        IReadOnlyList<RazerHidColor> colors)
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
            return RazerHidStandardProtocol.Create(device,
                "razer_chroma_standard_matrix_set_custom_frame", 0x46, 0x03, 0x0B,
                new byte[] { 0xFF, row, startColumn, stopColumn }.Concat(rgb).ToArray(),
                allowArgumentsBeyondDeclaredSize: true);
        }

        var builder = device.Transactions.ContainsKey("razer_chroma_extended_matrix_set_custom_frame2")
            ? "razer_chroma_extended_matrix_set_custom_frame2"
            : "razer_chroma_extended_matrix_set_custom_frame";
        var arguments = new byte[] { 0x00, 0x00, row, startColumn, stopColumn }.Concat(rgb).ToArray();
        var dataSize = builder.EndsWith('2') ? checked((byte)arguments.Length) : (byte)0x47;
        return RazerHidStandardProtocol.Create(device, builder, dataSize, 0x0F, 0x03, arguments,
            allowArgumentsBeyondDeclaredSize: arguments.Length > dataSize);
    }

    private static RazerHidRequest CreateStandard(
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidLightingSettings value,
        RazerHidStorage storage)
    {
        var speed = Math.Clamp(value.Speed, (byte)1, (byte)4);
        var arguments = value.Effect switch
        {
            RazerHidLightingEffect.Off => new byte[] { 0x00 },
            RazerHidLightingEffect.Wave => [0x01, Math.Clamp(value.Direction, (byte)1, (byte)2)],
            RazerHidLightingEffect.Spectrum => [0x04],
            // The middleware keeps the flags byte at zero for reactive and puts the colour
            // straight after it; the speed byte it would overwrite is not part of the frame.
            RazerHidLightingEffect.Reactive => [0x02, 0, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.Static => [0x06, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.StarlightRandom => [0x19, 0x03, Math.Clamp(value.Speed, (byte)1, (byte)3)],
            RazerHidLightingEffect.StarlightSingle =>
                [0x19, 0x01, Math.Clamp(value.Speed, (byte)1, (byte)3), value.Primary.Red, value.Primary.Green, value.Primary.Blue, 0, 0, 0],
            RazerHidLightingEffect.StarlightDual =>
                [0x19, 0x02, Math.Clamp(value.Speed, (byte)1, (byte)3), value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            RazerHidLightingEffect.BreathingRandom => [0x03, 0x03],
            RazerHidLightingEffect.BreathingSingle =>
                [0x03, 0x01, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.BreathingDual =>
                [0x03, 0x02, value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            RazerHidLightingEffect.Custom => [0x05, (byte)storage],
            _ => throw new NotSupportedException($"Standard lighting does not support {value.Effect}."),
        };
        var declaredSize = value.Effect is RazerHidLightingEffect.StarlightRandom or
            RazerHidLightingEffect.StarlightSingle or RazerHidLightingEffect.StarlightDual
            ? (byte)1
            : checked((byte)arguments.Length);
        return RazerHidStandardProtocol.Create(device, builder, declaredSize, 0x03, 0x0A, arguments,
            allowArgumentsBeyondDeclaredSize: arguments.Length > declaredSize);
    }

    private static RazerHidRequest CreateExtended(
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidLightingSettings value,
        RazerHidStorage storage,
        RazerHidLedZone zone)
    {
        var prefix = new byte[] { (byte)storage, (byte)zone };
        byte[] tail = value.Effect switch
        {
            RazerHidLightingEffect.Off => [0x00, 0, 0, 0],
            RazerHidLightingEffect.Static => [0x01, 0, 0, 1, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.Wave => [0x04, Math.Clamp(value.Direction, (byte)0, (byte)2), 0x28, 0],
            RazerHidLightingEffect.Spectrum => [0x03, 0, 0, 0],
            // Wheel is effect 12 in the middleware's Kw table; 10 is AudioMeter, and the
            // middleware pins the rate byte to zero for this effect.
            RazerHidLightingEffect.Wheel => [0x0C, Math.Clamp(value.Direction, (byte)1, (byte)2), 0, 0],
            RazerHidLightingEffect.Reactive =>
                [0x05, 0, Math.Clamp(value.Speed, (byte)1, (byte)4), 1, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.BreathingRandom => [0x02, 0, 0, 0],
            RazerHidLightingEffect.BreathingSingle =>
                [0x02, 1, 0, 1, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.BreathingDual =>
                [0x02, 2, 0, 2, value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            RazerHidLightingEffect.StarlightRandom =>
                [0x07, 0, Math.Clamp(value.Speed, (byte)1, (byte)3), 0],
            RazerHidLightingEffect.StarlightSingle =>
                [0x07, 1, Math.Clamp(value.Speed, (byte)1, (byte)3), 1, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.StarlightDual =>
                [0x07, 2, Math.Clamp(value.Speed, (byte)1, (byte)3), 2, value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            RazerHidLightingEffect.Custom => [0x08, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            _ => throw new NotSupportedException($"Extended lighting does not support {value.Effect}."),
        };
        var arguments = value.Effect == RazerHidLightingEffect.Custom
            ? new byte[] { 0x00, 0x00 }.Concat(tail).ToArray()
            : prefix.Concat(tail).ToArray();
        return RazerHidStandardProtocol.Create(device, builder, checked((byte)arguments.Length), 0x0F, 0x02, arguments);
    }

    private static RazerHidRequest CreateMouse(
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidLightingSettings value,
        RazerHidStorage storage,
        RazerHidLedZone zone)
    {
        byte[] tail = value.Effect switch
        {
            RazerHidLightingEffect.Off => [0x00],
            RazerHidLightingEffect.Static => [0x06, value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.Spectrum => [0x04],
            RazerHidLightingEffect.Reactive =>
                [0x02, Math.Clamp(value.Speed, (byte)1, (byte)4), value.Primary.Red, value.Primary.Green, value.Primary.Blue],
            RazerHidLightingEffect.BreathingRandom => [0x03, 0x03, 0, 0, 0, 0, 0, 0],
            RazerHidLightingEffect.BreathingSingle =>
                [0x03, 0x01, value.Primary.Red, value.Primary.Green, value.Primary.Blue, 0, 0, 0],
            RazerHidLightingEffect.BreathingDual =>
                [0x03, 0x02, value.Primary.Red, value.Primary.Green, value.Primary.Blue, value.Secondary.Red, value.Secondary.Green, value.Secondary.Blue],
            _ => throw new NotSupportedException($"Mouse lighting does not support {value.Effect}."),
        };
        var arguments = new byte[] { (byte)storage, (byte)zone }.Concat(tail).ToArray();
        return RazerHidStandardProtocol.Create(device, builder, checked((byte)arguments.Length), 0x03, 0x0D, arguments);
    }

    private static string SelectBuilder(
        RazerHidDeviceDefinition device,
        IReadOnlyList<string> candidates,
        RazerHidStorage storage,
        RazerHidLedZone zone)
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
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidStorage storage,
        RazerHidLedZone zone)
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
                    Enum.IsDefined((RazerHidLedZone)prefix.Span[1]))
                .Select(prefix => (RazerHidLedZone)prefix.Span[1])
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

    internal static RazerHidLedZone ResolveEffectZone(
        RazerHidDeviceDefinition device,
        RazerHidLightingEffect effect,
        RazerHidStorage? storage = null,
        RazerHidLedZone? zone = null)
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
            var settings = new RazerHidLightingSettings(effect, Storage: storage, Zone: zone);
            if (!CanCreateClassicEffect(device, settings))
            {
                throw;
            }
            return zone ?? device.DefaultLedZone;
        }
    }

    internal static RazerHidLedZone ResolveBrightnessZone(
        RazerHidDeviceDefinition device,
        bool write,
        RazerHidStorage? storage = null,
        RazerHidLedZone? zone = null) =>
        ResolveZone(device,
            write
                ? ["razer_chroma_extended_matrix_brightness", "razer_chroma_standard_set_led_brightness",
                    "razer_chroma_misc_set_blade_brightness", "razer_chroma_misc_set_dock_brightness"]
                : ["razer_chroma_extended_matrix_get_brightness", "razer_chroma_standard_get_led_brightness",
                    "razer_chroma_misc_get_blade_brightness", "razer_chroma_misc_get_dock_brightness"],
            storage ?? device.DefaultStorage, zone);

    private static RazerHidLedZone ResolveZone(
        RazerHidDeviceDefinition device,
        IReadOnlyList<string> candidates,
        RazerHidStorage storage,
        RazerHidLedZone? requested)
    {
        if (requested is { } explicitZone)
        {
            _ = SelectBuilder(device, candidates, storage, explicitZone);
            return explicitZone;
        }

        var zones = new List<RazerHidLedZone> { device.DefaultLedZone };
        foreach (var builder in candidates)
        {
            if (!device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes))
            {
                continue;
            }
            foreach (var prefix in prefixes)
            {
                if (prefix.Length >= 2 && prefix.Span[0] == (byte)storage &&
                    Enum.IsDefined((RazerHidLedZone)prefix.Span[1]))
                {
                    var zone = (RazerHidLedZone)prefix.Span[1];
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

    private static string GetEffectSuffix(RazerHidLightingEffect effect) => effect switch
    {
        RazerHidLightingEffect.Off => "none",
        RazerHidLightingEffect.Static => "static",
        RazerHidLightingEffect.Spectrum => "spectrum",
        RazerHidLightingEffect.Wave => "wave",
        RazerHidLightingEffect.Reactive => "reactive",
        RazerHidLightingEffect.Blinking => "blinking",
        RazerHidLightingEffect.BreathingRandom => "breathing_random",
        RazerHidLightingEffect.BreathingSingle => "breathing_single",
        RazerHidLightingEffect.BreathingDual => "breathing_dual",
        RazerHidLightingEffect.StarlightRandom => "starlight_random",
        RazerHidLightingEffect.StarlightSingle => "starlight_single",
        RazerHidLightingEffect.StarlightDual => "starlight_dual",
        RazerHidLightingEffect.Wheel => "wheel",
        RazerHidLightingEffect.Custom => "custom_frame",
        _ => throw new NotSupportedException($"{effect} is not supported by the standard RazerHid lighting protocol."),
    };

    private static string[] GetEffectBuilders(string suffix) =>
        [StandardPrefix + suffix, ExtendedPrefix + suffix, MousePrefix + suffix];

    private static (string Builder, RazerHidLedZone Zone) SelectDefaultEffect(
        RazerHidDeviceDefinition device,
        RazerHidLightingEffect effect,
        IReadOnlyList<string> candidates,
        RazerHidStorage storage)
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

    private static IReadOnlyList<RazerHidLedZone> GetBuilderZones(
        RazerHidDeviceDefinition device,
        string builder,
        RazerHidStorage storage)
    {
        if (builder.StartsWith(StandardPrefix, StringComparison.Ordinal))
        {
            return [device.DefaultLedZone];
        }
        if (device.BuilderArgumentPrefixes.TryGetValue(builder, out var prefixes))
        {
            var exact = prefixes
                .Where(prefix => prefix.Length >= 2 && prefix.Span[0] == (byte)storage &&
                    Enum.IsDefined((RazerHidLedZone)prefix.Span[1]))
                .Select(prefix => (RazerHidLedZone)prefix.Span[1])
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
                Enum.IsDefined((RazerHidLedZone)prefix.Span[1]))
            .Select(prefix => (RazerHidLedZone)prefix.Span[1])
            .Distinct()
            .ToList();
        if (siblingPrefixes.Any(prefix => prefix.Length == 1 && prefix.Span[0] == (byte)storage) &&
            !siblingZones.Contains(device.DefaultLedZone))
        {
            siblingZones.Add(device.DefaultLedZone);
        }
        return siblingZones;
    }

    internal static string GetGenericEffectCapability(RazerHidLightingEffect effect) => effect switch
    {
        RazerHidLightingEffect.Off => "set_none_effect",
        RazerHidLightingEffect.Static => "set_static_effect",
        RazerHidLightingEffect.Spectrum => "set_spectrum_effect",
        RazerHidLightingEffect.Wave => "set_wave_effect",
        RazerHidLightingEffect.Reactive => "set_reactive_effect",
        RazerHidLightingEffect.Blinking => "set_blinking_effect",
        RazerHidLightingEffect.BreathingRandom => "set_breath_random_effect",
        RazerHidLightingEffect.BreathingSingle => "set_breath_single_effect",
        RazerHidLightingEffect.BreathingDual => "set_breath_dual_effect",
        RazerHidLightingEffect.StarlightRandom => "set_starlight_random_effect",
        RazerHidLightingEffect.StarlightSingle => "set_starlight_single_effect",
        RazerHidLightingEffect.StarlightDual => "set_starlight_dual_effect",
        RazerHidLightingEffect.Wheel => "set_wheel_effect",
        RazerHidLightingEffect.Custom => "set_custom_effect",
        _ => throw new ArgumentOutOfRangeException(nameof(effect)),
    };

    private static bool CanCreateClassicEffect(
        RazerHidDeviceDefinition device,
        RazerHidLightingSettings settings)
    {
        string capability;
        try { capability = GetGenericEffectCapability(settings.Effect); }
        catch (ArgumentOutOfRangeException) { return false; }
        if (!device.HasSourceCapability(capability)) return false;
        var storage = settings.Storage ?? device.DefaultStorage;
        var zone = settings.Zone ?? device.DefaultLedZone;
        var builders = settings.Effect switch
        {
            RazerHidLightingEffect.Off => new[] { "razer_chroma_standard_set_led_state" },
            RazerHidLightingEffect.Spectrum =>
                ["razer_chroma_standard_set_led_state", "razer_chroma_standard_set_led_effect"],
            RazerHidLightingEffect.Static or RazerHidLightingEffect.BreathingSingle or RazerHidLightingEffect.Blinking =>
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

    private static IReadOnlyList<RazerHidRequest> CreateClassicEffect(
        RazerHidDeviceDefinition device,
        RazerHidLightingSettings settings)
    {
        var storage = (byte)(settings.Storage ?? device.DefaultStorage);
        var zone = (byte)(settings.Zone ?? device.DefaultLedZone);
        if (settings.Effect == RazerHidLightingEffect.Off)
        {
            return [RazerHidStandardProtocol.SetLedState(device, storage, zone, false)];
        }

        var requests = new List<RazerHidRequest>
        {
            RazerHidStandardProtocol.SetLedState(device, storage, zone, true),
        };
        if (settings.Effect is RazerHidLightingEffect.Static or
            RazerHidLightingEffect.BreathingSingle or RazerHidLightingEffect.Blinking)
        {
            requests.Add(RazerHidStandardProtocol.SetLedColor(device, storage, zone, settings.Primary));
        }
        requests.Add(RazerHidStandardProtocol.SetLedEffect(device, storage, zone, settings.Effect switch
        {
            RazerHidLightingEffect.Static => RazerHidClassicLedEffect.Static,
            RazerHidLightingEffect.BreathingSingle => RazerHidClassicLedEffect.Breathing,
            RazerHidLightingEffect.Blinking => RazerHidClassicLedEffect.Blinking,
            RazerHidLightingEffect.Spectrum => RazerHidClassicLedEffect.Spectrum,
            _ => throw new NotSupportedException(),
        }));
        return requests;
    }
}
