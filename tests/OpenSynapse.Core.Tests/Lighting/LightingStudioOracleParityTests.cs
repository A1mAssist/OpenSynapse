using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Locks the parameter semantics recovered from the official engine oracle
/// (artifacts/chroma-studio-2026-11-03/engine-oracle). These are not
/// approximations: each expected value is visible in the recorded frames.
/// </summary>
public sealed class LightingStudioOracleParityTests
{
    private static readonly StudioRgb Red = new(255, 0, 0);
    private static readonly StudioRgb Green = new(0, 255, 0);
    private static readonly StudioRgb Blue = new(0, 0, 255);

    [Fact]
    public void WaveAngleUsesNativePlus270Degrees()
    {
        var horizontal = Wave(90);
        var vertical = Wave(180);

        var horizontalFrame = StudioEffectRenderer.Render(horizontal, 3, 3, 0.1);
        var verticalFrame = StudioEffectRenderer.Render(vertical, 3, 3, 0.1);

        // Native angle 90 makes rows identical; angle 180 makes columns identical.
        Assert.Equal(horizontalFrame[0], horizontalFrame[3]);
        Assert.Equal(horizontalFrame[1], horizontalFrame[4]);
        Assert.Equal(verticalFrame[0], verticalFrame[1]);
        Assert.Equal(verticalFrame[3], verticalFrame[4]);
    }

    [Fact]
    public void WaveSamplesLedEdgesSoThreeColumnsCoverTwoThirdsOfThePalette()
    {
        var parameters = Wave(90);
        var frame = StudioEffectRenderer.Render(parameters, 1, 3, 0);

        // The old (columns - 1) denominator made the last LED wrap back to the
        // first palette stop; native uses column / columns (0, 1/3, 2/3).
        Assert.NotEqual(frame[0], frame[2]);
        Assert.Equal(frame[0], Red);
        Assert.True(frame[2].B > 240);
        Assert.True(frame[1].G > 200);
    }

