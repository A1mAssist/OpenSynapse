namespace OpenSynapse.Core.Lighting;

/// <summary>Expanding rings around each recent key press, in matrix-cell units.</summary>
internal sealed class KeyRippleModule : IRenderModuleFactory
{
    public const string Id = "key.ripple";

    public RenderModuleInfo Info { get; } = new(Id, "Key ripple");

    public IRenderModule Create(RenderModuleParams parameters)
    {
        var config = new Config(
            Rings: parameters.Integer("rings", 1, 1, 4),
            Diamond: parameters.Text("shape", "circle") switch
            {
                "circle" => false,
                "diamond" => true,
                var shape => throw new InvalidDataException(
                    $"Module '{Id}' parameter 'shape' must be 'circle' or 'diamond', not '{shape}'."),
            },
            Expansion: parameters.Number("expansion", 8, 1, 40),
            Spacing: parameters.Number("spacing", 2.5, 0, 12),
            Thickness: parameters.Number("thickness", 1, 0.2, 4),
            Life: parameters.Number("life", 1.4, 0.3, 5),
            Glow: parameters.Number("glow", 0.45, 0, 1),
            Hold: parameters.Boolean("hold", true));
        parameters.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(
        int Rings,
        bool Diamond,
        double Expansion,
        double Spacing,
        double Thickness,
        double Life,
        double Glow,
        bool Hold);

    private sealed class Layer(Config config) : IRenderModule
    {
        private const double GlowSigma = 1.4;

        public void Render(in RenderModuleContext context)
        {
            var keys = context.Keys;
            if (keys is null || keys.Presses.Count == 0) return;

            for (var row = 0; row < context.Rows; row++)
            {
                for (var column = 0; column < context.Columns; column++)
                {
                    var best = 0d;
                    var hue = 0d;
                    foreach (var press in keys.Presses)
                    {
                        var glowFade = config.Hold
                            ? press.Held ? 1 : Math.Max(0, 1 - press.ReleaseAge / config.Life)
                            : 0;
                        if (press.Age > config.Life && glowFade <= 0) continue;
                        var dx = column - press.Column;
                        var dy = row - press.Row;
                        var distance = config.Diamond
                            ? Math.Abs(dx) + Math.Abs(dy)
                            : Math.Sqrt(dx * dx + dy * dy);
                        if (glowFade > 0)
                        {
                            var glow = config.Glow * glowFade *
                                Math.Exp(-(distance * distance) / (2 * GlowSigma * GlowSigma));
                            if (glow > best)
                            {
                                best = glow;
                                hue = press.Hue;
                            }
                        }

                        if (press.Age > config.Life) continue;
                        var fade = 1 - press.Age / config.Life;
                        for (var ring = 0; ring < config.Rings; ring++)
                        {
                            var radius = press.Age * config.Expansion - ring * config.Spacing;
                            if (radius < -config.Thickness) continue;
                            var delta = distance - radius;
                            var band = Math.Exp(-(delta * delta) / (config.Thickness * config.Thickness)) * fade;
                            if (band <= best) continue;
                            best = band;
                            hue = press.Hue;
                        }
                    }

                    if (best <= 0.003) continue;
                    context.Output[row * context.Columns + column] =
                        StudioRgb.Lerp(StudioRgb.Black, context.Palette.Sample(hue), Math.Min(1, best));
                }
            }
        }
    }
}
