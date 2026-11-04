using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioEffectRendererTests
{
    [Fact]
    public void SolidFillsEveryPixelWithPrimary()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Solid,
            Primary = new StudioRgb(12, 34, 56),
        };

        var frame = StudioEffectRenderer.Render(parameters, 3, 5, 0);

        Assert.Equal(15, frame.Length);
        Assert.All(frame, pixel => Assert.Equal(parameters.Primary, pixel));
    }

    [Theory]
    [MemberData(nameof(Effects))]
    public void SameInputsProduceIdenticalFrames(StudioEffectKind effect)
    {
        var parameters = Create(effect);

        var first = StudioEffectRenderer.Render(parameters, 6, 17, 1.7);
        var second = StudioEffectRenderer.Render(parameters, 6, 17, 1.7);

        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(EffectShapes))]
    public void EveryEffectRendersExactPixelCount(StudioEffectKind effect, int rows, int columns)
    {
        var frame = StudioEffectRenderer.Render(Create(effect), rows, columns, 1.25);

        Assert.Equal(rows * columns, frame.Length);
    }

    [Fact]
    public void PaletteResolveUsesFirstAndLastStops()
    {
        var first = new StudioRgb(255, 0, 0);
        var last = new StudioRgb(0, 0, 255);
        var parameters = new StudioEffectParams();
        parameters.ColorStops.Add(new StudioColorStop(0, first));
        parameters.ColorStops.Add(new StudioColorStop(100, last));

        Assert.Equal(first, parameters.Resolve(0));
        Assert.Equal(last, parameters.Resolve(1));
    }

    [Fact]
    public void AmbientSamplesTheScreenAndReturnsBlackWithoutASampler()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ambient,
            Blur = 0,
        };
        var expected = new StudioRgb(10, 20, 30);
        var sampled = StudioEffectRenderer.Render(
            parameters,
            4,
            6,
            0,
            new StudioEffectInput(ScreenSampler: (_, _) => expected));

        Assert.All(sampled, pixel => Assert.Equal(expected, pixel));

        var unsampled = StudioEffectRenderer.Render(parameters, 4, 6, 0);
        Assert.All(unsampled, pixel => Assert.Equal(StudioRgb.Black, pixel));
    }

    [Fact]
    public void AmbientPresetsMapLedCentersOntoTheirScreenRegion()
    {
        var topLeft = new StudioRgb(1, 0, 0);
        var topRight = new StudioRgb(2, 0, 0);
        var bottomLeft = new StudioRgb(3, 0, 0);
        var bottomRight = new StudioRgb(4, 0, 0);
        StudioRgb Sampler(double u, double v) => u < 0.5
            ? v < 0.5 ? topLeft : bottomLeft
            : v < 0.5 ? topRight : bottomRight;

        StudioRgb[] Render(string screen) => StudioEffectRenderer.Render(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Ambient,
                Screen = screen,
                Blur = 0,
            },
            2,
            2,
            0,
            new StudioEffectInput(ScreenSamplerUv: Sampler));

        Assert.Equal(new[] { topLeft, topRight, bottomLeft, bottomRight }, Render("full"));
        Assert.Equal(new[] { topLeft, topLeft, bottomLeft, bottomLeft }, Render("left"));
        Assert.Equal(new[] { topLeft, topRight, topLeft, topRight }, Render("top"));
        Assert.Equal(new[] { topRight, topRight, bottomRight, bottomRight }, Render("right"));
        Assert.Equal(new[] { bottomLeft, bottomRight, bottomLeft, bottomRight }, Render("bottom"));
    }

    [Fact]
    public void AmbientCustomRectMapsLedCentersIntoTheRect()
    {
        var samples = new List<(double U, double V)>();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ambient,
            Screen = "custom",
            ScreenRect = new StudioScreenRect(0.25, 0.25, 0.5, 0.5),
            Blur = 0,
        };
        var expected = new StudioRgb(9, 8, 7);

        var frame = StudioEffectRenderer.Render(
            parameters,
            2,
            2,
            0,
            new StudioEffectInput(ScreenSamplerUv: (u, v) =>
            {
                samples.Add((u, v));
                return expected;
            }));

        Assert.All(frame, pixel => Assert.Equal(expected, pixel));
        Assert.Equal(4, samples.Count);
        Assert.All(samples, sample =>
        {
            Assert.InRange(sample.U, 0.25, 0.75);
            Assert.InRange(sample.V, 0.25, 0.75);
        });
        Assert.Equal((0.375, 0.375), samples[0]);
        Assert.Equal((0.625, 0.375), samples[1]);
        Assert.Equal((0.375, 0.625), samples[2]);
        Assert.Equal((0.625, 0.625), samples[3]);
    }

    [Fact]
    public void AmbientCustomWithoutRectSamplesTheWholeScreen()
    {
        var left = new StudioRgb(1, 0, 0);
        var right = new StudioRgb(2, 0, 0);
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ambient,
            Screen = "custom",
            Blur = 0,
        };

        var frame = StudioEffectRenderer.Render(
            parameters,
            1,
            2,
            0,
            new StudioEffectInput(ScreenSamplerUv: (u, _) => u < 0.5 ? left : right));

        Assert.Equal(new[] { left, right }, frame);
    }

    [Fact]
    public void AmbientWithoutUvSamplerKeepsWholeScreenIntegerSampling()
    {
        var colors = new[]
        {
            new StudioRgb(10, 0, 0),
            new StudioRgb(0, 20, 0),
            new StudioRgb(0, 0, 30),
        };
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ambient,
            Screen = "left",
            Blur = 0,
        };

        var frame = StudioEffectRenderer.Render(
            parameters,
            2,
            3,
            0,
            new StudioEffectInput(ScreenSampler: (column, _) => colors[column]));

        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                Assert.Equal(colors[column], frame[row * 3 + column]);
            }
        }
    }

    [Fact]
    public void AmbientBlurSamplesEachLedOnceBeforeBlurringLocally()
    {
        var calls = 0;
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ambient,
            Blur = 1,
        };
        var color = new StudioRgb(10, 20, 30);

        var frame = StudioEffectRenderer.Render(
            parameters,
            3,
            3,
            0,
            new StudioEffectInput(ScreenSamplerUv: (_, _) =>
            {
                calls++;
                return color;
            }));

        Assert.Equal(9, calls);
        Assert.All(frame, pixel => Assert.Equal(color, pixel));
    }

    [Fact]
    public void AudioMeterIsDimmerAtLevelZero()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Audio,
            Boost = 2,
            Decay = 75,
        };

        var quiet = StudioEffectRenderer.Render(parameters, 2, 8, 0, new StudioEffectInput(0));
        var loud = StudioEffectRenderer.Render(parameters, 2, 8, 0, new StudioEffectInput(1));

        Assert.True(Total(loud) > Total(quiet));
    }

    [Theory]
    [InlineData(StudioEffectKind.Fire)]
    [InlineData(StudioEffectKind.Starlight)]
    public void NoisyEffectsChangeOverTime(StudioEffectKind effect)
    {
        var parameters = new StudioEffectParams
        {
            Effect = effect,
            Density = 75,
        };

        var first = StudioEffectRenderer.Render(parameters, 6, 17, 0.7);
        var second = StudioEffectRenderer.Render(parameters, 6, 17, 2.1);

        Assert.False(first.SequenceEqual(second));
    }

    [Theory]
    [InlineData(StudioEffectKind.Fire)]
    [InlineData(StudioEffectKind.Starlight)]
    public void SeededRandomEffectsRepeatAtTheSameTime(StudioEffectKind effect)
    {
        var parameters = new StudioEffectParams
        {
            Effect = effect,
            Density = 5,
        };

        var first = StudioEffectRenderer.Render(parameters, 6, 17, 1.7);
        var second = StudioEffectRenderer.Render(parameters, 6, 17, 1.7);

        Assert.Equal(first, second);
    }

    [Fact]
    public void GradientInterpolatesAcrossColumns()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Gradient,
            Primary = new StudioRgb(255, 0, 0),
            Secondary = new StudioRgb(0, 0, 255),
        };

        var frame = StudioEffectRenderer.Render(parameters, 2, 5, 0);

        Assert.Equal(parameters.Primary, frame[0]);
        Assert.Equal(parameters.Secondary, frame[4]);
        Assert.Equal(frame[0], frame[5]);
    }

    [Fact]
    public void BreathingUsesPrimaryAndSecondaryWhenTheyDiffer()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Breathing,
            Primary = new StudioRgb(0, 0, 0),
            Secondary = new StudioRgb(255, 255, 255),
            DurationMs = 14_000,
        };

        var dark = StudioEffectRenderer.Render(parameters, 1, 2, 0);
        var bright = StudioEffectRenderer.Render(parameters, 1, 2, 3.5);

        Assert.Equal(parameters.Primary, dark[0]);
        Assert.Equal(parameters.Secondary, bright[0]);
    }

    [Fact]
    public void WheelCenterMovesTheColorPivot()
    {
        StudioRgb[] Render(int row, int column) => StudioEffectRenderer.Render(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Wheel,
                CenterRow = row,
                CenterCol = column,
            },
            9,
            13,
            1.75);

        var centered = Render(125, 125);
        var corner = Render(0, 0);

        Assert.True(centered.Distinct().Count() > 1);
        Assert.False(centered.SequenceEqual(corner));
        Assert.Equal(centered, Render(125, 125));
    }

    [Fact]
    public void WheelMissingCenterFallsBackToTheMatrixCenter()
    {
        var fallback = new StudioEffectParams { Effect = StudioEffectKind.Wheel };
        var centered = new StudioEffectParams
        {
            Effect = StudioEffectKind.Wheel,
            CenterRow = 4,
            CenterCol = 6,
        };

        Assert.Equal(
            StudioEffectRenderer.Render(centered, 9, 13, 1.75),
            StudioEffectRenderer.Render(fallback, 9, 13, 1.75));
    }

    [Fact]
    public void WheelCenterFallsBackForMissingAndClampsHighValues()
    {
        StudioRgb[] Render(int row, int column) => StudioEffectRenderer.Render(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Wheel,
                CenterRow = row,
                CenterCol = column,
            },
            9,
            13,
            1.75);

        Assert.Equal(
            Render(4, 6),
            Render(-100, -100));
        Assert.Equal(
            Render(StudioEffectParams.WheelCenterMaximum, StudioEffectParams.WheelCenterMaximum),
            Render(999, 999));
    }

    [Fact]
    public void TidalCenterPointMovesTheSweep()
    {
        StudioRgb[] Render(double centerX) => StudioEffectRenderer.Render(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Tidal,
                Angle = 0,
                Split = 100,
                Speed = 10,
                CenterPointX = centerX,
            },
            12,
            24,
            1.3);

        var centered = Render(5_000);
        var shifted = Render(2_500);

        Assert.False(centered.SequenceEqual(shifted));
    }

    [Fact]
    public void TidalCounterclockwiseChangesTheSweepDirection()
    {
        StudioRgb[] Render(bool counterclockwise) => StudioEffectRenderer.Render(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Tidal,
                Angle = 0,
                Split = 100,
                Speed = 10,
                Counterclockwise = counterclockwise,
            },
            12,
            23,
            1.3);

        var outward = Render(false);
        var inward = Render(true);

        Assert.True(outward.Distinct().Count() > 1);
        Assert.False(outward.SequenceEqual(inward));
        Assert.Equal(outward, Render(false));
        Assert.Equal(inward, Render(true));
    }

    [Fact]
    public void NegativeTimeIsClampedToZero()
    {
        var parameters = new StudioEffectParams { Effect = StudioEffectKind.Fire };

        var atZero = StudioEffectRenderer.Render(parameters, 4, 7, 0);
        var negative = StudioEffectRenderer.Render(parameters, 4, 7, -5);

        Assert.Equal(atZero, negative);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(64, 65)]
    public void InvalidDimensionsThrow(int rows, int columns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StudioEffectRenderer.Render(new StudioEffectParams(), rows, columns, 0));
    }

    public static IEnumerable<object[]> Effects()
    {
        foreach (var effect in Enum.GetValues<StudioEffectKind>())
        {
            yield return [effect];
        }
    }

    public static IEnumerable<object[]> EffectShapes()
    {
        (int Rows, int Columns)[] shapes = [(1, 1), (1, 17), (17, 1)];
        foreach (var effect in Enum.GetValues<StudioEffectKind>())
        {
            foreach (var shape in shapes)
            {
                yield return [effect, shape.Rows, shape.Columns];
            }
        }
    }

    private static StudioEffectParams Create(StudioEffectKind effect) => new() { Effect = effect };

    private static long Total(StudioRgb[] frame) =>
        frame.Sum(pixel => (long)pixel.R + pixel.G + pixel.B);
}
