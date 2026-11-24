namespace OpenSynapse.Core.Lighting;

/// <summary>Analytic particle fields; particle state is derived from time and index, never stored.</summary>
internal sealed class AmbientParticlesModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.particles", "环境粒子");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Mode: p.Choice("mode", "stars", "stars", "twinkle", "rain", "snow", "embers",
                "bubbles", "fireworks", "orbit", "meteors", "code", "confetti", "sparks",
                "dots", "fireflies", "splash", "pixels", "bounce"),
            Count: p.Integer("count", 40, 1, 160),
            Speed: p.Number("speed", 1, 0, 6),
            Direction: p.Number("direction", 270, -360, 360) * Math.PI / 180,
            Gravity: p.Number("gravity", 0, 0, 6),
            Wind: p.Number("wind", 0, -3, 3),
            Size: p.Number("size", 1.4, 0.2, 8),
            Tail: p.Number("tail", 0.35, 0, 1),
            Twinkle: p.Number("twinkle", 0.5, 0, 1),
            Spread: p.Number("spread", 1, 0.1, 3),
            Period: p.Number("period", 2.6, 0.2, 20),
            CenterX: p.Number("centerX", 0.5, 0, 1),
            CenterY: p.Number("centerY", 0.5, 0, 1),
            ColorMode: p.Choice("colorMode", "palette", "palette", "random", "mono"),
            Background: p.Number("background", 0, 0, 1),
            Gain: p.Number("gain", 1, 0, 3));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        string Mode, int Count, double Speed, double Direction, double Gravity, double Wind,
        double Size, double Tail, double Twinkle, double Spread, double Period,
        double CenterX, double CenterY, string ColorMode, double Background, double Gain);

    private sealed class Layer(Config c) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            AmbientPaint.Clear(context.Output);
            var t = double.IsFinite(context.Seconds) ? context.Seconds : 0;
            AmbientPaint.Background(context, c.Background, 0.035);

            for (var index = 0; index < c.Count; index++)
            {
                var h1 = AmbientMath.Hash(index, 0, 11);
                var h2 = AmbientMath.Hash(index, 1, 23);
                var h3 = AmbientMath.Hash(index, 2, 37);
                var color = ColorAt(context, h1, h2, h3, t);
                switch (c.Mode)
                {
                    case "stars":
                    {
                        var x = h1 + Math.Sin(t * 0.17 + h2 * AmbientMath.Tau) * 0.012;
                        var y = h2 + Math.Cos(t * 0.11 + h1 * AmbientMath.Tau) * 0.008;
                        var blink = 1 - c.Twinkle +
                            c.Twinkle * (0.5 + 0.5 * Math.Sin(t * (0.8 + h3 * 2.2) + h2 * AmbientMath.Tau));
                        AmbientPaint.Spot(context, x, y, color, blink * c.Gain, c.Size * (0.5 + h3 * 0.8));
                        break;
                    }
                    case "twinkle":
                    {
                        var x = h1;
                        var y = h2;
                        var pulse = Math.Pow(0.5 + 0.5 *
                            Math.Sin(t * (1.2 + h1 * 3) + h2 * AmbientMath.Tau), 6);
                        AmbientPaint.Spot(context, x, y, color, pulse * c.Gain, c.Size * 0.55);
                        AmbientPaint.Spot(context, x, y, color, pulse * c.Gain * 0.22, c.Size * 2.1);
                        break;
                    }
                    case "rain":
                    {
                        var phase = AmbientMath.Wrap(h2 + t * c.Speed * 0.35);
                        var x = AmbientMath.Wrap(h1 + c.Wind * phase * 0.06);
                        var y = phase;
                        AmbientPaint.Segment(context, x, y - c.Tail * 0.12, x, y, color,
                            (0.35 + 0.65 * h3) * c.Gain, c.Size * 0.5, 0.6);
                        break;
                    }
                    case "snow":
                    {
                        var x = AmbientMath.Wrap(h1 + Math.Sin(t * (0.35 + h3 * 0.8)
                            + h2 * AmbientMath.Tau) * 0.035 + c.Wind * t * 0.012);
                        var y = AmbientMath.Wrap(h2 + t * c.Speed * 0.1);
                        var fade = 0.55 + 0.45 * (0.5 + 0.5 * Math.Sin(t * 1.1 + h1 * AmbientMath.Tau));
                        AmbientPaint.Spot(context, x, y, color, fade * c.Gain, c.Size * (0.45 + h3 * 0.75));
                        break;
                    }
                    case "embers":
                    {
                        var x = AmbientMath.Wrap(h1 + Math.Sin(t * (0.7 + h2)
                            + h3 * AmbientMath.Tau) * 0.045 + c.Wind * t * 0.01);
                        var y = AmbientMath.Wrap(h2 - t * c.Speed * 0.12);
                        var fade = 0.3 + 0.7 * Math.Pow(0.5 + 0.5 *
                            Math.Sin(t * (1.4 + h1 * 2.4) + h2 * AmbientMath.Tau), 2);
                        AmbientPaint.Segment(context, x, y + 0.035, x, y, color,
                            fade * c.Gain, c.Size * 0.45, 0.7);
                        break;
                    }
                    case "bubbles":
                    {
                        var x = AmbientMath.Wrap(h1 + Math.Sin(t * (0.45 + h3) + h1 * AmbientMath.Tau) * 0.035);
                        var y = AmbientMath.Wrap(h2 - t * c.Speed * 0.09);
                        var fade = 0.4 + 0.35 * (0.5 + 0.5 * Math.Sin(t * 1.3 + h2 * AmbientMath.Tau));
                        AmbientPaint.Spot(context, x, y, color, fade * c.Gain, c.Size * (0.7 + h3 * 0.9));
                        break;
                    }
                    case "fireworks":
                    {
                        var groups = Math.Max(1, c.Count / 10);
                        var group = index % groups;
                        var centerX = 0.15 + 0.7 * AmbientMath.Hash(group, 9, 17);
                        var centerY = 0.15 + 0.45 * AmbientMath.Hash(group, 10, 19);
                        var phase = AmbientMath.Wrap(t / c.Period + AmbientMath.Hash(group, 11, 23));
                        var angle = h1 * AmbientMath.Tau;
                        var distance = c.Spread * 0.22 * (1 - Math.Pow(1 - phase, 2));
                        var x = centerX + Math.Cos(angle) * distance;
                        var y = centerY + Math.Sin(angle) * distance * 1.4 +
                            c.Gravity * phase * phase * 0.06;
                        var fade = Math.Pow(1 - phase, 1.2) * c.Gain;
                        var tailDistance = Math.Max(0, distance - c.Tail * 0.12);
                        AmbientPaint.Segment(context,
                            centerX + Math.Cos(angle) * tailDistance,
                            centerY + Math.Sin(angle) * tailDistance * 1.4,
                            x, y, color, fade, c.Size * 0.4, 0.55);
                        if (index < groups && phase < 0.12)
                            AmbientPaint.Spot(context, centerX, centerY, color,
                                (1 - phase / 0.12) * 0.8 * c.Gain, c.Size * 1.8);
                        break;
                    }
                    case "orbit":
                    {
                        var angle = t * c.Speed * (0.55 + h3 * 0.6) + h1 * AmbientMath.Tau;
                        var radius = c.Spread * (0.14 + 0.34 * h2);
                        var x = c.CenterX + Math.Cos(angle) * radius;
                        var y = c.CenterY + Math.Sin(angle) * radius * 0.55;
                        var previous = angle - 0.1 - c.Tail * 0.3;
                        AmbientPaint.Segment(context,
                            c.CenterX + Math.Cos(previous) * radius,
                            c.CenterY + Math.Sin(previous) * radius * 0.55,
                            x, y, color, 0.8 * c.Gain, c.Size * 0.5, 0.5);
                        AmbientPaint.Spot(context, x, y, color, c.Gain, c.Size * 0.55);
                        break;
                    }
                    case "meteors":
                    {
                        var phase = AmbientMath.Wrap(t / c.Period + h2);
                        var dx = Math.Cos(c.Direction);
                        var dy = -Math.Sin(c.Direction);
                        var x = -0.25 + h1 * 1.5 + phase * 1.5 * dx;
                        var y = -0.1 + phase * 1.2 * dy;
                        var fade = Math.Sin(phase * Math.PI);
                        AmbientPaint.Segment(context, x - c.Tail * 0.3 * dx, y - c.Tail * 0.3 * dy,
                            x, y, color, fade * c.Gain, c.Size * 0.5, 0.8);
                        break;
                    }
                    case "code":
                    {
                        var count = Math.Max(1, c.Count);
                        var x = (index + 0.5) / count;
                        var head = AmbientMath.Wrap(h2 + t * c.Speed * (0.15 + h1 * 0.3));
                        var column = Math.Clamp((int)(x * context.Columns), 0, context.Columns - 1);
                        for (var trail = 0; trail < 6; trail++)
                        {
                            var y = head - trail * 0.045;
                            if (y < 0) y += 1;
                            var row = Math.Clamp((int)(y * context.Rows), 0, context.Rows - 1);
                            var fade = Math.Pow(1 - trail / 6d, 1.7);
                            AmbientPaint.Put(context.Output, context.Rows, context.Columns,
                                row, column, color, fade * c.Gain);
                        }

                        break;
                    }
                    case "confetti":
                    {
                        var x = AmbientMath.Wrap(h1 + Math.Sin(t * (0.8 + h3)
                            + h2 * AmbientMath.Tau) * 0.06 + c.Wind * t * 0.02);
                        var y = AmbientMath.Wrap(h2 + t * c.Speed * 0.14);
                        var fade = 0.6 + 0.4 * (0.5 + 0.5 * Math.Sin(t * (1.7 + h1) + h3 * AmbientMath.Tau));
                        AmbientPaint.Spot(context, x, y, color, fade * c.Gain, c.Size * (0.5 + h3 * 0.7));
                        break;
                    }
                    case "sparks":
                    {
                        var phase = AmbientMath.Wrap(t / c.Period + h2);
                        var angle = h1 * AmbientMath.Tau + h3 * 0.4;
                        var distance = c.Spread * 0.28 * phase;
                        var tailDistance = Math.Max(0, distance - c.Tail * 0.1);
                        AmbientPaint.Segment(context,
                            c.CenterX + Math.Cos(angle) * tailDistance,
                            c.CenterY + Math.Sin(angle) * tailDistance * 1.3,
                            c.CenterX + Math.Cos(angle) * distance,
                            c.CenterY + Math.Sin(angle) * distance * 1.3,
                            color, Math.Pow(1 - phase, 1.4) * c.Gain, c.Size * 0.4, 0.5);
                        break;
                    }
                    case "dots":
                    {
                        var pulse = 0.35 + 0.65 * (0.5 + 0.5 *
                            Math.Sin(t * (0.5 + h3 * 1.5) + h1 * AmbientMath.Tau));
                        AmbientPaint.Spot(context, h1, h2, color, pulse * c.Gain,
                            c.Size * (0.45 + h3 * 0.65));
                        break;
                    }
                    case "fireflies":
                    {
                        var x = 0.5 + 0.45 * Math.Sin(t * (0.25 + h1 * 0.4) + h2 * AmbientMath.Tau);
                        var y = 0.5 + 0.4 * Math.Sin(t * (0.19 + h3 * 0.35) + h1 * AmbientMath.Tau);
                        var pulse = Math.Pow(0.5 + 0.5 *
                            Math.Sin(t * (1.3 + h2 * 2) + h3 * AmbientMath.Tau), 3);
                        AmbientPaint.Spot(context, x, y, color, pulse * c.Gain, c.Size * 0.6);
                        break;
                    }
                    case "splash":
                    {
                        var phase = AmbientMath.Wrap(t / c.Period + h2);
                        var angle = -Math.PI / 2 + (h1 - 0.5) * 1.6;
                        var velocityX = Math.Cos(angle) * 0.35;
                        var velocityY = Math.Sin(angle) * 0.55;
                        var x = c.CenterX + velocityX * phase;
                        var y = c.CenterY + velocityY * phase + 0.5 * c.Gravity * phase * phase;
                        AmbientPaint.Segment(context, x - velocityX * 0.03, y - velocityY * 0.03,
                            x, y, color, (1 - phase) * c.Gain, c.Size * 0.4, 0.5);
                        break;
                    }
                    case "pixels":
                    {
                        var x = AmbientMath.Wrap(h1 + t * c.Speed * 0.12);
                        var y = h2;
                        var fade = 0.55 + 0.45 * (0.5 + 0.5 *
                            Math.Sin(t * (0.8 + h3) + h1 * AmbientMath.Tau));
                        AmbientPaint.Segment(context, x - 0.045, y, x, y, color,
                            fade * c.Gain, c.Size * 0.45, 0.3);
                        break;
                    }
                    case "bounce":
                    {
                        var x = 0.1 + 0.8 * h1;
                        var phase = AmbientMath.Wrap(t * c.Speed * 0.45 + h2);
                        var height = 0.35 + 0.35 * h3;
                        var y = 0.88 - Math.Abs(Math.Sin(phase * Math.PI)) * height;
                        AmbientPaint.Segment(context, x, y + 0.08, x, y, color,
                            0.25 * c.Gain, c.Size * 0.35, 0.4);
                        AmbientPaint.Spot(context, x, y, color, c.Gain, c.Size * (0.65 + h3 * 0.5));
                        break;
                    }
                }
            }
        }

        private StudioRgb ColorAt(in RenderModuleContext context, double h1, double h2,
            double h3, double t) => c.ColorMode switch
        {
            "mono" => context.Palette.Sample(0.5),
            "random" => context.Palette.Sample(h3),
            _ => context.Palette.Sample(AmbientMath.Wrap(h1 * 0.65 + h2 * 0.25 + t * 0.015)),
        };
    }
}
