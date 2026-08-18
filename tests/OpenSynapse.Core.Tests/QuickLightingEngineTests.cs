using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class QuickLightingEngineTests
{
    private static readonly RazerRgb Black = new(0, 0, 0);

    [Fact]
    public void SolidFillsTheCompleteMatrix()
    {
        var color = new RazerRgb(0x99, 0xDD, 0x72);

        var frame = QuickLightingEngine.RenderSolid(color);

        AssertCompleteFrame(frame);
        Assert.All(frame, pixel => Assert.Equal(color, pixel));
        Assert.All(QuickLightingEngine.RenderSolid(Black), pixel => Assert.Equal(Black, pixel));
    }

    [Fact]
    public void BreathingMatchesSynapseSevenSecondColorStops()
    {
        var color = new RazerRgb(200, 100, 50);

        var start = QuickLightingEngine.RenderBreathing(TimeSpan.Zero, color);
        var peak = QuickLightingEngine.RenderBreathing(TimeSpan.FromMilliseconds(3500), color);
        var off = QuickLightingEngine.RenderBreathing(TimeSpan.FromMilliseconds(5250), color);
        var repeated = QuickLightingEngine.RenderBreathing(TimeSpan.FromMilliseconds(10500), color);

        Assert.All(start, pixel => Assert.Equal(Black, pixel));
        Assert.All(peak, pixel => Assert.Equal(color, pixel));
        Assert.All(off, pixel => Assert.Equal(Black, pixel));
        Assert.Equal(peak, repeated);
    }

    [Fact]
    public void SpectrumCyclesOverTime()
    {
        var initial = QuickLightingEngine.RenderSpectrum(TimeSpan.Zero);
        var green = QuickLightingEngine.RenderSpectrum(TimeSpan.FromMilliseconds(12454.2));
        var blue = QuickLightingEngine.RenderSpectrum(TimeSpan.FromMilliseconds(24908.4));

        AssertCompleteFrame(initial);
        Assert.Equal(new RazerRgb(255, 0, 0), AtLogical(initial, 0, 8));
        Assert.Equal(new RazerRgb(0, 255, 0), AtLogical(green, 0, 8));
        Assert.Equal(new RazerRgb(0, 0, 255), AtLogical(blue, 0, 8));
        Assert.Equal(AtLogical(initial, 0, 8), AtLogical(initial, 4, 8));
        Assert.Equal(AtLogical(green, 0, 0), AtLogical(green, 6, 13));
    }

    [Fact]
    public void WaveDirectionReversesTravel()
    {
        var right = QuickLightingEngine.RenderWave(TimeSpan.Zero, BladeWaveDirection.Right);
        var left = QuickLightingEngine.RenderWave(TimeSpan.Zero, BladeWaveDirection.Left);
        var advancedRight = QuickLightingEngine.RenderWave(
            TimeSpan.FromMilliseconds(40),
            BladeWaveDirection.Right);
        var advancedLeft = QuickLightingEngine.RenderWave(
            TimeSpan.FromMilliseconds(40),
            BladeWaveDirection.Left);

        AssertCompleteFrame(right);
        Assert.Equal(new RazerRgb(0xE0, 0x1E, 0x00), At(right, 0, 1));
        Assert.Equal(new RazerRgb(0xE0, 0x00, 0x1E), At(left, 0, 1));
        Assert.Equal(new RazerRgb(0x00, 0xCA, 0x34), At(right, 0, 7));
        Assert.Equal(new RazerRgb(0x00, 0x34, 0xCA), At(left, 0, 7));
        Assert.Equal(new RazerRgb(0xFF, 0x00, 0x00), At(advancedRight, 0, 1));
        Assert.Equal(new RazerRgb(0xE0, 0x1E, 0x00), At(advancedLeft, 0, 16));
        Assert.Equal(right, QuickLightingEngine.RenderWave(TimeSpan.FromSeconds(1), BladeWaveDirection.Right));
        Assert.Equal(At(right, 0, 4), At(right, QuickLightingEngine.Rows - 1, 4));
    }

    [Fact]
    public void AmbientAwarenessAveragesEachMatrixCell()
    {
        const int sourceWidth = QuickLightingEngine.LogicalColumns * 2;
        const int sourceHeight = QuickLightingEngine.LogicalRows * 2;
        var source = new RazerRgb[sourceWidth * sourceHeight];

        for (var row = 0; row < QuickLightingEngine.LogicalRows; row++)
        {
            for (var column = 0; column < QuickLightingEngine.LogicalColumns; column++)
            {
                var color = new RazerRgb(
                    checked((byte)(row * QuickLightingEngine.LogicalColumns + column)),
                    checked((byte)(row * 10)),
                    checked((byte)(column * 10)));
                for (var y = row * 2; y < row * 2 + 2; y++)
                {
                    for (var x = column * 2; x < column * 2 + 2; x++)
                    {
                        source[y * sourceWidth + x] = color;
                    }
                }
            }
        }

        var frame = QuickLightingEngine.RenderAmbientAwareness(source, sourceWidth, sourceHeight);

        AssertCompleteFrame(frame);
        Assert.Equal(new RazerRgb(0, 0, 0), AtLogical(frame, 0, 0));
        Assert.Equal(new RazerRgb(95, 50, 150), AtLogical(frame, 5, 15));
        Assert.Equal(new RazerRgb(109, 60, 130), AtLogical(frame, 6, 13));
    }

    [Fact]
    public void AmbientAwarenessRejectsInvalidPixelBuffer()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QuickLightingEngine.RenderAmbientAwareness([], 0, 1));
        Assert.Throws<ArgumentException>(
            () => QuickLightingEngine.RenderAmbientAwareness(
                [new RazerRgb()],
                QuickLightingEngine.Columns,
                QuickLightingEngine.Rows));
    }

    [Fact]
    public void AmbientAwarenessCanExpandAOnePixelCaptureRegion()
    {
        var color = new RazerRgb(12, 34, 56);

        var frame = QuickLightingEngine.RenderAmbientAwareness([color], 1, 1);

        AssertCompleteFrame(frame);
        Assert.Equal(86, frame.Count(pixel => pixel == color));
        Assert.Equal(color, AtLogical(frame, 0, 0));
        Assert.Equal(color, AtLogical(frame, 6, 13));
    }

    [Fact]
    public void AudioMeterMapsNormalizedLevelAndColorBoost()
    {
        var silent = QuickLightingEngine.RenderAudioMeter(0, 0);
        var normal = QuickLightingEngine.RenderAudioMeter(0.5, 0);
        var boosted = QuickLightingEngine.RenderAudioMeter(0.5, 1);
        var full = QuickLightingEngine.RenderAudioMeter(1, 1);

        Assert.All(silent, color => Assert.Equal(Black, color));
        Assert.NotEqual(Black, AtLogical(normal, 0, 0));
        Assert.Equal(Black, AtLogical(normal, 0, QuickLightingEngine.LogicalColumns - 1));
        Assert.True(boosted.Count(pixel => pixel != Black) > normal.Count(pixel => pixel != Black));
        Assert.Equal(86, full.Count(color => color != Black));
        Assert.Equal(AtLogical(full, 0, 8), AtLogical(full, 4, 8));
    }

    [Fact]
    public void FireIsDeterministicForTimeAndSeed()
    {
        var first = QuickLightingEngine.RenderFire(TimeSpan.FromMilliseconds(875), 42);
        var repeated = QuickLightingEngine.RenderFire(TimeSpan.FromMilliseconds(875), 42);
        var later = QuickLightingEngine.RenderFire(TimeSpan.FromMilliseconds(975), 42);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, later);
        Assert.Contains(first, color => color != Black);
        Assert.All(first, color =>
        {
            Assert.True(color.Red >= color.Green);
            Assert.True(color.Green >= color.Blue);
        });
        Assert.True(
            AverageLogicalRed(first, QuickLightingEngine.LogicalRows - 1) != AverageLogicalRed(first, 0));
        Assert.Equal(first, QuickLightingEngine.RenderFire(TimeSpan.FromMilliseconds(20875), 42));
    }

    [Fact]
    public void StarlightUsesBoundedDeterministicSourceBackedScheduler()
    {
        var first = new StarlightLightingRenderer(new RazerRgb(0, 255, 0), 710);
        var repeated = new StarlightLightingRenderer(new RazerRgb(0, 255, 0), 710);

        var frame = first.Render(TimeSpan.FromMilliseconds(160)).ToArray();
        var same = repeated.Render(TimeSpan.FromMilliseconds(160)).ToArray();
        var later = first.Render(TimeSpan.FromMilliseconds(800)).ToArray();
        var lit = frame
            .Select((color, index) => (index, color))
            .Where(entry => entry.color != Black)
            .ToArray();

        Assert.Equal(frame, same);
        Assert.NotEqual(frame, later);
        Assert.Equal(
            [
                (46, new RazerRgb(0x00, 0x51, 0x00)),
                (51, new RazerRgb(0x00, 0x52, 0x00)),
                (64, new RazerRgb(0x00, 0x60, 0x00)),
                (88, new RazerRgb(0x00, 0x42, 0x00)),
                (92, new RazerRgb(0x00, 0x6B, 0x00)),
            ],
            lit);
    }

    [Fact]
    public void TidalUsesProduct710CenterAngleAndTwoColorLanes()
    {
        var first = new RazerRgb(0, 255, 0);
        var second = new RazerRgb(0, 0, 255);
        var initial = QuickLightingEngine.RenderTidal(TimeSpan.Zero, first, second);
        var later = QuickLightingEngine.RenderTidal(TimeSpan.FromSeconds(1), first, second);

        AssertCompleteFrame(initial);
        Assert.NotEqual(initial, later);
        Assert.Equal(Black, At(initial, 4, 4));
        Assert.Equal(new RazerRgb(0x00, 0xAC, 0x00), At(initial, 0, 9));
        Assert.Equal(new RazerRgb(0x00, 0x00, 0xE3), At(initial, 2, 15));
        Assert.Equal(Black, At(initial, 3, 8));
        Assert.Equal(new RazerRgb(0x00, 0xAC, 0x00), At(later, 3, 8));
        Assert.Equal(new RazerRgb(0x00, 0x00, 0xF1), At(later, 4, 14));
        Assert.Equal(Black, At(later, 3, 16));
    }

    [Fact]
    public void MovingEffectsProduceDistinctSixtyHertzSubframes()
    {
        var subframe = TimeSpan.FromMilliseconds(1000d / 60d);

        Assert.NotEqual(
            QuickLightingEngine.RenderWave(TimeSpan.Zero, BladeWaveDirection.Right),
            QuickLightingEngine.RenderWave(subframe, BladeWaveDirection.Right));
        Assert.NotEqual(
            QuickLightingEngine.RenderFire(TimeSpan.Zero, 42),
            QuickLightingEngine.RenderFire(subframe, 42));
        Assert.NotEqual(
            QuickLightingEngine.RenderTidal(TimeSpan.Zero, new(0, 255, 0), new(0, 0, 255)),
            QuickLightingEngine.RenderTidal(subframe, new(0, 255, 0), new(0, 0, 255)));
    }

    [Fact]
    public void ReactiveLightsOnlyEventCellAndFadesLinearly()
    {
        var events = new[]
        {
            new QuickLightingKeyEvent(2, 7, TimeSpan.FromSeconds(1)),
        };
        var color = new RazerRgb(200, 100, 50);

        var halfway = QuickLightingEngine.RenderReactive(
            TimeSpan.FromSeconds(1.5),
            events,
            color,
            TimeSpan.FromSeconds(1));
        var expired = QuickLightingEngine.RenderReactive(
            TimeSpan.FromSeconds(2),
            events,
            color,
            TimeSpan.FromSeconds(1));

        Assert.Equal(new RazerRgb(100, 50, 25), AtLogical(halfway, 2, 7));
        Assert.Equal(1, halfway.Count(pixel => pixel != Black));
        Assert.All(expired, pixel => Assert.Equal(Black, pixel));
    }

    [Fact]
    public void ReactiveIgnoresFutureEventsAndRejectsInvalidCoordinates()
    {
        var future = new[] { new QuickLightingKeyEvent(1, 1, TimeSpan.FromSeconds(2)) };
        var invalid = new[] { new QuickLightingKeyEvent(QuickLightingEngine.LogicalRows, 0, TimeSpan.Zero) };

        Assert.All(
            QuickLightingEngine.RenderReactive(
                TimeSpan.FromSeconds(1),
                future,
                new RazerRgb(1, 2, 3),
                TimeSpan.FromSeconds(1)),
            pixel => Assert.Equal(Black, pixel));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuickLightingEngine.RenderReactive(
                TimeSpan.Zero,
                invalid,
                new RazerRgb(1, 2, 3),
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void RippleExpandsAwayFromEventOrigin()
    {
        var events = new[] { new QuickLightingKeyEvent(2, 8, TimeSpan.Zero) };
        var color = new RazerRgb(120, 60, 30);

        var start = QuickLightingEngine.RenderRipple(
            TimeSpan.Zero,
            events,
            color,
            TimeSpan.FromSeconds(1));
        var later = QuickLightingEngine.RenderRipple(
            TimeSpan.FromMilliseconds(250),
            events,
            color,
            TimeSpan.FromSeconds(1));

        Assert.Equal(color, AtLogical(start, 2, 8));
        Assert.Equal(1, start.Count(pixel => pixel != Black));
        Assert.Equal(Black, AtLogical(later, 2, 8));
        Assert.Contains(later, pixel => pixel != Black);
    }

    [Fact]
    public void WheelDirectionChangesRotationAndKeepsFullFrame()
    {
        var initialClockwise = QuickLightingEngine.RenderWheel(
            TimeSpan.Zero,
            QuickLightingDirection.Clockwise);
        var initialCounterClockwise = QuickLightingEngine.RenderWheel(
            TimeSpan.Zero,
            QuickLightingDirection.CounterClockwise);
        var clockwise = QuickLightingEngine.RenderWheel(
            TimeSpan.FromSeconds(1),
            QuickLightingDirection.Clockwise);
        var counterClockwise = QuickLightingEngine.RenderWheel(
            TimeSpan.FromSeconds(1),
            QuickLightingDirection.CounterClockwise);

        Assert.Equal(initialClockwise, initialCounterClockwise);
        Assert.NotEqual(clockwise, counterClockwise);
        AssertCompleteFrame(clockwise);
        Assert.Equal(86, clockwise.Count(pixel => pixel != Black));
        Assert.All(
            clockwise.Where(pixel => pixel != Black),
            pixel => Assert.Equal(255, Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue))));
    }

    [Fact]
    public void TimeAndNormalizedInputsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QuickLightingEngine.RenderAudioMeter(-0.01, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuickLightingEngine.RenderAudioMeter(0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuickLightingEngine.RenderFire(TimeSpan.FromTicks(-1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuickLightingEngine.RenderWheel(TimeSpan.Zero, (QuickLightingDirection)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuickLightingEngine.RenderRipple(
                TimeSpan.Zero,
                [],
                new RazerRgb(),
                TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QuickLightingEngine.RenderReactive(
                TimeSpan.Zero,
                [new QuickLightingKeyEvent(0, 0, TimeSpan.FromTicks(-1))],
                new RazerRgb(),
                TimeSpan.FromSeconds(1)));
    }

    private static RazerRgb At(IReadOnlyList<RazerRgb> frame, int row, int column) =>
        frame[row * QuickLightingEngine.Columns + column];

    private static RazerRgb AtLogical(IReadOnlyList<RazerRgb> frame, int row, int column)
    {
        Assert.True(BladeLightingLayout.TryGetDevicePosition(row, column, out var deviceRow, out var deviceColumn));
        return At(frame, deviceRow, deviceColumn);
    }

    private static double AverageLogicalRed(IReadOnlyList<RazerRgb> frame, int row) =>
        Enumerable.Range(0, QuickLightingEngine.LogicalColumns)
            .Where(column => BladeLightingLayout.TryGetDevicePosition(row, column, out _, out _))
            .Average(column => AtLogical(frame, row, column).Red);

    private static void AssertCompleteFrame(IReadOnlyList<RazerRgb> frame) =>
        Assert.Equal(QuickLightingEngine.Rows * QuickLightingEngine.Columns, frame.Count);
}
