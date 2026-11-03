namespace OpenSynapse.Core.Lighting;

/// <summary>Per-frame inputs that are not part of the serialized effect parameters.</summary>
/// <param name="AudioLevel">Normalized 0..1 audio level.</param>
/// <param name="ScreenSampler">
/// Samples the screen for one matrix cell. Arguments are (column, row); the
/// callback maps them to its own normalized capture coordinates.
/// </param>
/// <param name="ScreenSamplerUv">Normalized (u, v) screen sampler for region mapping.</param>
public readonly record struct StudioEffectInput(
    double AudioLevel = 0,
    Func<int, int, StudioRgb>? ScreenSampler = null,
    Func<double, double, StudioRgb>? ScreenSamplerUv = null);

/// <summary>
/// Renders one studio effect into an arbitrary LED matrix. The renderer is
/// clock-free and hash-based, so identical inputs always produce identical frames.
/// </summary>
public static class StudioEffectRenderer
{
    private const double DefaultBreathingSeconds = 7;
    private const double DefaultSpectrumSeconds = 10;
    private const double DefaultWaveSeconds = 4;
    private const double DefaultReactiveSeconds = 1;
    private const double DefaultRippleSeconds = 2.5;
    private const double DefaultStarlightSeconds = 1.2;
    private const double WheelSeconds = 4;
    private const double TidalFramesPerSecond = 25;

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
            StudioEffectKind.Fire => RenderFire(parameters, rows, columns, time),
            StudioEffectKind.Reactive => RenderReactive(parameters, rows, columns, time),
            StudioEffectKind.Ripple => RenderRipple(parameters, rows, columns, time),
            StudioEffectKind.Starlight => RenderStarlight(parameters, rows, columns, time),
            StudioEffectKind.Ambient => RenderAmbient(parameters, rows, columns, input),
            StudioEffectKind.Audio => RenderAudio(parameters, rows, columns, input),
            StudioEffectKind.Wheel => RenderWheel(parameters, rows, columns, time),
            StudioEffectKind.ChromaGenerate => RenderChromaGenerate(parameters, rows, columns, time),
            StudioEffectKind.Tidal => RenderTidal(parameters, rows, columns, time),
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
        var period = parameters.DurationMs > 0
            ? parameters.DurationMs / 1000d
            : DefaultBreathingSeconds;
        var position = Fraction(time / period);
        var intensity = position switch
        {
            < 0.25 => 0,
            < 0.50 => SmoothStep((position - 0.25) * 4),
            < 0.75 => SmoothStep((0.75 - position) * 4),
            _ => 0,
        };
        var color = parameters.Secondary != parameters.Primary
            ? StudioRgb.Lerp(parameters.Primary, parameters.Secondary, intensity)
            : StudioRgb.Lerp(StudioRgb.Black, parameters.Primary, intensity);
        return Fill(rows, columns, color);
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
        var position = Fraction(time / period);
        var color = parameters.ColorStops.Count >= 2
            ? parameters.Resolve(position)
            : StudioRgb.FromHsv(position, 1, 1);
        return Fill(rows, columns, color);
    }

    private static StudioRgb[] RenderWave(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var cycle = parameters.DurationMs > 0 ? parameters.DurationMs / 1000d : DefaultWaveSeconds;
        var pause = Math.Max(0, parameters.PauseMs) / 1000d;
        var span = cycle + pause;
        var local = span > 0 ? time % span : 0;
        var phase = local < cycle ? local / cycle : 1;
        var angle = parameters.Angle * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var extent = Math.Abs(cosine) + Math.Abs(sine);
        if (extent < 1e-9) extent = 1;

        var width = Math.Clamp(parameters.Width, 0, 100);
        var speed = StudioSpeed.Multiplier(parameters);
        var bands = Math.Max(
            1,
            (int)Math.Round((1 + (100 - width) / 12.5) * (0.5 + speed / 2)));
        var split = Math.Clamp(parameters.Split, 0, 100) / 100;
        var hasStops = parameters.ColorStops.Count >= 2;
        var frame = new StudioRgb[rows * columns];
        for (var row = 0; row < rows; row++)
        {
            var y = rows == 1 ? 0 : row / (double)(rows - 1);
            for (var column = 0; column < columns; column++)
            {
                var x = columns == 1 ? 0 : column / (double)(columns - 1);
                var projection = ((x - 0.5) * cosine + (y - 0.5) * sine) / extent + 0.5;
                var position = Fraction(projection * bands - phase);
                frame[row * columns + column] = hasStops
                    ? parameters.Resolve(position)
                    : position < split ? parameters.Primary : parameters.Secondary;
            }
        }

        return frame;
    }

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

    private static StudioRgb[] RenderFire(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var frame = new StudioRgb[rows * columns];
        var hasStops = parameters.ColorStops.Count >= 2;
        var frameIndex = (long)Math.Floor(time * 12);
        for (var row = 0; row < rows; row++)
        {
            var height = rows == 1 ? 1 : row / (double)(rows - 1);
            for (var column = 0; column < columns; column++)
            {
                var noise = Hash(row, column, frameIndex) / (double)uint.MaxValue;
                var flicker = 0.05 * Math.Sin(time * 2.1 + column * 0.35 + row * 0.6);
                var heat = Math.Clamp(0.18 + 0.72 * height + (noise - 0.5) * 0.55 + flicker, 0, 1);
                var ramp = hasStops
                    ? parameters.Resolve(heat)
                    : heat < 0.5
                        ? StudioRgb.Lerp(parameters.Primary, parameters.Secondary, heat * 2)
                        : StudioRgb.Lerp(parameters.Secondary, StudioRgb.White, (heat - 0.5) * 2);
                frame[row * columns + column] = Scale(ramp, Math.Pow(heat, 0.7));
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
        var phase = Fraction(time / period);
        var exponent = 1 + Math.Clamp(parameters.Decay, 0, 100) / 25;
        var factor = Math.Pow(1 - phase, exponent);
        return Fill(rows, columns, Scale(parameters.Primary, factor));
    }

    private static StudioRgb[] RenderRipple(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var frame = new StudioRgb[rows * columns];
        var centerRow = (rows - 1) / 2d;
        var centerColumn = (columns - 1) / 2d;
        var maxRadius = Math.Sqrt(centerRow * centerRow + centerColumn * centerColumn);
        if (maxRadius < 1e-9) maxRadius = 1;

        var period = parameters.DurationMs > 0 ? parameters.DurationMs / 1000d : DefaultRippleSeconds;
        var phase = Fraction(time / period);
        var rings = 1 + StudioSpeed.Multiplier(parameters);
        var halfBand = 0.05 + 0.45 * Math.Clamp(parameters.Width, 0, 100) / 100;
        var hasStops = parameters.ColorStops.Count >= 2;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var deltaRow = row - centerRow;
                var deltaColumn = column - centerColumn;
                var distance = Math.Sqrt(deltaRow * deltaRow + deltaColumn * deltaColumn) / maxRadius;
                var position = Fraction(distance * rings - phase);
                var band = Math.Max(0, 1 - Math.Abs(position - halfBand) / halfBand);
                if (band <= 0) continue;

                var color = hasStops
                    ? parameters.Resolve(position)
                    : StudioRgb.Lerp(parameters.Primary, parameters.Secondary, position);
                frame[row * columns + column] = Scale(color, band);
            }
        }

        return frame;
    }

    private static StudioRgb[] RenderStarlight(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var frame = new StudioRgb[rows * columns];
        var period = parameters.DurationMs > 0
            ? parameters.DurationMs / 1000d
            : DefaultStarlightSeconds;
        var slot = (long)Math.Floor(time / period);
        var local = Fraction(time / period);
        var density = Math.Clamp(parameters.Density, 0, 100);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var presence = Hash(row, column, slot) % 1000 / 10d;
                if (presence >= density) continue;

                var colorSeed = Hash(column, row, slot + 0x51ED);
                var offset = colorSeed / (double)uint.MaxValue;
                var phase = Fraction(local + offset);
                var brightness = Math.Sin(phase * Math.PI);
                var color = parameters.Resolve(offset);
                frame[row * columns + column] = Scale(color, brightness);
            }
        }

        return frame;
    }

    private static StudioRgb[] RenderAmbient(StudioEffectParams parameters, int rows, int columns, StudioEffectInput input)
    {
        var frame = new StudioRgb[rows * columns];
        if (input.ScreenSamplerUv is { } uv)
        {
            var rect = ResolveScreenRect(parameters);
            for (var index = 0; index < frame.Length; index++)
            {
                frame[index] = uv(
                    rect.X + (index % columns + 0.5) / columns * rect.Width,
                    rect.Y + (index / columns + 0.5) / rows * rect.Height);
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

    private static StudioScreenRect ResolveScreenRect(StudioEffectParams parameters) =>
        parameters.Screen?.Trim().ToLowerInvariant() switch
        {
            "left" => new StudioScreenRect(0, 0, 0.5, 1),
            "top" => new StudioScreenRect(0, 0, 1, 0.5),
            "right" => new StudioScreenRect(0.5, 0, 0.5, 1),
            "bottom" => new StudioScreenRect(0, 0.5, 1, 0.5),
            "custom" when parameters.ScreenRect is { } rect => rect,
            _ => new StudioScreenRect(0, 0, 1, 1),
        };

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
        var boost = Math.Clamp(parameters.Boost, 0, 3);
        var adjusted = Math.Clamp(level * (1 + boost), 0, 1);
        var litColumns = (int)Math.Ceiling(adjusted * columns);
        var exponent = 1 + Math.Clamp(parameters.Decay, 0, 100) / 25;
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

    private static StudioRgb[] RenderWheel(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var frame = new StudioRgb[rows * columns];
        var centerRow = (rows - 1) / 2d;
        var centerColumn = (columns - 1) / 2d;
        var rotation = Fraction(time / WheelSeconds);
        var offset = parameters.Angle / 360d;
        var wraps = 0.5 + StudioSpeed.Multiplier(parameters) / 2;
        var hasStops = parameters.ColorStops.Count >= 2;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var angle = Math.Atan2(row - centerRow, column - centerColumn) / (Math.PI * 2);
                var hue = Fraction(angle * wraps + rotation + offset);
                frame[row * columns + column] = hasStops
                    ? parameters.Resolve(hue)
                    : StudioRgb.FromHsv(hue, 1, 1);
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

    private static StudioRgb[] RenderTidal(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time)
    {
        var frame = new StudioRgb[rows * columns];
        var angle = parameters.Angle * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var centerRow = (rows - 1) / 2d;
        var centerColumn = (columns - 1) / 2d;
        var columnExtent = Math.Max(centerColumn, columns - 1 - centerColumn);
        var rowExtent = Math.Max(centerRow, rows - 1 - centerRow);
        var distance = Math.Abs(columnExtent * cosine) + Math.Abs(rowExtent * sine);
        if (distance < 1e-6) distance = 1;

        var speed = StudioSpeed.Multiplier(parameters);
        var sliceCount = Math.Max(8, (int)Math.Round(12 + speed * 8));
        var spatialStep = distance / sliceCount;
        var cycleFrames = sliceCount * 3;
        var tick = time * TidalFramesPerSecond % cycleFrames;
        var temporalFraction = tick - Math.Floor(tick);
        var splitOffset = (Math.Clamp(parameters.Split, 0, 100) - 50) / 100 * sliceCount;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var projection = ((column - centerColumn) * cosine + (row - centerRow) * sine) /
                    spatialStep;
                for (var lane = 0; lane < 3; lane++)
                {
                    var laneFront = tick - lane * sliceCount - splitOffset;
                    if (laneFront > sliceCount * 2) laneFront -= cycleFrames;

                    var colorFrame = laneFront - projection;
                    if (colorFrame <= 0 || colorFrame >= sliceCount) continue;

                    var position = Math.Clamp(
                        (colorFrame + temporalFraction) / (sliceCount - 1),
                        0,
                        1);
                    frame[row * columns + column] = parameters.Resolve(position);
                    break;
                }
            }
        }

        return frame;
    }

    private readonly record struct ChromaField(uint ConfigHash, uint MediaHash);

    private static StudioRgb[] Fill(int rows, int columns, StudioRgb color)
    {
        var frame = new StudioRgb[rows * columns];
        Array.Fill(frame, color);
        return frame;
    }

    private static StudioRgb Scale(StudioRgb color, double factor)
    {
        var amount = Math.Clamp(factor, 0, 1);
        return new StudioRgb(
            (byte)Math.Clamp(Math.Round(color.R * amount), 0, 255),
            (byte)Math.Clamp(Math.Round(color.G * amount), 0, 255),
            (byte)Math.Clamp(Math.Round(color.B * amount), 0, 255));
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    private static double SmoothStep(double value)
    {
        var clamped = Math.Clamp(value, 0, 1);
        return clamped * clamped * (3 - 2 * clamped);
    }

    private static uint Hash(long first, long second, long third)
    {
        unchecked
        {
            var hash = (ulong)(first * 374761393 + second * 668265263 + third * 2246822519);
            hash = (hash ^ (hash >> 13)) * 1274126177UL;
            return (uint)(hash ^ (hash >> 16));
        }
    }

    private static uint HashText(string? text)
    {
        unchecked
        {
            var hash = 2166136261u;
            if (text is null) return hash;
            foreach (var character in text)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash;
        }
    }
}
