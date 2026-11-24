namespace OpenSynapse.Core.Lighting;

/// <summary>Liquid surfaces, drops, bubbles, mercury and caustic interference.</summary>
internal sealed class AmbientLiquidModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.liquid", "液体表面");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Mode: p.Choice("mode", "surface", "surface", "droplets", "oil", "mercury",
                "bubbles", "rain", "caustic", "ripple", "viscous", "foam"),
            Frequency: p.Number("frequency", 1.8, 0.1, 12),
            Speed: p.Number("speed", 0.18, -4, 4),
            Angle: p.Number("angle", 0, -360, 360) * Math.PI / 180,
            Scale: p.Number("scale", 1, 0.1, 4),
            Softness: p.Number("softness", 0.12, 0.005, 0.6),
            Viscosity: p.Number("viscosity", 0.5, 0, 1),
            Contrast: p.Number("contrast", 1.2, 0.2, 4),
            CenterX: p.Number("centerX", 0.5, 0, 1),
            CenterY: p.Number("centerY", 0.5, 0, 1),
            Seed: p.Integer("seed", 41, 0, 9999),
            Drops: p.Integer("drops", 7, 1, 24),
            Flow: p.Number("flow", 0.06, -2, 2),
            Gain: p.Number("gain", 1, 0, 2));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        string Mode, double Frequency, double Speed, double Angle, double Scale,
        double Softness, double Viscosity, double Contrast, double CenterX, double CenterY,
        int Seed, int Drops, double Flow, double Gain);

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
                var radius = Math.Sqrt(u * u + v * v);
                var intensity = c.Mode switch
                {
                    "surface" => Surface(u, v, t),
                    "droplets" => Droplets(x, y, t),
                    "oil" => Oil(x, y, t),
                    "mercury" => Mercury(x, y, t),
                    "bubbles" => Bubbles(x, y, t),
                    "rain" => Rain(x, y, t),
                    "caustic" => Caustic(u, v, t),
                    "ripple" => Ripple(radius, t),
                    "viscous" => Viscous(x, y, t),
                    _ => Foam(x, y, t),
                };
                intensity = Math.Pow(AmbientMath.Clamp(intensity), c.Contrast) * c.Gain;
                var color = context.Palette.Sample(AmbientMath.Wrap(
                    (u * cos + v * sin) * c.Scale + t * c.Flow + intensity * 0.18));
                context.Output[row * columns + column] =
                    StudioRgb.Lerp(StudioRgb.Black, color, intensity);
            }
        }

        private double Surface(double u, double v, double t)
        {
            var a = Math.Sin((u * Math.Cos(c.Angle) + v * Math.Sin(c.Angle))
                * c.Frequency * AmbientMath.Tau + t * c.Speed);
            var b = Math.Sin((u * Math.Cos(c.Angle + 0.9) + v * Math.Sin(c.Angle + 0.9))
                * c.Frequency * 1.37 * AmbientMath.Tau - t * c.Speed * 0.73);
            var height = 0.5 + 0.25 * (a + b);
            return AmbientMath.Smooth(0.28, 0.78, height);
        }

        private double Droplets(double x, double y, double t)
        {
            var intensity = 0d;
            for (var index = 0; index < c.Drops; index++)
            {
                var phase = AmbientMath.Wrap(t * c.Speed * 0.11
                    + AmbientMath.Hash(index, c.Seed, 3));
                var cx = AmbientMath.Hash(index, c.Seed, 11);
                var cy = AmbientMath.Hash(index, c.Seed, 23);
                var radius = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                var ring = Math.Abs(radius - (0.02 + phase * 0.42));
                var ripple = 1 - AmbientMath.Smooth(0.01, 0.01 + c.Softness, ring);
                intensity = Math.Max(intensity, ripple * (1 - phase));
            }
            return intensity;
        }

        private double Oil(double x, double y, double t)
        {
            var warp = AmbientMath.Fbm(x * 2.1 + t * c.Speed * 0.07,
                y * 1.7 - t * c.Speed * 0.05, c.Seed + 5);
            var value = AmbientMath.Fbm(
                x * c.Frequency + warp * 1.8 + t * c.Speed * 0.06,
                y * c.Frequency * 0.82 - warp * 1.2, c.Seed + 9);
            return AmbientMath.Smooth(0.2, 0.84, value);
        }

        private double Mercury(double x, double y, double t)
        {
            var value = AmbientMath.Fbm(x * c.Frequency * 1.3 + t * c.Speed * 0.1,
                y * c.Frequency * 0.9 - t * c.Speed * 0.07, c.Seed + 13);
            var ridge = 1 - Math.Abs(2 * value - 1);
            return Math.Pow(ridge, 1.6);
        }

        private double Bubbles(double x, double y, double t)
        {
            var intensity = 0d;
            for (var index = 0; index < Math.Min(c.Drops, 14); index++)
            {
                var phase = AmbientMath.Wrap(t * c.Speed * 0.16
                    + AmbientMath.Hash(index, c.Seed, 31));
                var bx = AmbientMath.Wrap(AmbientMath.Hash(index, c.Seed, 37)
                    + Math.Sin(t * c.Speed + index) * 0.03);
                var by = 1 - phase;
                var size = 0.018 + AmbientMath.Hash(index, c.Seed, 41) * 0.035;
                var distance = Math.Sqrt((x - bx) * (x - bx) + (y - by) * (y - by));
                var spot = 1 - AmbientMath.Smooth(size, size + c.Softness, distance);
                intensity = Math.Max(intensity, spot * Math.Sin(phase * Math.PI));
            }
            return intensity;
        }

        private double Rain(double x, double y, double t)
        {
            var intensity = 0d;
            for (var index = 0; index < Math.Min(c.Drops, 18); index++)
            {
                var phase = AmbientMath.Wrap(t * c.Speed * 0.2
                    + AmbientMath.Hash(index, c.Seed, 47));
                var cx = AmbientMath.Hash(index, c.Seed, 53);
                var cy = AmbientMath.Hash(index, c.Seed, 59);
                var radius = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                var wave = Math.Abs(radius - phase * 0.38);
                intensity = Math.Max(intensity, (1 - AmbientMath.Smooth(0.01,
                    0.01 + c.Softness, wave)) * (1 - phase));
            }
            return intensity;
        }

        private double Caustic(double u, double v, double t)
        {
            var a = Math.Sin((u * c.Frequency + Math.Sin(v * c.Frequency * 1.4
                + t * c.Speed * 0.6) * 0.3) * AmbientMath.Tau);
            var b = Math.Sin((v * c.Frequency * 0.9 - Math.Sin(u * c.Frequency
                - t * c.Speed * 0.5) * 0.3) * AmbientMath.Tau);
            return 0.5 + 0.5 * a * b;
        }

        private double Ripple(double radius, double t)
        {
            var value = 0.5 + 0.5 * Math.Sin(
                (radius * c.Frequency - t * c.Speed) * AmbientMath.Tau);
            return AmbientMath.Smooth(0.5 - c.Softness, 0.5 + c.Softness, value);
        }

        private double Viscous(double x, double y, double t)
        {
            var warp = AmbientMath.Fbm(x * 1.4 + t * c.Speed * 0.04,
                y * 1.2 - t * c.Speed * 0.03, c.Seed + 61);
            var value = AmbientMath.Fbm(x * c.Frequency * 0.7 + warp * 2.2,
                y * c.Frequency * 1.4 + warp * 1.5 - t * c.Speed * 0.05, c.Seed + 67);
            return AmbientMath.Smooth(0.15, 0.72 + c.Viscosity * 0.15, value);
        }

        private double Foam(double x, double y, double t)
        {
            var value = AmbientMath.Fbm(x * c.Frequency * 2.4 + t * c.Speed * 0.16,
                y * c.Frequency * 2.1 - t * c.Speed * 0.11, c.Seed + 71);
            return value > 0.58 - c.Softness * 0.4 ? 1 : value * 0.25;
        }
    }
}
