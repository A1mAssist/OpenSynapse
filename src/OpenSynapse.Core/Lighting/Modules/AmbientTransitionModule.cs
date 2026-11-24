namespace OpenSynapse.Core.Lighting;

/// <summary>Two-colour boundary sweeps; composited above a field to make wipes and reveals.</summary>
internal sealed class AmbientTransitionModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.transition", "环境转场");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Mode: p.Choice("mode", "wipe", "wipe", "split", "blinds", "mosaic", "iris",
                "spiral", "scan", "dissolve", "stack", "cross", "diagonal", "curtain"),
            Period: p.Number("period", 3.2, 0.3, 20),
            Direction: p.Number("direction", 0, -360, 360) * Math.PI / 180,
            Blocks: p.Integer("blocks", 4, 1, 32),
            Stagger: p.Number("stagger", 0.45, 0, 1),
            Softness: p.Number("softness", 0.055, 0.005, 0.4),
            Shift: p.Number("shift", 0.5, 0, 1),
            Gain: p.Number("gain", 1, 0, 2));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        string Mode, double Period, double Direction, int Blocks, double Stagger,
        double Softness, double Shift, double Gain);

    private sealed class Layer(Config c) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            AmbientPaint.Clear(context.Output);
            var t = double.IsFinite(context.Seconds) ? context.Seconds : 0;
            var phase = AmbientMath.Wrap(t / c.Period);
            var progress = 0.5 - 0.5 * Math.Cos(phase * AmbientMath.Tau);
            var colorA = context.Palette.Sample(0);
            var colorB = context.Palette.Sample(c.Shift);
            var edgeColor = context.Palette.Sample(AmbientMath.Wrap(c.Shift + 0.25));
            var aspect = context.Columns / (double)context.Rows;
            var cos = Math.Cos(c.Direction);
            var sin = Math.Sin(c.Direction);

            for (var row = 0; row < context.Rows; row++)
            for (var column = 0; column < context.Columns; column++)
            {
                var x = (column + 0.5) / context.Columns;
                var y = (row + 0.5) / context.Rows;
                var coordinate = (x - 0.5) * cos + (y - 0.5) * sin + 0.5;
                var color = colorA;
                var edge = 0d;
                switch (c.Mode)
                {
                    case "wipe":
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(progress - c.Softness, progress + c.Softness, coordinate));
                        break;
                    case "split":
                        var split = Math.Abs(coordinate - 0.5) * 2;
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(progress - c.Softness, progress + c.Softness, split));
                        break;
                    case "blinds":
                        var stripeCoordinate = AmbientMath.Wrap(coordinate * c.Blocks);
                        var stripe = (int)Math.Floor(coordinate * c.Blocks);
                        var stripeProgress = AmbientMath.Wrap(progress +
                            AmbientMath.Hash(stripe, 0, 31) * c.Stagger);
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(stripeProgress - c.Softness,
                                stripeProgress + c.Softness, stripeCoordinate));
                        edge = Edge(stripeCoordinate, stripeProgress, c.Softness);
                        break;
                    case "mosaic":
                        var blockX = Math.Clamp((int)(x * c.Blocks), 0, c.Blocks - 1);
                        var blockY = Math.Clamp((int)(y * c.Blocks), 0, c.Blocks - 1);
                        var threshold = AmbientMath.Hash(blockX, blockY, 43);
                        color = threshold < progress ? colorA : colorB;
                        edge = Edge(threshold, progress, c.Softness);
                        break;
                    case "iris":
                        var maxRadius = Math.Sqrt(aspect * aspect / 4 + 0.25);
                        var radius = Math.Sqrt(Math.Pow((x - 0.5) * aspect, 2) +
                            Math.Pow(y - 0.5, 2)) / maxRadius;
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(progress - c.Softness, progress + c.Softness, radius));
                        edge = Edge(radius, progress, c.Softness);
                        break;
                    case "spiral":
                        var angle = Math.Atan2(y - 0.5, (x - 0.5) * aspect) / AmbientMath.Tau;
                        var spiralRadius = Math.Sqrt(Math.Pow((x - 0.5) * aspect, 2) +
                            Math.Pow(y - 0.5, 2));
                        var spiral = AmbientMath.Wrap(angle + spiralRadius * c.Blocks);
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(progress - c.Softness, progress + c.Softness, spiral));
                        edge = Edge(spiral, progress, c.Softness);
                        break;
                    case "scan":
                        color = StudioRgb.Lerp(colorA, colorB, 0.18);
                        edge = Math.Exp(-Math.Pow((y - progress) / Math.Max(0.01, c.Softness), 2));
                        break;
                    case "dissolve":
                        var noise = AmbientMath.Fbm(x * c.Blocks * 0.7, y * c.Blocks * 0.7, 59);
                        color = noise < progress ? colorA : colorB;
                        edge = Edge(noise, progress, c.Softness);
                        break;
                    case "stack":
                        var band = (int)Math.Floor(y * c.Blocks);
                        var bandProgress = AmbientMath.Wrap(progress +
                            AmbientMath.Hash(band, 1, 61) * c.Stagger);
                        var local = AmbientMath.Wrap(y * c.Blocks);
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(bandProgress - c.Softness,
                                bandProgress + c.Softness, local));
                        edge = Edge(local, bandProgress, c.Softness);
                        break;
                    case "cross":
                        var cross = Math.Max(Math.Abs(x - 0.5) * 2, Math.Abs(y - 0.5) * 2);
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(progress - c.Softness, progress + c.Softness, cross));
                        edge = Edge(cross, progress, c.Softness);
                        break;
                    case "diagonal":
                        var diagonal = (x + y) * 0.5;
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(progress - c.Softness, progress + c.Softness, diagonal));
                        edge = Edge(diagonal, progress, c.Softness);
                        break;
                    case "curtain":
                        var curtain = AmbientMath.Wrap(x + 0.08 * Math.Sin(y * c.Blocks * AmbientMath.Tau));
                        color = StudioRgb.Lerp(colorA, colorB,
                            AmbientMath.Smooth(progress - c.Softness, progress + c.Softness, curtain));
                        edge = Edge(curtain, progress, c.Softness);
                        break;
                }

                color = StudioRgb.Lerp(color, edgeColor, AmbientMath.Clamp(edge * 0.85));
                context.Output[row * context.Columns + column] =
                    StudioRgb.Lerp(StudioRgb.Black, color, c.Gain);
            }
        }

        private static double Edge(double coordinate, double progress, double softness) =>
            Math.Exp(-Math.Pow((coordinate - progress) / Math.Max(0.008, softness), 2));
    }
}
