using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public static partial class QuickLightingEngine
{
    private static byte Average(long total, int count) =>
        checked((byte)((total + count / 2) / count));

    private static RazerRgb RenderSpectrumColor(double position, double blueStop = 0.66)
    {
        ReadOnlySpan<double> stops = [0, 0.33, blueStop, 1];
        ReadOnlySpan<RazerRgb> colors =
        [
            new(255, 0, 0),
            new(0, 255, 0),
            new(0, 0, 255),
            new(255, 0, 0),
        ];
        for (var index = 0; index < stops.Length - 1; index++)
        {
            if (position > stops[index + 1])
            {
                continue;
            }
            var factor = (position - stops[index]) / (stops[index + 1] - stops[index]);
            return LerpColor(colors[index], colors[index + 1], factor);
        }

        return colors[^1];
    }

    private static RazerRgb RenderAudioMeterColor(double position)
    {
        var scaled = Math.Clamp(position, 0, 1) * 255;
        return scaled <= 127.5
            ? new RazerRgb(Scale(255, scaled / 127.5), 255, 0)
            : new RazerRgb(255, Scale(255, (255 - scaled) / 127.5), 0);
    }

    private static RazerRgb RenderTidalColor(
        double position,
        RazerRgb firstColor,
        RazerRgb secondColor)
    {
        ReadOnlySpan<double> stops = [0, 0.10, 0.25, 0.40, 0.60, 0.75, 0.90, 1];
        ReadOnlySpan<RazerRgb> colors =
        [
            default, default, firstColor, default,
            default, secondColor, default, default,
        ];
        for (var index = 0; index < stops.Length - 1; index++)
        {
            if (position <= stops[index + 1])
            {
                return LerpColor(
                    colors[index],
                    colors[index + 1],
                    (position - stops[index]) / (stops[index + 1] - stops[index]));
            }
        }
        return default;
    }

    private static int PositiveModulo(long value, int divisor)
    {
        var remainder = value % divisor;
        return checked((int)(remainder < 0 ? remainder + divisor : remainder));
    }

    private static RazerRgb LerpColor(RazerRgb start, RazerRgb end, double factor) =>
        new(
            LerpByte(start.Red, end.Red, factor),
            LerpByte(start.Green, end.Green, factor),
            LerpByte(start.Blue, end.Blue, factor));

    private static byte LerpByte(byte start, byte end, double factor) =>
        checked((byte)Math.Clamp((int)(start + (end - start) * factor), 0, 255));

    private static byte[] BuildFireKeyFrames(int seed)
    {
        var cellsPerFrame = FireWorkRows * FireWorkColumns;
        var frames = new byte[FireKeyFrameCount * cellsPerFrame];
        var randomState = unchecked((uint)seed);
        for (var frame = 0; frame < FireKeyFrameCount; frame++)
        {
            var bottom = frame * cellsPerFrame + (FireWorkRows - 1) * FireWorkColumns;
            for (var column = 0; column < FireWorkColumns; column++)
            {
                var value = NextLightingRandom(ref randomState) % 192 + 32;
                frames[bottom + column] = checked((byte)(value - FireSourceMask[column] * value / 255));
            }
        }

        for (var sourceRow = FireWorkRows - 1; sourceRow > 0; sourceRow--)
        {
            var mask = FirePropagationMasks[FireWorkRows - 1 - sourceRow];
            for (var frame = 0; frame < FireKeyFrameCount; frame++)
            {
                var source = frame * cellsPerFrame + sourceRow * FireWorkColumns;
                var destination = ((frame + 1) % FireKeyFrameCount) * cellsPerFrame +
                    (sourceRow - 1) * FireWorkColumns;
                for (var column = 0; column < FireWorkColumns; column++)
                {
                    var value = frames[source + column];
                    frames[destination + column] = checked((byte)(value - value * mask[column] / 255));
                }
            }
        }

        return frames;
    }

    private static int NextLightingRandom(ref uint state)
    {
        state = unchecked(state * 0x343FD + 0x269EC3);
        return (int)((state >> 16) & 0x7FFF);
    }

    private static RazerRgb RenderFireRamp(byte position, byte heat)
    {
        var color = position <= 128
            ? LerpColor(new RazerRgb(255, 127, 0), new RazerRgb(255, 0, 0), position / 128d)
            : LerpColor(new RazerRgb(255, 0, 0), default, (position - 128) / 128d);
        return new RazerRgb(
            checked((byte)(color.Red * heat / 255)),
            checked((byte)(color.Green * heat / 255)),
            checked((byte)(color.Blue * heat / 255)));
    }

    private static RazerRgb HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        var match = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        return new RazerRgb(
            Scale(red + match, 255),
            Scale(green + match, 255),
            Scale(blue + match, 255));
    }

    private static RazerRgb MaxColor(RazerRgb first, RazerRgb second) =>
        new(
            Math.Max(first.Red, second.Red),
            Math.Max(first.Green, second.Green),
            Math.Max(first.Blue, second.Blue));

    private static RazerRgb ScaleColor(RazerRgb color, double factor) =>
        new(Scale(color.Red, factor), Scale(color.Green, factor), Scale(color.Blue, factor));

    private static RazerRgb ScaleColorTruncated(RazerRgb color, double factor) =>
        new(
            TruncateScale(color.Red, factor),
            TruncateScale(color.Green, factor),
            TruncateScale(color.Blue, factor));

    private static byte TruncateScale(byte value, double factor) =>
        checked((byte)Math.Clamp((int)(value * factor), 0, 255));

    private static byte Scale(double value, double factor) =>
        checked((byte)Math.Clamp(Math.Round(value * factor, MidpointRounding.AwayFromZero), 0, 255));

    private static void ValidateCoordinates(int row, int column)
    {
        if ((uint)row >= LogicalRows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        if ((uint)column >= LogicalColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }
    }

    private static void ValidateMatrixDimensions(int rows, int columns)
    {
        if (rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        if (columns <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns));
        }

        _ = checked(rows * columns);
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }

    private static void ValidateElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }
    }

    private static void ValidateEventTime(TimeSpan at)
    {
        if (at < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(at));
        }
    }

    private static void ValidateNormalized(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
