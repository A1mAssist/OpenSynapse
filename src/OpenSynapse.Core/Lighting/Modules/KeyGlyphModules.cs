namespace OpenSynapse.Core.Lighting;

internal sealed class KeyGlyphModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.glyph", "按键图形与心跳");
    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var shape = KeyDraw.Choice(p, "shape", "heart", "heart", "flower", "arc");
        var centered = p.Boolean("centered", false);
        var heartbeat = p.Boolean("heartbeat", false);
        var expansion = p.Number("expansion", 2, 0, 10);
        p.EnsureKnown();
        return new Layer(style, shape, centered, heartbeat, expansion);
    }

    private sealed class Layer(KeyStyle style, string shape, bool centered, bool heartbeat, double expansion) : KeyFieldLayer(style)
    {
        protected override double Sample(in RenderModuleContext c, StudioKeyPress p, double x, double y)
        {
            if (centered) { x += p.Column - (c.Columns - 1) / 2d; y += p.Row - (c.Rows - 1) / 2d; }
            var pulse = heartbeat ? KeyDraw.Gaussian(p.Age, 0.09) + 0.7 * KeyDraw.Gaussian(p.Age - 0.24, 0.1) : 1;
            var size = heartbeat ? Math.Max(1, c.Rows * 0.32) * (1 + pulse * 0.15) : 0.8 + Math.Min(3, p.Age * expansion);
            var d = KeyDraw.Length(x, y);
            var angle = Math.Atan2(y, x);
            var mask = 0d;
            switch (shape)
            {
                case "heart":
                    var u = x / size;
                    var v = -y / size;
                    var equation = Math.Pow(u * u + v * v - 1, 3) - u * u * v * v * v;
                    mask = Math.Clamp(0.65 - equation * 2, 0, 1);
                    break;
                case "flower":
                    var petal = size * (0.65 + 0.35 * Math.Cos(5 * angle));
                    mask = KeyDraw.Gaussian(d - petal, 0.55) + KeyDraw.Gaussian(d, 0.55) * 0.4;
                    break;
                case "arc":
                    var radius = Math.Max(0.2, 3 * (1 - p.Age / Style.Life));
                    var phase = KeyDraw.Wrap((angle - p.Age * 7) / KeyDraw.Tau);
                    mask = KeyDraw.Gaussian(d - radius, 0.5) * Math.Pow(1 - phase, 3);
                    mask += KeyDraw.Gaussian(d, 0.6) * KeyDraw.Gaussian(p.Age - Style.Life * 0.8, 0.2);
                    break;
            }
            return mask * Style.Fade(p) * (heartbeat ? 0.2 + 0.8 * Math.Min(1, pulse) : 1);
        }
    }
}

internal sealed class KeyActivityModule : IRenderModuleFactory
{
    public RenderModuleInfo Info { get; } = new("key.activity", "打字计量与连击");
    public IRenderModule Create(RenderModuleParams p)
    {
        var style = KeyStyle.Read(p);
        var mode = KeyDraw.Choice(p, "mode", "speed", "speed", "heat", "combo");
        var window = p.Number("window", 2, 0.2, 10);
        p.EnsureKnown();
        return new Layer(style, mode, window);
    }

    private sealed class Activity(int cells)
    {
        public double[] Heat { get; } = new double[cells];
        public HashSet<(int Row, int Column, double Hue)> Seen { get; } = [];
        public double LastTime = double.NaN;
        public double LastPress = double.NegativeInfinity;
        public int Combo;
    }

    private sealed class Layer(KeyStyle style, string mode, double window) : IRenderModule
    {
        // Three-by-five glyphs stay readable on the smallest supported keyboard matrix.
        private static readonly string[] Digits =
        ["111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
         "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111"];

        public void Render(in RenderModuleContext c)
        {
            if (c.State.Tag is not Activity state || state.Heat.Length != c.Output.Length || c.Seconds < state.LastTime)
                c.State.Tag = state = new Activity(c.Output.Length);
            var dt = double.IsNaN(state.LastTime) ? 0 : Math.Max(0, c.Seconds - state.LastTime);
            state.LastTime = c.Seconds;
            for (var i = 0; i < state.Heat.Length; i++) state.Heat[i] *= Math.Exp(-dt / style.Life);
            var rate = 0d;
            if (c.Keys is { } keys)
            {
                // Oldest first makes a batch of new presses preserve the real combo chronology.
                for (var i = keys.Presses.Count - 1; i >= 0; i--)
                {
                    var p = keys.Presses[i];
                    if (!KeyDraw.Valid(c, p)) continue;
                    var at = c.Seconds - p.Age;
                    var index = p.Row * c.Columns + p.Column;
                    if (state.Seen.Add((p.Row, p.Column, p.Hue)))
                    {
                        state.Heat[index] += Math.Exp(-p.Age / style.Life);
                        state.Combo = at - state.LastPress <= window ? Math.Min(999, state.Combo + 1) : 1;
                        state.LastPress = at;
                    }
                    rate += Math.Max(0, 1 - p.Age / window);
                    KeyDraw.Put(c, p.Row, p.Column, style.Color(c, p, 0, 0), style.Fade(p) * 0.4);
                }
                state.Seen.RemoveWhere(seen => !keys.Presses.Any(p => p.Row == seen.Row && p.Column == seen.Column && p.Hue == seen.Hue));
            }
            if (mode == "combo")
            {
                var fade = Math.Max(0, 1 - (c.Seconds - state.LastPress) / style.Life);
                if (state.Combo == 0 || fade <= 0) return;
                Span<char> digits = stackalloc char[3];
                state.Combo.TryFormat(digits, out var count);
                var left = Math.Max(0, (c.Columns - (count * 4 - 1)) / 2);
                var top = Math.Max(0, (c.Rows - 5) / 2);
                var color = c.Palette.Sample(Math.Min(1, state.Combo / 30d));
                for (var n = 0; n < count; n++)
                for (var row = 0; row < 5; row++)
                for (var col = 0; col < 3; col++)
                    if (Digits[digits[n] - '0'][row * 3 + col] == '1') KeyDraw.Put(c, top + row, left + n * 4 + col, color, fade);
                return;
            }
            for (var row = 0; row < c.Rows; row++)
            for (var col = 0; col < c.Columns; col++)
            {
                var amount = mode == "speed" ? Math.Clamp(rate / (window * 3), 0, 1) : Math.Clamp(state.Heat[row * c.Columns + col] / 4, 0, 1);
                var color = c.Palette.Sample(amount);
                var brightness = style.Background + (1 - style.Background) * amount;
                KeyDraw.Put(c, row, col, color, style.Inverse ? 1 - brightness : brightness);
            }
        }
    }
}
