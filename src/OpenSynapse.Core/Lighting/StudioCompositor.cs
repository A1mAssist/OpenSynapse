namespace OpenSynapse.Core.Lighting;

/// <summary>V2 render input: one layer with its own parameters and alpha mask.</summary>
public readonly record struct StudioLayerRender(
    StudioEffectParams Params,
    byte[]? Mask,
    double Opacity,
    StudioBlendMode Blend,
    bool Enabled = true,
    Guid Id = default);

/// <summary>
/// Deterministic layer compositor. It has no device, clock, capture, or UI
/// dependencies, so the same document renders identically in preview and on
/// the hardware frame thread.
/// </summary>
public static class StudioCompositor
{
    public const int MaxPixels = 4096;
    private const double BreathingPeriodSeconds = 7;
    private const double SpectrumPeriodSeconds = 20;

    /// <summary>
    /// Renders the v2 layer stack. The list is top-first; masks are 0..100 per
    /// LED and the engine blend factor is opacity * mask, matching the native
    /// steady-state formula with alpha = 0.
    /// </summary>
    public static StudioRgb[] RenderLayers(
        IReadOnlyList<StudioLayerRender> layers,
        int rows,
        int columns,
        double seconds,
        StudioEffectInput input = default)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var timed = new (StudioLayerRender Layer, double Seconds)[layers.Count];
        for (var index = 0; index < layers.Count; index++)
        {
            timed[index] = (layers[index], seconds);
        }

        return RenderTimedLayers(timed, rows, columns, input);
    }

    /// <summary>
    /// Same composite, but every layer supplies its own local time. Playback
    /// clocks resolve to distinct offsets for each layer.
    /// </summary>
    public static StudioRgb[] RenderTimedLayers(
        IReadOnlyList<(StudioLayerRender Layer, double Seconds)> layers,
        int rows,
        int columns,
        StudioEffectInput input = default)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (rows <= 0 || columns <= 0 || rows * columns > MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "The studio canvas is outside the supported range.");
        }

        var frame = new StudioRgb[rows * columns];
        for (var index = layers.Count - 1; index >= 0; index--)
        {
            var (layer, seconds) = layers[index];
            if (!layer.Enabled || layer.Opacity <= 0) continue;
            if (layer.Mask is { } mask && mask.Length != frame.Length)
            {
                throw new ArgumentException("The layer mask does not match the canvas size.", nameof(layers));
            }

            var time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
            var effect = StudioEffectRenderer.Render(layer.Params, rows, columns, time, input);
            var alpha = Math.Clamp(layer.Opacity / 100, 0, 1);
            for (var pixel = 0; pixel < frame.Length; pixel++)
            {
                var factor = layer.Mask is null ? 1 : layer.Mask[pixel] / 100d;
                frame[pixel] = Blend(frame[pixel], effect[pixel], alpha * factor, layer.Blend);
            }
        }

        return frame;
    }

    public static StudioRgb[] Render(
        IReadOnlyList<StudioLayerSnapshot> layers,
        int rows,
        int columns,
        TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (rows <= 0 || columns <= 0 || rows * columns > MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "The studio canvas is outside the supported range.");
        }

        var frame = new StudioRgb[rows * columns];
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        // The list is top-first, so paint from the bottom of the stack upward.
        for (var index = layers.Count - 1; index >= 0; index--)
        {
            var layer = layers[index];
            if (layer.Opacity <= 0) continue;
            RenderLayer(layer, rows, columns, seconds, frame);
        }

        return frame;
    }

    private static void RenderLayer(
        StudioLayerSnapshot layer,
        int rows,
        int columns,
        double seconds,
        StudioRgb[] frame)
    {
        var alpha = Math.Clamp(layer.Opacity / 100, 0, 1);
        var speed = Math.Clamp(layer.Speed, 0.1, 5);
        var time = seconds * speed;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var index = row * columns + column;
                var color = Sample(layer, row, column, rows, columns, time, seconds);
                frame[index] = Blend(frame[index], color, alpha, layer.Blend);
            }
        }
    }

    private static StudioRgb Sample(
        StudioLayerSnapshot layer,
        int row,
        int column,
        int rows,
        int columns,
        double time,
        double absoluteSeconds)
    {
        switch (layer.Effect)
        {
            case StudioEffectKind.Solid:
                return layer.Primary;
            case StudioEffectKind.Gradient:
                return StudioRgb.Lerp(
                    layer.Primary,
                    layer.Secondary,
                    columns <= 1 ? 0 : column / (double)(columns - 1));
            case StudioEffectKind.Breathing:
            {
                var position = absoluteSeconds % BreathingPeriodSeconds / BreathingPeriodSeconds;
                var intensity = 0.5 - 0.5 * Math.Cos(position * Math.PI * 2);
                return StudioRgb.Lerp(StudioRgb.Black, layer.Primary, intensity);
            }
            case StudioEffectKind.Spectrum:
                return StudioRgb.FromHsv(absoluteSeconds / SpectrumPeriodSeconds, 1, 1);
            case StudioEffectKind.Wave:
            {
                var hue = column / (double)columns - time / 4;
                return StudioRgb.FromHsv(hue, 1, 1);
            }
            case StudioEffectKind.Fire:
            {
                var frame = (int)(time * 12);
                var noise = Noise(row, column, frame) / (double)uint.MaxValue;
                var heat = Math.Clamp(
                    0.52 + 0.34 * Math.Sin(time * 2.1 + row * 0.8 + column * 0.35) +
                    0.28 * (noise - 0.5),
                    0,
                    1);
                var color = StudioRgb.Lerp(StudioRgb.Black, layer.Primary, Math.Pow(heat, 0.7));
                return StudioRgb.Lerp(color, StudioRgb.White, Math.Max(0, heat - 0.82) * 3);
            }
            default:
                return layer.Primary;
        }
    }

    private static StudioRgb Blend(StudioRgb destination, StudioRgb source, double alpha, StudioBlendMode mode)
    {
        var d = destination;
        return new StudioRgb(
            BlendChannel(d.R, source.R, alpha, mode),
            BlendChannel(d.G, source.G, alpha, mode),
            BlendChannel(d.B, source.B, alpha, mode));
    }

    private static byte BlendChannel(byte destination, byte source, double alpha, StudioBlendMode mode)
    {
        var d = destination / 255d;
        var s = source / 255d;
        var result = mode switch
        {
            StudioBlendMode.Add => d + s * alpha,
            StudioBlendMode.Multiply => d * (1 - alpha + alpha * s),
            StudioBlendMode.Screen => 1 - (1 - d) * (1 - alpha * s),
            _ => s * alpha + d * (1 - alpha),
        };
        return (byte)Math.Clamp(Math.Round(result * 255), 0, 255);
    }

    private static uint Noise(int row, int column, int frame)
    {
        unchecked
        {
            var hash = (uint)(row * 374761393 + column * 668265263 + frame * 2246822519);
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            return hash ^ (hash >> 16);
        }
    }
}
