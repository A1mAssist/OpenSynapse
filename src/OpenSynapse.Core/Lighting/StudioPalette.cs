namespace OpenSynapse.Core.Lighting;

/// <summary>Named or inline color ramp sampled by the preset modules.</summary>
public sealed class StudioPalette
{
    private readonly StudioColorStop[] _stops;

    public StudioPalette(string name, IReadOnlyList<StudioColorStop> stops)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count == 0) throw new ArgumentException("A palette needs at least one stop.", nameof(stops));

        Name = name;
        var sorted = stops
            .OrderBy(stop => stop.Stop)
            .Select(stop => new StudioColorStop(Math.Clamp(stop.Stop, 0, 100), stop.Color))
            .ToList();
        if (sorted[0].Stop > 0) sorted.Insert(0, new StudioColorStop(0, sorted[0].Color));
        if (sorted[^1].Stop < 100) sorted.Add(new StudioColorStop(100, sorted[^1].Color));
        _stops = sorted.ToArray();
    }

    public static StudioPalette White { get; } = new(
        "white",
        [new StudioColorStop(0, StudioRgb.White), new StudioColorStop(100, StudioRgb.White)]);

    public string Name { get; }

    public IReadOnlyList<StudioColorStop> Stops => _stops;

    public StudioRgb Sample(double position)
    {
        var target = Math.Clamp(position, 0, 1) * 100;
        if (target <= _stops[0].Stop) return _stops[0].Color;
        if (target >= _stops[^1].Stop) return _stops[^1].Color;

        for (var index = 1; index < _stops.Length; index++)
        {
            var right = _stops[index];
            if (target > right.Stop) continue;
            var left = _stops[index - 1];
            var span = right.Stop - left.Stop;
            return span <= 0
                ? right.Color
                : StudioRgb.Lerp(left.Color, right.Color, (target - left.Stop) / span);
        }

        return _stops[^1].Color;
    }

    public static bool TryGet(string? name, out StudioPalette palette)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            palette = White;
            return false;
        }

        return Named.TryGetValue(name.Trim().ToLowerInvariant(), out palette!);
    }

    private static readonly IReadOnlyDictionary<string, StudioPalette> Named =
        BuildNamed();

    private static Dictionary<string, StudioPalette> BuildNamed()
    {
        var palettes = new Dictionary<string, StudioPalette>(StringComparer.Ordinal)
        {
            ["white"] = White,
            ["mono"] = Ramp("mono", "#404040", "#FFFFFF"),
            ["green"] = Ramp("green", "#00FF00", "#8CFF66"),
            ["blue"] = Ramp("blue", "#0040FF", "#00BFFF"),
            ["purple"] = Ramp("purple", "#7A00FF", "#FF00D4"),
            ["gold"] = Ramp("gold", "#FFB300", "#FFF3B0"),
            ["warm"] = Ramp("warm", "#FF4D00", "#FFC400"),
            ["cool"] = Ramp("cool", "#00A8FF", "#6A5CFF"),
            ["mint"] = Ramp("mint", "#00FFA3", "#B6FFEA"),
            ["ice"] = Ramp("ice", "#0A1A4A", "#3FC8FF", "#E8FFFF"),
            ["ocean"] = Ramp("ocean", "#001B44", "#0077FF", "#00E5D0"),
            ["aurora"] = Ramp("aurora", "#00FF88", "#00CFFF", "#8A2BE2"),
            ["sunset"] = Ramp("sunset", "#FF3D00", "#FF9100", "#FFD54F", "#FF4081"),
            ["candy"] = Ramp("candy", "#FF4FA3", "#FFD166", "#7BE0FF"),
            ["neon"] = Ramp("neon", "#FF00E5", "#00FFF0", "#9DFF00"),
            ["fire"] = Ramp("fire", "#2A0000", "#FF2A00", "#FFB000", "#FFF2A8"),
            ["lava"] = Ramp("lava", "#1A0000", "#B30000", "#FF6A00", "#FFD000"),
            ["spectrum"] = Ramp("spectrum", "#00FF00", "#FFFF00", "#FF8000", "#FF0000"),
            ["rainbow"] = Ramp(
                "rainbow",
                "#FF0000", "#FFCC00", "#36FF48", "#62F1FF", "#000CFF", "#8000FF", "#FF0000"),
        };
        return palettes;
    }

    private static StudioPalette Ramp(string name, params string[] hex)
    {
        var stops = new StudioColorStop[hex.Length];
        for (var index = 0; index < hex.Length; index++)
        {
            stops[index] = new StudioColorStop(
                hex.Length == 1 ? 0 : (int)Math.Round(index * 100d / (hex.Length - 1)),
                StudioRgb.FromHex(hex[index]));
        }

        return new StudioPalette(name, stops);
    }
}
