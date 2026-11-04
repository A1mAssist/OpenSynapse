namespace OpenSynapse.Core.Lighting;

public static partial class StudioEffectRenderer
{
    private static StudioRgb[] RenderRipple(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        // CRippleEffect 0x53c60 draws concentric filled circles: the outer
        // palette color, a fractional next-color rim, the inner ring edge and
        // a black hole. The image is cropped to the device matrix.
        var frame = new StudioRgb[rows * columns];
        var geometry = MeasureRipple(parameters, rows, columns);
        var maxSide = Math.Max(rows, columns);
        // Native gradient count: longest side (at least 4) plus the ring
        // thickness. The extra short-side/2 entries are a repeated tail.
        var paletteLength = Math.Max(4, maxSide) + geometry.Thickness;
        var palette = GradientTable(parameters, paletteLength);

        var tick = (int)Math.Floor(time * NativeFramesPerSecond);
        if (tick <= 0) return frame;

        var radius = (tick - 1) * geometry.Velocity;
        var outer = (int)Math.Floor(radius);
        var fraction = radius - outer;
        if (outer - geometry.Thickness > geometry.MaxDistance + 1) return frame;

        PaintCircle(frame, rows, columns, geometry.CenterRow, geometry.CenterColumn, outer + 1,
            ScaleTruncated(palette[Math.Min(palette.Length - 1, outer + 1)], fraction));
        PaintCircle(frame, rows, columns, geometry.CenterRow, geometry.CenterColumn, outer,
            palette[Math.Min(palette.Length - 1, outer)]);
        PaintCircle(frame, rows, columns, geometry.CenterRow, geometry.CenterColumn,
            outer - geometry.Thickness + 1,
            ScaleTruncated(palette[Math.Min(palette.Length - 1, outer)], 1 - fraction));
        PaintCircle(frame, rows, columns, geometry.CenterRow, geometry.CenterColumn,
            outer - geometry.Thickness, StudioRgb.Black);
        return frame;
    }

    private static RippleGeometry MeasureRipple(
        StudioEffectParams parameters,
        int rows,
        int columns)
    {
        var speed = Math.Clamp(parameters.Speed, 1, 50);
        var velocity = speed / (double)NativeFramesPerSecond;
        var thickness = Math.Max(1, (int)(Math.Clamp(parameters.Width, 100, 400) / 100d));
        // Native uses the last LED index, not the matrix extent, as its center.
        var centerRow = (rows - 1) / 2;
        var centerColumn = (columns - 1) / 2;
        var maxDistance = Math.Sqrt(
            Math.Pow(Math.Max(centerRow, rows - 1 - centerRow), 2) +
            Math.Pow(Math.Max(centerColumn, columns - 1 - centerColumn), 2));
        return new RippleGeometry(
            velocity,
            thickness,
            centerRow,
            centerColumn,
            maxDistance,
            Math.Max(1, (maxDistance + thickness + 1) / speed));
    }

    private readonly record struct RippleGeometry(
        double Velocity,
        int Thickness,
        int CenterRow,
        int CenterColumn,
        double MaxDistance,
        double LifetimeSeconds);

    private static void PaintCircle(
        StudioRgb[] frame,
        int rows,
        int columns,
        int centerRow,
        int centerColumn,
        int radius,
        StudioRgb color)
    {
        if (radius < 0) return;

        // 0x54a00 fills a midpoint circle with horizontal spans. A Euclidean
        // radius test moves the diagonal edge by one pixel on large matrices.
        PaintSpan(frame, rows, columns, centerRow, centerColumn - radius, centerColumn + radius, color);
        var x = 0;
        var y = radius;
        var decision = 1 - radius;
        while (x < y)
        {
            if (decision < 0)
            {
                decision += 2 * x + 3;
            }
            else
            {
                decision += 2 * (x - y) + 5;
                y--;
            }

            x++;
            PaintSpan(frame, rows, columns, centerRow + y, centerColumn - x, centerColumn + x, color);
            PaintSpan(frame, rows, columns, centerRow - y, centerColumn - x, centerColumn + x, color);
            PaintSpan(frame, rows, columns, centerRow + x, centerColumn - y, centerColumn + y, color);
            PaintSpan(frame, rows, columns, centerRow - x, centerColumn - y, centerColumn + y, color);
        }
    }

    private static void PaintSpan(
        StudioRgb[] frame,
        int rows,
        int columns,
        int row,
        int start,
        int end,
        StudioRgb color)
    {
        if (row < 0 || row >= rows) return;
        if (start < 0) start = 0;
        if (end >= columns) end = columns - 1;
        for (var column = start; column <= end; column++)
        {
            frame[row * columns + column] = color;
        }
    }

