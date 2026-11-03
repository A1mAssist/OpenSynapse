using System.Globalization;
using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Samples a decoded image or video frame in normalized tile coordinates.
/// Implementations wrap coordinates outside 0..1 and return false when no
/// pixel is available.
/// </summary>
public interface IStudioMediaSampler
{
    bool TrySample(double u, double v, out StudioRgb color);

    /// <summary>
    /// Samples the frame nearest <paramref name="seconds"/>. Static samplers
    /// ignore time and delegate to <see cref="TrySample(double, double, out StudioRgb)"/>.
    /// </summary>
    bool TrySampleAt(double u, double v, double seconds, out StudioRgb color) =>
        TrySample(u, v, out color);
}

/// <summary>Chroma Generate blend modes in native enum order.</summary>
public enum StudioGenerateBlend
{
    Pattern,
    Sample,
    Grayscale,
    Lerp2Colors,
    DarkenLighten20P,
    Split2Colors,
    Mix2Colors,
    Mix3Colors,
    LerpColors,
    PatternLerp,
    PatternLerpGT20,
    PatternGT20Color1,
    PatternLerpGT20Color1,
    Mix3ColorsLerp,
    PatternMixColorsLerp,
}

/// <summary>
/// Generate settings parsed from <see cref="StudioEffectParams.CurrentConfigJson"/>.
/// Defaults and ranges follow the captured effect-18 generator and native parser.
/// </summary>
public readonly record struct StudioGenerateConfig(
    double OffsetX,
    double OffsetY,
    double Rotation,
    double ScaleX,
    double ScaleY,
    bool MirrorX,
    bool MirrorY,
    bool FlipX,
    bool FlipY,
    double PatternSpread,
    double PatternSpeed,
    int Pattern,
    StudioGenerateBlend Blend,
    bool IsAi)
{
    private const double MaxOffset = 1000;
    private const double MaxScale = 1000;
    private const double MaxSpread = 1000;
    private const double MaxSpeed = 1000;

    public static StudioGenerateConfig Default => new(
        OffsetX: 0,
        OffsetY: 0,
        Rotation: 0,
        ScaleX: 1,
        ScaleY: 1,
        MirrorX: false,
        MirrorY: false,
        FlipX: false,
        FlipY: false,
        PatternSpread: 1,
        PatternSpeed: 0,
        Pattern: 1,
        Blend: StudioGenerateBlend.Pattern,
        IsAi: false);

    /// <summary>Parses the flat currentConfig object; invalid or missing JSON yields defaults.</summary>
    public static StudioGenerateConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Default;

            var scale = Number(root, "scale", 1, MaxScale);
            return new StudioGenerateConfig(
                OffsetX: Number(root, "offsetX", 0, MaxOffset),
                OffsetY: Number(root, "offsetY", 0, MaxOffset),
                Rotation: Number(root, "rotation", 0),
                ScaleX: Math.Clamp(Number(root, "scaleX", 1, MaxScale) * scale, -MaxScale, MaxScale),
                ScaleY: Math.Clamp(Number(root, "scaleY", 1, MaxScale) * scale, -MaxScale, MaxScale),
                MirrorX: Flag(root, "mirrorX"),
                MirrorY: Flag(root, "mirrorY"),
                FlipX: Flag(root, "flipX"),
                FlipY: Flag(root, "flipY"),
                PatternSpread: Number(root, "patternSpread", 1, MaxSpread),
                PatternSpeed: Number(root, "patternSpeed", 0, MaxSpeed),
                Pattern: ReadPattern(root),
                Blend: ReadBlend(root),
                IsAi: string.Equals(ReadText(root, "colorSource"), "ai", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    private static double Number(JsonElement root, string name, double fallback, double limit = double.MaxValue)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;

        double number;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDouble(out number)) return fallback;
        }
        else if (value.ValueKind != JsonValueKind.String ||
                 !double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return fallback;
        }

        return double.IsFinite(number) ? Math.Clamp(number, -limit, limit) : fallback;
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        (value.ValueKind == JsonValueKind.True ||
         (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var flag) && flag));

    private static string? ReadText(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static StudioGenerateBlend ReadBlend(JsonElement root)
    {
        var text = ReadText(root, "blend");
        if (string.IsNullOrWhiteSpace(text)) return StudioGenerateBlend.Pattern;
        foreach (var name in Enum.GetNames<StudioGenerateBlend>())
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<StudioGenerateBlend>(name);
            }
        }

        return StudioGenerateBlend.Pattern;
    }

    private static int ReadPattern(JsonElement root)
    {
        if (!root.TryGetProperty("pattern", out var value)) return 1;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pattern) ? pattern : 1;
    }
}

/// <summary>
/// Pure Chroma Generate colour math: media sampling transforms, palette
/// extraction and blend modes. It has no Windows or drawing dependencies.
/// </summary>
public static class StudioMediaPalette
{
    public const int GridSize = 32;
    public const int PaletteSize = 8;

