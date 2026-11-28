namespace OpenSynapse.Core.Lighting;

public static partial class StudioEffectRenderer
{
    private static StudioRgb[] RenderKeyFade(
        StudioEffectParams parameters, int rows, int columns, StudioKeyState keys)
    {
        var frame = Fill(rows, columns, StudioRgb.Transparent);
        var duration = (parameters.DurationMs > 0 ? parameters.DurationMs : 1_500) / 1000d;
        var cycles = parameters.PlaybackEnd == StudioPlaybackEnd.After ? Math.Max(1, parameters.Cycles) : -1;
        var frames = PaletteFrames(parameters, 1_500);
        var palette = parameters.RandomColor ? null : GradientTable([], parameters.Primary, StudioRgb.Transparent, frames);
        // Paint oldest first: a new press restarts only its LED, never the other keys.
        for (var index = keys.Presses.Count - 1; index >= 0; index--)
        {
            var press = keys.Presses[index];
            if ((uint)press.Row >= (uint)rows || (uint)press.Column >= (uint)columns ||
                (cycles >= 0 && press.Age >= duration * cycles)) continue;
            var colors = palette ?? GradientTable([], StudioRgb.FromHsv(press.Hue, 1, 1), StudioRgb.Transparent, frames);
            frame[press.Row * columns + press.Column] = colors[PaletteCursor(press.Age, frames)];
        }
        return frame;
    }

    /// <summary>
    /// Native key-reactive visuals. Every style replays the live press history, so a
    /// new key press or release changes the next frame.
    /// </summary>
    private static StudioRgb[] RenderKeyStyle(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time,
        StudioKeyState keys)
    {
        var frame = new StudioRgb[rows * columns];
        var life = parameters.DurationMs > 0 ? parameters.DurationMs / 1000.0 : 1.5;
        var style = parameters.ReactiveStyle;
        for (var row = 0; row < rows; row++)
        {
            var y = (row + 0.5) / rows;
            for (var column = 0; column < columns; column++)
            {
                var x = (column + 0.5) / columns;
                var result = StudioRgb.Black;
                if (style is StudioReactiveStyle.Trail or StudioReactiveStyle.Heatmap)
                {
                    var heat = keys[row, column];
                    result = Scale(parameters.Resolve(Math.Clamp(heat, 0, 1)), heat);
                }
                else if (style == StudioReactiveStyle.Constellation)
                {
                    var noise = Noise(column, row, 7);
                    var twinkle = Math.Pow(Math.Max(0, Math.Sin(time * (1 + noise * 3) + noise * 50)), 8) * 0.45;
                    var heat = keys[row, column];
                    result = Scale(new StudioRgb(200, 214, 255), Math.Min(1, twinkle + heat));
                }

                for (var index = 0; index < keys.Presses.Count; index++)
                {
                    var press = keys.Presses[index];
                    if (press.Age > life) continue;
                    var pressX = (press.Column + 0.5) / Math.Max(1, keys.Columns);
                    var pressY = (press.Row + 0.5) / Math.Max(1, keys.Rows);
                    var dx = (x - pressX) * 2.2;
                    var dy = y - pressY;
                    var distance = Hypot(dx, dy);
                    var fade = Math.Max(0, 1 - press.Age / life);
                    var anchored = style == StudioReactiveStyle.Web &&
                        index + 1 < keys.Presses.Count &&
                        keys.Presses[index + 1].Age <= life;
                    var anchorX = anchored
                        ? (keys.Presses[index + 1].Column + 0.5) / Math.Max(1, keys.Columns)
                        : 0;
                    var anchorY = anchored
                        ? (keys.Presses[index + 1].Row + 0.5) / Math.Max(1, keys.Rows)
                        : 0;
                    var value = style switch
                    {
                        StudioReactiveStyle.Diamond => Ring(Math.Abs(dx) + Math.Abs(dy), press.Age * 0.9, 0.12) * fade,
                        StudioReactiveStyle.DoubleRing => Math.Max(
                            Ring(distance, press.Age * 0.9, 0.12),
                            Ring(distance, Math.Max(0, press.Age * 0.9 - 0.3), 0.1)) * fade,
                        StudioReactiveStyle.Sonar => Ring(distance, press.Age * 1.1, 0.1) *
                            (0.6 + 0.4 * Math.Sin(Math.Atan2(dy, dx) * 4 + time * 3)) * fade,
                        StudioReactiveStyle.Burst => Math.Exp(-distance / 0.25) * Math.Exp(-press.Age * 3),
                        StudioReactiveStyle.Fireworks => Fireworks(dx, dy, press.Age),
                        StudioReactiveStyle.Spotlight => Math.Exp(-Math.Pow(distance / 0.35, 2)) * fade,
                        StudioReactiveStyle.Ink => Math.Exp(-Math.Pow(distance / 0.3, 2)) *
                            (0.65 + 0.35 * Noise(column, row, (long)(press.Age * 2))) * fade,
                        StudioReactiveStyle.Comet => Math.Abs(dy) < 0.12
                            ? Math.Exp(-Math.Pow((Math.Abs(dx) - press.Age * 0.9) / 0.15, 2)) * fade
                            : 0,
                        StudioReactiveStyle.Cross => Math.Min(Math.Abs(dx), Math.Abs(dy)) < 0.12
                            ? Math.Exp(-Math.Min(Math.Abs(dx), Math.Abs(dy)) / 0.3) * fade
                            : 0,
                        StudioReactiveStyle.Ray => Math.Abs(dy) < 0.08
                            ? Math.Exp(-Math.Max(0, Math.Abs(dx) - press.Age * 0.6) / 0.25) * fade
                            : 0,
                        StudioReactiveStyle.Web => anchored
                            ? Web(x * 2.2, y, pressX * 2.2, pressY, anchorX * 2.2, anchorY, fade)
                            : 0,
                        StudioReactiveStyle.Vortex => Vortex(dx, dy, press.Age, time),
                        _ => 0,
                    };
                    if (value <= 0) continue;
                    var color = Scale(parameters.Resolve(Frac(press.Hue)), Math.Min(1, value));
                    result = new StudioRgb(
                        Math.Max(result.R, color.R),
                        Math.Max(result.G, color.G),
                        Math.Max(result.B, color.B));
                }

                frame[row * columns + column] = result;
            }
        }

        return frame;
    }

