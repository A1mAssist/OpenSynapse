using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenSynapse.Core.Lighting;

public enum StudioPlaybackStart
{
    Random,
    OnPress,
    OnSelectedKeys,
}

public enum StudioPlaybackEnd
{
    Never,
    After,
    Toggle,
}




public readonly record struct StudioColorStop(int Stop, StudioRgb Color);

/// <summary>
/// One effect instance. Chroma keeps these fields flat in the layer or region
/// "params" object, so this type stays flat as well and the codec maps names.
/// </summary>
public sealed class StudioEffectParams
{
    public StudioEffectKind Effect { get; set; } = StudioEffectKind.Solid;

    public StudioRgb Primary { get; set; } = new(0x44, 0xD6, 0x2C);

    public StudioRgb Secondary { get; set; } = new(0x00, 0xBF, 0xFF);

    /// <summary>Ordered stops, 0..100. Empty means "use Primary/Secondary".</summary>
    public List<StudioColorStop> ColorStops { get; } = new();

    public bool RandomColor { get; set; }

    /// <summary>True when ColorStops came from a user palette instead of a preset.</summary>
    public bool ColorStopsCustom { get; set; }

    /// <summary>One cycle length in milliseconds; 0 means the effect default.</summary>
    public int DurationMs { get; set; }

    public int PauseMs { get; set; }

    /// <summary>-1 is infinite; the UI only offers 1..100 when End is After.</summary>
    public int Cycles { get; set; } = -1;

    public StudioPlaybackStart PlaybackStart { get; set; }

    public StudioPlaybackEnd PlaybackEnd { get; set; }

    /// <summary>Multiplier applied on top of DurationMs.</summary>
    public double Speed { get; set; } = 1;

    /// <summary>Wave/ripple width, 10..400 (Chroma's 100..400 ring steps).</summary>
    public double Width { get; set; } = 50;

    /// <summary>Degrees, 0..359.</summary>
    public int Angle { get; set; }

    /// <summary>Split point, 0..100.</summary>
    public double Split { get; set; } = 50;

    /// <summary>Starlight max-star density, 1..10.</summary>
    public double Density { get; set; } = 50;

    /// <summary>
    /// CFireEffect seed/rate. Native defaults are 100/160, Seed 0 maps to 50 and
    /// Rate below 40 maps to 40; the upper bounds cap the generated frame table.
    /// </summary>
    public const int FireSeedMinimum = 1;
    public const int FireSeedMaximum = 1_000;
    public const int FireRateMinimum = 40;
    public const int FireRateMaximum = 1_000;
    public const int FireDefaultSeed = 100;
    public const int FireDefaultRate = 160;

    public int FireSeed { get; set; } = FireDefaultSeed;

    /// <summary>CFireEffect frame rate; native frames = Seed * (Rate / 40 + 1).</summary>
    public int FireRate { get; set; } = FireDefaultRate;

    /// <summary>Audio boost, 0.25..4.</summary>
    public double Boost { get; set; } = 1;

    public bool AutoBoost { get; set; }

    /// <summary>Audio decay, 0.1..2.</summary>
    public double Decay { get; set; } = 50;

    /// <summary>Ambient blur radius, 0..9.</summary>
    public int Blur { get; set; }

    /// <summary>Chroma design-canvas center (0..10000) used by tidal.</summary>
    public double CenterPointX { get; set; } = 5_000;

    public double CenterPointY { get; set; } = 5_000;

    /// <summary>Chroma's 0..250 editor space for the color-wheel pivot.</summary>
    public const int WheelCenterMaximum = 250;

    /// <summary>Wheel center row (Y); -1 falls back to the matrix center.</summary>
    public int CenterRow { get; set; } = -1;

    /// <summary>Wheel center column (X); -1 falls back to the matrix center.</summary>
    public int CenterCol { get; set; } = -1;

    /// <summary>Tidal sweep direction; true is Chroma's inward direction.</summary>
    public bool Counterclockwise { get; set; }

    /// <summary>Raw Chroma editor state naming the devices that share the custom center.</summary>
    public string? DeviceWithCenterPointJson { get; set; }

    public string? Screen { get; set; }

    public StudioScreenRect? ScreenRect { get; set; }

    /// <summary>Local path used by the Chroma Generate effect.</summary>
    public string? MediaSrc { get; set; }

    /// <summary>Raw generator configuration; interpreted by the generate effect only.</summary>
    public string? CurrentConfigJson { get; set; }

    /// <summary>Raw Chroma Generate presets (effectConfigList), at most five entries.</summary>
    public string? EffectConfigListJson { get; set; }

    /// <summary>Selected Chroma Generate preset, -1 when none is selected.</summary>
    public int CurrentConfigId { get; set; } = -1;