    [Fact]
    public void WaveMatchesTheNativeFortyMillisecondFrame()
    {
        var frame = StudioEffectRenderer.Render(Wave(90), 3, 3, 0.04);

        // effect_2 at 40 ms: red, one-third of the rainbow, then stop 68.
        var expected = new StudioRgb(255, 0, 0);
        var middle = new StudioRgb(0x9A, 0xF9, 0x24);
        var right = new StudioRgb(0x00, 0x0C, 0xFF);
        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(expected, frame[row * 3]);
            Assert.Equal(middle, frame[row * 3 + 1]);
            Assert.Equal(right, frame[row * 3 + 2]);
        }
    }

    [Fact]
    public void WaveMatchesTheNativeMidCycleFrame()
    {
        var frame = StudioEffectRenderer.Render(Wave(90), 3, 3, 0.2);

        var expected = new StudioRgb(0xC8, 0x00, 0x6D);
        var middle = new StudioRgb(0xFF, 0xF3, 0x00);
        var right = new StudioRgb(0x49, 0xB7, 0xFF);
        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(expected, frame[row * 3]);
            Assert.Equal(middle, frame[row * 3 + 1]);
            Assert.Equal(right, frame[row * 3 + 2]);
        }
    }

    [Fact]
    public void WavePauseAddsEmptyFramesToTheCycle()
    {
        var running = Wave(90);
        var paused = Wave(90);
        paused.PauseMs = 40;

        Assert.Equal(
            StudioEffectRenderer.Render(running, 3, 3, 0.04),
            StudioEffectRenderer.Render(paused, 3, 3, 0.04),
            new FrameComparer());
        Assert.NotEqual(
            StudioEffectRenderer.Render(running, 3, 3, 0.08),
            StudioEffectRenderer.Render(paused, 3, 3, 0.08));
    }

    [Fact]
    public void RippleMatchesTheNativeRingSequence()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ripple,
            Width = 300,
            Speed = 10,
        };
        AddRainbow(parameters);

        var first = StudioEffectRenderer.Render(parameters, 3, 3, 0.04);
        Assert.Equal(new StudioRgb(255, 0, 0), first[4]);
        Assert.All(first.Where((_, index) => index != 4), pixel => Assert.Equal(StudioRgb.Black, pixel));

        var rim = StudioEffectRenderer.Render(parameters, 3, 3, 0.16);
        Assert.Equal(new StudioRgb(0xFF, 0xF3, 0x00), rim[4]);
        Assert.Equal(new StudioRgb(0xFF, 0xF3, 0x00), rim[1]);
        Assert.Equal(new StudioRgb(0x0A, 0x33, 0x0E), rim[0]);

        var full = StudioEffectRenderer.Render(parameters, 3, 3, 0.24);
        Assert.All(full, pixel => Assert.Equal(new StudioRgb(0x36, 0xFF, 0x48), pixel));

        Assert.All(
            StudioEffectRenderer.Render(parameters, 3, 3, 0.56),
            pixel => Assert.Equal(StudioRgb.Black, pixel));
    }

    [Fact]
    public void BreathingUsesTheNativeStopPalette()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Breathing,
            DurationMs = 14_000,
        };
        AddStop(parameters, 0, StudioRgb.Black);
        AddStop(parameters, 12, StudioRgb.Black);
        AddStop(parameters, 25, Green);
        AddStop(parameters, 38, Green);
        AddStop(parameters, 50, StudioRgb.Black);
        AddStop(parameters, 62, StudioRgb.Black);
        AddStop(parameters, 75, Green);
        AddStop(parameters, 88, Green);
        AddStop(parameters, 100, StudioRgb.Black);

        // The double peak is in the stop list; the palette cursor supplies the
        // 25 fps timing (1.72 s = frame 42, 3.48 s = frame 86).
        Assert.Equal(StudioRgb.Black, RenderAt(parameters, 1.72));
        Assert.Equal(Green, RenderAt(parameters, 3.48));
        Assert.Equal(Green, RenderAt(parameters, 5.2));
        Assert.InRange(RenderAt(parameters, 6.9).G, 0, 20);
        Assert.Equal(StudioRgb.Black, RenderAt(parameters, 8.75));
    }

    [Fact]
    public void TidalMatchesTheOracleThreeSliceFrames()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Tidal,
            Width = 100,
            Speed = 10,
            Angle = 160,
            CenterPointX = 5_000,
            CenterPointY = 5_000,
        };
        AddRainbow(parameters);

        var frame = StudioEffectRenderer.Render(parameters, 3, 3, 0.6);

        // effect_19 at 600 ms: top/bottom red, center orange, sides dark.
        Assert.Equal(new StudioRgb(255, 0, 0), frame[1]);
        Assert.InRange(frame[4].G, 0x6C, 0x6E);
        Assert.Equal(new StudioRgb(255, 0, 0), frame[7]);
        Assert.Equal(StudioRgb.Black, frame[3]);
        Assert.Equal(StudioRgb.Black, frame[5]);
    }

    [Fact]
    public void WheelUsesTheNative256CanvasCenter()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Wheel,
            Speed = 60,
            CenterRow = 125,
            CenterCol = 125,
            Counterclockwise = true,
        };
        AddRainbow(parameters);

        var frame = StudioEffectRenderer.Render(parameters, 3, 3, 0.04);

        // effect_13 at 40 ms: the wheel center is far outside the 3x3 device,
        // so the whole crop is one orange hue rather than a local rainbow.
        Assert.InRange(frame[4].R, 253, 255);
        Assert.InRange(frame[4].G, 115, 119);
        Assert.Equal(0, frame[4].B);
        Assert.Equal(frame[4], frame[0]);
    }

    [Fact]
    public void RadiateMatchesTheOracleColumnPalette()
    {
        var parameters = new StudioEffectParams { Effect = StudioEffectKind.Radiate };
        AddRainbow(parameters);

        var frame = StudioEffectRenderer.Render(parameters, 3, 3, 0);

        // effect_17 uses gradient[column], not radial distance.
        Assert.Equal(new StudioRgb(255, 0, 0), frame[0]);
        Assert.Equal(new StudioRgb(255, 0xF3, 0), frame[1]);
        Assert.Equal(new StudioRgb(0x36, 0xFF, 0x48), frame[2]);
        Assert.Equal(frame[0], frame[3]);
        Assert.Equal(frame[1], frame[4]);
        Assert.Equal(frame[2], frame[5]);
    }

    [Fact]
    public void RippleCentersOnTheLastIndexForEvenRows()
    {
        var frame = StudioEffectRenderer.Render(Ripple(), 6, 17, 0.8);

        // Native 6x17 centers on row 2, not rows / 2 (row 3).
        for (var column = 0; column < 17; column++)
        {
            Assert.Equal(frame[column], frame[4 * 17 + column]);
            Assert.Equal(frame[17 + column], frame[3 * 17 + column]);
        }

        Assert.Contains(frame, pixel => pixel != StudioRgb.Black);
    }

    [Fact]
    public void RippleCentersOnTheLastIndexForEvenColumns()
    {
        var frame = StudioEffectRenderer.Render(Ripple(), 9, 26, 0.8);

        // Native 9x26 centers on column 12, not columns / 2 (column 13).
        for (var row = 0; row < 9; row++)
        {
            Assert.Equal(frame[row * 26], frame[row * 26 + 24]);
            Assert.Equal(frame[row * 26 + 1], frame[row * 26 + 23]);
        }

        Assert.Contains(frame, pixel => pixel != StudioRgb.Black);
    }

    [Fact]
    public void RippleRunsOneShortLifetimeAndStops()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ripple,
            Width = 300,
            Speed = 10,
        };
        AddRainbow(parameters);

        var first = StudioEffectRenderer.Render(parameters, 3, 3, 0.04);
        Assert.Equal(new StudioRgb(255, 0, 0), first[4]);
        Assert.True(first[1].R < first[4].R);

        var mid = StudioEffectRenderer.Render(parameters, 3, 3, 0.28);
        Assert.NotEqual(StudioRgb.Black, mid[4]);
        Assert.True(Sum(mid) > Sum(first));

        var afterLifetime = StudioEffectRenderer.Render(parameters, 3, 3, 0.56);
        Assert.All(afterLifetime, pixel => Assert.Equal(StudioRgb.Black, pixel));
    }

    [Fact]
    public void RippleFillsTheNativeMidpointDiskOnLargeMatrices()
    {
        var frame = StudioEffectRenderer.Render(Ripple(), 6, 17, 0.6);

        // 0x54a00's midpoint disk includes (2,5) at radius 5 and (2,6) at
        // radius 6; a Euclidean test drops both, leaving a black notch.
        Assert.Equal(new StudioRgb(0xFF, 0xF3, 0x00), frame[3]);
        Assert.NotEqual(StudioRgb.Black, frame[2]);
        Assert.Equal(StudioRgb.Black, frame[5 * 17 + 2]);

        // The disk is symmetric about the last-index center (row 2).
        for (var column = 0; column < 17; column++)
        {
            Assert.Equal(frame[column], frame[4 * 17 + column]);
        }
    }

    [Fact]
    public void RipplePaletteLengthUsesNativeGradientCount()
    {
        var frame = StudioEffectRenderer.Render(Ripple(), 7, 23, 0.8);

        // 7x23 native palette[7] is BCF718; the old +1 length produced FFF300.
        Assert.Equal(new StudioRgb(0xBC, 0xF7, 0x18), frame[3 * 23 + 17]);
    }

    [Fact]
    public void BreathingMatchesTheNativePaletteFrames()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Breathing,
            DurationMs = 14_000,
        };
        AddStop(parameters, 0, StudioRgb.Black);
        AddStop(parameters, 12, StudioRgb.Black);
        AddStop(parameters, 25, Green);
        AddStop(parameters, 38, Green);
        AddStop(parameters, 50, StudioRgb.Black);
        AddStop(parameters, 62, StudioRgb.Black);
        AddStop(parameters, 75, Green);
        AddStop(parameters, 88, Green);
        AddStop(parameters, 100, StudioRgb.Black);

        // effect_5: 14 s is 350 native frames, and the first ramp starts at
        // frame 43 (1760 ms), not from a continuous-time envelope.
        AssertFlat(parameters, 1.72, StudioRgb.Black);
        AssertFlat(parameters, 1.76, new StudioRgb(0, 5, 0));
        AssertFlat(parameters, 1.80, new StudioRgb(0, 11, 0));
        AssertFlat(parameters, 1.84, new StudioRgb(0, 17, 0));
        AssertFlat(parameters, 1.88, new StudioRgb(0, 23, 0));
        AssertFlat(parameters, 1.92, new StudioRgb(0, 28, 0));
        AssertFlat(parameters, 1.96, new StudioRgb(0, 34, 0));
        AssertFlat(parameters, 2.00, new StudioRgb(0, 40, 0));
    }

    [Fact]
    public void ReactiveMatchesTheNativePaletteFrames()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Reactive,
            DurationMs = 1_500,
            Decay = 0.1,
        };
        AddStop(parameters, 0, Green);
        AddStop(parameters, 100, StudioRgb.Black);

        // effect_3: the trigger is at 200 ms, so 240 ms is local frame 0.
        AssertFlat(parameters, 0.04, Green);
        AssertFlat(parameters, 0.08, new StudioRgb(0, 247, 0));
        AssertFlat(parameters, 0.12, new StudioRgb(0, 240, 0));
        AssertFlat(parameters, 0.80, new StudioRgb(0, 120, 0));
        AssertFlat(parameters, 1.44, new StudioRgb(0, 7, 0));
        AssertFlat(parameters, 1.48, StudioRgb.Black);
    }

    [Fact]
    public void SpectrumMatchesTheNativePaletteFrames()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Spectrum,
            DurationMs = 37_740,
        };
        AddStop(parameters, 0, Hex(0xFF18FF));
        AddStop(parameters, 18, Hex(0xFF1010));
        AddStop(parameters, 28, Hex(0xFF8000));
        AddStop(parameters, 35, Hex(0xEFFF00));
        AddStop(parameters, 42, Hex(0xDFFF00));
        AddStop(parameters, 49, Hex(0x00FF00));
        AddStop(parameters, 52, Hex(0x00FFFF));
        AddStop(parameters, 64, Hex(0x1858FF));
        AddStop(parameters, 77, Hex(0x1858FF));
        AddStop(parameters, 100, Hex(0xFF18FF));

        // effect_4: 37.74 s is 943 native frames, one palette entry per tick.
        AssertFlat(parameters, 0.04, Hex(0xFF18FF));
        AssertFlat(parameters, 0.08, Hex(0xFF17FD));
        AssertFlat(parameters, 0.52, Hex(0xFF17ED));
        AssertFlat(parameters, 2.00, Hex(0xFF15B9));
    }

    [Fact]
    public void TidalMatchesTheNativeTravelBoundaryFrames()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Tidal,
            Width = 100,
            Speed = 10,
            Angle = 160,
            CenterPointX = 5_000,
            CenterPointY = 5_000,
        };
        AddRainbow(parameters);

        // effect_19 around the first recycle: the side pixels appear at 1560
        // and then stay one palette entry ahead of the old single-step model.
        Assert.Equal(
            new[]
            {
                Hex(0xFF6500), Hex(0xFFB400), StudioRgb.Black,
                Hex(0xFF0000), Hex(0xBFF616), Hex(0xFF0000),
                StudioRgb.Black, Hex(0xFFB400), Hex(0xFF6500),
            },
            StudioEffectRenderer.Render(parameters, 3, 3, 1.56));
        Assert.Equal(
            new[]
            {
                Hex(0xFF6D00), Hex(0xFFBC00), StudioRgb.Black,
                Hex(0xFF0000), Hex(0xB4F71A), Hex(0xFF0000),
                StudioRgb.Black, Hex(0xFFBC00), Hex(0xFF6D00),
            },
            StudioEffectRenderer.Render(parameters, 3, 3, 1.60));
        Assert.Equal(
            new[]
            {
                Hex(0xFF7500), Hex(0xFFC300), StudioRgb.Black,
                Hex(0xFF0700), Hex(0xAAF81E), Hex(0xFF0700),
                StudioRgb.Black, Hex(0xFFC300), Hex(0xFF7500),
            },
            StudioEffectRenderer.Render(parameters, 3, 3, 1.64));
    }

    [Fact]
    public void WheelMatchesTheNativeFortyMillisecondFrame()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Wheel,
            Speed = 60,
            CenterRow = 125,
            CenterCol = 125,
            Counterclockwise = true,
        };
        AddRainbow(parameters);

        Assert.Equal(
            new[]
            {
                Hex(0xFF7500), Hex(0xFF7500), Hex(0xFF7600),
                Hex(0xFF7400), Hex(0xFF7500), Hex(0xFF7500),
                Hex(0xFF7300), Hex(0xFF7400), Hex(0xFF7500),
            },
            StudioEffectRenderer.Render(parameters, 3, 3, 0.04));
    }

    private static void AssertFlat(StudioEffectParams parameters, double seconds, StudioRgb expected)
    {
        var frame = StudioEffectRenderer.Render(parameters, 3, 3, seconds);
        Assert.All(frame, pixel => Assert.Equal(expected, pixel));
    }

    private static void AddStop(StudioEffectParams parameters, int stop, StudioRgb color) =>
        parameters.ColorStops.Add(new StudioColorStop(stop, color));

    private static StudioRgb Hex(uint value) =>
        new((byte)(value >> 16), (byte)(value >> 8), (byte)value);

    private sealed class FrameComparer : IEqualityComparer<StudioRgb[]>
    {
        public bool Equals(StudioRgb[]? left, StudioRgb[]? right) =>
            left is not null && right is not null && left.SequenceEqual(right);

        public int GetHashCode(StudioRgb[] frame) => frame.Length;
    }

    private static void AddRainbow(StudioEffectParams parameters)
    {
        parameters.ColorStops.Add(new StudioColorStop(0, new StudioRgb(255, 0, 0)));
        parameters.ColorStops.Add(new StudioColorStop(26, new StudioRgb(255, 0xF3, 0)));
        parameters.ColorStops.Add(new StudioColorStop(42, new StudioRgb(0x36, 0xFF, 0x48)));
        parameters.ColorStops.Add(new StudioColorStop(55, new StudioRgb(0x62, 0xF1, 0xFF)));
        parameters.ColorStops.Add(new StudioColorStop(68, new StudioRgb(0x00, 0x0C, 0xFF)));
        parameters.ColorStops.Add(new StudioColorStop(81, new StudioRgb(0x80, 0x00, 0xFF)));
        parameters.ColorStops.Add(new StudioColorStop(100, new StudioRgb(255, 0, 0)));
    }

    private static long Sum(StudioRgb[] frame) =>
        frame.Sum(pixel => (long)pixel.R + pixel.G + pixel.B);

    private static StudioRgb RenderAt(StudioEffectParams parameters, double seconds) =>
        StudioEffectRenderer.Render(parameters, 1, 1, seconds)[0];

    private static StudioEffectParams Ripple()
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ripple,
            Width = 300,
            Speed = 10,
        };
        AddRainbow(parameters);
        return parameters;
    }

    private static StudioEffectParams Wave(int angle)
    {
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Wave,
            Width = 100,
            Speed = 15,
            Angle = angle,
        };
        AddRainbow(parameters);
        return parameters;
    }
}
