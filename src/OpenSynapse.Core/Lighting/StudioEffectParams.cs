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

/// <summary>Normalized 0..1 screen region used by the ambient effect.</summary>
public readonly record struct StudioScreenRect(double X, double Y, double Width, double Height);

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

    /// <summary>Wave/ripple band width, 0..100.</summary>
    public double Width { get; set; } = 50;

    /// <summary>Degrees, 0..359.</summary>
    public int Angle { get; set; }

    /// <summary>Split point, 0..100.</summary>
    public double Split { get; set; } = 50;

    /// <summary>Starlight density, 0..100.</summary>
    public double Density { get; set; } = 50;

    /// <summary>Audio boost, 0..3.</summary>
    public double Boost { get; set; } = 1;

    public bool AutoBoost { get; set; }

    /// <summary>Audio decay, 0..100.</summary>
    public double Decay { get; set; } = 50;

    /// <summary>Ambient blur radius, 0..20.</summary>
    public int Blur { get; set; }

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
        Boost = Math.Clamp(Boost, 0.25, 4);
        Decay = Math.Clamp(Decay, 0.1, 2);
        Blur = Math.Clamp(Blur, 0, 9);
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
            Boost = Boost,
            AutoBoost = AutoBoost,
            Decay = Decay,
            Blur = Blur,
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
