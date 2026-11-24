namespace OpenSynapse.Core.Lighting;

/// <summary>Procedural geometry: moire, kaleidoscope, Voronoi, lattices, tunnels and polygons.</summary>
internal sealed class AmbientGeometryModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.geometry", "几何棱镜");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Mode: p.Choice("mode", "moire", "moire", "kaleido", "voronoi", "rings",
                "tunnel", "lattice", "polygon", "hex", "diamond", "spiral"),
            Frequency: p.Number("frequency", 2.4, 0.1, 16),
            Speed: p.Number("speed", 0.2, -4, 4),
            Rotation: p.Number("rotation", 0, -360, 360) * Math.PI / 180,
            Sides: p.Integer("sides", 6, 3, 16),
            Scale: p.Number("scale", 1, 0.1, 4),
            Softness: p.Number("softness", 0.08, 0.005, 0.5),
            Contrast: p.Number("contrast", 1.1, 0.2, 4),
            Ratio: p.Number("ratio", 1, 0.2, 3),
            CenterX: p.Number("centerX", 0.5, 0, 1),
            CenterY: p.Number("centerY", 0.5, 0, 1),
            Flow: p.Number("flow", 0.08, -2, 2),
            Seed: p.Integer("seed", 31, 0, 9999),
            Mirror: p.Boolean("mirror", false),
            Gain: p.Number("gain", 1, 0, 2));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        string Mode, double Frequency, double Speed, double Rotation, int Sides, double Scale,
        double Softness, double Contrast, double Ratio, double CenterX, double CenterY,
        double Flow, int Seed, bool Mirror, double Gain);

    private sealed class Layer(Config c) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            AmbientPaint.Clear(context.Output);
            var t = double.IsFinite(context.Seconds) ? Math.Max(0, context.Seconds) : 0;
            var columns = context.Columns;
            var rows = context.Rows;
            var aspect = columns / (double)rows;
            var rotation = c.Rotation + t * c.Speed * 0.11;
            var cos = Math.Cos(rotation);
            var sin = Math.Sin(rotation);
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var x = (column + 0.5) / columns;
                var y = (row + 0.5) / rows;
                if (c.Mirror) x = Math.Abs(x - 0.5) * 2;
                var u = (x - c.CenterX) * aspect * c.Ratio;
                var v = y - c.CenterY;
                var rx = u * cos - v * sin;
                var ry = u * sin + v * cos;
                var radius = Math.Sqrt(rx * rx + ry * ry);
                var theta = Math.Atan2(ry, rx);
                var intensity = c.Mode switch
                {
                    "moire" => Moire(rx, ry, t),
                    "kaleido" => Kaleido(radius, theta, t),
                    "voronoi" => Voronoi(u, v, t),
                    "rings" => Rings(radius, t),
                    "tunnel" => Tunnel(radius, theta, t),
                    "lattice" => Lattice(rx, ry, t),
                    "polygon" => Polygon(radius, theta, t),
                    "hex" => Hex(rx, ry, t),
                    "diamond" => Diamond(rx, ry, t),
                    _ => Spiral(radius, theta, t),
                };
                intensity = Math.Pow(AmbientMath.Clamp(intensity), c.Contrast) * c.Gain;
                var color = context.Palette.Sample(AmbientMath.Wrap(
                    (x * cos + y * sin) * c.Scale + t * c.Flow + intensity * 0.14));
                context.Output[row * columns + column] =
                    StudioRgb.Lerp(StudioRgb.Black, color, intensity);
            }
        }

        private double Moire(double x, double y, double t)
        {
            var a = Math.Sin((x * c.Frequency + y * 0.16 + t * c.Speed) * AmbientMath.Tau);
            var b = Math.Sin((x * c.Frequency * 1.07 - y * 0.31
                - t * c.Speed * 0.8) * AmbientMath.Tau);
            return 0.5 + 0.5 * a * b;
        }

        private double Kaleido(double radius, double theta, double t)
        {
            var sector = Math.Abs(AmbientMath.Wrap(theta * c.Sides / AmbientMath.Tau) - 0.5) * 2;
            var ring = 0.5 + 0.5 * Math.Sin(
                (radius * c.Frequency + sector * 0.9 + t * c.Speed) * AmbientMath.Tau);
            return Math.Pow(ring, 1.45);
        }

        private double Voronoi(double u, double v, double t)
        {
            var gx = u * c.Frequency + 0.5 + t * c.Speed * 0.03;
            var gy = v * c.Frequency + 0.5;
            var cellX = (int)Math.Floor(gx);
            var cellY = (int)Math.Floor(gy);
            var nearest = double.MaxValue;
            var second = double.MaxValue;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var cx = cellX + dx;
                var cy = cellY + dy;
                var px = cx + AmbientMath.Hash(cx, cy, c.Seed);
                var py = cy + AmbientMath.Hash(cx, cy, c.Seed + 7);
                var distance = Math.Sqrt((gx - px) * (gx - px) + (gy - py) * (gy - py));
                if (distance < nearest)
                {
                    second = nearest;
                    nearest = distance;
                }
                else if (distance < second)
                {
                    second = distance;
                }
            }
            var edge = second - nearest;
            return AmbientMath.Smooth(0.02, 0.02 + c.Softness, edge);
        }

        private double Rings(double radius, double t)
        {
            var value = 0.5 + 0.5 * Math.Sin(
                (radius * c.Frequency - t * c.Speed) * AmbientMath.Tau);
            return AmbientMath.Smooth(0.5 - c.Softness, 0.5 + c.Softness, value);
        }

        private double Tunnel(double radius, double theta, double t)
        {
            var depth = 1 / (radius * 1.4 + 0.12);
            var ring = 0.5 + 0.5 * Math.Sin(
                (depth * c.Frequency + theta * c.Sides / AmbientMath.Tau
                - t * c.Speed) * AmbientMath.Tau);
            return Math.Pow(ring, 1.3);
        }

        private double Lattice(double x, double y, double t)
        {
            var gx = Math.Abs(AmbientMath.Wrap(x * c.Frequency + t * c.Speed * 0.1) - 0.5);
            var gy = Math.Abs(AmbientMath.Wrap(y * c.Frequency) - 0.5);
            var diagonal = Math.Abs(AmbientMath.Wrap((x + y) * c.Frequency * 0.55) - 0.5);
            var line = Math.Min(gx, Math.Min(gy, diagonal * 1.6));
            return 1 - AmbientMath.Smooth(0, Math.Max(0.01, c.Softness), line);
        }

        private double Polygon(double radius, double theta, double t)
        {
            var angle = AmbientMath.Angle(theta - t * c.Speed * 0.04);
            var boundary = 0.34 * (0.86 + 0.14 * Math.Cos(angle * c.Sides));
            var distance = Math.Abs(radius - boundary);
            var outline = 1 - AmbientMath.Smooth(0, Math.Max(0.012, c.Softness), distance);
            var fill = AmbientMath.Smooth(boundary, boundary - c.Softness * 0.5, radius) * 0.24;
            return Math.Max(outline, fill);
        }

        private double Hex(double x, double y, double t)
        {
            var px = x * c.Frequency * 1.15 + t * c.Speed * 0.04;
            var py = y * c.Frequency;
            var row = (int)Math.Floor(py);
            var offset = (row & 1) * 0.5;
            var gx = Math.Abs(AmbientMath.Wrap(px + offset) - 0.5);
            var gy = Math.Abs(AmbientMath.Wrap(py * Math.Sqrt(3)) - 0.5);
            var edge = Math.Max(gx, gy * 0.7);
            return 1 - AmbientMath.Smooth(0.5 - c.Softness, 0.5, edge);
        }

        private double Diamond(double x, double y, double t)
        {
            var diamond = Math.Abs(x) + Math.Abs(y);
            var value = 0.5 + 0.5 * Math.Sin(
                (diamond * c.Frequency - t * c.Speed) * AmbientMath.Tau);
            return AmbientMath.Smooth(0.5 - c.Softness, 0.5 + c.Softness, value);
        }

        private double Spiral(double radius, double theta, double t)
        {
            var value = 0.5 + 0.5 * Math.Sin(
                (theta * c.Sides / AmbientMath.Tau + Math.Log(radius + 0.02) * 1.7
                - t * c.Speed) * AmbientMath.Tau);
            return Math.Pow(value, 1.2);
        }
    }
}
