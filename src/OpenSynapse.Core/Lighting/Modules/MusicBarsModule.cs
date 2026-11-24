namespace OpenSynapse.Core.Lighting;

/// <summary>Per-column spectrum bars with an optional centre mirror, peak dots and glow.</summary>
internal sealed class MusicBarsModule : IRenderModuleFactory
{
    public const string Id = "music.bars";

    public RenderModuleInfo Info { get; } = new(Id, "Music bars");

    public IRenderModule Create(RenderModuleParams parameters)
    {
        var config = new Config(
            Mirror: parameters.Boolean("mirror", false),
            Peaks: parameters.Boolean("peaks", true),
            Glow: parameters.Number("glow", 0.3, 0, 1));
        parameters.EnsureKnown();
        return new Layer(config);
    }

    private readonly record struct Config(bool Mirror, bool Peaks, double Glow);

    private sealed class Layer(Config config) : IRenderModule
    {
        public void Render(in RenderModuleContext context)
        {
            Array.Clear(context.Output);
            var audio = context.Audio;
            if (audio is null) return;

            var glowSpan = 0.15 + 0.35 * config.Glow;
            var dot = 0.5 / context.Rows;
            for (var row = 0; row < context.Rows; row++)
            {
                var height = 1 - (row + 0.5) / context.Rows;
                for (var column = 0; column < context.Columns; column++)
                {
                    var nx = (column + 0.5) / context.Columns;
                    var level = audio.Column(nx);
                    var peak = audio.ColumnPeak(nx);
                    var color = context.Palette.Sample(nx);
                    var result = StudioRgb.Black;

                    var barTop = config.Mirror ? 0.5 + level * 0.5 : level;
                    var barBottom = config.Mirror ? 0.5 - level * 0.5 : 0;
                    if (level > 0.01 && height >= barBottom && height <= barTop)
                    {
                        result = StudioRgb.Lerp(StudioRgb.Black, color, 0.55 + 0.45 * level);
                    }
                    else if (config.Glow > 0 && level > 0.01)
                    {
                        var gap = height > barTop ? height - barTop : barBottom - height;
                        if (gap > 0 && gap <= glowSpan)
                        {
                            result = StudioRgb.Lerp(
                                StudioRgb.Black,
                                color,
                                config.Glow * level * (1 - gap / glowSpan) * 0.45);
                        }
                    }

                    if (config.Peaks && peak > 0.06)
                    {
                        var nearTop = Math.Abs(height - (config.Mirror ? 0.5 + peak * 0.5 : peak)) <= dot;
                        var nearBottom = config.Mirror &&
                            Math.Abs(height - (0.5 - peak * 0.5)) <= dot;
                        if (nearTop || nearBottom)
                        {
                            result = StudioRgb.Lerp(result, StudioRgb.White, 0.65);
                        }
                    }

                    context.Output[row * context.Columns + column] = result;
                }
            }
        }
    }
}
