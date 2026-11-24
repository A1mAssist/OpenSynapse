namespace OpenSynapse.Core.Lighting;

internal sealed class KeyGlowModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.glow", "按键余辉与柔光");

    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var shape = KeyDraw.Choice(p, "shape", "point", "point", "spot", "diffuse", "halo", "ink");
        var radius = p.Number("radius", 1.5, 0.3, 12);
        var expansion = p.Number("expansion", 2, 0, 20);
        var stars = p.Number("stars", 0, 0, 1);
        var delay = p.Number("delay", 0, 0, 5);
        p.EnsureKnown();
        return new Layer(style, shape, radius, expansion, stars, delay);
    }

    private sealed class Layer(KeyStyle style, string shape, double radius, double expansion, double stars, double delay) : KeyFieldLayer(style)
    {
        public override void Render(in RenderModuleContext c)
        {
            base.Render(c);
            if (stars <= 0) return;
            for (var row = 0; row < c.Rows; row++)
            for (var col = 0; col < c.Columns; col++)
            {
                var seed = KeyDraw.Noise(col, row);
                if (seed < 0.88) continue;
                KeyDraw.Put(c, row, col, c.Palette.Sample(seed), stars * Math.Pow(0.5 + 0.5 * Math.Sin(c.Seconds * 1.8 + seed * 90), 6));
            }
        }

        protected override double Sample(in RenderModuleContext c, StudioKeyPress p, double x, double y)
        {
            var d = KeyDraw.Length(x, y);
            if (p.Age < delay) return 0;
            var age = Math.Min(p.Age - delay, Style.Life);
            var size = radius + expansion * age;
            var envelope = shape switch
            {
                "point" => x == 0 && y == 0 ? 1 : 0,
                "spot" => KeyDraw.Gaussian(d, radius),
                "diffuse" => KeyDraw.Gaussian(d, size) / (1 + age * 0.25),
                "halo" => d < 0.5 ? 0 : KeyDraw.Gaussian(d - radius, 0.6),
                "ink" => KeyDraw.Gaussian(d, size) * (0.55 + 0.45 * KeyDraw.Noise(x + p.Column, y + p.Row)),
                _ => 0,
            };
            return envelope * Style.Fade(p);
        }
    }
}

