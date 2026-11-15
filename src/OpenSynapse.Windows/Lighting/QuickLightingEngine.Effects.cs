using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

public static partial class QuickLightingEngine
{
    public static RazerRgb[] RenderSolid(RazerRgb color) =>
        Enumerable.Repeat(color, PixelCount).ToArray();

    /// <summary>Matches Synapse's black/black/color/black/black stops over seven seconds.</summary>
    public static RazerRgb[] RenderBreathing(TimeSpan elapsed, RazerRgb color)
    {
        ValidateElapsed(elapsed);
        var position = elapsed.TotalMilliseconds % BreathingPeriodMilliseconds /
            BreathingPeriodMilliseconds;
        var intensity = position switch
        {
            < 0.25 => 0,
            < 0.50 => SmoothStep((position - 0.25) * 4),
            < 0.75 => SmoothStep((0.75 - position) * 4),
            _ => 0,
        };
        return RenderSolid(ScaleColor(color, intensity));
    }

    private static double SmoothStep(double value)
    {
        var clamped = Math.Clamp(value, 0, 1);
        return clamped * clamped * (3 - 2 * clamped);
    }

    public static RazerRgb[] RenderSpectrum(TimeSpan elapsed)
    {
        return RenderSpectrum(elapsed, Rows, Columns);
    }

    public static RazerRgb[] RenderSpectrum(
        TimeSpan elapsed,
        int rows,
        int columns,
        LightingPalette? palette = null)
    {
        ValidateElapsed(elapsed);
        ValidateMatrixDimensions(rows, columns);
        var position = elapsed.TotalMilliseconds % SpectrumPeriodMilliseconds /
            SpectrumPeriodMilliseconds;
        var color = palette is null
            ? RenderSpectrumColor(position)
            : palette.Sample(position);
        return Enumerable.Repeat(color, checked(rows * columns)).ToArray();
    }

    public static RazerRgb[] RenderWave(TimeSpan elapsed, BladeWaveDirection direction)
    {
        return RenderWave(elapsed, direction, Rows, Columns);
    }