    private static StudioRgb[] RenderWheel(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var frame = new StudioRgb[rows * columns];
        var speed = Math.Clamp(parameters.Speed, 0, 360);

        // 0x66b40: one 1024-entry wheel, advanced one tick per 40 ms frame.
        // Speed 0 keeps the initial phase instead of blanking the effect.
        var steps = MeasureWheelSteps(parameters);
        var ticks = Math.Max(0, (int)Math.Floor(time * 25) - 1);
        var phase = speed <= 0 || steps <= 0
            ? 0
            : parameters.Counterclockwise
                ? 1024d - ticks * 1024d / steps
                : ticks * 1024d / steps;
        var hasCenter = parameters.CenterRow >= 0 && parameters.CenterCol >= 0;
        var centerRow = hasCenter
            ? Math.Clamp(parameters.CenterRow, 0, StudioEffectParams.WheelCenterMaximum)
            : (rows - 1) / 2d;
        var centerColumn = hasCenter
            ? Math.Clamp(parameters.CenterCol, 0, StudioEffectParams.WheelCenterMaximum)
            : (columns - 1) / 2d;
        var wheel = GradientTable(parameters, 1024);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var angle = Math.Atan2(row - centerRow, column - centerColumn);
                var position = (int)(1024 * (angle + Math.PI) / (Math.PI * 2));
                var index = (int)((position + phase) % 1024);
                if (index < 0) index += 1024;
                frame[row * columns + column] = wheel[index];
            }
        }

        return frame;
    }

    private static int MeasureWheelSteps(StudioEffectParams parameters)
    {
        var speed = Math.Clamp(parameters.Speed, 0, 360);
        return speed <= 0 ? 0 : (int)((360_000d / speed) / 40d);
    }

    private static StudioRgb[] RenderTidal(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var frame = new StudioRgb[rows * columns];
        var geometry = MeasureTidal(parameters, rows, columns);
        var width = geometry.Width;
        var travel = geometry.Travel;

        // 0x673b0: three wave slices offset by one palette width, advancing
        // one unit per 25 fps tick and recycling before the oldest slot.
        var offsets = new[] { 0d, -width, -2d * width };
        var ticks = (int)Math.Floor(time * NativeFramesPerSecond) % (3 * width + 1);
        for (var tick = 0; tick < ticks; tick++)
        {
            for (var wave = 0; wave < offsets.Length; wave++)
            {
                offsets[wave] += 1;
                if (offsets[wave] - width > travel)
                {
                    offsets[wave] = offsets.Min() - width;
                }
            }
        }

        var palettes = new StudioRgb[3][];
        var shared = parameters.RandomColor ? null : GradientTable(parameters, width);
        for (var wave = 0; wave < palettes.Length; wave++)
        {
            if (shared is not null)
            {
                palettes[wave] = shared;
                continue;
            }

            var random = new StudioEffectParams
            {
                Primary = RandomHue(Hash(19, wave, 0x51ED)),
                Secondary = RandomHue(Hash(19, wave, 0x9E37)),
            };
            palettes[wave] = GradientTable(random, width);
        }

        var reverse = parameters.Counterclockwise;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var distance = Math.Abs(
                    (column - geometry.CenterColumn) * geometry.ColumnFactor +
                    (row - geometry.CenterRow) * geometry.RowFactor);
                var position = reverse ? travel - distance : distance;
                for (var wave = 0; wave < offsets.Length; wave++)
                {
                    var offset = offsets[wave];
                    if (offset <= position || position < offset - width) continue;
                    var index = reverse
                        ? (int)(position - offset + width)
                        : (int)(offset - position - 1);
                    // 0x68300 bounds the palette index by the travel count, not
                    // by the allocated palette length.
                    if (index >= 0 && index < travel)
                    {
                        frame[row * columns + column] = palettes[wave][index];
                    }

                    break;
                }
            }
        }

        return frame;
    }

    private static TidalGeometry MeasureTidal(
        StudioEffectParams parameters,
        int rows,
        int columns)
    {
        var centerRow = (int)Math.Round(
            Math.Clamp(parameters.CenterPointY, 0, 10_000) / 10_000d * (rows - 1));
        var centerColumn = (int)Math.Round(
            Math.Clamp(parameters.CenterPointX, 0, 10_000) / 10_000d * (columns - 1));
        var extentRows = Math.Max(Math.Abs(centerRow), Math.Abs(rows - centerRow));
        var extentColumns = Math.Max(Math.Abs(centerColumn), Math.Abs(columns - centerColumn));
        var angle = parameters.Angle * Math.PI / 180d;
        var sine = Math.Sin(angle);
        var cosine = Math.Cos(angle);
        var extent = Math.Max(
            1,
            Math.Abs((int)(sine * extentRows)) + Math.Abs((int)(cosine * extentColumns)));
        var speed = Math.Clamp(parameters.Speed, 0, 50);
        var step = speed == 0 ? 1d / NativeFramesPerSecond : 2 * speed / 100d * extent / NativeFramesPerSecond;
        // Native truncates a float quotient that lands just under the integer;
        // double division rounds 125.0 exactly, so bias only exact-integer cases.
        var quotient = extent / step;
        var travel = Math.Max(1, (int)(speed > 0 && Math.Abs(quotient - Math.Round(quotient)) < 1e-9
            ? quotient - 1e-9
            : quotient));
        var width = Math.Clamp(
            Math.Max(
                (int)(travel * Math.Max(parameters.Width, 10) / 100d),
                Math.Max(parameters.ColorStops.Count, 2)),
            2,
            8192);
        var rowFactor = travel * sine / Math.Max(extentRows, rows);
        var columnFactor = travel * cosine / Math.Max(extentColumns, columns);
        return new TidalGeometry(
            centerRow, centerColumn, travel, width, rowFactor, columnFactor);
    }

    private readonly record struct TidalGeometry(
        int CenterRow,
        int CenterColumn,
        int Travel,
        int Width,
        double RowFactor,
        double ColumnFactor);

    private static StudioRgb[] RenderRadiate(
        StudioEffectParams parameters,
        int rows,
        int columns)
    {
        // 0x69f80 takes its color per column, not per radius. The oracle's
        // level-100 input fills the 3x3 crop, so the preview is the raw table.
        var palette = GradientTable(parameters, Math.Max(rows, columns) + 1);
        var frame = new StudioRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                frame[row * columns + column] = palette[Math.Min(column, palette.Length - 1)];
            }
        }

        return frame;
    }
}
