namespace OpenSynapse.Core.Lighting;

/// <summary>Time-driven color fields; one reusable field evaluator with small field kernels.</summary>
internal sealed class AmbientFieldModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.field", "环境色场");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Mode: p.Choice("mode", "wave", "gradient", "wave", "radial", "interference", "noise",
                "plasma", "aurora", "pulse", "stripes", "rows", "bars", "spiral", "radar",
                "grid", "border", "ecg", "static"),
            Frequency: p.Number("frequency", 1.6, 0.1, 12),
            Angle: p.Number("angle", 0, -360, 360) * Math.PI / 180,
            Speed: p.Number("speed", 0.16, -4, 4),
            Twist: p.Number("twist", 0, -8, 8),
            Flow: p.Number("flow", 0, -2, 2),
            Floor: p.Number("floor", 0, 0, 1),
            Contrast: p.Number("contrast", 1, 0.2, 4),
            Softness: p.Number("softness", 0.18, 0.005, 0.5),
            CenterX: p.Number("centerX", 0.5, 0, 1),
            CenterY: p.Number("centerY", 0.5, 0, 1),
            Aspect: p.Number("aspect", 2.4, 0.2, 5),
            Spokes: p.Integer("spokes", 0, 0, 32),
            Scale: p.Number("scale", 1, 0.1, 8),
            Mirror: p.Boolean("mirror", false),
            Gain: p.Number("gain", 1, 0, 3));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        string Mode, double Frequency, double Angle, double Speed, double Twist, double Flow,
        double Floor, double Contrast, double Softness, double CenterX, double CenterY,
        double Aspect, int Spokes, double Scale, bool Mirror, double Gain);

    private sealed class Layer(Config c) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            Array.Clear(context.Output);
            var t = double.IsFinite(context.Seconds) ? context.Seconds : 0;
            var columns = context.Columns;
            var rows = context.Rows;
            var flow = c.Flow == 0 && c.Speed != 0 &&
                c.Mode is "gradient" or "static" or "grid" or "border"
                ? c.Speed
                : c.Flow;
            var cos = Math.Cos(c.Angle);
            var sin = Math.Sin(c.Angle);
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var x = (column + 0.5) / columns;
                var y = (row + 0.5) / rows;
                if (c.Mirror) x = Math.Abs(x - 0.5) * 2;
                var dx = (x - c.CenterX) * c.Aspect;
                var dy = y - c.CenterY;
                var radius = Math.Sqrt(dx * dx + dy * dy);
                var theta = Math.Atan2(dy, dx);
                var coordinate = (x * cos + y * sin) * c.Frequency + t * c.Speed;
                var intensity = 1d;
                var colorPosition = x;
                switch (c.Mode)
                {
                    case "gradient":
                        colorPosition = (x * cos + y * sin) * c.Frequency + t * flow;
                        intensity = 1;
                        break;
                    case "wave":
                        var wave = Math.Sin(coordinate * AmbientMath.Tau);
                        intensity = AmbientMath.Smooth(0.5 - c.Softness, 0.5 + c.Softness,
                            0.5 + 0.5 * wave);
                        colorPosition = (x * cos + y * sin) * c.Scale + t * flow + wave * 0.08;
                        break;
                    case "radial":
                        var ring = Math.Sin((radius * c.Frequency - t * c.Speed) * AmbientMath.Tau);
                        intensity = AmbientMath.Smooth(0.5 - c.Softness, 0.5 + c.Softness,
                            0.5 + 0.5 * ring);
                        colorPosition = radius * c.Scale + t * flow;
                        if (c.Spokes > 0)
                            intensity *= 0.4 + 0.6 * Math.Pow(Math.Abs(Math.Sin(
                                (theta * c.Spokes + t * c.Speed) * 0.5)), 0.5);
                        break;
                    case "spiral":
                        var spiral = Math.Sin((theta * Math.Max(1, c.Spokes) / 2 + radius * c.Twist
                            - t * c.Speed) * AmbientMath.Tau);
                        intensity = AmbientMath.Smooth(0.5 - c.Softness, 0.5 + c.Softness,
                            0.5 + 0.5 * spiral);
                        colorPosition = radius * c.Scale + flow * t + theta / AmbientMath.Tau;
                        break;
                    case "interference":
                        var w1 = Math.Sin((x * cos + y * sin) * c.Frequency * AmbientMath.Tau
                            - t * c.Speed * AmbientMath.Tau);
                        var w2 = Math.Sin((x * -sin + y * cos) * c.Frequency * 0.73
                            + t * c.Speed * 0.61 * AmbientMath.Tau);
                        intensity = 0.5 + 0.25 * (w1 + w2);
                        colorPosition = x * c.Scale + y * 0.3 + t * flow + w1 * 0.08;
                        break;
                    case "noise":
                        var n = AmbientMath.Fbm(x * c.Frequency + t * c.Speed * 0.4,
                            y * c.Frequency - t * c.Speed * 0.31, 17);
                        intensity = Math.Pow(AmbientMath.Clamp(n), 1.2);
                        colorPosition = n * 0.8 + x * 0.2 + t * flow;
                        break;
                    case "plasma":
                        var plasma = Math.Sin((x * c.Frequency + t * c.Speed) * AmbientMath.Tau)
                            + Math.Sin((y * c.Frequency * 0.83 - t * c.Speed * 0.7) * AmbientMath.Tau)
                            + Math.Sin(((x + y) * c.Frequency * 1.17 + t * c.Speed * 0.5) * AmbientMath.Tau);
                        intensity = 0.5 + plasma / 6;
                        colorPosition = (plasma + 3) / 6 + t * flow;
                        break;
                    case "aurora":
                        var band = Math.Sin((x * c.Frequency + Math.Sin(y * 5 + t * c.Speed) * 0.12
                            + t * c.Speed) * AmbientMath.Tau);
                        var veil = AmbientMath.Fbm(x * 2.7 + t * c.Speed * 0.17, y * 1.3, 31);
                        intensity = (0.35 + 0.65 * (0.5 + 0.5 * band)) * (0.45 + 0.55 * veil);
                        colorPosition = x * c.Scale + veil * 0.35 + t * flow;
                        break;
                    case "pulse":
                        intensity = 0.22 + 0.78 * (0.5 + 0.5 * Math.Sin(t * c.Speed * AmbientMath.Tau));
                        colorPosition = x * c.Scale + t * flow;
                        break;
                    case "stripes":
                        var stripe = AmbientMath.Wrap(coordinate);
                        intensity = stripe < 0.5 ? 1 : 0.08;
                        intensity = AmbientMath.Smooth(0.5 - c.Softness, 0.5 + c.Softness,
                            stripe < 0.5 ? 1 - stripe : 0.5);
                        colorPosition = x * c.Scale + t * flow;
                        break;
                    case "rows":
                        var rowPhase = AmbientMath.Wrap(y * c.Frequency + t * c.Speed * 0.35);
                        intensity = rowPhase < 0.5 ? 1 : 0.08;
                        colorPosition = x * c.Scale + y * 0.3 + t * flow;
                        break;
                    case "bars":
                        var bar = Math.Floor(x * Math.Max(1, c.Frequency * 4));
                        var height = 0.45 + 0.45 * Math.Sin(bar * 1.7 + t * c.Speed * 2);
                        intensity = y > 1 - height ? 0.85 : 0.05;
                        colorPosition = bar / Math.Max(1, c.Frequency * 4) + t * flow;
                        break;
                    case "grid":
                        var gx = Math.Abs(AmbientMath.Wrap(x * Math.Max(1, c.Frequency * 3)) - 0.5);
                        var gy = Math.Abs(AmbientMath.Wrap(y * Math.Max(1, c.Frequency * 3)) - 0.5);
                        var line = Math.Min(gx, gy);
                        intensity = 1 - AmbientMath.Smooth(0, c.Softness, line);
                        colorPosition = x * c.Scale + y * 0.3 + t * flow;
                        break;
                    case "border":
                        var edge = Math.Min(Math.Min(x, 1 - x), Math.Min(y, 1 - y));
                        intensity = 1 - AmbientMath.Smooth(0, c.Softness, edge);
                        colorPosition = x * c.Scale + t * flow;
                        break;
                    case "radar":
                        var angle = AmbientMath.Angle(theta + t * c.Speed * 1.7);
                        var beam = Math.Exp(-Math.Pow(AmbientMath.PolarDistance(angle, 0) / c.Softness, 2));
                        var radarRing = 0.5 + 0.5 * Math.Sin((radius * c.Frequency - t * c.Speed) * AmbientMath.Tau);
                        intensity = Math.Max(beam * 0.9, radarRing * 0.28);
                        colorPosition = radius * c.Scale + t * flow;
                        break;
                    case "ecg":
                        var waveY = 0.5 - AmbientMath.Ecg(x * 0.75 - t * c.Speed * 0.25) * 0.085;
                        intensity = Math.Exp(-Math.Pow((y - waveY) / Math.Max(0.015, c.Softness * 0.32), 2));
                        colorPosition = x * c.Scale + t * flow;
                        break;
                    case "static":
                        colorPosition = (x * cos + y * sin) * c.Frequency + flow * t;
                        intensity = 1;
                        break;
                }
                intensity = Math.Pow(AmbientMath.Clamp(intensity), c.Contrast) * c.Gain;
                var color = context.Palette.Sample(AmbientMath.Wrap(colorPosition));
                var baseColor = StudioRgb.Lerp(StudioRgb.Black,
                    context.Palette.Sample(AmbientMath.Wrap(colorPosition + 0.5)), c.Floor);
                context.Output[row * columns + column] = StudioRgb.Lerp(baseColor, color,
                    AmbientMath.Clamp(intensity));
            }
        }
    }
}