    /// <summary>
    /// Builds the palette from the unique colours of a 32 x 32 sample of the
    /// media. Returns an empty array when the sampler yields no pixel.
    /// </summary>
    public static StudioRgb[] BuildPalette(StudioEffectParams parameters, IStudioMediaSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(sampler);

        var histogram = new Dictionary<int, int>();
        for (var row = 0; row < GridSize; row++)
        {
            for (var column = 0; column < GridSize; column++)
            {
                if (!sampler.TrySample((column + 0.5) / GridSize, (row + 0.5) / GridSize, out var color)) continue;
                var key = (color.R << 16) | (color.G << 8) | color.B;
                histogram[key] = histogram.GetValueOrDefault(key) + 1;
            }
        }

        if (histogram.Count == 0) return [];

        double sine = 0;
        double cosine = 0;
        double saturation = 0;
        double lightness = 0;
        foreach (var key in histogram.Keys.Order())
        {
            var color = new StudioRgb((byte)(key >> 16), (byte)(key >> 8), (byte)key);
            var (hue, sampleSaturation, sampleLightness) = ToHsl(color);
            sine += Math.Sin(hue * Math.Tau);
            cosine += Math.Cos(hue * Math.Tau);
            saturation += sampleSaturation;
            lightness += sampleLightness;
        }

        var hueMean = Math.Atan2(sine, cosine) / Math.Tau;
        if (hueMean < 0) hueMean += 1;

        var isAi = parameters.GenerateConfig.IsAi;
        var saturationMean = isAi ? 0.1 : Math.Clamp(saturation / histogram.Count + 0.3, 0, 1);
        // shortcut: procedural and AI-fallback palettes share the fallback lightness
        // series because native procedural entry expansion was not captured; upgrade
        // when the procedural entry set can be compared against the engine.
        return BuildStops(hueMean, saturationMean, lightness / histogram.Count);
    }

    /// <summary>
    /// Applies the native transform order: scale, flip, offset, mirror, rotate.
    /// Mirror folds coordinates past the centre back onto the other half, which
    /// matches the native size/2 test and makes the result symmetric.
    /// </summary>
    public static (double U, double V) Transform(double u, double v, StudioGenerateConfig config)
    {
        var x = (u - 0.5) * config.ScaleX;
        var y = (v - 0.5) * config.ScaleY;

        if (config.FlipX) x = -x;
        if (config.FlipY) y = -y;

        x -= config.OffsetX;
        y -= config.OffsetY;

        if (config.MirrorX) x = Math.Abs(x);
        if (config.MirrorY) y = Math.Abs(y);

        var angle = config.Rotation * Math.PI / 180;
        if (Math.Abs(angle) > 1e-9)
        {
            var cosine = Math.Cos(angle);
            var sine = Math.Sin(angle);
            (x, y) = (x * cosine - y * sine, x * sine + y * cosine);
        }

        return (Wrap(x + 0.5), Wrap(y + 0.5));
    }

