namespace OpenSynapse.Core.Lighting;

/// <summary>Per-frame inputs; FireSeed/FireRate override the serialized Fire values.</summary>
/// <param name="AudioLevel">Normalized 0..1 audio level.</param>
/// <param name="ScreenSampler">
/// Samples the screen for one matrix cell. Arguments are (column, row); the
/// callback maps them to its own normalized capture coordinates.
/// </param>
/// <param name="ScreenSamplerUv">Normalized (u, v) screen sampler for region mapping.</param>
/// <param name="ScreenCaptureRect">
/// Normalized screen region already cropped by the capture source; effect regions
/// are mapped into it so a shared capture does not double-crop.
/// </param>
public readonly record struct StudioEffectInput(
    double AudioLevel = 0,
    Func<int, int, StudioRgb>? ScreenSampler = null,
    Func<double, double, StudioRgb>? ScreenSamplerUv = null,
    StudioScreenRect? ScreenCaptureRect = null,
    uint? StarlightSeed = null,
    int? FireSeed = null,
    int? FireRate = null);

/// <summary>
/// Renders one studio effect into an arbitrary LED matrix. Every effect is a pure
/// function of its parameters and time; Starlight keeps a per-thread tick cursor so
/// long playback advances its native random stream instead of replaying it.
/// </summary>
public static partial class StudioEffectRenderer
{
    private const double DefaultBreathingSeconds = 7;
    private const double DefaultSpectrumSeconds = 10;
    private const double DefaultReactiveSeconds = 1;
    private const double DefaultStarlightSeconds = 1.2;
    /// <summary>Chroma Studio's native playback rate in frames per second.</summary>
    public const double NativeFramesPerSecond = 25;

    public static StudioRgb[] Render(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double seconds,
        StudioEffectInput input = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if ((long)rows * columns > StudioCompositor.MaxPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "The matrix is larger than the supported canvas.");
        }

