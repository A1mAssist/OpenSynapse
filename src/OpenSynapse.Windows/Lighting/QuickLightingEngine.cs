using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Direction used by the software-rendered color wheel.
/// </summary>
public enum QuickLightingDirection
{
    Clockwise,
    CounterClockwise,
}

/// <summary>
/// A keyboard input event expressed in Blade matrix coordinates and effect time.
/// Input adapters are responsible for translating Windows key events to these coordinates.
/// </summary>
public readonly record struct QuickLightingKeyEvent(int Row, int Column, TimeSpan At);

/// <summary>
/// Deterministic software effects for the Blade 6 x 17 matrix.
/// This class has no device, clock, capture, audio, or input dependencies.
/// </summary>
public static class QuickLightingEngine
{
    public const int Rows = BladeLightingProtocol.Rows;
    public const int Columns = BladeLightingProtocol.Columns;
    public const int PixelCount = Rows * Columns;
    public const int LogicalRows = BladeLightingLayout.LogicalRows;
    public const int LogicalColumns = BladeLightingLayout.LogicalColumns;

    private const int FireFrameMilliseconds = 50;
    private const double WheelPeriodSeconds = 4;
    private const double WavePeriodSeconds = 4;
    private const double BreathingPeriodMilliseconds = 7000;
    private const double SpectrumPeriodMilliseconds = 37740;

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
            < 0.50 => (position - 0.25) * 4,
            < 0.75 => (0.75 - position) * 4,
            _ => 0,
        };
        return RenderSolid(ScaleColorTruncated(color, intensity));
    }

    public static RazerRgb[] RenderSpectrum(TimeSpan elapsed)
    {
        ValidateElapsed(elapsed);
        var position = elapsed.TotalMilliseconds % SpectrumPeriodMilliseconds /
            SpectrumPeriodMilliseconds;
        return RenderSolid(RenderSpectrumColor(position));
    }

    public static RazerRgb[] RenderWave(TimeSpan elapsed, BladeWaveDirection direction)
    {
        ValidateElapsed(elapsed);
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        return RenderSpectrumColumns(elapsed, direction == BladeWaveDirection.Right ? 1 : -1);
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
    /// RMS/peak input from a future WASAPI adapter; color boost expands the lit range.
    /// </summary>
    public static RazerRgb[] RenderAudioMeter(double level, double colorBoost)
    {
        ValidateNormalized(level, nameof(level));
        ValidateNormalized(colorBoost, nameof(colorBoost));

        var litColumns = (int)Math.Ceiling(Math.Clamp(level * (1 + colorBoost), 0, 1) * LogicalColumns);
        var frame = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        for (var row = 0; row < LogicalRows; row++)
        {
            for (var column = 0; column < LogicalColumns; column++)
            {
                if (column >= litColumns)
                {
                    continue;
                }

                var position = column * 255d / (LogicalColumns - 1);
                var color = position <= 127.5
                    ? new RazerRgb(Scale(255, position / 127.5), 255, 0)
                    : new RazerRgb(255, Scale(255, (255 - position) / 127.5), 0);
                frame[row * LogicalColumns + column] = ScaleColor(color, 1 + colorBoost);
            }
        }

        return BladeLightingLayout.MapToDeviceFrame(frame);
    }

    /// <summary>Renders a deterministic orange/red fire field for the supplied time and seed.</summary>
    public static RazerRgb[] RenderFire(TimeSpan elapsed, int seed)
    {
        ValidateElapsed(elapsed);
        var frameNumber = elapsed.TotalMilliseconds <= 0
            ? 0
            : (long)(elapsed.TotalMilliseconds / FireFrameMilliseconds);
        var frame = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        for (var row = 0; row < LogicalRows; row++)
        {
            for (var column = 0; column < LogicalColumns; column++)
            {
                var verticalHeat = (row + 1) * 255 / LogicalRows;
                var noise = (int)(Hash(seed, frameNumber, row, column) % 96);
                var heat = Math.Clamp(verticalHeat + noise - 28, 0, 255);
                frame[row * LogicalColumns + column] = new RazerRgb(
                    (byte)heat,
                    (byte)(heat * 3 / 5),
                    (byte)(heat / 16));
            }
        }

        return BladeLightingLayout.MapToDeviceFrame(frame);
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

            var factor = 1 - age.TotalMilliseconds / duration.TotalMilliseconds;
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
            var fade = 1 - progress;
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
        ValidateElapsed(elapsed);
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var frame = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        var phase = elapsed.TotalSeconds / WheelPeriodSeconds * 360 *
            (direction == QuickLightingDirection.Clockwise ? 1 : -1);
        var centerRow = (LogicalRows - 1) / 2d;
        var centerColumn = (LogicalColumns - 1) / 2d;
        for (var row = 0; row < LogicalRows; row++)
        {
            for (var column = 0; column < LogicalColumns; column++)
            {
                var hue = (Math.Atan2(row - centerRow, column - centerColumn) * 180 / Math.PI + phase + 360) % 360;
                frame[row * LogicalColumns + column] = HsvToRgb(hue, 1, 1);
            }
        }

        return BladeLightingLayout.MapToDeviceFrame(frame);
    }

    private static byte Average(long total, int count) =>
        checked((byte)((total + count / 2) / count));

    private static RazerRgb[] RenderSpectrumColumns(TimeSpan elapsed, int direction)
    {
        var phase = elapsed.TotalSeconds / WavePeriodSeconds;
        var frame = new RazerRgb[BladeLightingLayout.LogicalPixelCount];
        for (var row = 0; row < LogicalRows; row++)
        {
            for (var column = 0; column < LogicalColumns; column++)
            {
                var directedColumn = direction > 0 ? column : LogicalColumns - 1 - column;
                var position = (directedColumn / (double)LogicalColumns - phase) % 1;
                if (position < 0)
                {
                    position += 1;
                }

                frame[row * LogicalColumns + column] = RenderSpectrumColor(position);
            }
        }

        return BladeLightingLayout.MapToDeviceFrame(frame);
    }

    private static RazerRgb RenderSpectrumColor(double position)
    {
        ReadOnlySpan<double> stops = [0, 0.33, 0.66, 1];
        ReadOnlySpan<RazerRgb> colors =
        [
            new(255, 0, 0),
            new(0, 255, 0),
            new(0, 0, 255),
            new(255, 0, 0),
        ];
        for (var index = 0; index < stops.Length - 1; index++)
        {
            if (position > stops[index + 1])
            {
                continue;
            }
            var factor = (position - stops[index]) / (stops[index + 1] - stops[index]);
            return LerpColor(colors[index], colors[index + 1], factor);
        }

        return colors[^1];
    }

    private static RazerRgb LerpColor(RazerRgb start, RazerRgb end, double factor) =>
        new(
            LerpByte(start.Red, end.Red, factor),
            LerpByte(start.Green, end.Green, factor),
            LerpByte(start.Blue, end.Blue, factor));

    private static byte LerpByte(byte start, byte end, double factor) =>
        checked((byte)Math.Clamp((int)(start + (end - start) * factor), 0, 255));

    private static uint Hash(int seed, long frame, int row, int column)
    {
        unchecked
        {
            var value = (uint)seed;
            value ^= (uint)frame * 0x9E3779B9u;
            value ^= (uint)(frame >> 32) * 0x27D4EB2Fu;
            value ^= (uint)row * 0x85EBCA6Bu;
            value ^= (uint)column * 0xC2B2AE35u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            return value;
        }
    }

    private static RazerRgb HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        var match = value - chroma;
        var (red, green, blue) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        return new RazerRgb(
            Scale(red + match, 255),
            Scale(green + match, 255),
            Scale(blue + match, 255));
    }

    private static RazerRgb MaxColor(RazerRgb first, RazerRgb second) =>
        new(
            Math.Max(first.Red, second.Red),
            Math.Max(first.Green, second.Green),
            Math.Max(first.Blue, second.Blue));

    private static RazerRgb ScaleColor(RazerRgb color, double factor) =>
        new(Scale(color.Red, factor), Scale(color.Green, factor), Scale(color.Blue, factor));

    private static RazerRgb ScaleColorTruncated(RazerRgb color, double factor) =>
        new(
            TruncateScale(color.Red, factor),
            TruncateScale(color.Green, factor),
            TruncateScale(color.Blue, factor));

    private static byte TruncateScale(byte value, double factor) =>
        checked((byte)Math.Clamp((int)(value * factor), 0, 255));

    private static byte Scale(double value, double factor) =>
        checked((byte)Math.Clamp(Math.Round(value * factor, MidpointRounding.AwayFromZero), 0, 255));

    private static void ValidateCoordinates(int row, int column)
    {
        if ((uint)row >= LogicalRows)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        if ((uint)column >= LogicalColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }
    }

    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }

    private static void ValidateElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }
    }

    private static void ValidateEventTime(TimeSpan at)
    {
        if (at < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(at));
        }
    }

    private static void ValidateNormalized(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