    /// <summary>Samples the palette through the pattern scalar and phase formula.</summary>
    public static StudioRgb SamplePattern(
        IReadOnlyList<StudioRgb> palette,
        double u,
        double v,
        StudioGenerateConfig config,
        double seconds)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0) return StudioRgb.Black;
        if (palette.Count == 1) return palette[0];

        var scalar = PatternScalar(u, v, config.Pattern);
        if (config.PatternSpeed > 0) scalar = -scalar;

        var time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        var value = scalar * palette.Count * config.PatternSpread +
            Math.Abs(config.PatternSpeed) * (time / 90);
        if (!double.IsFinite(value)) return palette[0];

        // The native phase uses 64-bit unsigned wraparound, not a positive remainder.
        var phase = (ulong)(long)Math.Truncate(value) % 100;
        var position = phase / 99d * (palette.Count - 1);
        return Interpolate(palette, position);
    }

    /// <summary>Applies one native blend mode to the sampled pixel and pattern colour.</summary>
    public static StudioRgb ApplyBlend(
        StudioGenerateBlend blend,
        StudioRgb sample,
        StudioRgb pattern,
        IReadOnlyList<StudioRgb> palette,
        double lightness)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0) return sample;

        var amount = Math.Clamp(lightness, 0, 1);
        var pick = (int)Math.Truncate(10 * amount);
        var second = palette.Count > 1 ? palette[1] : palette[0];
        return blend switch
        {
            StudioGenerateBlend.Sample => sample,
            StudioGenerateBlend.Grayscale => StudioRgb.Lerp(StudioRgb.Black, StudioRgb.White, amount),
            StudioGenerateBlend.Lerp2Colors => StudioRgb.Lerp(palette[0], second, amount),
            StudioGenerateBlend.DarkenLighten20P => Multiply(sample, amount < 0.5 ? 0.8 : 1.2),
            StudioGenerateBlend.Split2Colors => amount < 0.5 ? palette[0] : second,
            StudioGenerateBlend.Mix2Colors => palette[pick % Math.Min(2, palette.Count)],
            StudioGenerateBlend.Mix3Colors => palette[pick % Math.Min(3, palette.Count)],
            StudioGenerateBlend.LerpColors => Interpolate(palette, amount * (palette.Count - 1)),
            StudioGenerateBlend.PatternLerp => StudioRgb.Lerp(StudioRgb.Black, pattern, amount),
            StudioGenerateBlend.PatternLerpGT20 => amount >= 0.2
                ? StudioRgb.Lerp(StudioRgb.Black, pattern, amount)
                : pattern,
            StudioGenerateBlend.PatternGT20Color1 => amount < 0.2 ? palette[0] : pattern,
            StudioGenerateBlend.PatternLerpGT20Color1 => amount < 0.2
                ? palette[0]
                : StudioRgb.Lerp(StudioRgb.Black, pattern, amount),
            StudioGenerateBlend.Mix3ColorsLerp => StudioRgb.Lerp(
                StudioRgb.Black,
                palette[pick % Math.Min(3, palette.Count)],
                amount),
            StudioGenerateBlend.PatternMixColorsLerp => pick % 3 == 0
                ? palette[0]
                : StudioRgb.Lerp(StudioRgb.Black, pattern, amount),
            _ => pattern,
        };
    }

    /// <summary>Native lightness: (max channel + min channel) / 510.</summary>
    public static double Lightness(StudioRgb color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        return (max + min) / 510d;
    }

    private static StudioRgb[] BuildStops(double hue, double saturation, double lightness)
    {
        var delta = lightness <= 0.5 ? 0.2 : -0.2;
        double[] levels =
        [
            lightness,
            lightness + delta,
            1 - lightness,
            0.9,
            lightness,
            lightness - delta,
            lightness + 1,
            0.1,
        ];

        var palette = new StudioRgb[levels.Length];
        for (var index = 0; index < levels.Length; index++)
        {
            palette[index] = FromHsl(hue, saturation, Math.Clamp(levels[index], 0.05, 0.95));
        }

        return palette;
    }

    private static double PatternScalar(double u, double v, int pattern) => pattern switch
    {
        3 => 4 * (u - 0.5) * (v + 1),
        4 => 4 * (u - 0.5) * v,
        5 => (u - 0.5) * v,
        6 => 0.5 * Math.Cos(5 * (u - 0.5) * v),
        7 => u * (v - 0.5),
        8 => 0.5 * Math.Cos(5 * u * (v - 0.5)),
        9 => Math.Pow(u * u - v * v, 2),
        _ => u,
    };

    private static StudioRgb Interpolate(IReadOnlyList<StudioRgb> palette, double position)
    {
        var clamped = Math.Clamp(position, 0, palette.Count - 1);
        var index = (int)Math.Floor(clamped);
        if (index >= palette.Count - 1) return palette[palette.Count - 1];
        return StudioRgb.Lerp(palette[index], palette[index + 1], clamped - index);
    }

    private static StudioRgb Multiply(StudioRgb color, double factor) => new(
        (byte)Math.Clamp(Math.Round(color.R * factor), 0, 255),
        (byte)Math.Clamp(Math.Round(color.G * factor), 0, 255),
        (byte)Math.Clamp(Math.Round(color.B * factor), 0, 255));

    private static (double Hue, double Saturation, double Lightness) ToHsl(StudioRgb color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var lightness = (max + min) / 2;
        var delta = max - min;
        if (delta < 1e-9) return (0, 0, lightness);

        var saturation = delta / (1 - Math.Abs(2 * lightness - 1));
        double hue;
        if (max == red)
        {
            hue = ((green - blue) / delta % 6 + 6) % 6;
        }
        else if (max == green)
        {
            hue = (blue - red) / delta + 2;
        }
        else
        {
            hue = (red - green) / delta + 4;
        }

        return (hue / 6, saturation, lightness);
    }

    private static StudioRgb FromHsl(double hue, double saturation, double lightness)
    {
        var sector = (hue % 1 + 1) % 1 * 6;
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * Math.Clamp(saturation, 0, 1);
        var second = chroma * (1 - Math.Abs(sector % 2 - 1));
        var match = lightness - chroma / 2;
        var (red, green, blue) = (int)sector switch
        {
            0 => (chroma, second, 0d),
            1 => (second, chroma, 0d),
            2 => (0d, chroma, second),
            3 => (0d, second, chroma),
            4 => (second, 0d, chroma),
            _ => (chroma, 0d, second),
        };
        return new StudioRgb(ToByte(red + match), ToByte(green + match), ToByte(blue + match));
    }

    private static double Wrap(double value)
    {
        var wrapped = value - Math.Floor(value);
        return double.IsFinite(wrapped) ? wrapped : 0;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
