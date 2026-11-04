namespace OpenSynapse.Core.Lighting;

public static partial class StudioEffectRenderer
{
    private readonly record struct ChromaField(uint ConfigHash, uint MediaHash);

    private static StudioRgb[] Fill(int rows, int columns, StudioRgb color)
    {
        var frame = new StudioRgb[rows * columns];
        Array.Fill(frame, color);
        return frame;
    }

    /// <summary>
    /// Chroma's 0x692e0 gradient: integer stop frames, adjusted when two stops
    /// land on the same frame, then per-channel float accumulation truncated.
    /// </summary>
    private static StudioRgb[] GradientTable(StudioEffectParams parameters, int count) =>
        GradientTable(parameters.ColorStops, parameters.Primary, parameters.Secondary, count);

    private static StudioRgb[] GradientTable(
        IReadOnlyList<StudioColorStop> colorStops,
        StudioRgb primary,
        StudioRgb secondary,
        int count)
    {
        count = Math.Clamp(count, 1, 1_000_000);
        var source = colorStops.Count >= 2
            ? colorStops.OrderBy(stop => stop.Stop).ToArray()
            : new[] { new StudioColorStop(0, primary), new StudioColorStop(100, secondary) };
        if (source[0].Stop != 0)
        {
            source = new[] { new StudioColorStop(0, source[0].Color) }.Concat(source).ToArray();
        }

        if (source[^1].Stop != 100)
        {
            source = source.Append(new StudioColorStop(100, source[^1].Color)).ToArray();
        }

        var stops = source.Select(stop => stop.Stop).ToArray();
        for (var index = 0; index < stops.Length - 1; index++)
        {
            var start = count * stops[index] / 100;
            if (stops[index] == 100) break;
            if (start >= count * stops[index + 1] / 100)
            {
                stops[index + 1] = Math.Min(100, (int)((start + 1) * 100d / count + 0.5));
            }
        }

        var table = new StudioRgb[count];
        Array.Fill(table, StudioRgb.Black);
        for (var index = 0; index < stops.Length - 1; index++)
        {
            if (stops[index] == 100) break;
            var start = count * stops[index] / 100;
            var end = count * stops[index + 1] / 100;
            if (start >= count) break;
            table[start] = source[index].Color;
            if (start + 1 >= end) continue;

            table[end - 1] = source[index + 1].Color;
            // 0x692e0 accumulates float deltas and truncates each channel;
            // rounding here shifts the palette by one on half-step frames.
            var first = table[start];
            var last = table[end - 1];
            var span = end - 1 - start;
            var red = (float)first.R;
            var green = (float)first.G;
            var blue = (float)first.B;
            var redStep = (last.R - first.R) / (float)span;
            var greenStep = (last.G - first.G) / (float)span;
            var blueStep = (last.B - first.B) / (float)span;
            for (var position = start + 1; position < end - 1; position++)
            {
                red += redStep;
                green += greenStep;
                blue += blueStep;
                table[position] = new StudioRgb(
                    (byte)Math.Clamp((int)red, 0, 255),
                    (byte)Math.Clamp((int)green, 0, 255),
                    (byte)Math.Clamp((int)blue, 0, 255));
            }

            if (stops[index + 1] == 100)
            {
                table[0] = source[0].Color;
                table[^1] = source[index + 1].Color;
                break;
            }
        }

        return table;
    }

    private static int PaletteStopCount(StudioEffectParams parameters)
    {
        if (parameters.ColorStops.Count == 0) return 2;
        var count = parameters.ColorStops.Count;
        if (!parameters.ColorStops.Any(stop => stop.Stop == 0)) count++;
        if (!parameters.ColorStops.Any(stop => stop.Stop == 100)) count++;
        return count;
    }

    private static StudioRgb Scale(StudioRgb color, double factor)
    {
        var amount = Math.Clamp(factor, 0, 1);
        return new StudioRgb(
            (byte)Math.Clamp(Math.Round(color.R * amount), 0, 255),
            (byte)Math.Clamp(Math.Round(color.G * amount), 0, 255),
            (byte)Math.Clamp(Math.Round(color.B * amount), 0, 255));
    }

    /// <summary>Native frame scaling truncates each channel instead of rounding.</summary>
    private static StudioRgb ScaleTruncated(StudioRgb color, double factor)
    {
        var amount = Math.Clamp(factor, 0, 1);
        return new StudioRgb(
            (byte)Math.Clamp((int)(color.R * amount), 0, 255),
            (byte)Math.Clamp((int)(color.G * amount), 0, 255),
            (byte)Math.Clamp((int)(color.B * amount), 0, 255));
    }

    private static StudioRgb RandomHue(long slot) =>
        StudioRgb.FromHsv(Hash(slot, 0x51ED, 0x9E37) / (double)uint.MaxValue, 1, 1);

    private static double Fraction(double value) => value - Math.Floor(value);

    /// <summary>
    /// Native palette length: one entry per 25 fps frame of the duration, but
    /// never fewer than the stop count.
    /// </summary>
    private static int PaletteFrames(StudioEffectParams parameters, int defaultDurationMs)
    {
        var duration = parameters.DurationMs > 0 ? parameters.DurationMs : defaultDurationMs;
        var frames = (int)(duration / (1000 / NativeFramesPerSecond));
        return Math.Max(Math.Max(PaletteStopCount(parameters), 2), frames);
    }

    /// <summary>Native render writes palette[0] on its first 25 fps tick.</summary>
    private static int PaletteCursor(double time, int frames)
    {
        var tick = (int)Math.Floor(time * NativeFramesPerSecond);
        return tick <= 1 ? 0 : (tick - 1) % frames;
    }

    private static uint Hash(long first, long second, long third)
    {
        unchecked
        {
            var hash = (ulong)(first * 374761393 + second * 668265263 + third * 2246822519);
            hash = (hash ^ (hash >> 13)) * 1274126177UL;
            return (uint)(hash ^ (hash >> 16));
        }
    }

    private static uint HashText(string? text)
    {
        unchecked
        {
            var hash = 2166136261u;
            if (text is null) return hash;
            foreach (var character in text)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash;
        }
    }
}