internal sealed class KeyWaveModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.wave", "径向扩散与介质波");

    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var profile = KeyDraw.Choice(p, "profile", "ring", "ring", "dye", "wake", "sonar", "jelly", "warp", "burn");
        var expansion = p.Number("expansion", 7, 0.2, 40);
        var thickness = p.Number("thickness", 0.8, 0.2, 5);
        var rainbow = p.Boolean("rainbow", false);
        p.EnsureKnown();
        return new Layer(style, profile, expansion, thickness, rainbow);
    }

    private sealed class Paint(int count)
    {
        public StudioRgb[] Colors { get; } = new StudioRgb[count];
        public double[] Times { get; } = Enumerable.Repeat(double.NegativeInfinity, count).ToArray();
    }

    private sealed class Layer(KeyStyle style, string profile, double expansion, double thickness, bool rainbow) : KeyFieldLayer(style)
    {
        public override void Render(in RenderModuleContext c)
        {
            if (profile != "dye") { base.Render(c); return; }
            if (c.State.Tag is not Paint paint || paint.Colors.Length != c.Output.Length)
            {
                c.State.Tag = paint = new Paint(c.Output.Length);
                KeyDraw.Background(c, Style.Background);
                Array.Copy(c.Output, paint.Colors, c.Output.Length);
            }
            Array.Copy(paint.Colors, c.Output, c.Output.Length);
            if (c.Keys is not { } keys) return;
            for (var i = keys.Presses.Count - 1; i >= 0; i--)
            {
                var p = keys.Presses[i];
                if (!KeyDraw.Valid(c, p)) continue;
                var start = c.Seconds - p.Age;
                for (var row = 0; row < c.Rows; row++)
                for (var col = 0; col < c.Columns; col++)
                {
                    var index = row * c.Columns + col;
                    var d = KeyDraw.Length(col - p.Column, row - p.Row);
                    if (d > p.Age * expansion || start + 0.000001 < paint.Times[index]) continue;
                    paint.Times[index] = start;
                    var hue = rainbow ? KeyDraw.Wrap(p.Hue + d * 0.12 * Math.Exp(-p.Age)) : p.Hue;
                    paint.Colors[index] = c.Palette.Sample(hue);
                    c.Output[index] = KeyDraw.Scale(paint.Colors[index], Style.Inverse
                        ? 1 - 0.85 * KeyDraw.Gaussian(d - p.Age * expansion, thickness) : 1);
                }
            }
        }

        protected override double Sample(in RenderModuleContext c, StudioKeyPress p, double x, double y)
        {
            var d = KeyDraw.Length(x, y);
            var radius = p.Age * expansion;
            var fade = Math.Max(0, 1 - p.Age / Style.Life);
            var hold = p.Held ? 0.35 * KeyDraw.Gaussian(d, 0.8) : 0;
            switch (profile)
            {
                case "ring": return Math.Max(hold, KeyDraw.Gaussian(d - radius, thickness) * fade);
                case "wake":
                    var coverage = Math.Clamp((radius - d) / thickness + 0.5, 0, 1);
                    var release = double.IsFinite(p.ReleaseAge) ? p.ReleaseAge : p.Age;
                    var wipe = rainbow && !p.Held ? Math.Clamp((d - release * expansion) / thickness, 0, 1) : 1;
                    return coverage * Style.Fade(p) * wipe;
                case "sonar":
                    var reflected = Math.Max(
                        Math.Max(KeyDraw.Gaussian(KeyDraw.Length(x + 2 * p.Column, y) - radius, thickness),
                            KeyDraw.Gaussian(KeyDraw.Length(x - 2 * (c.Columns - 1 - p.Column), y) - radius, thickness)),
                        Math.Max(KeyDraw.Gaussian(KeyDraw.Length(x, y + 2 * p.Row) - radius, thickness),
                            KeyDraw.Gaussian(KeyDraw.Length(x, y - 2 * (c.Rows - 1 - p.Row)) - radius, thickness)));
                    return Math.Max(hold, (KeyDraw.Gaussian(d - radius, thickness) + reflected * 0.5) * fade);
                case "jelly":
                    var wobble = 1.5 + 0.8 * Math.Cos(p.Age * 14 - d * 1.5) * Math.Exp(-p.Age * 0.6);
                    return KeyDraw.Gaussian(d, wobble + radius * 0.15) * Style.Fade(p);
                case "warp":
                    var twist = 5 * Math.Exp(-d * 0.24) * Style.Fade(p);
                    return (0.5 + 0.5 * Math.Sin((x * Math.Cos(twist) - y * Math.Sin(twist)) * 1.5 - p.Age * 4)) *
                        KeyDraw.Gaussian(d, 5) * Style.Fade(p);
                case "burn":
                    var edge = KeyDraw.Gaussian(d - radius, thickness);
                    var ember = d < radius ? 0.3 * Math.Exp(-(radius - d) * 0.3) : 0;
                    return (edge * (0.5 + 0.5 * KeyDraw.Noise(x + p.Age * 3, y)) + ember) * fade;
                default: return 0;
            }
        }
    }
}

internal sealed class KeyVortexModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.vortex", "按键旋涡吸入");
    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var radius = p.Number("radius", 6, 1, 20);
        var rotation = p.Number("rotation", 4, -20, 20);
        p.EnsureKnown();
        return new Layer(style, radius, rotation);
    }

    private sealed class Layer(KeyStyle style, double radius, double rotation) : KeyFieldLayer(style)
    {
        protected override double Sample(in RenderModuleContext c, StudioKeyPress p, double x, double y)
        {
            var d = KeyDraw.Length(x, y);
            var hole = 0.4 + Math.Min(1.5, p.Age * 0.5);
            if (d < hole) return 0;
            var spiral = 0.5 + 0.5 * Math.Cos(Math.Atan2(y, x) * 3 + d * 1.4 + p.Age * rotation);
            return Math.Max(KeyDraw.Gaussian(d - hole - 0.5, 0.5), Math.Pow(spiral, 4) * KeyDraw.Gaussian(d, radius)) * Style.Fade(p);
        }
    }
}