    /// <summary>
    /// Runtime-only media sampler for the generate effect. It lives on the
    /// parameters so the layer compositor can carry it without a new render
    /// input channel; the codec never serializes it.
    /// </summary>
    public IStudioMediaSampler? MediaSampler { get; set; }

    /// <summary>Generate fields parsed from <see cref="CurrentConfigJson"/>.</summary>
    public StudioGenerateConfig GenerateConfig => StudioGenerateConfig.Parse(CurrentConfigJson);

    /// <summary>Sample the palette at 0..1, falling back to Primary/Secondary.</summary>
    public StudioRgb Resolve(double position)
    {
        if (ColorStops.Count == 0)
        {
            return StudioRgb.Lerp(Primary, Secondary, position);
        }

        var stops = ColorStops.OrderBy(stop => stop.Stop).ToArray();
        var target = Math.Clamp(position, 0, 1) * 100;
        if (target <= stops[0].Stop) return stops[0].Color;
        if (target >= stops[^1].Stop) return stops[^1].Color;

        for (var index = 1; index < stops.Length; index++)
        {
            var right = stops[index];
            if (target > right.Stop) continue;
            var left = stops[index - 1];
            var span = right.Stop - left.Stop;
            var amount = span <= 0 ? 0 : (target - left.Stop) / span;
            return StudioRgb.Lerp(left.Color, right.Color, amount);
        }

        return stops[^1].Color;
    }

    public void Normalize()
    {
        while (ColorStops.Count > 10) ColorStops.RemoveAt(ColorStops.Count - 1);
        for (var index = 0; index < ColorStops.Count; index++)
        {
            var stop = ColorStops[index];
            ColorStops[index] = stop with { Stop = Math.Clamp(stop.Stop, 0, 100) };
        }

        DurationMs = Math.Clamp(DurationMs, 0, 600_000);
        PauseMs = Math.Clamp(PauseMs, 0, 60_000);
        Cycles = Math.Clamp(Cycles, -1, 100);
        // Chroma ranges: wave/ripple speed 0..50, wheel 0..360, width 10..400.
        Speed = Math.Clamp(Speed, 0, 360);
        Width = Math.Clamp(Width, 0, 400);
        Angle = Math.Clamp(Angle, 0, 359);
        Split = Math.Clamp(Split, 0, 100);
        Density = Math.Clamp(Density, 0, 10);
        FireSeed = Math.Clamp(FireSeed, FireSeedMinimum, FireSeedMaximum);
        FireRate = Math.Clamp(FireRate, FireRateMinimum, FireRateMaximum);
        Boost = Math.Clamp(Boost, 0.25, 4);
        Decay = Math.Clamp(Decay, 0.1, 2);
        Blur = Math.Clamp(Blur, 0, 9);
        CenterPointX = Math.Clamp(CenterPointX, 0, 10_000);
        CenterPointY = Math.Clamp(CenterPointY, 0, 10_000);
        if (CenterRow < 0 || CenterCol < 0)
        {
            CenterRow = -1;
            CenterCol = -1;
        }
        else
        {
            CenterRow = Math.Clamp(CenterRow, 0, WheelCenterMaximum);
            CenterCol = Math.Clamp(CenterCol, 0, WheelCenterMaximum);
        }
    }

    public StudioEffectParams Clone()
    {
        var clone = new StudioEffectParams
        {
            Effect = Effect,
            Primary = Primary,
            Secondary = Secondary,
            RandomColor = RandomColor,
            ColorStopsCustom = ColorStopsCustom,
            DurationMs = DurationMs,
            PauseMs = PauseMs,
            Cycles = Cycles,
            PlaybackStart = PlaybackStart,
            PlaybackEnd = PlaybackEnd,
            Speed = Speed,
            Width = Width,
            Angle = Angle,
            Split = Split,
            Density = Density,
            FireSeed = FireSeed,
            FireRate = FireRate,
            Boost = Boost,
            AutoBoost = AutoBoost,
            Decay = Decay,
            Blur = Blur,
            CenterPointX = CenterPointX,
            CenterPointY = CenterPointY,
            CenterRow = CenterRow,
            CenterCol = CenterCol,
            Counterclockwise = Counterclockwise,
            DeviceWithCenterPointJson = DeviceWithCenterPointJson,
            Screen = Screen,
            ScreenRect = ScreenRect,
            MediaSrc = MediaSrc,
            CurrentConfigJson = CurrentConfigJson,
            EffectConfigListJson = EffectConfigListJson,
            CurrentConfigId = CurrentConfigId,
            MediaSampler = MediaSampler,
        };
        clone.ColorStops.AddRange(ColorStops);
        return clone;
    }

