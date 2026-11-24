namespace OpenSynapse.Core.Lighting;

internal sealed class MusicParticlesModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.particles", "节拍星火与高音星点");
    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p), p.Boolean("onBeat", true),
            p.Number("density", 0.35, 0.05, 1), p.Number("decay", 3, 0.1, 8));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, bool onBeat, double density, double decay) : MusicLayer(settings)
    {
        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            var cell = (int)(x * c.Columns) + (int)(y * c.Rows) * c.Columns;
            var seed = MusicMath.Noise(cell, 17);
            var amount = 0d;
            var color = seed;
            if (onBeat)
            {
                for (var i = 0; i < Math.Min(8, a.Beats.Count); i++)
                {
                    var beat = a.Beats[i];
                    if (beat.Age is < 0 or > 1.6 || MusicMath.Noise(cell, beat.Index) > density * beat.Hit) continue;
                    var light = beat.Hit * Math.Exp(-beat.Age * decay);
                    if (light > amount) { amount = light; color = MusicMath.Noise(cell + 7, beat.Index); }
                }
            }
            else if (seed < density)
                amount = (a.High + a.Band(seed)) * Math.Pow(Math.Max(0, Math.Sin(c.Seconds * (1 + seed * 3) + seed * 50)), decay);
            return new(amount, color);
        }
    }
}

internal sealed class MusicLatticeModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.lattice", "节拍交替棋盘");
    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p), p.Integer("cellsX", 9, 1, 24),
            p.Integer("cellsY", 3, 1, 12), p.Number("decay", 3, 0.1, 8));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, int cellsX, int cellsY, double decay) : MusicLayer(settings)
    {
        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity) =>
            new(((int)(x * cellsX) + (int)(y * cellsY)) % 2 == a.BeatCount % 2
                ? 0.25 * a.Rms + 0.75 * MusicMath.Pulse(a, decay) : 0, MusicMath.Frac(a.BeatCount * 0.13));
    }
}

internal sealed class MusicLightningModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.lightning", "频谱等离子放射闪电");
    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p), p.Integer("rays", 14, 2, 32),
            p.Number("width", 0.07, 0.01, 0.5), p.Number("twist", 0.35, 0, 1));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, int rays, double width, double twist) : MusicLayer(settings)
    {
        protected override void Prepare(in RenderModuleContext c, StudioAudioFrame a, double dt)
        {
            var levels = c.State.Field("rays", rays);
            for (var i = 0; i < rays; i++)
                levels[i] += (float)((a.Column(i / (double)(rays - 1)) - levels[i]) * (1 - Math.Exp(-dt / 0.08)));
        }

        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            var dx = (x - 0.5) * 2.4;
            var dy = y - 0.5;
            var distance = MusicMath.Length(dx, dy);
            var angle = MusicMath.Frac(Math.Atan2(dy, dx) / MusicMath.Tau - c.Seconds * 0.03);
            var ray = Math.Min(rays - 1, (int)(angle * rays));
            var level = c.State.Field("rays", rays)[ray];
            var core = 0.12 + 0.18 * a.Bass;
            var turbulence = 0.5 + 0.5 * Math.Sin(angle * 31 + c.Seconds * 2) * Math.Sin(distance * 23 - c.Seconds * 3);
            var orb = MusicMath.Clamp((core - distance) / width + 0.5) * turbulence * activity;
            var jitter = twist * (MusicMath.Noise(ray, (int)(c.Seconds * 12)) - 0.5) * level;
            var offset = (MusicMath.Frac(angle * rays) - 0.5) * distance + jitter * 0.3;
            var lightning = MusicMath.Line(offset, width) * level * MusicMath.Clamp((core + level * 1.2 - distance) / width);
            return new(Math.Max(orb, lightning), angle * 0.5 + turbulence * 0.5);
        }
    }
}
