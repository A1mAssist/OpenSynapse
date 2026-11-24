namespace OpenSynapse.Core.Lighting;

internal sealed class MusicTraceModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.trace", "波形曲线与折线轨迹");
    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p),
            MusicMath.Choice(p, "path", "sine", "sine", "polyline"),
            MusicMath.Choice(p, "source", "waveform", "waveform", "envelope"),
            MusicMath.Choice(p, "extent", "full", "full", "left", "right", "center"),
            p.Boolean("mirror", false), p.Number("width", 0.08, 0.02, 0.5),
            p.Number("amplitude", 0.4, 0, 1), p.Number("cycles", 1.4, 0.1, 8),
            p.Number("taper", 0, -1, 1), p.Number("memory", 0, 0, 1), p.Number("fill", 0, 0, 1));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, string path, string source, string extent,
        bool mirror, double width, double amplitude, double cycles, double taper, double memory,
        double fill) : MusicLayer(settings)
    {
        protected override void Prepare(in RenderModuleContext c, StudioAudioFrame a, double dt)
        {
            if (memory == 0) return;
            var history = c.State.Field("trace-history", c.Columns);
            var clock = c.State.Field("trace-clock", 2);
            clock[0] += (float)dt;
            if (clock[1] == 0)
            {
                for (var i = 0; i < history.Length; i++)
                    history[i] = (float)(source == "waveform"
                        ? MusicMath.Sample(a.Waveform, i / (double)Math.Max(1, history.Length - 1)) : MusicMath.Energy(a));
                clock[1] = 1;
            }
            var step = 0.02 + memory * 0.1;
            while (clock[0] >= step)
            {
                Array.Copy(history, 0, history, 1, history.Length - 1);
                history[0] = (float)(source == "waveform" ? MusicMath.Sample(a.Waveform, 0.5) : MusicMath.Energy(a));
                clock[0] -= (float)step;
            }
        }

        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            var energy = MusicMath.Energy(a);
            if (path == "polyline")
            {
                var left = 0.95 - energy * 0.9;
                var pathDistance = Math.Min(MusicMath.Segment(x, y, left, 0.08, 0.95, 0.08),
                    Math.Min(MusicMath.Segment(x, y, 0.95, 0.08, 0.95, 0.08 + energy * 0.84),
                        MusicMath.Segment(x, y, left, 0.92, 0.95, 0.08)));
                return new(MusicMath.Line(pathDistance, width) * energy, (x + y) * 0.5);
            }
            var u = extent == "center" ? Math.Abs(x - 0.5) * 2 : x;
            var sample = memory > 0 ? MusicMath.Sample(c.State.Field("trace-history", c.Columns), u)
                : MusicMath.Sample(a.Waveform, u);
            var envelope = memory > 0 || source == "waveform" ? sample : 0.5 * energy + 0.5 * a.Column(u);
            var reach = extent switch { "left" => x, "right" => 1 - x, "center" => u, _ => 0 };
            var gate = extent == "full" ? 1 : MusicMath.Clamp((energy - reach) / width + 0.5);
            var shape = taper >= 0 ? 1 - taper * x : 1 + taper * (1 - x);
            var displacement = amplitude * shape * envelope * Math.Sin(u * MusicMath.Tau * cycles - c.Seconds * 3.5);
            // Opposed traces share the same sampled envelope, so symmetry survives resize and palette changes.
            var distance = Math.Abs(y - 0.5 - displacement);
            if (mirror) distance = Math.Min(distance, Math.Abs(y - 0.5 + displacement));
            var ink = MusicMath.Line(distance, width);
            var between = Math.Abs(y - 0.5) < Math.Abs(displacement) ? fill : 0;
            return new(Math.Max(ink, between) * gate * activity, x);
        }
    }
}

internal sealed class MusicArrowsModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("music.arrows", "节拍扫线与箭形");
    public IRenderModule Create(RenderModuleParams p)
    {
        var layer = new Layer(MusicSettings.Read(p),
            MusicMath.Choice(p, "shape", "chevron", "stripe", "chevron", "shaft"),
            MusicMath.Choice(p, "direction", "outward", "outward", "inward", "right", "left", "alternate"),
            p.Number("width", 0.1, 0.02, 0.5), p.Number("travel", 1.5, 0.1, 5), p.Number("tail", 0.25, 0, 1));
        p.EnsureKnown();
        return layer;
    }

    private sealed class Layer(MusicSettings settings, string shape, string direction, double width,
        double travel, double tail) : MusicLayer(settings)
    {
        protected override MusicLight Shade(in RenderModuleContext c, StudioAudioFrame a, double x, double y, double activity)
        {
            if (direction == "left") x = 1 - x;
            var coordinate = direction is "inward" or "outward" ? Math.Abs(x - 0.5) * 2 : x;
            if (shape == "chevron") coordinate += Math.Abs(y - 0.5) * 0.8;
            if (shape == "shaft")
            {
                var tip = MusicMath.Clamp(MusicMath.Energy(a) * travel);
                var stem = x < tip ? MusicMath.Line(y - 0.5, width) * tail : 0;
                var head = MusicMath.Line(x - tip + Math.Abs(y - 0.5), width) *
                    MusicMath.Clamp(1 - Math.Abs(y - 0.5) / 0.28);
                return new(Math.Max(stem, head) * activity, x);
            }
            var best = new MusicLight(0, 0);
            for (var i = 0; i < Math.Min(16, a.Beats.Count); i++)
            {
                var beat = a.Beats[i];
                if (beat.Age < 0 || beat.Age * travel > 1.6) continue;
                var front = direction == "inward" ? 1.4 - beat.Age * travel : beat.Age * travel;
                var position = direction == "alternate" && beat.Index % 2 != 0 ? 1 - coordinate : coordinate;
                var gap = position - front;
                var behind = direction == "inward" ? gap > 0 : gap < 0;
                var amount = (MusicMath.Line(gap, width) + (behind ? tail * Math.Exp(-Math.Abs(gap) / 0.25) : 0)) *
                    beat.Hit * Math.Exp(-beat.Age * 1.4);
                if (amount > best.Amount) best = new(amount, MusicMath.Frac(beat.Index * 0.17));
            }
            return best;
        }
    }
}