    public static StudioEffectParams FromPalette(StudioEffectKind effect, params StudioRgb[] colors)
    {
        var result = new StudioEffectParams { Effect = effect };
        if (colors.Length > 0) result.Primary = colors[0];
        if (colors.Length > 1) result.Secondary = colors[1];
        for (var index = 0; index < colors.Length; index++)
        {
            var stop = colors.Length == 1 ? 0 : (int)Math.Round(index * 100d / (colors.Length - 1));
            result.ColorStops.Add(new StudioColorStop(stop, colors[index]));
        }

        return result;
    }
}

/// <summary>
/// Local, offline Chroma Generate presets: the captured web generator ranges and
/// its five-entry effectConfigList history. No network or model is involved.
/// </summary>
public static class StudioGeneratePresets
{
    public const int HistoryLimit = 5;

    private static readonly StudioGenerateBlend[] Blends = Enum.GetValues<StudioGenerateBlend>();

    /// <summary>Builds one preset; a seeded <see cref="Random"/> makes the output repeatable.</summary>
    public static StudioGenerateConfig Create(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return new StudioGenerateConfig(
            OffsetX: OneDecimal(10 * random.NextDouble()),
            OffsetY: OneDecimal(10 * random.NextDouble()),
            Rotation: OneDecimal(360 * random.NextDouble() - 180),
            ScaleX: OneDecimal(9.9 * random.NextDouble() + 0.1),
            ScaleY: OneDecimal(9.9 * random.NextDouble() + 0.1),
            MirrorX: random.NextDouble() < 0.5,
            MirrorY: random.NextDouble() < 0.5,
            FlipX: random.NextDouble() < 0.5,
            FlipY: random.NextDouble() < 0.5,
            PatternSpread: OneDecimal(49 * random.NextDouble() + 1),
            PatternSpeed: OneDecimal(20 * random.NextDouble() - 10),
            Pattern: random.Next(1, 10),
            Blend: Blends[random.Next(Blends.Length)],
            IsAi: false);
    }

    /// <summary>Serializes a preset in the flat shape the native parser reads.</summary>
    public static string Format(StudioGenerateConfig config)
    {
        var json = new JsonObject
        {
            // The AI generator has no local model yet; keep the native selector procedural.
            ["colorSource"] = "procedural",
            ["offsetX"] = Round(config.OffsetX),
            ["offsetY"] = Round(config.OffsetY),
            ["rotation"] = Round(config.Rotation),
            // ScaleX/ScaleY already include the parsed multiplier, so emit scale as 1.
            ["scale"] = 1,
            ["scaleX"] = Round(config.ScaleX),
            ["scaleY"] = Round(config.ScaleY),
            ["mirrorX"] = config.MirrorX,
            ["mirrorY"] = config.MirrorY,
            ["flipX"] = config.FlipX,
            ["flipY"] = config.FlipY,
            ["patternSpread"] = Round(config.PatternSpread),
            ["patternSpeed"] = Round(config.PatternSpeed),
            ["blend"] = config.Blend.ToString(),
            ["pattern"] = config.Pattern.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return json.ToJsonString();
    }

    /// <summary>Reads the stored presets; malformed or non-object entries yield none.</summary>
    public static IReadOnlyList<string> Read(string? listJson)
    {
        if (string.IsNullOrWhiteSpace(listJson)) return [];
        try
        {
            using var document = JsonDocument.Parse(listJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

            var entries = new List<string>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object) entries.Add(entry.GetRawText());
            }

            return entries;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Appends a preset, dropping the oldest once five are stored.</summary>
    public static string Append(string? listJson, string configJson, out int currentConfigId)
    {
        var entries = Read(listJson).ToList();
        entries.Add(configJson);
        if (entries.Count > HistoryLimit) entries.RemoveRange(0, entries.Count - HistoryLimit);
        currentConfigId = entries.Count - 1;
        return Serialize(entries);
    }

    /// <summary>
    /// Previous/next preset id for a list of <paramref name="count"/> entries.
    /// Returns -1 when the list is empty, the cursor is at the edge, or the move leaves the range.
    /// </summary>
    public static int Step(int count, int currentConfigId, int direction)
    {
        if (count <= 0) return -1;
        var current = Math.Clamp(currentConfigId, -1, count - 1);
        var target = current + Math.Sign(direction);
        return target >= 0 && target < count ? target : -1;
    }

    /// <summary>Rewrites the selected preset; an unselected or missing slot keeps the list as-is.</summary>
    public static string? Replace(string? listJson, int currentConfigId, string configJson)
    {
        var entries = Read(listJson).ToList();
        if (currentConfigId < 0 || currentConfigId >= entries.Count) return listJson;
        entries[currentConfigId] = configJson;
        return Serialize(entries);
    }

    private static string Serialize(IEnumerable<string> entries)
    {
        var array = new JsonArray();
        foreach (var entry in entries) array.Add(JsonNode.Parse(entry));
        return array.ToJsonString();
    }

    private static double OneDecimal(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static double Round(double value) =>
        double.IsFinite(value) ? Math.Round(value, 3) : 0;
}
