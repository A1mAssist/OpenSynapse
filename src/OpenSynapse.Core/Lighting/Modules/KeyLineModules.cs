namespace OpenSynapse.Core.Lighting;

internal sealed class KeySweepModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.sweep", "按键定向光迹");
    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var motion = KeyDraw.Choice(p, "motion", "rays", "rays", "comet", "drop", "domino", "shock");
        var velocity = p.Number("velocity", 8, 0.2, 40);
        var tail = p.Number("tail", 2, 0.2, 12);
        p.EnsureKnown();
        return new Layer(style, motion, velocity, tail);
    }

    private sealed class Layer(KeyStyle style, string motion, double velocity, double tail) : KeyFieldLayer(style)
    {
        protected override bool RetainHeld => false;
        protected override double Sample(in RenderModuleContext c, StudioKeyPress p, double x, double y)
        {
            var travel = p.Age * velocity;
            var distance = motion == "drop" ? y : motion == "comet" ? x : Math.Abs(x);
            var cross = motion == "drop" ? x : y;
            var behind = travel - distance;
            if (distance < 0 || behind < -0.5) return 0;
            var brightness = motion == "domino"
                ? Math.Exp(-Math.Max(0, p.Age - Math.Floor(distance) / velocity) * 4)
                : Math.Exp(-Math.Max(0, behind) / tail);
            if (motion != "shock") brightness *= KeyDraw.Gaussian(cross, 0.5);
            return brightness * Math.Max(0, 1 - p.Age / Style.Life) * Style.Fade(p);
        }
    }
}

internal sealed class KeyBeamModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.beam", "行列光幕");
    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var axis = KeyDraw.Choice(p, "axis", "cross", "cross", "row", "column");
        var motion = KeyDraw.Choice(p, "motion", "static", "static", "curtain", "grow");
        var width = p.Number("width", 0.5, 0.2, 4);
        p.EnsureKnown();
        return new Layer(style, axis, motion, width);
    }

    private sealed class Layer(KeyStyle style, string axis, string motion, double width) : KeyFieldLayer(style)
    {
        protected override double Sample(in RenderModuleContext c, StudioKeyPress p, double x, double y)
        {
            var across = axis == "row" ? Math.Abs(y) : axis == "column" ? Math.Abs(x) : Math.Min(Math.Abs(x), Math.Abs(y));
            var along = axis == "row" ? Math.Abs(x) : axis == "column" ? Math.Abs(y) : Math.Max(Math.Abs(x), Math.Abs(y));
            var age = p.Age / Style.Life;
            if (motion == "curtain")
            {
                var w = 0.3 + width * Math.Max(0, 1 - age * 3);
                var end = Math.Max(0, 1 - age) * Math.Max(c.Rows, c.Columns);
                return KeyDraw.Gaussian(across, w) * Math.Clamp(end - along + 1, 0, 1) * Math.Max(0, 1 - age);
            }
            if (motion == "grow")
            {
                var height = c.Rows * Math.Min(1, p.Age * 3) * Style.Fade(p);
                var fromBottom = c.Rows - 1 - (p.Row + y);
                return KeyDraw.Gaussian(x, width) * Math.Clamp(height - fromBottom, 0, 1) * Style.Fade(p);
            }
            return KeyDraw.Gaussian(across, width) * Style.Fade(p);
        }
    }
}

internal sealed class KeyLinksModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.links", "按键连线与分叉");
    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var topology = KeyDraw.Choice(p, "topology", "chain", "chain", "constellation", "circuit", "lightning", "cracks");
        var width = p.Number("width", 0.5, 0.15, 3);
        p.EnsureKnown();
        return new Layer(style, topology, width);
    }

    private sealed class Layer(KeyStyle style, string topology, double width) : IRenderModule
    {
        public void Render(in RenderModuleContext c)
        {
            KeyDraw.Background(c, style.Background);
            if (c.Keys is not { } keys) return;
            for (var i = 0; i < keys.Presses.Count; i++)
            {
                var p = keys.Presses[i];
                if (!KeyDraw.Valid(c, p) || style.Fade(p) <= 0) continue;
                var fade = style.Fade(p);
                var next = i + 1 < keys.Presses.Count ? keys.Presses[i + 1] : p;
                if (!KeyDraw.Valid(c, next)) next = p;
                for (var row = 0; row < c.Rows; row++)
                for (var col = 0; col < c.Columns; col++)
                {
                    var x = col - p.Column;
                    var y = row - p.Row;
                    var color = topology == "constellation" ? c.Palette.Sample(0.9) : style.Color(c, p, x, y);
                    var strength = KeyDraw.Gaussian(KeyDraw.Length(x, y), 0.65) * fade;
                    if (topology is "chain" or "constellation" or "circuit")
                    {
                        if (i + 1 < keys.Presses.Count && style.Fade(next) > 0)
                        {
                            var bx = next.Column - p.Column;
                            var by = next.Row - p.Row;
                            var distance = topology == "circuit"
                                ? Math.Min(KeyDraw.Segment(x, y, 0, 0, bx, 0), KeyDraw.Segment(x, y, bx, 0, bx, by))
                                : KeyDraw.Segment(x, y, 0, 0, bx, by);
                            var line = KeyDraw.Gaussian(distance, width) * Math.Min(fade, style.Fade(next));
                            if (topology == "circuit")
                            {
                                var path = Math.Abs(x) + Math.Abs(y);
                                line *= 0.2 + 0.8 * KeyDraw.Gaussian(path - p.Age * 9, 1.8);
                            }
                            if (topology == "constellation")
                            {
                                line *= 0.45;
                                if (strength < line) color = c.Palette.Sample(0.15);
                            }
                            strength = Math.Max(strength, line);
                        }
                    }
                    else
                    {
                        var branches = topology == "cracks" ? 5 : 2;
                        var reach = Math.Min(12, p.Age * 18 + 0.5);
                        var line = 0d;
                        for (var branch = 0; branch < branches; branch++)
                        {
                            var angle = branch * KeyDraw.Tau / branches + p.Hue * 3;
                            var ax = 0d;
                            var ay = 0d;
                            for (var segment = 1; segment <= 6; segment++)
                            {
                                var distance = segment * 2d;
                                if (distance - 2 > reach) break;
                                var bend = (KeyDraw.Noise(branch + p.Column, segment + p.Row) - 0.5) * 2;
                                var bx = Math.Cos(angle) * Math.Min(distance, reach) - Math.Sin(angle) * bend;
                                var by = Math.Sin(angle) * Math.Min(distance, reach) + Math.Cos(angle) * bend;
                                line = Math.Max(line, KeyDraw.Gaussian(KeyDraw.Segment(x, y, ax, ay, bx, by), width));
                                ax = bx; ay = by;
                            }
                        }
                        var flicker = topology == "lightning" ? Math.Pow(0.5 + 0.5 * Math.Cos(p.Age * 45), 2) : 1;
                        var tipFade = Math.Max(0, 1 - p.Age / style.Life * (0.5 + KeyDraw.Length(x, y) / 12));
                        strength = Math.Max(strength * 0.4, line * tipFade * flicker * fade);
                    }
                    if (style.Inverse)
                    {
                        var index = row * c.Columns + col;
                        c.Output[index] = KeyDraw.Scale(c.Output[index], 1 - strength);
                    }
                    else KeyDraw.Put(c, row, col, color, strength);
                }
            }
        }
    }
}
