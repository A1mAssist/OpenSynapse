namespace OpenSynapse.Core.Lighting;

internal sealed class MusicRadialModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.radial", "径向频谱与环波");
    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p),
            MusicMath.Choice(p, "source", "spectrum", "spectrum", "energy", "beat", "waves"),
            MusicMath.Choice(p, "metric", "circle", "circle", "box", "perimeter"),
            MusicMath.Choice(p, "mapping", "radius", "radius", "angle"),
            p.Boolean("randomCenters", false), p.Number("centerX", 0.5, 0, 1),
            p.Number("centerY", 0.5, 0, 1), p.Number("aspect", 2.4, 0.2, 5),
            p.Number("radius", 1, 0.05, 2), p.Number("width", 0.12, 0.02, 0.5),
            p.Number("fill", 0, 0, 1), p.Integer("centers", 1, 1, 2),
            p.Integer("rings", 1, 1, 8), p.Integer("spokes", 0, 0, 24),
            p.Number("twist", 0, -4, 4), p.Number("spin", 0, -3, 3));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, string source, string metric, string mapping,
        bool randomCenters, double centerX, double centerY, double aspect, double radius,
        double width, double fill, int centers, int rings, int spokes, double twist, double spin) : MusicLayer(settings)
    {
        protected override void Prepare(in RenderModuleContext c, StudioAudioFrame a, double dt)
        {
            if (source != "waves") return;
            var clock = c.State.Field("wave-clock", 4);
            var waves = c.State.Field("waves", 16);
            for (var i = 0; i < 4; i++)
                if (waves[i * 4 + 1] > 0) waves[i * 4] += (float)dt;
            var first = clock[0] == 0;
            var newBeat = !first && a.BeatCount != clock[1];
            clock[0] = 1;
            clock[1] = a.BeatCount;
            clock[2] += (float)dt;
            var level = MusicMath.Energy(a);
            if (!newBeat && !(level > 0.04 && (first || clock[2] >= 0.6))) return;
            var index = (int)clock[3];
            var slot = index % 4 * 4;
            waves[slot] = 0;
            waves[slot + 1] = (float)Math.Max(level, MusicMath.Pulse(a));
            waves[slot + 2] = (float)(0.1 + 0.8 * MusicMath.Noise(index, 1));
            waves[slot + 3] = (float)(0.15 + 0.7 * MusicMath.Noise(index, 2));
            clock[2] = 0;
            clock[3]++;
        }

        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            var best = new MusicLight(0, 0);
            var waves = source == "waves" ? c.State.Field("waves", 16) : null;
            var count = waves is not null ? 4 : source == "beat" ? Math.Min(a.Beats.Count, 16) : centers;
            for (var i = 0; i < count; i++)
            {
                var beat = source == "beat" ? a.Beats[i] : default;
                if (source == "beat" && (beat.Age < 0 || beat.Age >= 1.6)) continue;
                var age = waves is not null ? waves[i * 4] : beat.Age;
                var hit = waves is not null ? waves[i * 4 + 1] : beat.Hit;
                if (waves is not null && (hit <= 0 || age >= 1.6)) continue;
                var cx = randomCenters ? 0.1 + 0.8 * MusicMath.Noise(beat.Index, 1)
                    : centers == 2 ? centerX + (i % 2 == 0 ? -0.25 : 0.25) : centerX;
                var cy = randomCenters ? 0.15 + 0.7 * MusicMath.Noise(beat.Index, 2) : centerY;
                if (waves is not null && randomCenters) { cx = waves[i * 4 + 2]; cy = waves[i * 4 + 3]; }
                var dx = (x - cx) * aspect;
                var dy = y - cy;
                var distance = metric == "circle" ? MusicMath.Length(dx, dy) : Math.Max(Math.Abs(dx), Math.Abs(dy));
                var angle = MusicMath.Frac(Math.Atan2(dy, dx) / MusicMath.Tau + c.Seconds * spin + distance * twist);
                // Rectangular perimeter follows all four sides, including the keyboard's long edges.
                if (metric == "perimeter")
                {
                    var scale = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    var px = scale == 0 ? 0 : dx / scale;
                    var py = scale == 0 ? 0 : dy / scale;
                    angle = MusicMath.Frac((px >= Math.Abs(py) ? py + 1 : py >= Math.Abs(px) ? 3 - px
                        : -px >= Math.Abs(py) ? 5 - py : 7 + px) / 8 + c.Seconds * spin);
                }
                var band = mapping == "angle" ? angle : distance / radius;
                var amount = 0d;
                var color = band;
                if (source == "spectrum")
                {
                    var level = a.Column(MusicMath.Clamp(band));
                    amount = spokes == 0 ? Math.Pow(level, 1.25)
                        : level * MusicMath.Clamp((radius * level - distance) / width + 0.5);
                }
                else
                {
                    var extent = source is "beat" or "waves" ? age * 2.6 * radius
                        : radius * (0.7 * a.Bass + 0.3 * MusicMath.Energy(a) + 0.2 * MusicMath.Pulse(a));
                    var strength = source is "beat" or "waves" ? (1 - age / 1.6) * hit : MusicMath.Energy(a);
                    for (var ring = 0; ring < rings; ring++)
                    {
                        var edge = extent - ring * radius / Math.Max(2, rings);
                        if (edge <= 0) continue;
                        amount = Math.Max(amount, strength * Math.Max(MusicMath.Line(distance - edge, width),
                            fill * MusicMath.Clamp((edge - distance) / width)));
                    }
                    if (rings > 1 && source == "energy")
                        amount *= 0.55 + 0.45 * (0.5 + 0.5 * Math.Cos(
                            MusicMath.Tau * (distance * rings / radius - c.Seconds * 0.75)));
                    color = source == "beat" ? MusicMath.Frac(beat.Index * 0.17 + distance * 0.15) : distance / radius + i * 0.5;
                }
                if (spokes > 0)
                    amount *= MusicMath.Line((MusicMath.Frac(angle * spokes) - 0.5) * distance,
                        width * 0.8);
                if (amount > best.Amount) best = new(amount, color);
            }
            return best;
        }
    }
}

