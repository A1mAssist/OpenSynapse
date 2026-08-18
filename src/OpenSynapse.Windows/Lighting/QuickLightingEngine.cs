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

    private const int FireKeyFrameCount = 100;
    private const int FireInterpolationFrames = 5;
    private const int FireWorkRows = 7;
    private const int FireWorkColumns = 23;
    private const double WheelPeriodSeconds = 4;
    private const double BreathingPeriodMilliseconds = 7000;
    private const double SpectrumPeriodMilliseconds = 37740;
    private const int LightingFramesPerSecond = 25;

    private static readonly byte[] FireSourceMask = Convert.FromHexString(
        "0806040606040202020406060406080604020204060810");

    private static readonly byte[][] FirePropagationMasks =
    [
        Convert.FromHexString("1008060808060402040608080608100806040406081020"),
        Convert.FromHexString("2010081010080604060810100810201008060608102040"),
        Convert.FromHexString("4020102020100806081020201020402010080810204060"),
        Convert.FromHexString("6040204040201008102040402040604020101020406080"),
        Convert.FromHexString("8060406060402010204060604060806040202040608080"),
        Convert.FromHexString("8080608080604020406080806080808060404060808080"),
    ];

    private static readonly byte[][] FireColorLookup =
    [
        Convert.FromHexString("E0E0C0E0E0C0A080A0C0E0E0C0E0E0E0C0A0A0C0E0E0E0"),
        Convert.FromHexString("E0C0A0C0C0A0806080A0C0C0A0C0E0C0A08080A0C0E0E0"),
        Convert.FromHexString("C0A080A0A08060406080A0A080A0C0A080606080A0C0E0"),
        Convert.FromHexString("A080608080604020406080806080A0806040406080A0C0"),
        Convert.FromHexString("80604060604020102040606040608060402020406080A0"),
        Convert.FromHexString("6040204040201008102040402040604020101020406080"),
        Convert.FromHexString("4020102020100808081020201020402010080810204060"),
    ];

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

        const int activeFrames = 25;
        var tick = elapsed.TotalSeconds * LightingFramesPerSecond % activeFrames;
        var cosine = direction == BladeWaveDirection.Right ? 1 : -1;
        var frame = new RazerRgb[PixelCount];
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var projected = (int)(column * cosine / (double)Columns * activeFrames);
                var colorFrame = (projected + activeFrames - tick) % activeFrames;
                if (colorFrame < 0)
                {
                    colorFrame += activeFrames;
                }
                frame[row * Columns + column] = RenderSpectrumColor(
                    colorFrame / (double)activeFrames, blueStop: 0.67);
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

    /// <summary>Renders Product 710's native 100-keyframe, five-step fire cycle.</summary>
    public static RazerRgb[] RenderFire(TimeSpan elapsed, int seed)
    {
        ValidateElapsed(elapsed);
        var keyFrames = BuildFireKeyFrames(seed);
        var animationFrame = elapsed.TotalSeconds * LightingFramesPerSecond %
            (FireKeyFrameCount * FireInterpolationFrames);
        var keyFrame = (int)(animationFrame / FireInterpolationFrames);
        var nextKeyFrame = (keyFrame + 1) % FireKeyFrameCount;
        var interpolation = animationFrame % FireInterpolationFrames / (double)FireInterpolationFrames;
        var frame = new RazerRgb[PixelCount];
        for (var outputRow = 0; outputRow < Rows; outputRow++)
        {
            var sourceRow = FireWorkRows - 1 - outputRow;
            var lookup = FireColorLookup[sourceRow];
            for (var column = 0; column < Columns; column++)
            {
                var cell = sourceRow * FireWorkColumns + column;
                var start = keyFrames[keyFrame * FireWorkRows * FireWorkColumns + cell];
                var end = keyFrames[nextKeyFrame * FireWorkRows * FireWorkColumns + cell];
                var heat = (byte)(start + (end - start) * interpolation);
                frame[outputRow * Columns + column] = RenderFireRamp(lookup[column], heat);
            }
        }

        return frame;
    }

    /// <summary>Renders Product 710's default three-lane Tidal projection.</summary>
    public static RazerRgb[] RenderTidal(
        TimeSpan elapsed,
        RazerRgb firstColor,
        RazerRgb secondColor)
    {
        ValidateElapsed(elapsed);
        const double angleRadians = 160 * Math.PI / 180;
        const double centerColumn = 8;
        const double centerRow = 3;
        const double speed = 10;
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);
        var columnExtent = Math.Max(centerColumn, Columns - centerColumn);
        var rowExtent = Math.Max(centerRow, Rows - centerRow);
        var distance = Math.Abs(columnExtent * cos) + Math.Abs(rowExtent * sin);
        var spatialStep = ((speed * 2) / 100 * distance) / LightingFramesPerSecond;
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

    private static RazerRgb RenderSpectrumColor(double position, double blueStop = 0.66)
    {
        ReadOnlySpan<double> stops = [0, 0.33, blueStop, 1];
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

    private static RazerRgb RenderTidalColor(
        double position,
        RazerRgb firstColor,
        RazerRgb secondColor)
    {
        ReadOnlySpan<double> stops = [0, 0.10, 0.25, 0.40, 0.60, 0.75, 0.90, 1];
        ReadOnlySpan<RazerRgb> colors =
        [
            default, default, firstColor, default,
            default, secondColor, default, default,
        ];
        for (var index = 0; index < stops.Length - 1; index++)
        {
            if (position <= stops[index + 1])
            {
                return LerpColor(
                    colors[index],
                    colors[index + 1],
                    (position - stops[index]) / (stops[index + 1] - stops[index]));
            }
        }
        return default;
    }

    private static int PositiveModulo(long value, int divisor)
    {
        var remainder = value % divisor;
        return checked((int)(remainder < 0 ? remainder + divisor : remainder));
    }

    private static RazerRgb LerpColor(RazerRgb start, RazerRgb end, double factor) =>
        new(
            LerpByte(start.Red, end.Red, factor),
            LerpByte(start.Green, end.Green, factor),
            LerpByte(start.Blue, end.Blue, factor));

    private static byte LerpByte(byte start, byte end, double factor) =>
        checked((byte)Math.Clamp((int)(start + (end - start) * factor), 0, 255));

    private static byte[] BuildFireKeyFrames(int seed)
    {
        var cellsPerFrame = FireWorkRows * FireWorkColumns;
        var frames = new byte[FireKeyFrameCount * cellsPerFrame];
        var randomState = unchecked((uint)seed);
        for (var frame = 0; frame < FireKeyFrameCount; frame++)
        {
            var bottom = frame * cellsPerFrame + (FireWorkRows - 1) * FireWorkColumns;
            for (var column = 0; column < FireWorkColumns; column++)
            {
                var value = NextLightingRandom(ref randomState) % 192 + 32;
                frames[bottom + column] = checked((byte)(value - FireSourceMask[column] * value / 255));
            }
        }

        for (var sourceRow = FireWorkRows - 1; sourceRow > 0; sourceRow--)
        {
            var mask = FirePropagationMasks[FireWorkRows - 1 - sourceRow];
            for (var frame = 0; frame < FireKeyFrameCount; frame++)
            {
                var source = frame * cellsPerFrame + sourceRow * FireWorkColumns;
                var destination = ((frame + 1) % FireKeyFrameCount) * cellsPerFrame +
                    (sourceRow - 1) * FireWorkColumns;
                for (var column = 0; column < FireWorkColumns; column++)
                {
                    var value = frames[source + column];
                    frames[destination + column] = checked((byte)(value - value * mask[column] / 255));
                }
            }
        }

        return frames;
    }

    private static int NextLightingRandom(ref uint state)
    {
        state = unchecked(state * 0x343FD + 0x269EC3);
        return (int)((state >> 16) & 0x7FFF);
    }

    private static RazerRgb RenderFireRamp(byte position, byte heat)
    {
        var color = position <= 128
            ? LerpColor(new RazerRgb(255, 127, 0), new RazerRgb(255, 0, 0), position / 128d)
            : LerpColor(new RazerRgb(255, 0, 0), default, (position - 128) / 128d);
        return new RazerRgb(
            checked((byte)(color.Red * heat / 255)),
            checked((byte)(color.Green * heat / 255)),
            checked((byte)(color.Blue * heat / 255)));
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

internal sealed class StarlightLightingRenderer
{
    private const int MaximumStars = 20;
    private const int EffectFrames = 42;
    private const int RegenerationFrames = 5;
    private readonly RazerRgb _color;
    private readonly List<Star> _stars = [];
    private uint _randomState;
    private long _nextFrame;
    private RazerRgb[] _lastFrame = new RazerRgb[QuickLightingEngine.PixelCount];

    public StarlightLightingRenderer(RazerRgb color, int seed)
    {
        _color = color;
        _randomState = unchecked((uint)seed);
    }

    public IReadOnlyList<RazerRgb> Render(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        var targetFrame = (long)(elapsed.TotalMilliseconds * 25 / 1000);
        if (targetFrame < _nextFrame - 1)
        {
            throw new InvalidOperationException("Starlight 时间必须单调递增。");
        }
        while (_nextFrame <= targetFrame)
        {
            RenderNextFrame();
            _nextFrame++;
        }
        return _lastFrame;
    }

    private void RenderNextFrame()
    {
        _stars.RemoveAll(star => star.Age >= star.FrameCount);
        if (_nextFrame % RegenerationFrames == 0)
        {
            SpawnStars();
        }

        var frame = new RazerRgb[QuickLightingEngine.PixelCount];
        foreach (var star in _stars)
        {
            var position = star.FrameCount == 1 ? 1 : star.Age / (double)(star.FrameCount - 1);
            var factor = position switch
            {
                <= 0.33 => position / 0.33,
                <= 0.67 => (0.67 - position) / 0.34,
                _ => 0,
            };
            frame[star.Position] = new RazerRgb(
                Scale(_color.Red, factor * star.Intensity / 100),
                Scale(_color.Green, factor * star.Intensity / 100),
                Scale(_color.Blue, factor * star.Intensity / 100));
            star.Age++;
        }
        _lastFrame = frame;
    }

    private void SpawnStars()
    {
        var free = MaximumStars - _stars.Count;
        var spawnCount = NextRandom(free + 1);
        for (var index = 0; index < spawnCount; index++)
        {
            var position = NextRandom(QuickLightingEngine.PixelCount);
            if (_stars.Any(star => star.Position == position))
            {
                break;
            }
            _stars.Add(new Star(
                position,
                NextRandom(EffectFrames) + 5,
                NextRandom(80) + 20));
        }
    }

    private int NextRandom(int exclusiveMaximum)
    {
        _randomState = unchecked(_randomState * 0x343FD + 0x269EC3);
        return (int)((_randomState >> 16) & 0x7FFF) % exclusiveMaximum;
    }

    private static byte Scale(byte value, double factor) =>
        checked((byte)Math.Clamp((int)(value * factor), 0, 255));

    private sealed class Star(int position, int frameCount, int intensity)
    {
        public int Position { get; } = position;
        public int FrameCount { get; } = frameCount;
        public int Intensity { get; } = intensity;
        public int Age { get; set; }
    }
}