    private static double Ring(double distance, double radius, double width) =>
        Math.Exp(-Math.Pow((distance - radius) / width, 2));

    private static readonly (double X, double Y)[] FireworkDirections = BuildFireworkDirections();

    private static (double X, double Y)[] BuildFireworkDirections()
    {
        var directions = new (double, double)[14];
        for (var index = 0; index < directions.Length; index++)
        {
            var angle = index * Math.PI * 2 / directions.Length + Noise(index, 1, 3) * 0.25;
            directions[index] = (Math.Cos(angle), Math.Sin(angle));
        }

        return directions;
    }

    private static double Fireworks(double dx, double dy, double age)
    {
        var best = 0.0;
        var radius = age * 0.8;
        foreach (var (x, y) in FireworkDirections)
        {
            var distance = Hypot(dx - x * radius, dy - y * radius);
            if (distance > 0.5) continue;
            best = Math.Max(best, Math.Exp(-Math.Pow(distance / 0.12, 2)));
        }

        return best * Math.Max(0, 1 - age / 1.4);
    }

    private static double Web(
        double x,
        double y,
        double pressX,
        double pressY,
        double anchorX,
        double anchorY,
        double fade)
    {
        var distance = SegmentDistance(x, y, pressX, pressY, anchorX, anchorY);
        return distance < 0.12 ? Math.Exp(-Math.Pow(distance / 0.05, 2)) * fade : 0;
    }

    private static double SegmentDistance(
        double x,
        double y,
        double ax,
        double ay,
        double bx,
        double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var length = dx * dx + dy * dy;
        if (length <= 1e-9) return Hypot(x - ax, y - ay);
        var t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length, 0, 1);
        return Hypot(x - (ax + dx * t), y - (ay + dy * t));
    }

    private static double Vortex(double dx, double dy, double age, double time)
    {
        var angle = Math.Atan2(dy, dx) + age * 4 + time * 1.5;
        var radius = Hypot(dx, dy) - age * 0.15;
        var band = 0.5 + 0.5 * Math.Sin(angle * 3 - radius * 10);
        return Math.Pow(band, 3) * Math.Max(0, 1 - age / 1.6);
    }
}