internal sealed class MusicHeartModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.heart", "节拍爱心轮廓");
    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p), p.Number("size", 0.55, 0.1, 1), p.Number("halo", 0.5, 0, 1));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, double size, double halo) : MusicLayer(settings)
    {
        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            var pulse = MusicMath.Pulse(a, 3.5);
            var scale = size * (0.75 + 0.25 * a.Bass + 0.15 * pulse);
            var hx = (x - 0.5) * 3 / scale;
            var hy = (0.53 - y) * 2 / scale;
            var distance = MusicMath.Length(hx, hy - 0.35 * Math.Sqrt(Math.Abs(hx)));
            var amount = MusicMath.Clamp((1 - distance) * 5) * Math.Max(a.Rms, pulse);
            amount = Math.Max(amount, halo * pulse * MusicMath.Line(distance - 1 - a.BeatAge * 2, 0.2));
            return new(amount, y);
        }
    }
}

internal sealed class MusicContourModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.contour", "眼形光圈与瞳孔");
    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p), p.Number("aperture", 0.75, 0.05, 1),
            p.Number("pupil", 0.12, 0.01, 0.5), p.Number("flare", 0.8, 0, 1),
            p.Number("gaze", 0.08, 0, 0.3), p.Number("rim", 0.08, 0.01, 0.5));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, double aperture, double pupil, double flare,
        double gaze, double rim) : MusicLayer(settings)
    {
        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            var dx = (x - 0.5) * 2.1;
            var dy = (y - 0.5) * 2;
            var open = aperture * (0.6 + 0.4 * a.Bass);
            var edge = Math.Abs(dy) - open * Math.Max(0, 1 - dx * dx);
            var irisX = dx - gaze * Math.Sin(c.Seconds * 0.8);
            var iris = MusicMath.Length(irisX, dy);
            var slit = pupil * (0.5 + a.Bass + MusicMath.Kick(a)) * Math.Sqrt(Math.Max(0, 1 - dy * dy));
            var body = MusicMath.Clamp(-edge / rim + 0.5) * MusicMath.Clamp((Math.Abs(irisX) - slit) / rim);
            var hotRim = MusicMath.Line(edge, rim) * (0.6 + 0.4 * Math.Sin(x * 19 + c.Seconds * 4) * Math.Sin(x * 9 - c.Seconds));
            var shock = flare * MusicMath.Pulse(a) * MusicMath.Line(iris - 0.3 - a.BeatAge * 1.8, rim * 2);
            return new(Math.Max(body * (0.4 + 0.6 * a.Column(iris)), Math.Max(hotRim, shock)) * activity, 1 - iris);
        }
    }
}
