namespace OpenSynapse.Core.Lighting;

/// <summary>Shared deterministic math for the ambient field, particle, mask and transition modules.</summary>
internal static class AmbientMath
{
    public const double Tau = Math.PI * 2;

    public static double Clamp(double value) => Math.Clamp(value, 0, 1);

    public static double Wrap(double value) => value - Math.Floor(value);

    public static double Smooth(double edge0, double edge1, double value)
    {
        if (edge0 == edge1) return value < edge0 ? 0 : 1;
        var x = Clamp((value - edge0) / (edge1 - edge0));
        return x * x * (3 - 2 * x);
    }

    public static double Hash(int x, int y, int seed = 0)
    {
        unchecked
        {
            var value = (uint)(x * 374761393 + y * 668265263 + seed * 69069);
            value = (value ^ (value >> 13)) * 1274126177;
            return ((value ^ (value >> 16)) & 0xFFFFFF) / 16777216d;
        }
    }

    public static double Noise(double x, double y, int seed = 0)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var sx = fx * fx * (3 - 2 * fx);
        var sy = fy * fy * (3 - 2 * fy);
        var a = Hash(x0, y0, seed);
        var b = Hash(x0 + 1, y0, seed);
        var c = Hash(x0, y0 + 1, seed);
        var d = Hash(x0 + 1, y0 + 1, seed);
        return (a + (b - a) * sx) * (1 - sy) + (c + (d - c) * sx) * sy;
    }

    public static double Fbm(double x, double y, int seed = 0)
    {
        var value = 0d;
        var amplitude = 0.5;
        for (var octave = 0; octave < 4; octave++)
        {
            value += Noise(x + octave * 17.13, y - octave * 9.71, seed + octave) * amplitude;
            x = x * 2.03 + 3.17;
            y = y * 2.01 - 2.41;
            amplitude *= 0.5;
        }
        return value / 0.9375;
    }

    /// <summary>A compact heartbeat waveform with a narrow QRS spike.</summary>
    public static double Ecg(double u)
    {
        var x = Wrap(u);
        var beat = Math.Exp(-Math.Pow((x - 0.18) / 0.055, 2)) * 2.5
            - Math.Exp(-Math.Pow((x - 0.27) / 0.035, 2)) * 1.4
            + Math.Exp(-Math.Pow((x - 0.34) / 0.026, 2)) * 4.1
            - Math.Exp(-Math.Pow((x - 0.41) / 0.05, 2)) * 1.1
            + Math.Exp(-Math.Pow((x - 0.66) / 0.13, 2)) * 0.55;
        return beat;
    }

    public static double Angle(double radians)
    {
        var value = radians % Tau;
        return value < 0 ? value + Tau : value;
    }

    public static double PolarDistance(double angle, double center)
    {
        var delta = Math.Abs(Angle(angle) - Angle(center));
        return Math.Min(delta, Tau - delta);
    }
}

internal static class AmbientPaint
{
    public static void Clear(StudioRgb[] output) => Array.Clear(output);

    public static void Background(in RenderModuleContext context, double amount, double flow)
    {
        if (amount <= 0) return;
        var columns = context.Columns;
        var rows = context.Rows;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var u = (column + 0.5) / columns;
            var v = (row + 0.5) / rows;
            var color = context.Palette.Sample(AmbientMath.Wrap(u * 0.65 + v * 0.35
                + context.Seconds * flow));
            context.Output[row * columns + column] = StudioRgb.Lerp(StudioRgb.Black, color,
                AmbientMath.Clamp(amount * (0.35 + 0.65 * v)));
        }
    }

    public static void Spot(in RenderModuleContext context, double x, double y, StudioRgb color,
        double intensity, double size)
    {
        if (intensity <= 0.002 || size <= 0) return;
        var columns = context.Columns;
        var rows = context.Rows;
        var centerColumn = x * columns - 0.5;
        var centerRow = y * rows - 0.5;
        var reach = Math.Max(1, (int)Math.Ceiling(size + 1));
        var left = Math.Max(0, (int)Math.Floor(centerColumn) - reach);
        var right = Math.Min(columns - 1, (int)Math.Ceiling(centerColumn) + reach);
        var top = Math.Max(0, (int)Math.Floor(centerRow) - reach);
        var bottom = Math.Min(rows - 1, (int)Math.Ceiling(centerRow) + reach);
        for (var row = top; row <= bottom; row++)
        for (var column = left; column <= right; column++)
        {
            var dx = column - centerColumn;
            var dy = row - centerRow;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var falloff = Math.Exp(-(distance * distance) / (size * size));
            if (falloff <= 0.003) continue;
            Put(context.Output, rows, columns, row, column, color, intensity * falloff);
        }
    }

    public static void Segment(in RenderModuleContext context, double x0, double y0,
        double x1, double y1, StudioRgb color, double intensity, double width, double tail)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = Math.Max(Math.Abs(dx) * context.Columns, Math.Abs(dy) * context.Rows);
        var steps = Math.Clamp((int)Math.Ceiling(length * Math.Max(0.2, tail)), 1, 64);
        for (var step = 0; step <= steps; step++)
        {
            var t = step / (double)steps;
            Spot(context, x0 + dx * t, y0 + dy * t, color,
                intensity * (1 - t * 0.72), width);
        }
    }

    public static void Put(StudioRgb[] output, int rows, int columns, int row, int column,
        StudioRgb color, double intensity)
    {
        if ((uint)row >= (uint)rows || (uint)column >= (uint)columns) return;
        intensity = AmbientMath.Clamp(intensity);
        if (intensity <= 0.003) return;
        var index = row * columns + column;
        var add = StudioRgb.Lerp(StudioRgb.Black, color, intensity);
        var existing = output[index];
        output[index] = new StudioRgb(
            Math.Max(existing.R, add.R),
            Math.Max(existing.G, add.G),
            Math.Max(existing.B, add.B));
    }
}
