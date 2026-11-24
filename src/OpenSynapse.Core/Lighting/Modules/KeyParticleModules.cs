namespace OpenSynapse.Core.Lighting;

internal sealed class KeyParticlesModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.particles", "按键粒子运动");
    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var motion = KeyDraw.Choice(p, "motion", "burst", "burst", "flame", "snow", "bubbles", "splash", "cascade", "magnet");
        var count = p.Integer("count", 10, 3, 32);
        var velocity = p.Number("velocity", 5, 0.2, 20);
        var gravity = p.Number("gravity", 3, -10, 20);
        p.EnsureKnown();
        return new Layer(style, motion, count, velocity, gravity);
    }

    private sealed class Layer(KeyStyle style, string motion, int count, double velocity, double gravity) : KeyFieldLayer(style)
    {
        protected override double Sample(in RenderModuleContext c, StudioKeyPress p, double x, double y)
        {
            var best = 0d;
            var continuous = motion is "flame" or "snow" or "bubbles";
            for (var i = 0; i < count; i++)
            {
                var seed = KeyDraw.Noise(p.Column + i * 7, p.Row + p.Hue * 13);
                var t = p.Age;
                if (continuous)
                {
                    var cycle = Math.Max(0.2, Style.Life * 0.6);
                    t -= i * cycle / count;
                    // A held key emits continuously; released keys stop emitting at key-up.
                    if (t < 0) continue;
                    if (p.Held) t %= cycle;
                    else if (double.IsFinite(p.ReleaseAge))
                    {
                        var heldAge = p.Age - p.ReleaseAge - i * cycle / count;
                        if (heldAge < 0) continue;
                        t = heldAge % cycle + p.ReleaseAge;
                    }
                }
                if (motion != "magnet" && t > Style.Life) continue;
                var angle = KeyDraw.Tau * i / count + seed * 0.4;
                var speed = velocity * (0.5 + seed * 0.5);
                var px = Math.Cos(angle) * speed * t;
                var py = Math.Sin(angle) * speed * t + gravity * t * t * 0.5;
                var size = 0.45;
                var fade = Math.Max(0, 1 - t / Style.Life);
                switch (motion)
                {
                    case "flame":
                        px = (seed - 0.5) * (1 + t * 1.7) + Math.Sin(t * 9 + i) * 0.3;
                        py = -speed * t;
                        size = 0.65;
                        break;
                    case "snow":
                        px = (seed - 0.5) * 3 + Math.Sin(t * 2 + i) * t;
                        py = speed * t * 0.4;
                        size = 0.55;
                        break;
                    case "bubbles":
                        px = (seed - 0.5) * 2 + Math.Sin(t * 4 + i) * 0.7;
                        py = -speed * t * 0.55;
                        size = 0.3 + 0.2 * t;
                        break;
                    case "splash":
                        px = Math.Cos(angle) * speed * t;
                        py = -Math.Abs(Math.Sin(angle)) * speed * t + gravity * t * t;
                        var ground = c.Rows - 1 - p.Row;
                        if (py > ground)
                        {
                            var impact = (Math.Abs(Math.Sin(angle)) * speed + Math.Sqrt(Math.Pow(Math.Sin(angle) * speed, 2) + 4 * gravity * Math.Max(0, ground))) / (2 * Math.Max(0.1, gravity));
                            var after = Math.Max(0, t - impact);
                            var landingX = Math.Cos(angle) * speed * impact;
                            best = Math.Max(best, KeyDraw.Gaussian(KeyDraw.Length(x - landingX, y - ground) - after * 4, 0.45) * fade * 0.6);
                            continue;
                        }
                        break;
                    case "cascade":
                        var delay = i * 0.09;
                        if (t < delay) continue;
                        var hop = t - delay;
                        px = (seed - 0.5) * velocity + Math.Cos(angle) * hop * 2;
                        py = (KeyDraw.Noise(i, p.Column) - 0.5) * 3 - Math.Sin(Math.Min(1, hop) * Math.PI) * 1.5;
                        fade *= Math.Exp(-hop * 2.5);
                        size = 0.6;
                        break;
                    case "magnet":
                        var distance = p.Held ? velocity * Math.Exp(-t * 3) : Math.Min(velocity, (double.IsFinite(p.ReleaseAge) ? p.ReleaseAge : t) * speed);
                        px = Math.Cos(angle + t) * distance;
                        py = Math.Sin(angle + t) * distance * 0.7;
                        fade = Style.Fade(p);
                        break;
                }
                var particle = KeyDraw.Gaussian(KeyDraw.Length(x - px, y - py), size) * fade;
                if (motion == "bubbles")
                    particle = KeyDraw.Gaussian(KeyDraw.Length(x - px, y - py) - size, 0.3) * fade;
                best = Math.Max(best, particle);
            }
            var source = KeyDraw.Gaussian(KeyDraw.Length(x, y), 0.6) * (p.Held && continuous ? 0.55 : Math.Exp(-p.Age * 5));
            return Math.Max(best, source) * Style.Fade(p);
        }
    }
}

internal sealed class KeyChargeModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.charge", "按住蓄力松手爆发");
    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var maxHold = p.Number("maxHold", 2.2, 0.1, 10);
        var expansion = p.Number("expansion", 12, 1, 40);
        p.EnsureKnown();
        return new Layer(style, maxHold, expansion);
    }

    private sealed class Layer(KeyStyle style, double maxHold, double expansion) : KeyFieldLayer(style)
    {
        protected override double Sample(in RenderModuleContext c, StudioKeyPress p, double x, double y)
        {
            var d = KeyDraw.Length(x, y);
            var held = p.Held ? p.Age : double.IsFinite(p.ReleaseAge) ? Math.Max(0, p.Age - p.ReleaseAge) : 0;
            var charge = Math.Clamp(held / maxHold, 0.05, 1);
            if (p.Held)
            {
                var core = KeyDraw.Gaussian(d, 0.5 + charge) * (0.25 + charge * 0.65);
                var inward = (1 - KeyDraw.Wrap(p.Age * 1.4)) * (1.5 + charge * 2);
                return Math.Max(core, KeyDraw.Gaussian(d - inward, 0.3) * charge * 0.7);
            }
            if (!double.IsFinite(p.ReleaseAge)) return 0;
            var radius = expansion * (0.15 + charge * 0.85) * (1 - Math.Exp(-p.ReleaseAge * 3));
            var flash = KeyDraw.Gaussian(d, 1.5) * Math.Exp(-p.ReleaseAge * 8);
            return Math.Max(flash, KeyDraw.Gaussian(d - radius, 0.4 + charge * 0.4)) * Style.Fade(p) * (0.2 + charge * 0.8);
        }
    }
}