    public static RazerRgb[] RenderWave(
        TimeSpan elapsed,
        BladeWaveDirection direction,
        int rows,
        int columns,
        LightingPalette? palette = null)
    {
        ValidateElapsed(elapsed);
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        ValidateMatrixDimensions(rows, columns);

        const int activeFrames = 25;
        var tick = elapsed.TotalSeconds * LightingFramesPerSecond % activeFrames;
        var cosine = direction == BladeWaveDirection.Right ? 1 : -1;
        var frame = new RazerRgb[checked(rows * columns)];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var projected = (int)(column * cosine / (double)columns * activeFrames);
                var colorFrame = (projected + activeFrames - tick) % activeFrames;
                if (colorFrame < 0)
                {
                    colorFrame += activeFrames;
                }
                var position = colorFrame / (double)activeFrames;
                frame[row * columns + column] = palette is null
                    ? RenderSpectrumColor(position, blueStop: 0.67)
                    : palette.Sample(position);
            }
        }

        return frame;
    }

    /// <summary>
    /// Resamples an RGB capture into one average color per matrix cell.
    /// Pixels are row-major and must contain exactly <paramref name="sourceWidth"/> x
    /// <paramref name="sourceHeight"/> values.
    /// </summary>
    public static RazerRgb[] RenderAmbientAwareness(
        IReadOnlyList<RazerRgb> pixels,
        int sourceWidth,
        int sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (sourceWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }

        if (sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeight));
        }

        var expectedLength = checked(sourceWidth * sourceHeight);
        if (pixels.Count != expectedLength)
        {
            throw new ArgumentException("Ambient source pixels must match source dimensions.", nameof(pixels));
        }

        var frame = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        for (var row = 0; row < LogicalRows; row++)
        {
            var y0 = (int)((long)row * sourceHeight / LogicalRows);
            var y1 = Math.Max(y0 + 1, (int)((long)(row + 1) * sourceHeight / LogicalRows));
            y1 = Math.Min(y1, sourceHeight);
            for (var column = 0; column < LogicalColumns; column++)
            {
                var x0 = (int)((long)column * sourceWidth / LogicalColumns);
                var x1 = Math.Max(x0 + 1, (int)((long)(column + 1) * sourceWidth / LogicalColumns));
                x1 = Math.Min(x1, sourceWidth);
                long red = 0;
                long green = 0;
                long blue = 0;
                var sampleCount = 0;
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var pixel = pixels[y * sourceWidth + x];
                        red += pixel.Red;
                        green += pixel.Green;
                        blue += pixel.Blue;
                        sampleCount++;
                    }
                }

                frame[row * LogicalColumns + column] = new RazerRgb(
                    Average(red, sampleCount),
                    Average(green, sampleCount),
                    Average(blue, sampleCount));
            }
        }

        return BladeLightingLayout.MapToDeviceFrame(frame);
    }

    /// <summary>
    /// Renders a left-to-right audio meter. <paramref name="level"/> is normalized
    /// RMS/peak input from a normalized audio adapter; color boost expands the lit range.
    /// </summary>
    public static RazerRgb[] RenderAudioMeter(double level, double colorBoost)
    {
        var logical = RenderAudioMeter(
            level,
            colorBoost,
            LogicalRows,
            LogicalColumns);
        return BladeLightingLayout.MapToDeviceFrame(logical);
    }

    public static RazerRgb[] RenderAudioMeter(
        double level,
        double colorBoost,
        int rows,
        int columns,
        LightingPalette? palette = null)
    {
        ValidateNormalized(level, nameof(level));
        ValidateNormalized(colorBoost, nameof(colorBoost));
        ValidateMatrixDimensions(rows, columns);

        var adjustedLevel = level < AudioSilenceThreshold
            ? 0
            : Math.Clamp(level * (1 + colorBoost), 0, 1);
        var litColumns = (int)Math.Ceiling(adjustedLevel * columns);
        var frame = new RazerRgb[checked(rows * columns)];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (column >= litColumns)
                {
                    continue;
                }

                var position = columns == 1 ? 0 : column / (double)(columns - 1);
                var color = palette is null
                    ? RenderAudioMeterColor(position)
                    : palette.Sample(position);
                frame[row * columns + column] = ScaleColor(color, 1 + colorBoost);
            }
        }

        return frame;
    }

    /// <summary>
    /// Smooths audio-driven brightness without delaying the lighting loop.
    /// Attack is deliberately faster than release so transients remain visible
    /// while the meter does not chatter when a sample falls between packets.
    /// </summary>
    public static double SmoothAudioLevel(
        double previous,
        double current,
        TimeSpan elapsed)
    {
        ValidateNormalized(previous, nameof(previous));
        ValidateNormalized(current, nameof(current));
        if (elapsed <= TimeSpan.Zero)
        {
            return current;
        }

        var timeConstant = current >= previous ? AudioAttackSeconds : AudioReleaseSeconds;
        var factor = 1 - Math.Exp(-elapsed.TotalSeconds / timeConstant);
        return previous + (current - previous) * factor;
    }

    /// <summary>Renders Product 710's native 100-keyframe, five-step fire cycle.</summary>
    public static RazerRgb[] RenderFire(TimeSpan elapsed, int seed)
    {
        return RenderFire(elapsed, seed, Rows, Columns);
    }

    public static RazerRgb[] RenderFire(
        TimeSpan elapsed,
        int seed,
        int rows,
        int columns,
        LightingPalette? palette = null)
    {
        ValidateElapsed(elapsed);
        ValidateMatrixDimensions(rows, columns);
        var keyFrames = BuildFireKeyFrames(seed);
        var animationFrame = elapsed.TotalSeconds * LightingFramesPerSecond %
            (FireKeyFrameCount * FireInterpolationFrames);
        var keyFrame = (int)(animationFrame / FireInterpolationFrames);
        var nextKeyFrame = (keyFrame + 1) % FireKeyFrameCount;
        var interpolation = animationFrame % FireInterpolationFrames / (double)FireInterpolationFrames;
        var frame = new RazerRgb[checked(rows * columns)];
        for (var outputRow = 0; outputRow < rows; outputRow++)
        {
            var sourceRow = rows <= FireWorkRows
                ? FireWorkRows - 1 - outputRow
                : FireWorkRows - 1 - (int)Math.Round(
                    outputRow * (FireWorkRows - 1d) / Math.Max(1, rows - 1),
                    MidpointRounding.AwayFromZero);
            sourceRow = Math.Clamp(sourceRow, 0, FireWorkRows - 1);
            var lookup = FireColorLookup[sourceRow];
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = columns <= FireWorkColumns
                    ? column
                    : (int)Math.Round(
                        column * (FireWorkColumns - 1d) / Math.Max(1, columns - 1),
                        MidpointRounding.AwayFromZero);
                sourceColumn = Math.Clamp(sourceColumn, 0, FireWorkColumns - 1);
                var cell = sourceRow * FireWorkColumns + sourceColumn;
                var start = keyFrames[keyFrame * FireWorkRows * FireWorkColumns + cell];
                var end = keyFrames[nextKeyFrame * FireWorkRows * FireWorkColumns + cell];
                var heat = (byte)(start + (end - start) * interpolation);
                frame[outputRow * columns + column] = palette is null
                    ? RenderFireRamp(lookup[sourceColumn], heat)
                    : ScaleColor(palette.Sample(heat / 255d), heat / 255d);
            }
        }

        return frame;
    }

    /// <summary>Renders Product 710's default three-lane Tidal projection.</summary>
    public static RazerRgb[] RenderTidal(
        TimeSpan elapsed,
        RazerRgb firstColor,
        RazerRgb secondColor,
        byte speed = 2)
    {
        ValidateElapsed(elapsed);
        if (speed is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(speed));
        const double angleRadians = 160 * Math.PI / 180;
        const double centerColumn = 8;
        const double centerRow = 3;
        var rate = speed * 5d;
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);
        var columnExtent = Math.Max(centerColumn, Columns - centerColumn);
        var rowExtent = Math.Max(centerRow, Rows - centerRow);
        var distance = Math.Abs(columnExtent * cos) + Math.Abs(rowExtent * sin);
        var spatialStep = ((rate * 2) / 100 * distance) / LightingFramesPerSecond;
        var colorFrameCount = Math.Max(8, (int)(distance / spatialStep));
        var cycleFrames = colorFrameCount * 3;
        var tick = elapsed.TotalSeconds * LightingFramesPerSecond % cycleFrames;
        var temporalFraction = tick - Math.Floor(tick);
        var frame = new RazerRgb[PixelCount];
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var projection = ((column - centerColumn) * cos + (row - centerRow) * sin) /
                    spatialStep;
                // Native lanes start at 0, -N, and -2N; pixels sample behind each moving front.
                for (var lane = 0; lane < 3; lane++)
                {
                    var laneFront = tick - lane * colorFrameCount;
                    if (laneFront > colorFrameCount * 2)
                    {
                        laneFront -= cycleFrames;
                    }

                    var colorFrame = laneFront - projection;
                    if (colorFrame <= 0 || colorFrame >= colorFrameCount)
                    {
                        continue;
                    }

                    frame[row * Columns + column] = RenderTidalColor(
                        ((int)colorFrame + temporalFraction) /
                        (double)(colorFrameCount - 1),
                        firstColor,
                        secondColor);
                    break;
                }
            }
        }
        return frame;
    }

    /// <summary>Renders keyboard-triggered cells with linear fade-out.</summary>
    public static RazerRgb[] RenderReactive(
        TimeSpan elapsed,
        IReadOnlyList<QuickLightingKeyEvent> events,
        RazerRgb color,
        TimeSpan duration)
    {
        ValidateElapsed(elapsed);
        ValidateDuration(duration);
        ArgumentNullException.ThrowIfNull(events);

        var frame = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        foreach (var keyEvent in events)
        {
            ValidateCoordinates(keyEvent.Row, keyEvent.Column);
            ValidateEventTime(keyEvent.At);
            if (keyEvent.At > elapsed)
            {
                continue;
            }

            var age = elapsed - keyEvent.At;
            if (age >= duration)
            {
                continue;
            }

            var factor = SmoothStep(1 - age.TotalMilliseconds / duration.TotalMilliseconds);
            var index = keyEvent.Row * LogicalColumns + keyEvent.Column;
            var candidate = ScaleColor(color, factor);
            frame[index] = MaxColor(frame[index], candidate);
        }

        return BladeLightingLayout.MapToDeviceFrame(frame);
    }

    /// <summary>Renders expanding rings around keyboard events with linear fade-out.</summary>
    public static RazerRgb[] RenderRipple(
        TimeSpan elapsed,
        IReadOnlyList<QuickLightingKeyEvent> events,
        RazerRgb color,
        TimeSpan duration)
    {
        ValidateElapsed(elapsed);
        ValidateDuration(duration);
        ArgumentNullException.ThrowIfNull(events);

        var frame = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        var maxRadius = Math.Sqrt(
            (LogicalRows - 1) * (LogicalRows - 1) +
            (LogicalColumns - 1) * (LogicalColumns - 1));
        foreach (var keyEvent in events)
        {
            ValidateCoordinates(keyEvent.Row, keyEvent.Column);
            ValidateEventTime(keyEvent.At);
            if (keyEvent.At > elapsed)
            {
                continue;
            }

            var age = elapsed - keyEvent.At;
            if (age >= duration)
            {
                continue;
            }

            var progress = age.TotalMilliseconds / duration.TotalMilliseconds;
            var radius = progress * maxRadius;
            var fade = SmoothStep(1 - progress);
            for (var row = 0; row < LogicalRows; row++)
            {
                for (var column = 0; column < LogicalColumns; column++)
                {
                    var distance = Math.Sqrt(
                        (row - keyEvent.Row) * (row - keyEvent.Row) +
                        (column - keyEvent.Column) * (column - keyEvent.Column));
                    var ring = Math.Max(0, 1 - Math.Abs(distance - radius));
                    if (ring <= 0)
                    {
                        continue;
                    }

                    var candidate = ScaleColor(color, ring * fade);
                    var index = row * LogicalColumns + column;
                    frame[index] = MaxColor(frame[index], candidate);
                }
            }
        }

        return BladeLightingLayout.MapToDeviceFrame(frame);
    }

    /// <summary>Renders a full-saturation hue wheel, rotating in the requested direction.</summary>
    public static RazerRgb[] RenderWheel(TimeSpan elapsed, QuickLightingDirection direction)
    {
        return RenderWheel(elapsed, direction, LogicalRows, LogicalColumns);
    }

    public static RazerRgb[] RenderWheel(
        TimeSpan elapsed,
        QuickLightingDirection direction,
        int rows,
        int columns,
        LightingPalette? palette = null,
        byte speed = 2)
    {
        ValidateElapsed(elapsed);
        if (speed is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(speed));
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        ValidateMatrixDimensions(rows, columns);

        var frame = new RazerRgb[checked(rows * columns)];
        var phase = elapsed.TotalSeconds / WheelPeriodSeconds * 360 * (speed / 2d) *
            (direction == QuickLightingDirection.Clockwise ? 1 : -1);
        var centerRow = (rows - 1) / 2d;
        var centerColumn = (columns - 1) / 2d;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var hue = ((Math.Atan2(row - centerRow, column - centerColumn) * 180 / Math.PI + phase) % 360 + 360) % 360;
                frame[row * columns + column] = palette is null
                    ? HsvToRgb(hue, 1, 1)
                    : palette.Sample(hue / 360, wrap: true);
            }
        }

        return rows == LogicalRows && columns == LogicalColumns
            ? BladeLightingLayout.MapToDeviceFrame(frame)
            : frame;
    }
}