        var time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        return parameters.Effect switch
        {
            StudioEffectKind.Solid => RenderSolid(parameters, rows, columns),
            StudioEffectKind.Breathing => RenderBreathing(parameters, rows, columns, time),
            StudioEffectKind.Spectrum => RenderSpectrum(parameters, rows, columns, time),
            StudioEffectKind.Wave => RenderWave(parameters, rows, columns, time),
            StudioEffectKind.Gradient => RenderGradient(parameters, rows, columns),
            StudioEffectKind.Fire => RenderFire(
                parameters,
                rows,
                columns,
                time,
                input.FireSeed ?? parameters.FireSeed,
                input.FireRate ?? parameters.FireRate),
            StudioEffectKind.Reactive => RenderReactive(parameters, rows, columns, time),
            StudioEffectKind.Ripple => RenderRipple(parameters, rows, columns, time),
            StudioEffectKind.Starlight => RenderStarlight(parameters, rows, columns, time, input.StarlightSeed),
            StudioEffectKind.Ambient => RenderAmbient(parameters, rows, columns, input),
            StudioEffectKind.Audio => RenderAudio(parameters, rows, columns, input),
            StudioEffectKind.Wheel => RenderWheel(parameters, rows, columns, time),
            StudioEffectKind.ChromaGenerate => RenderChromaGenerate(parameters, rows, columns, time),
            StudioEffectKind.Tidal => RenderTidal(parameters, rows, columns, time),
            StudioEffectKind.Radiate => RenderRadiate(parameters, rows, columns),
            _ => RenderSolid(parameters, rows, columns),
        };
    }

    private static StudioRgb[] RenderSolid(StudioEffectParams parameters, int rows, int columns) =>
        Fill(rows, columns, parameters.Primary);

    private static StudioRgb[] RenderBreathing(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        // Native 14s Duration renders a 7s cycle: quarter off, quarter ramp,
        // quarter hold, quarter fade (oracle effect_5).
        var period = parameters.DurationMs > 0
            ? parameters.DurationMs / 2000d
            : DefaultBreathingSeconds;
        var position = Fraction(time / period);
        var intensity = position switch
        {
            < 0.25 => 0,
            < 0.50 => (position - 0.25) * 4,
            < 0.75 => 1,
            _ => (1 - position) * 4,
        };
        if (parameters.RandomColor)
        {
            return Fill(rows, columns, Scale(RandomHue((long)Math.Floor(time / period)), intensity));
        }

        if (parameters.ColorStops.Count > 0)
        {
            // CBreathingEffect 0x650f0 builds a duration-length palette and
            // advances one entry per 25 fps tick; the stops already describe
            // the two peaks, so no separate envelope is applied.
            var frames = PaletteFrames(parameters, 14_000);
            var palette = GradientTable(parameters, frames);
            return Fill(rows, columns, palette[PaletteCursor(time, frames)]);
        }

        // Preserve the UI contract for layers that use only Color/Color2.
        return Fill(rows, columns, StudioRgb.Lerp(parameters.Primary, parameters.Secondary, intensity));
    }

    private static StudioRgb[] RenderSpectrum(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var period = parameters.DurationMs > 0
            ? parameters.DurationMs / 1000d
            : DefaultSpectrumSeconds;
        if (parameters.ColorStops.Count >= 2)
        {
            // CSpectrumEffect 0x61dc0 writes one palette entry per 25 fps tick.
            var frames = PaletteFrames(parameters, 37_740);
            var palette = GradientTable(parameters, frames);
            return Fill(rows, columns, palette[PaletteCursor(time, frames)]);
        }

        return Fill(rows, columns, StudioRgb.FromHsv(Fraction(time / period), 1, 1));
    }

    private static StudioRgb[] RenderWave(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        // CWaveEffect 0x65f50: a palette cursor advances one entry per 25 fps
        // frame. Width controls how many frames one palette spans; Duration is
        // not read by the native factory. Pause adds empty frames to the cycle.
        var angle = (parameters.Angle + 270) * Math.PI / 180d;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        if (Math.Abs(cosine) < 1e-12) cosine = 0;
        if (Math.Abs(sine) < 1e-12) sine = 0;

        var timing = MeasureWave(parameters, rows, columns);
        var palette = GradientTable(parameters, timing.ActiveFrames);

        var tick = (int)Math.Floor(time * NativeFramesPerSecond);
        var cursor = timing.Speed <= 0 || tick <= 0
            ? 0
            : (tick - 1) % timing.CycleFrames;
        var offset = timing.CycleFrames - cursor;
        var frame = new StudioRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var y = row / (double)rows;
            for (var column = 0; column < columns; column++)
            {
                var x = column / (double)columns;
                var projection = (int)((x * cosine + y * sine) * timing.SpanFrames);
                var index = ((projection + offset) % timing.CycleFrames + timing.CycleFrames) %
                    timing.CycleFrames;
                if (index < palette.Length) frame[row * columns + column] = palette[index];
            }
        }

        return frame;
    }

    private static WaveTiming MeasureWave(StudioEffectParams parameters, int rows, int columns)
    {
        var angle = (parameters.Angle + 270) * Math.PI / 180d;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        if (Math.Abs(cosine) < 1e-12) cosine = 0;
        if (Math.Abs(sine) < 1e-12) sine = 0;

        // Native samples LED edges and truncates the projected extent.
        var extent = Math.Max(1, (int)(Math.Abs(cosine) * columns) + (int)(Math.Abs(sine) * rows));
        var speed = Math.Clamp(parameters.Speed, 0, 50);
        var step = speed <= 0
            ? 1d / NativeFramesPerSecond
            : 4 * speed / 100d * extent / NativeFramesPerSecond;
        var spanFrames = Math.Max(1, (int)(extent / step));
        var width = Math.Clamp(parameters.Width, 10, 400);
        var widthFrames = Math.Max(1, (int)(spanFrames * width / 100d));
        var stopCount = Math.Max(PaletteStopCount(parameters), 2);
        var activeFrames = Math.Max(widthFrames, stopCount);
        var pauseFrames = (int)(Math.Max(0, parameters.PauseMs) * NativeFramesPerSecond / 1000);
        return new WaveTiming(spanFrames, activeFrames, activeFrames + pauseFrames, speed);
    }

    /// <summary>
    /// The renderer's visible cycle, shared with the playback clock so an
    /// "after N cycles" stop lands on the same frame the user sees.
    /// </summary>
    public static double CycleSeconds(StudioEffectParams parameters, int rows, int columns)
    {
        var seconds = parameters.Effect switch
        {
            StudioEffectKind.Breathing => parameters.DurationMs > 0
                ? parameters.DurationMs / 2000d
                : DefaultBreathingSeconds,
            StudioEffectKind.Spectrum => parameters.DurationMs > 0
                ? parameters.DurationMs / 1000d
                : DefaultSpectrumSeconds,
            StudioEffectKind.Reactive => parameters.DurationMs > 0
                ? parameters.DurationMs / 1000d
                : DefaultReactiveSeconds,
            StudioEffectKind.Starlight => parameters.DurationMs > 0
                ? parameters.DurationMs / 1000d
                : DefaultStarlightSeconds,
            StudioEffectKind.Wave when rows > 0 && columns > 0 =>
                MeasureWave(parameters, rows, columns).CycleFrames / NativeFramesPerSecond,
            StudioEffectKind.Ripple when rows > 0 && columns > 0 =>
                MeasureRipple(parameters, rows, columns).LifetimeSeconds,
            StudioEffectKind.Wheel when MeasureWheelSteps(parameters) > 0 =>
                MeasureWheelSteps(parameters) / (double)NativeFramesPerSecond,
            StudioEffectKind.Tidal when rows > 0 && columns > 0 =>
                (3 * MeasureTidal(parameters, rows, columns).Width + 1) /
                (double)NativeFramesPerSecond,
            _ => Math.Max(1, parameters.DurationMs > 0
                ? parameters.DurationMs
                : StudioEffectDefaults.DurationMs(parameters.Effect)) / 1000d,
        };
        return Math.Max(0.05, seconds);
    }

    private readonly record struct WaveTiming(
        int SpanFrames,
        int ActiveFrames,
        int CycleFrames,
        double Speed);

    private static StudioRgb[] RenderGradient(StudioEffectParams parameters, int rows, int columns)
    {
        var frame = new StudioRgb[rows * columns];
        for (var column = 0; column < columns; column++)
        {
            var position = columns == 1 ? 0 : column / (double)(columns - 1);
            var color = StudioRgb.Lerp(parameters.Primary, parameters.Secondary, position);
            for (var row = 0; row < rows; row++)
            {
                frame[row * columns + column] = color;
            }
        }

        return frame;
    }

    private static StudioRgb[] RenderReactive(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var period = parameters.DurationMs > 0 ? parameters.DurationMs / 1000d : DefaultReactiveSeconds;
        var frames = PaletteFrames(parameters, 1_500);
        if (parameters.RandomColor)
        {
            var peak = RandomHue((long)Math.Floor(time / period));
            var random = GradientTable([], peak, StudioRgb.Black, frames);
            return Fill(rows, columns, random[PaletteCursor(time, frames)]);
        }

        // CReactiveEffect 0x63680 fades the stop palette to the transparent
        // sentinel at one 25 fps frame per entry.
        var palette = parameters.ColorStops.Count > 0
            ? GradientTable(parameters, frames)
            : GradientTable([], parameters.Primary, StudioRgb.Black, frames);
        return Fill(rows, columns, palette[PaletteCursor(time, frames)]);
    }

    private static StudioRgb[] RenderAmbient(StudioEffectParams parameters, int rows, int columns, StudioEffectInput input)
    {
        var frame = new StudioRgb[rows * columns];
        if (input.ScreenSamplerUv is { } uv)
        {
            var rect = StudioScreenRect.Resolve(parameters.Screen, parameters.ScreenRect);
            var capture = input.ScreenCaptureRect?.Clamp();
            if (capture is { } cropped)
            {
                rect = StudioScreenRect.MapIntoCapture(rect, cropped);
            }
            for (var index = 0; index < frame.Length; index++)
            {
                var u = rect.X + (index % columns + 0.5) / columns * rect.Width;
                var v = rect.Y + (index / columns + 0.5) / rows * rect.Height;
                if (capture is { } source)
                {
                    u = source.X + u * source.Width;
                    v = source.Y + v * source.Height;
                }
                frame[index] = uv(u, v);
            }
        }
        else if (input.ScreenSampler is { } grid)
        {
            // The integer sampler already maps the full screen to the matrix.
            for (var index = 0; index < frame.Length; index++)
                frame[index] = grid(index % columns, index / columns);
        }
        else return frame;
        return Blur(frame, rows, columns, Math.Clamp(parameters.Blur, 0, 20));
    }

    private static StudioRgb[] Blur(StudioRgb[] frame, int rows, int columns, int radius)
    {
        if (radius <= 0) return frame;
        var blurred = new StudioRgb[frame.Length];
        var count = (2 * radius + 1) * (2 * radius + 1);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                long red = 0, green = 0, blue = 0;
                for (var offsetRow = -radius; offsetRow <= radius; offsetRow++)
                {
                    var rowOffset = Math.Clamp(row + offsetRow, 0, rows - 1) * columns;
                    for (var offsetColumn = -radius; offsetColumn <= radius; offsetColumn++)
                    {
                        var sample = frame[rowOffset + Math.Clamp(column + offsetColumn, 0, columns - 1)];
                        red += sample.R;
                        green += sample.G;
                        blue += sample.B;
                    }
                }

                blurred[row * columns + column] = new StudioRgb(
                    (byte)((red + count / 2) / count),
                    (byte)((green + count / 2) / count),
                    (byte)((blue + count / 2) / count));
            }
        }
        return blurred;
    }

    private static StudioRgb[] RenderAudio(
        StudioEffectParams parameters,
        int rows,
        int columns,
        StudioEffectInput input)
    {
        var frame = new StudioRgb[rows * columns];
        var level = double.IsFinite(input.AudioLevel) ? Math.Clamp(input.AudioLevel, 0, 1) : 0;
        var boost = Math.Clamp(parameters.Boost, 0.25, 4);
        if (parameters.AutoBoost) boost = Math.Max(boost, 1 + level * 3);
        var adjusted = Math.Clamp(level * boost, 0, 1);
        var litColumns = (int)Math.Ceiling(adjusted * columns);
        var exponent = 0.25 + Math.Clamp(parameters.Decay, 0.1, 2) * 1.25;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (column >= litColumns) continue;

                var edge = litColumns <= 1 ? 0 : (litColumns - 1 - column) / (double)(litColumns - 1);
                var brightness = 0.1 + 0.9 * Math.Pow(1 - edge, exponent);
                var position = columns == 1 ? 0 : column / (double)(columns - 1);
                frame[row * columns + column] = Scale(parameters.Resolve(position), brightness);
            }
        }

        return frame;
    }

    private static StudioRgb[] RenderChromaGenerate(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var field = new ChromaField(HashText(parameters.CurrentConfigJson), HashText(parameters.MediaSrc));
        if (parameters.MediaSampler is not { } sampler)
        {
            return RenderChromaGenerateFallback(parameters, field, rows, columns, time);
        }

        var palette = StudioMediaPalette.BuildPalette(parameters, sampler);
        if (palette.Length == 0)
        {
            return RenderChromaGenerateFallback(parameters, field, rows, columns, time);
        }

        var config = parameters.GenerateConfig;
        if (config.Pattern == 2)
        {
            // Pattern 2 adds a slow cosine oscillation to the rotation angle.
            config = config with
            {
                Rotation = config.Rotation + 180 * Math.Cos(Math.PI * (time / 180) / 50),
            };
        }

        var frame = new StudioRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var y = rows == 1 ? 0 : row / (double)(rows - 1);
            for (var column = 0; column < columns; column++)
            {
                var x = columns == 1 ? 0 : column / (double)(columns - 1);
                var (u, v) = StudioMediaPalette.Transform(x, y, config);
                var index = row * columns + column;
                if (!sampler.TrySampleAt(u, v, time, out var sample))
                {
                    frame[index] = ProceduralChroma(parameters, field, x, y, time);
                    continue;
                }

                var pattern = StudioMediaPalette.SamplePattern(palette, u, v, config, time);
                frame[index] = StudioMediaPalette.ApplyBlend(
                    config.Blend,
                    sample,
                    pattern,
                    palette,
                    StudioMediaPalette.Lightness(sample));
            }
        }

        return frame;
    }

    private static StudioRgb[] RenderChromaGenerateFallback(
        StudioEffectParams parameters,
        ChromaField field,
        int rows,
        int columns,
        double time)
    {
        var frame = new StudioRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var y = rows == 1 ? 0 : row / (double)(rows - 1);
            for (var column = 0; column < columns; column++)
            {
                var x = columns == 1 ? 0 : column / (double)(columns - 1);
                frame[row * columns + column] = ProceduralChroma(parameters, field, x, y, time);
            }
        }

        return frame;
    }

    private static StudioRgb ProceduralChroma(
        StudioEffectParams parameters,
        ChromaField field,
        double x,
        double y,
        double time)
    {
        var configHash = field.ConfigHash;
        var mediaHash = field.MediaHash;
        var firstFrequency = 2 + configHash % 1000 / 250d;
        var secondFrequency = 2 + mediaHash % 1000 / 250d;
        var firstPhase = configHash % 6283 / 1000d;
        var secondPhase = mediaHash % 6283 / 1000d;
        var drift = 0.25 + configHash % 100 / 100d;
        var value = 0.5
            + 0.25 * Math.Sin(x * firstFrequency * Math.PI + time * drift + firstPhase)
            + 0.25 * Math.Sin(y * secondFrequency * Math.PI - time * drift * 0.7 + secondPhase)
            + 0.2 * Math.Sin((x + y) * 3 + time * 0.4 + firstPhase * 0.5);
        return parameters.Resolve(Math.Clamp(value, 0, 1));
    }
}
