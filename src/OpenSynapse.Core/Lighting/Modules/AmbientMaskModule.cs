namespace OpenSynapse.Core.Lighting;

/// <summary>Shape masks evaluated per cell: hearts, rings, glyphs, ECG lines and frames.</summary>
internal sealed class AmbientMaskModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("ambient.mask", "环境遮罩");

    public IRenderModule Create(RenderModuleParams p)
    {
        var config = new Config(
            Shape: p.Choice("shape", "heart", "heart", "ring", "diamond", "cross", "star",
                "flower", "eye", "arc", "text", "ecg", "frame", "moon", "triangle", "disk",
                "checker", "fill", "sine"),
            X: p.Number("x", 0.5, 0, 1),
            Y: p.Number("y", 0.5, 0, 1),
            Scale: p.Number("scale", 1, 0.1, 3),
            Stretch: p.Number("stretch", 1, 0.2, 4),
            Rotation: p.Number("rotation", 0, -360, 360) * Math.PI / 180,
            Thickness: p.Number("thickness", 0.06, 0.005, 0.5),
            Fill: p.Number("fill", 0, 0, 1),
            Softness: p.Number("softness", 0.025, 0.004, 0.3),
            Pulse: p.Number("pulse", 0, 0, 1),
            Speed: p.Number("speed", 1, 0, 6),
            Glow: p.Number("glow", 0, 0, 1),
            Frequency: p.Number("frequency", 1, 0.1, 8),
            Blocks: p.Integer("blocks", 4, 1, 16),
            Scroll: p.Boolean("scroll", false),
            ScrollSpeed: p.Number("scrollSpeed", 0.12, 0.01, 2),
            Text: p.Text("text", "OPEN"),
            ColorFlow: p.Number("colorFlow", 0, -2, 2),
            Gain: p.Number("gain", 1, 0, 3));
        p.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        string Shape, double X, double Y, double Scale, double Stretch, double Rotation,
        double Thickness, double Fill, double Softness, double Pulse, double Speed,
        double Glow, double Frequency, int Blocks, bool Scroll, double ScrollSpeed,
        string Text, double ColorFlow, double Gain);

    private sealed class Layer(Config c) : IRenderModule
    {
        private readonly string[][] _textGlyphs = GlyphsFor(c.Text);
        private int _textColumns = -1;
        private int _textRows = -1;
        private int _textScale = 1;
        private int _textWidth;
        private double _textLeft;
        private double _textTop;

        public void Render(in RenderModuleContext context)
        {
            AmbientPaint.Clear(context.Output);
            var t = double.IsFinite(context.Seconds) ? context.Seconds : 0;
            if (c.Shape == "text") EnsureTextLayout(context, t);
            var pulse = 1 - c.Pulse + c.Pulse * (0.5 + 0.5 * Math.Sin(t * c.Speed * AmbientMath.Tau));
            for (var row = 0; row < context.Rows; row++)
            for (var column = 0; column < context.Columns; column++)
            {
                var x = (column + 0.5) / context.Columns;
                var y = (row + 0.5) / context.Rows;
                var intensity = Shape(context, x, y, c.Softness, t);
                if (c.Glow > 0 && c.Shape != "text")
                    intensity = Math.Max(intensity,
                        Shape(context, x, y, Math.Min(0.35, c.Softness * 4 + 0.05), t) * c.Glow);
                if (intensity <= 0.004) continue;
                var position = c.Shape == "fill"
                    ? 0.5 + t * c.ColorFlow
                    : x * 0.65 + y * 0.35 + t * c.ColorFlow;
                var color = context.Palette.Sample(AmbientMath.Wrap(position));
                context.Output[row * context.Columns + column] =
                    StudioRgb.Lerp(StudioRgb.Black, color, AmbientMath.Clamp(intensity * pulse * c.Gain));
            }
        }

        private double Shape(in RenderModuleContext context, double x, double y,
            double softness, double t)
        {
            if (c.Shape == "fill") return 1;
            if (c.Shape == "text") return Text(context, x, y);
            if (c.Shape == "checker")
            {
                var block = Math.Max(1, (int)Math.Round(context.Columns / (double)c.Blocks));
                var cellX = (int)(x * context.Columns) / block;
                var cellY = (int)(y * context.Rows) / block;
                return (cellX + cellY) % 2 == 0 ? 1 : 0;
            }

            var aspect = context.Columns / (double)context.Rows;
            var u = (x - c.X) * aspect / c.Scale;
            var v = (y - c.Y) / c.Scale;
            var cos = Math.Cos(c.Rotation);
            var sin = Math.Sin(c.Rotation);
            var rx = (u * cos - v * sin) * c.Stretch;
            var ry = u * sin + v * cos;
            var radius = Math.Sqrt(rx * rx + ry * ry);
            var angle = Math.Atan2(ry, rx);
            return c.Shape switch
            {
                "heart" => Heart(rx, ry, softness),
                "ring" => Band(Math.Abs(radius - 0.55), c.Thickness, softness),
                "disk" => Filled(radius - 0.5, softness),
                "diamond" => Filled(Math.Abs(rx) + Math.Abs(ry) - 0.7, softness),
                "cross" => Math.Max(
                    Band(Math.Abs(rx), c.Thickness * 0.7, softness),
                    Band(Math.Abs(ry), c.Thickness * 0.7, softness)),
                "star" => Filled(radius - 0.5 * (0.55 + 0.45 * Math.Abs(Math.Cos(2.5 * angle))), softness),
                "flower" => Flower(radius, angle, softness),
                "eye" => Eye(rx, ry, softness),
                "arc" => ry > 0.06 ? 0 : Band(Math.Abs(radius - 0.55), c.Thickness, softness),
                "ecg" => Ecg(x, y, t, softness),
                "frame" => Band(Math.Min(Math.Min(x, 1 - x), Math.Min(y, 1 - y)), c.Thickness, softness),
                "moon" => Moon(rx, ry, softness),
                "triangle" => Triangle(rx, ry, softness),
                "sine" => Sine(x, y, t, softness),
                _ => 0,
            };
        }

        private double Heart(double rx, double ry, double softness)
        {
            var a = rx / 1.05;
            var b = -ry / 0.52;
            var f = Math.Pow(a * a + b * b - 1, 3) - a * a * b * b * b;
            return Filled(f, softness);
        }

        private double Eye(double rx, double ry, double softness)
        {
            var ellipse = Math.Sqrt(rx * rx / (0.9 * 0.9) + ry * ry / (0.45 * 0.45));
            var outline = Band(Math.Abs(ellipse - 1) * 0.4, c.Thickness, softness);
            var pupil = Band(Math.Sqrt(rx * rx + ry * ry), 0.13, softness);
            return Math.Max(outline, pupil);
        }

        private double Flower(double radius, double angle, double softness)
        {
            var boundary = 0.5 * (0.58 + 0.42 * Math.Cos(5 * angle));
            var petals = Filled(radius - boundary, softness);
            var center = 1 - AmbientMath.Smooth(0.08, 0.13, radius);
            return Math.Max(petals, center * 0.8);
        }

        private double Ecg(double x, double y, double t, double softness)
        {
            var line = 0.5 - AmbientMath.Ecg(x * 0.8 - t * c.Speed * 0.22) * 0.1 * c.Frequency;
            return Band(Math.Abs(y - line), Math.Max(0.012, c.Thickness * 0.35), softness);
        }

        private double Moon(double rx, double ry, double softness)
        {
            var outer = Math.Sqrt(rx * rx + ry * ry);
            var inner = Math.Sqrt((rx - 0.2) * (rx - 0.2) + (ry - 0.06) * (ry - 0.06));
            var edge = Math.Min(Math.Abs(outer - 0.48), Math.Abs(inner - 0.48));
            var inside = outer < 0.48 && inner > 0.48 ? 1 : 0;
            return Math.Max(inside * c.Fill, (1 - c.Fill) * Band(edge, c.Thickness, softness));
        }

        private double Triangle(double rx, double ry, double softness)
        {
            var d1 = SegmentDistance(rx, ry, 0, -0.62, -0.68, 0.5);
            var d2 = SegmentDistance(rx, ry, 0, -0.62, 0.68, 0.5);
            var d3 = SegmentDistance(rx, ry, -0.68, 0.5, 0.68, 0.5);
            var outline = Band(Math.Min(d1, Math.Min(d2, d3)), c.Thickness, softness);
            var inside = PointInTriangle(rx, ry, 0, -0.62, -0.68, 0.5, 0.68, 0.5) ? 1 : 0;
            return Math.Max(inside * c.Fill, (1 - c.Fill) * outline);
        }

        private double Sine(double x, double y, double t, double softness)
        {
            var line = 0.5 + 0.28 * Math.Sin((x * c.Frequency + t * c.Speed * 0.18) * AmbientMath.Tau);
            return Band(Math.Abs(y - line), Math.Max(0.012, c.Thickness * 0.35), softness);
        }

        private double Filled(double signed, double softness)
        {
            var inside = 1 - AmbientMath.Smooth(-Math.Max(0.004, softness), 0, signed);
            return Math.Max(inside * c.Fill,
                (1 - c.Fill) * Band(Math.Abs(signed), c.Thickness, softness));
        }

        private static double Band(double distance, double thickness, double softness) =>
            1 - AmbientMath.Smooth(thickness, thickness + Math.Max(0.004, softness), distance);

        private double Text(in RenderModuleContext context, double x, double y)
        {
            var px = x * context.Columns - _textLeft;
            var py = y * context.Rows - _textTop;
            if (px < 0 || py < 0 || px >= _textWidth || py >= 5 * _textScale) return 0;
            var glyphX = (int)(px / _textScale);
            var glyphY = (int)(py / _textScale);
            var characterIndex = glyphX / 4;
            var column = glyphX % 4;
            if (characterIndex >= _textGlyphs.Length || column >= 3) return 0;
            return _textGlyphs[characterIndex][glyphY][column] == '1' ? 1 : 0;
        }

        private void EnsureTextLayout(in RenderModuleContext context, double t)
        {
            if (context.Columns != _textColumns || context.Rows != _textRows)
            {
                _textColumns = context.Columns;
                _textRows = context.Rows;
                var width = _textGlyphs.Length * 4 - 1;
                _textScale = Math.Max(1, Math.Min(context.Columns / width,
                    (int)(context.Rows / 5.0)));
                _textWidth = width * _textScale;
                _textTop = (context.Rows - 5 * _textScale) / 2.0;
            }

            _textLeft = c.Scroll
                ? context.Columns * 0.5 -
                    AmbientMath.Wrap(t * c.ScrollSpeed) * (_textWidth + context.Columns * 1.5)
                : (context.Columns - _textWidth) / 2.0;
        }

        private static string[][] GlyphsFor(string text)
        {
            var glyphs = new List<string[]>();
            foreach (var character in text.ToUpperInvariant())
            {
                if (Glyphs.TryGetValue(character, out var glyph)) glyphs.Add(glyph);
                else if (character == ' ') glyphs.Add(Blank);
            }
            if (glyphs.Count == 0) glyphs.Add(Glyphs['I']);
            return glyphs.ToArray();
        }

        private static double SegmentDistance(double px, double py,
            double ax, double ay, double bx, double by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var lengthSquared = dx * dx + dy * dy;
            var amount = lengthSquared <= 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) /
                lengthSquared, 0, 1);
            var x = ax + dx * amount - px;
            var y = ay + dy * amount - py;
            return Math.Sqrt(x * x + y * y);
        }

        private static bool PointInTriangle(double px, double py,
            double ax, double ay, double bx, double by, double cx, double cy)
        {
            var d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            var d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            var d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            return (d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0);
        }
    }

    private static readonly string[] Blank = ["000", "000", "000", "000", "000"];

    private static readonly IReadOnlyDictionary<char, string[]> Glyphs =
        new Dictionary<char, string[]>
        {
            ['A'] = ["010", "101", "111", "101", "101"],
            ['B'] = ["110", "101", "110", "101", "110"],
            ['C'] = ["011", "100", "100", "100", "011"],
            ['D'] = ["110", "101", "101", "101", "110"],
            ['E'] = ["111", "100", "110", "100", "111"],
            ['F'] = ["111", "100", "110", "100", "100"],
            ['G'] = ["011", "100", "101", "101", "011"],
            ['H'] = ["101", "101", "111", "101", "101"],
            ['I'] = ["111", "010", "010", "010", "111"],
            ['J'] = ["001", "001", "001", "101", "010"],
            ['K'] = ["101", "101", "110", "101", "101"],
            ['L'] = ["100", "100", "100", "100", "111"],
            ['M'] = ["101", "111", "111", "101", "101"],
            ['N'] = ["101", "111", "101", "101", "101"],
            ['O'] = ["010", "101", "101", "101", "010"],
            ['P'] = ["110", "101", "110", "100", "100"],
            ['Q'] = ["010", "101", "101", "110", "011"],
            ['R'] = ["110", "101", "110", "101", "101"],
            ['S'] = ["011", "100", "010", "001", "110"],
            ['T'] = ["111", "010", "010", "010", "010"],
            ['U'] = ["101", "101", "101", "101", "111"],
            ['V'] = ["101", "101", "101", "101", "010"],
            ['W'] = ["101", "101", "111", "111", "101"],
            ['X'] = ["101", "101", "010", "101", "101"],
            ['Y'] = ["101", "101", "010", "010", "010"],
            ['Z'] = ["111", "001", "010", "100", "111"],
            ['0'] = ["111", "101", "101", "101", "111"],
            ['1'] = ["010", "110", "010", "010", "111"],
            ['2'] = ["110", "001", "010", "100", "111"],
            ['3'] = ["111", "001", "011", "001", "111"],
            ['4'] = ["101", "101", "111", "001", "001"],
            ['5'] = ["111", "100", "110", "001", "110"],
            ['6'] = ["011", "100", "111", "101", "111"],
            ['7'] = ["111", "001", "010", "010", "010"],
            ['8'] = ["111", "101", "111", "101", "111"],
            ['9'] = ["111", "101", "111", "001", "110"],
            ['<'] = ["001", "010", "100", "010", "001"],
            ['!'] = ["010", "010", "010", "000", "010"],
            ['?'] = ["110", "001", "010", "000", "010"],
            ['-'] = ["000", "000", "111", "000", "000"],
            ['.'] = ["000", "000", "000", "000", "010"],
            ['\''] = ["010", "010", "000", "000", "000"],
        };
}
