namespace OpenSynapse.Core.Lighting;

internal static class KeyRenderModules
{
    public static IReadOnlyList<IRenderModuleFactory> All { get; } = Array.AsReadOnly<IRenderModuleFactory>(
    [
        new KeyRippleModule(),
        new KeyGlowModule(), new KeyWaveModule(), new KeySweepModule(), new KeyBeamModule(),
        new KeyParticlesModule(), new KeyLinksModule(), new KeyGlyphModule(), new KeyChargeModule(),
        new KeyActivityModule(), new KeyVortexModule(),
    ]);
}

internal readonly record struct KeyStyle(double Life, double Background, string ColorMode, bool Inverse)
{
    public static KeyStyle Read(RenderModuleParams p) => new(
        p.Number("life", 2, 0.1, 30), p.Number("background", 0, 0, 1),
        KeyDraw.Choice(p, "colorMode", "press", "press", "position", "radial", "white", "scatter", "age"),
        p.Boolean("inverse", false));

    public double Fade(StudioKeyPress press) => press.Held ? 1 :
        Math.Max(0, 1 - (double.IsFinite(press.ReleaseAge) ? press.ReleaseAge : press.Age) / Life);

    public StudioRgb Color(in RenderModuleContext c, StudioKeyPress p, double x, double y) =>
        ColorMode == "white" ? StudioRgb.White : c.Palette.Sample(KeyDraw.Wrap(ColorMode switch
        {
            "position" => (p.Column * 0.137 + p.Row * 0.271),
            "radial" => p.Hue + KeyDraw.Length(x, y) * 0.09,
            "scatter" => KeyDraw.Noise(p.Column + x, p.Row + y),
            "age" => p.Age / Life,
            _ => p.Hue,
        }));
}

internal static class KeyDraw
{
    public const double Tau = 2 * Math.PI;

    public static string Choice(RenderModuleParams p, string name, string fallback, params string[] choices) =>
        p.Choice(name, fallback, choices);

    public static double Wrap(double x) => x - Math.Floor(x);
    public static double Length(double x, double y) => Math.Sqrt(x * x + y * y);
    public static double Gaussian(double distance, double width) => Math.Exp(-distance * distance / (width * width));
    public static double Noise(double x, double y) => Wrap(Math.Sin(x * 127.1 + y * 311.7 + 17.3) * 43758.5453);
    public static StudioRgb Scale(StudioRgb color, double amount) => StudioRgb.Lerp(StudioRgb.Black, color, amount);
    public static bool Valid(in RenderModuleContext c, StudioKeyPress p) =>
        (uint)p.Row < (uint)c.Rows && (uint)p.Column < (uint)c.Columns && double.IsFinite(p.Age) && p.Age >= 0;

    public static void Put(in RenderModuleContext c, int row, int col, StudioRgb color, double strength)
    {
        if ((uint)row >= (uint)c.Rows || (uint)col >= (uint)c.Columns || strength <= 0.003) return;
        var index = row * c.Columns + col;
        var old = c.Output[index];
        var add = Scale(color, strength);
        c.Output[index] = new(Math.Max(old.R, add.R), Math.Max(old.G, add.G), Math.Max(old.B, add.B));
    }

    public static void Background(in RenderModuleContext c, double amount)
    {
        if (amount <= 0) return;
        for (var row = 0; row < c.Rows; row++)
        for (var col = 0; col < c.Columns; col++)
            c.Output[row * c.Columns + col] = Scale(c.Palette.Sample(Wrap(col / (double)c.Columns + c.Seconds * 0.06)), amount);
    }

    public static double Segment(double x, double y, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length, 0, 1);
        return Length(x - ax - t * dx, y - ay - t * dy);
    }
}

/// <summary>Shared matrix sampling for fields anchored to actual input presses.</summary>
internal abstract class KeyFieldLayer(KeyStyle style) : IRenderModule
{
    protected KeyStyle Style { get; } = style;
    protected virtual bool RetainHeld => true;
    protected abstract double Sample(in RenderModuleContext context, StudioKeyPress press, double x, double y);

    public virtual void Render(in RenderModuleContext context)
    {
        KeyDraw.Background(context, Style.Background);
        if (context.Keys is not { } keys) return;
        for (var row = 0; row < context.Rows; row++)
        for (var col = 0; col < context.Columns; col++)
        {
            var darkness = 0d;
            foreach (var press in keys.Presses)
            {
                if (!KeyDraw.Valid(context, press) || (!RetainHeld && press.Age > Style.Life) || Style.Fade(press) <= 0) continue;
                var x = col - press.Column;
                var y = row - press.Row;
                var strength = Math.Clamp(Sample(context, press, x, y), 0, 1);
                if (Style.Inverse) darkness = Math.Max(darkness, strength);
                else KeyDraw.Put(context, row, col, Style.Color(context, press, x, y), strength);
            }
            if (Style.Inverse)
            {
                var index = row * context.Columns + col;
                context.Output[index] = KeyDraw.Scale(context.Output[index], 1 - darkness);
            }
        }
    }
}
