namespace OpenSynapse.Core.Lighting;

internal sealed class MusicSpectrumModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.spectrum", "频谱条与能量轮廓");

    public IRenderModule Create(RenderModuleParams p)
    {
        var settings = MusicSettings.Read(p);
        var layer = new Layer(settings,
            MusicMath.Choice(p, "axis", "vertical", "vertical", "horizontal"),
            MusicMath.Choice(p, "origin", "edge", "edge", "center", "outside"),
            MusicMath.Choice(p, "source", "spectrum", "spectrum", "envelope", "bass", "waveform"),
            MusicMath.Choice(p, "profile", "flat", "flat", "curve", "volcano", "prism"),
            p.Boolean("mirror", false), p.Boolean("peaks", true),
            p.Boolean("heightColor", false), p.Number("softness", 0.04, 0.005, 0.5),
            p.Number("smoothing", 0, 0, 1), p.Number("jitter", 0, 0, 0.5),
            p.Number("tilt", 0, 0, 2));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, string axis, string origin, string source,
        string profile, bool mirror, bool peaks, bool heightColor, double softness,
        double smoothing, double jitter, double tilt) : MusicLayer(settings)
    {
        protected override void Prepare(in RenderModuleContext c, StudioAudioFrame a, double dt)
        {
            var levels = c.State.Field("levels", axis == "vertical" ? c.Columns : c.Rows);
            for (var i = 0; i < levels.Length; i++)
            {
                var u = (i + 0.5) / levels.Length;
                var band = mirror ? Math.Abs(u - 0.5) * 2 : u;
                var target = source switch
                {
                    "envelope" => MusicMath.Energy(a),
                    "bass" => a.Bass,
                    "waveform" => MusicMath.Sample(a.Waveform, band),
                    _ => a.Column(band),
                };
                target *= 1 + tilt * u * u;
                if (profile == "curve") target *= MusicMath.Clamp(0.65 + 0.35 * Math.Cos(u * Math.PI) +
                    0.22 * Math.Sin(u * 12 + c.Seconds * 2) + 0.13 * Math.Sin(u * 23 - c.Seconds * 3));
                if (profile == "volcano")
                {
                    var distance = Math.Min(Math.Abs(u - 0.04), Math.Min(Math.Abs(u - 0.5), Math.Abs(u - 0.96)));
                    target *= MusicMath.Clamp(1 - distance / (0.12 + 0.16 * MusicMath.Energy(a)));
                }
                if (profile == "prism") target *= 1.4 * (1 - 0.85 * Math.Abs(u - 0.5) * 2);
                target = MusicMath.Clamp(target + target * jitter * (MusicMath.Noise(i, (int)(c.Seconds * 15)) - 0.5));
                var response = smoothing == 0 ? 1 : 1 - Math.Exp(-dt / (target > levels[i] ? smoothing * 0.3 : smoothing));
                levels[i] += (float)((target - levels[i]) * response);
            }
        }

        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            var u = axis == "vertical" ? x : y;
            var height = axis == "vertical" ? 1 - y : x;
            height = origin switch { "center" => Math.Abs(height - 0.5) * 2,
                "outside" => Math.Min(height, 1 - height) * 2, _ => height };
            var count = axis == "vertical" ? c.Columns : c.Rows;
            var levels = c.State.Field("levels", count);
            var level = levels[Math.Min(count - 1, (int)(u * count))];
            var amount = level <= 0.001 ? 0 : MusicMath.Clamp((level - height) / softness + 0.5) * (0.45 + 0.55 * level);
            var band = mirror ? Math.Abs(u - 0.5) * 2 : u;
            if (peaks && source == "spectrum" && a.ColumnPeak(band) > 0.02)
                amount = Math.Max(amount, MusicMath.Line(height - a.ColumnPeak(band),
                    0.45 / (axis == "vertical" ? c.Rows : c.Columns)) * a.ColumnPeak(band));
            return new(amount, heightColor ? height : band);
        }
    }
}

internal sealed class MusicFieldModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.field", "音量与历史色场");

    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p),
            MusicMath.Choice(p, "source", "volume", "volume", "spectrum", "beat", "history", "bass-history"),
            p.Boolean("reverse", false), p.Number("focus", 0, 0, 2), p.Number("gamma", 1, 0.2, 3));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, string source, bool reverse, double focus, double gamma) : MusicLayer(settings)
    {
        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            var age = reverse ? 1 - y : y;
            var value = source switch
            {
                "spectrum" => a.Column(x),
                "beat" => 0.25 * a.Rms + 0.85 * MusicMath.Pulse(a, 2.6),
                "history" => MusicMath.History(a, age, x),
                "bass-history" => (MusicMath.History(a, age, 0) + MusicMath.History(a, age, 1d / 17) +
                    MusicMath.History(a, age, 2d / 17)) / 3,
                _ => a.Rms,
            };
            value *= Math.Max(0, 1 - focus * MusicMath.Length((x - 0.5) * 2, y - 0.5) * (1 - MusicMath.Pulse(a)));
            var color = source switch { "beat" => MusicMath.Frac(a.BeatCount * 0.17),
                "history" or "bass-history" => value, _ => x };
            return new(Math.Pow(MusicMath.Clamp(value), gamma), color);
        }
    }
}
