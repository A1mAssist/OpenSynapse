namespace OpenSynapse.Core.Lighting;

/// <summary>Deterministic flow fields: smoke, ink, dunes, caustics, wind and nebulas.</summary>
internal sealed class AmbientFlowModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.flow", "流场幻境");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Mode: p.Choice("mode", "ink", "ink", "smoke", "dunes", "caustic", "wind",
                "melt", "nebula", "aurora", "current", "plasma"),
            Frequency: p.Number("frequency", 2.2, 0.1, 12),
            Speed: p.Number("speed", 0.18, -4, 4),
            Angle: p.Number("angle", 0, -360, 360) * Math.PI / 180,
            Warp: p.Number("warp", 0.8, 0, 4),
            Scale: p.Number("scale", 1, 0.1, 4),
            Contrast: p.Number("contrast", 1.2, 0.2, 4),
            Softness: p.Number("softness", 0.25, 0.01, 0.8),
            Flow: p.Number("flow", 0.1, -2, 2),
            CenterX: p.Number("centerX", 0.5, 0, 1),
            CenterY: p.Number("centerY", 0.5, 0, 1),
            Seed: p.Integer("seed", 17, 0, 9999),
            Gain: p.Number("gain", 1, 0, 2));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        string Mode, double Frequency, double Speed, double Angle, double Warp, double Scale,
        double Contrast, double Softness, double Flow, double CenterX, double CenterY,
        int Seed, double Gain);

    private sealed class Layer(Config c) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            AmbientPaint.Clear(context.Output);
            var t = double.IsFinite(context.Seconds) ? Math.Max(0, context.Seconds) : 0;
            var columns = context.Columns;
            var rows = context.Rows;
            var aspect = columns / (double)rows;
            var cos = Math.Cos(c.Angle);
            var sin = Math.Sin(c.Angle);
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var x = (column + 0.5) / columns;
                var y = (row + 0.5) / rows;
                var u = (x - c.CenterX) * aspect;
                var v = y - c.CenterY;
                var wx = (AmbientMath.Fbm(x * c.Frequency + t * c.Speed * 0.17,
                    y * c.Frequency - t * c.Speed * 0.11, c.Seed) - 0.5) * c.Warp;
                var wy = (AmbientMath.Fbm(x * c.Frequency - t * c.Speed * 0.13 + 11.7,
                    y * c.Frequency + t * c.Speed * 0.09 + 5.3, c.Seed + 19) - 0.5) * c.Warp;
                var coordinate = (u * cos + v * sin) * c.Frequency + t * c.Speed;
                var intensity = c.Mode switch
                {
                    "ink" => Ink(x, y, t, wx, wy),
                    "smoke" => Smoke(x, y, t, wx, wy),
                    "dunes" => Dunes(u, v, t, wx, wy),
                    "caustic" => Caustic(u, v, t),
                    "wind" => Wind(x, y, t, wx, wy),
                    "melt" => Melt(x, y, t, wx, wy),
                    "nebula" => Nebula(x, y, u, v, t),
                    "aurora" => Aurora(x, y, t, wx, wy),
                    "current" => Current(u, v, t),
                    _ => Plasma(u, v, t),
                };
                intensity = Math.Pow(AmbientMath.Clamp(intensity), c.Contrast) * c.Gain;
                var color = context.Palette.Sample(AmbientMath.Wrap(
                    coordinate * c.Scale + t * c.Flow + intensity * 0.16));
                context.Output[row * columns + column] =
                    StudioRgb.Lerp(StudioRgb.Black, color, intensity);
            }
        }

        private double Ink(double x, double y, double t, double wx, double wy)
        {
            var value = AmbientMath.Fbm(
                x * c.Frequency * 0.9 + wx * 2.2 + t * c.Speed * 0.13,
                y * c.Frequency * 1.15 + wy * 2.2 - t * c.Speed * 0.07, c.Seed + 3);
            return AmbientMath.Smooth(0.2, 0.82 - c.Softness * 0.12, value);
        }

        private double Smoke(double x, double y, double t, double wx, double wy)
        {
            var value = AmbientMath.Fbm(
                x * c.Frequency * 1.2 + wx * 1.8 + t * c.Speed * 0.18,
                y * c.Frequency * 1.4 + wy * 1.8 - t * c.Speed * 0.12, c.Seed + 7);
            return Math.Pow(value, 1.1 + c.Softness * 1.2);
        }

        private double Dunes(double u, double v, double t, double wx, double wy)
        {
            var ridge = 0.5 + 0.5 * Math.Sin(
                (u * Math.Cos(c.Angle) + v * Math.Sin(c.Angle)) * c.Frequency
                * AmbientMath.Tau + wx * 3.5 + wy * 1.4 + t * c.Speed);
            return Math.Pow(ridge, 1.5);
        }

        private double Caustic(double u, double v, double t)
        {
            var a = Math.Sin((u * c.Frequency + Math.Sin(v * c.Frequency * 1.3
                + t * c.Speed * 0.7) * 0.35) * AmbientMath.Tau);
            var b = Math.Sin((v * c.Frequency - Math.Sin(u * c.Frequency * 0.81
                - t * c.Speed * 0.5) * 0.35) * AmbientMath.Tau);
            return 0.5 + 0.5 * a * b;
        }

        private double Wind(double x, double y, double t, double wx, double wy)
        {
            var value = AmbientMath.Fbm(
                x * c.Frequency * 0.65 + wx * 1.4 + t * c.Speed * 0.7,
                y * c.Frequency * 0.95 + wy * 1.4 + t * c.Speed * 0.2, c.Seed + 11);
            return Math.Pow(value, 1.1);
        }

        private double Melt(double x, double y, double t, double wx, double wy)
        {
            var value = AmbientMath.Fbm(
                x * c.Frequency * 0.8 + Math.Sin(y * 7 + t * c.Speed) * 0.17 + wx,
                y * c.Frequency * 1.55 + wy * 1.7 - t * c.Speed * 0.06, c.Seed + 13);
            return AmbientMath.Smooth(0.18, 0.78 + c.Softness * 0.08, value);
        }

        private double Nebula(double x, double y, double u, double v, double t)
        {
            var value = AmbientMath.Fbm(x * c.Frequency + t * c.Speed * 0.09,
                y * c.Frequency - t * c.Speed * 0.06, c.Seed + 17);
            var halo = Math.Exp(-Math.Pow(Math.Sqrt(u * u + v * v) / 0.7, 2));
            return Math.Pow(value, 1.25) + halo * 0.18;
        }

        private double Aurora(double x, double y, double t, double wx, double wy)
        {
            var band = 0.5 + 0.5 * Math.Sin((x * c.Frequency + Math.Sin(y * 5
                + t * c.Speed) * 0.18 + t * c.Speed + wx) * AmbientMath.Tau);
            var veil = AmbientMath.Fbm(x * 2.7 + t * c.Speed * 0.13,
                y * 1.3 + wy, c.Seed + 23);
            return band * (0.35 + 0.65 * veil);
        }

        private double Current(double u, double v, double t)
        {
            var a = Math.Sin((u * c.Frequency + v * 0.3 + t * c.Speed) * AmbientMath.Tau);
            var b = Math.Sin((u * 0.4 - v * c.Frequency * 0.8
                - t * c.Speed * 0.73) * AmbientMath.Tau);
            return 0.5 + 0.25 * (a + b);
        }

        private double Plasma(double u, double v, double t)
        {
            var value = Math.Sin((u * c.Frequency + t * c.Speed) * AmbientMath.Tau)
                + Math.Sin((v * c.Frequency * 0.83 - t * c.Speed * 0.7) * AmbientMath.Tau)
                + Math.Sin(((u + v) * c.Frequency * 1.17 + t * c.Speed * 0.5)
                    * AmbientMath.Tau);
            return 0.5 + value / 6;
        }
    }
}
