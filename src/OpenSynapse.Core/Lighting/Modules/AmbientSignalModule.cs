namespace OpenSynapse.Core.Lighting;

/// <summary>Digital signals: circuits, glitches, scans, data rain, radar and binary blocks.</summary>
internal sealed class AmbientSignalModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.signal", "数字信号");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Mode: p.Choice("mode", "circuit", "circuit", "glitch", "scan", "data",
                "matrix", "pulse", "radar", "sync", "noise", "binary"),
            Frequency: p.Number("frequency", 3, 0.1, 16),
            Speed: p.Number("speed", 0.2, -4, 4),
            Blocks: p.Integer("blocks", 6, 1, 24),
            Jitter: p.Number("jitter", 0.08, 0, 0.8),
            Softness: p.Number("softness", 0.06, 0.005, 0.5),
            Contrast: p.Number("contrast", 1.2, 0.2, 4),
            Direction: p.Number("direction", 0, -360, 360) * Math.PI / 180,
            Seed: p.Integer("seed", 71, 0, 9999),
            Gain: p.Number("gain", 1, 0, 2));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        string Mode, double Frequency, double Speed, int Blocks, double Jitter,
        double Softness, double Contrast, double Direction, int Seed, double Gain);

    private sealed class Layer(Config c) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            AmbientPaint.Clear(context.Output);
            var t = double.IsFinite(context.Seconds) ? Math.Max(0, context.Seconds) : 0;
            var columns = context.Columns;
            var rows = context.Rows;
            var aspect = columns / (double)rows;
            var cos = Math.Cos(c.Direction);
            var sin = Math.Sin(c.Direction);
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var x = (column + 0.5) / columns;
                var y = (row + 0.5) / rows;
                var u = (x - 0.5) * aspect;
                var v = y - 0.5;
                var radius = Math.Sqrt(u * u + v * v);
                var theta = Math.Atan2(v, u);
                var intensity = c.Mode switch
                {
                    "circuit" => Circuit(x, y, t),
                    "glitch" => Glitch(x, y, t),
                    "scan" => Scan(x, y, t),
                    "data" => Data(x, y, t),
                    "matrix" => Matrix(x, y, t),
                    "pulse" => Pulse(radius, t),
                    "radar" => Radar(radius, theta, t),
                    "sync" => Sync(x, y, t),
                    "noise" => Noise(x, y, t),
                    _ => Binary(x, y, t),
                };
                intensity = Math.Pow(AmbientMath.Clamp(intensity), c.Contrast) * c.Gain;
                var color = context.Palette.Sample(AmbientMath.Wrap(
                    (x * cos + y * sin) * 2.0 + t * c.Speed * 0.08 + intensity * 0.12));
                context.Output[row * columns + column] =
                    StudioRgb.Lerp(StudioRgb.Black, color, intensity);
            }
        }

        private double Circuit(double x, double y, double t)
        {
            var gx = x * c.Blocks;
            var gy = y * c.Blocks;
            var cellX = (int)Math.Floor(gx);
            var cellY = (int)Math.Floor(gy);
            var horizontal = AmbientMath.Hash(cellX, cellY, c.Seed) > 0.48;
            var local = horizontal
                ? Math.Abs(gy - Math.Round(gy))
                : Math.Abs(gx - Math.Round(gx));
            var line = 1 - AmbientMath.Smooth(0, Math.Max(0.012, c.Softness), local);
            var pulse = 0.45 + 0.55 * (0.5 + 0.5 * Math.Sin(
                t * c.Speed * (1 + AmbientMath.Hash(cellX, cellY, c.Seed + 7) * 2)));
            return line * pulse;
        }

        private double Glitch(double x, double y, double t)
        {
            var row = (int)Math.Floor(y * c.Blocks);
            var shift = AmbientMath.Hash(row, c.Seed, 11);
            var phase = AmbientMath.Wrap(t * c.Speed * 0.35 + shift);
            var offset = phase < 0.4 ? 0 : (int)(c.Jitter * c.Blocks);
            var shifted = AmbientMath.Wrap(x + offset / (double)c.Blocks);
            var block = AmbientMath.Hash((int)(shifted * c.Blocks), row, c.Seed + 13);
            var active = block > 0.58 ? 1 : 0.12;
            var scan = Math.Abs(Math.Sin((y * c.Frequency + t * c.Speed) * AmbientMath.Tau));
            return active * (0.3 + 0.7 * scan);
        }

        private double Scan(double x, double y, double t)
        {
            var phase = AmbientMath.Wrap(t * c.Speed * 0.3);
            var band = Math.Exp(-Math.Pow((y - phase) / Math.Max(0.012, c.Softness), 2));
            var second = Math.Exp(-Math.Pow((y - (1 - phase)) / Math.Max(0.02, c.Softness * 1.5), 2));
            return Math.Max(band, second * 0.6) + 0.04 * (0.5 + 0.5 * Math.Sin(x * 5 + t));
        }

        private double Data(double x, double y, double t)
        {
            var column = (int)Math.Floor(x * c.Blocks);
            var phase = AmbientMath.Wrap(t * c.Speed * 0.25 + AmbientMath.Hash(column, c.Seed, 17));
            var head = 1 - phase;
            var distance = Math.Abs(y - head);
            var headGlow = Math.Exp(-Math.Pow(distance / Math.Max(0.015, c.Softness), 2));
            var trail = y > head && y < head + 0.28 ? (head + 0.28 - y) / 0.28 : 0;
            var bit = AmbientMath.Hash(column, (int)Math.Floor(y * c.Blocks * 4), c.Seed + 19);
            return headGlow + trail * (bit > 0.5 ? 0.7 : 0.2);
        }

        private double Matrix(double x, double y, double t)
        {
            var column = (int)Math.Floor(x * c.Blocks);
            var phase = AmbientMath.Wrap(t * c.Speed * 0.23 + AmbientMath.Hash(column, c.Seed, 23));
            var head = 1 - phase;
            var cell = (int)Math.Floor(y * c.Blocks * 3);
            var bit = AmbientMath.Hash(column, cell, c.Seed + 29) > 0.5;
            if (!bit) return 0;
            var distance = head - y;
            if (distance is < -0.02 or > 0.35) return 0;
            return distance < 0.04 ? 1 : Math.Pow(1 - distance / 0.35, 1.7);
        }

        private double Pulse(double radius, double t)
        {
            var value = 0.5 + 0.5 * Math.Sin(
                (radius * c.Frequency - t * c.Speed) * AmbientMath.Tau);
            var ring = AmbientMath.Smooth(0.5 - c.Softness, 0.5 + c.Softness, value);
            return Math.Max(ring, 0.08);
        }

        private double Radar(double radius, double theta, double t)
        {
            var beamAngle = t * c.Speed * 1.5;
            var beam = Math.Exp(-Math.Pow(
                AmbientMath.PolarDistance(theta, beamAngle) / Math.Max(0.02, c.Softness), 2));
            var rings = 0.5 + 0.5 * Math.Sin(
                (radius * c.Frequency - t * c.Speed * 0.4) * AmbientMath.Tau);
            return Math.Max(beam, rings * 0.28);
        }

        private double Sync(double x, double y, double t)
        {
            var row = Math.Sin((y * c.Frequency + t * c.Speed) * AmbientMath.Tau);
            var column = Math.Sin((x * c.Frequency * 0.7 - t * c.Speed * 0.8) * AmbientMath.Tau);
            var cell = AmbientMath.Hash((int)(x * c.Blocks), (int)(y * c.Blocks), c.Seed + 31);
            return (0.5 + 0.5 * row * column) * (0.35 + 0.65 * cell);
        }

        private double Noise(double x, double y, double t)
        {
            var step = (int)Math.Floor(t * c.Speed * 4);
            var value = AmbientMath.Hash(
                (int)Math.Floor(x * c.Blocks * 4),
                (int)Math.Floor(y * c.Blocks * 4) + step,
                c.Seed);
            return value > 0.52 ? 1 : value * 0.18;
        }

        private double Binary(double x, double y, double t)
        {
            var step = (int)Math.Floor(t * c.Speed * 3);
            var value = AmbientMath.Hash(
                (int)Math.Floor(x * c.Blocks),
                (int)Math.Floor(y * c.Blocks),
                c.Seed + step);
            return value > 0.55 ? 1 : 0.06;
        }
    }
}
