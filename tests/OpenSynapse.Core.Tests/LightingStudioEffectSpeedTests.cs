using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioEffectSpeedTests
{
    [Fact]
    public void WaveSpeedAndTickChangeThePaletteMapping()
    {
        var slow = Create(StudioEffectKind.Wave);
        slow.Speed = 10;
        slow.Width = 100;
        slow.Angle = 90;
        var fast = Create(StudioEffectKind.Wave);
        fast.Speed = 30;
        fast.Width = 100;
        fast.Angle = 90;

        var slowAtFirstTick = StudioEffectRenderer.Render(slow, 1, 17, 0.04);
        var fastAtFirstTick = StudioEffectRenderer.Render(fast, 1, 17, 0.04);

        // Speed changes the palette span; the cursor advances one entry per
        // native 25 fps frame, independent of Duration.
        Assert.NotEqual(slowAtFirstTick, fastAtFirstTick);
        Assert.NotEqual(slowAtFirstTick, StudioEffectRenderer.Render(slow, 1, 17, 0.08));
    }

    [Fact]
    public void WaveWidthChangesSpatialPeriod()
    {
        var narrow = Create(StudioEffectKind.Wave);
        narrow.Width = 100;
        narrow.Speed = 10;
        narrow.Angle = 90;
        var wide = Create(StudioEffectKind.Wave);
        wide.Width = 400;
        wide.Speed = 10;
        wide.Angle = 90;

        var narrowFrame = StudioEffectRenderer.Render(narrow, 1, 17, 0);
        var wideFrame = StudioEffectRenderer.Render(wide, 1, 17, 0);

        Assert.False(narrowFrame.SequenceEqual(wideFrame));
    }

    [Fact]
    public void RippleWidthChangesLifetime()
    {
        var narrow = Create(StudioEffectKind.Ripple);
        narrow.Width = 100;
        narrow.Speed = 10;
        var wide = Create(StudioEffectKind.Ripple);
        wide.Width = 400;
        wide.Speed = 10;

        // Width/100 is the native ring thickness; the narrow ripple has
        // fully left the 9x9 crop at 800 ms while the wide one still shows.
        var narrowFrame = StudioEffectRenderer.Render(narrow, 9, 9, 0.8);
        var wideFrame = StudioEffectRenderer.Render(wide, 9, 9, 0.8);

        Assert.Equal(0, Lit(narrowFrame));
        Assert.True(Lit(wideFrame) > 0);
    }

    [Fact]
    public void WheelSpeedChangesTheRotation()
    {
        var slow = Create(StudioEffectKind.Wheel);
        slow.Speed = 60;
        var fast = Create(StudioEffectKind.Wheel);
        fast.Speed = 300;

        var slowFrame = StudioEffectRenderer.Render(slow, 1, 17, 1);
        var fastFrame = StudioEffectRenderer.Render(fast, 1, 17, 1);

        Assert.False(slowFrame.SequenceEqual(fastFrame));
    }

    [Fact]
    public void TidalSpeedChangesTheWavefront()
    {
        var slow = Create(StudioEffectKind.Tidal);
        slow.Speed = 10;
        var fast = Create(StudioEffectKind.Tidal);
        fast.Speed = 20;

        var slowFrame = StudioEffectRenderer.Render(slow, 9, 17, 1);
        var fastFrame = StudioEffectRenderer.Render(fast, 9, 17, 1);

        Assert.False(slowFrame.SequenceEqual(fastFrame));
    }

    [Fact]
    public void RandomColorChangesBreathingOutput()
    {
        var fixedColor = Create(StudioEffectKind.Breathing);
        fixedColor.RandomColor = false;
        var randomColor = Create(StudioEffectKind.Breathing);
        randomColor.RandomColor = true;

        var fixedFrame = StudioEffectRenderer.Render(fixedColor, 1, 4, 7);
        var randomFrame = StudioEffectRenderer.Render(randomColor, 1, 4, 7);

        Assert.False(fixedFrame.SequenceEqual(randomFrame));
    }

    [Theory]
    [InlineData(StudioEffectKind.Wave)]
    [InlineData(StudioEffectKind.Wheel)]
    public void ZeroSpeedFreezesTemporalPhase(StudioEffectKind effect)
    {
        var parameters = Create(effect);
        parameters.Speed = 0;

        var first = StudioEffectRenderer.Render(parameters, 6, 17, 1);
        var second = StudioEffectRenderer.Render(parameters, 6, 17, 2);

        Assert.Equal(first, second);
        Assert.Contains(first, pixel => pixel != StudioRgb.Black);
    }

    [Fact]
    public void TidalZeroSpeedStillAdvancesTheWaveSlices()
    {
        var parameters = Create(StudioEffectKind.Tidal);
        parameters.Speed = 0;

        var first = StudioEffectRenderer.Render(parameters, 6, 17, 1);
        var second = StudioEffectRenderer.Render(parameters, 6, 17, 2);

        Assert.False(first.SequenceEqual(second));
    }

    [Fact]
    public void RandomColorChangesStarlightOutput()
    {
        var fixedColor = Create(StudioEffectKind.Starlight);
        fixedColor.Density = 100;
        fixedColor.RandomColor = false;
        var randomColor = Create(StudioEffectKind.Starlight);
        randomColor.Density = 100;
        randomColor.RandomColor = true;

        var fixedFrame = StudioEffectRenderer.Render(fixedColor, 6, 17, 0.4);
        var randomFrame = StudioEffectRenderer.Render(randomColor, 6, 17, 0.4);

        Assert.False(fixedFrame.SequenceEqual(randomFrame));
    }

    [Fact]
    public void AutoBoostRaisesAudioMeterAtTheSameLevel()
    {
        var fixedBoost = Create(StudioEffectKind.Audio);
        fixedBoost.Boost = 1;
        fixedBoost.AutoBoost = false;
        var autoBoost = Create(StudioEffectKind.Audio);
        autoBoost.Boost = 1;
        autoBoost.AutoBoost = true;

        var fixedFrame = StudioEffectRenderer.Render(fixedBoost, 2, 16, 0, new StudioEffectInput(0.25));
        var autoFrame = StudioEffectRenderer.Render(autoBoost, 2, 16, 0, new StudioEffectInput(0.25));

        Assert.True(Total(autoFrame) > Total(fixedFrame));
    }

    [Fact]
    public void DecayChangesAudioFalloff()
    {
        var slowDecay = Create(StudioEffectKind.Audio);
        slowDecay.Boost = 1;
        slowDecay.AutoBoost = false;
        slowDecay.Decay = 0.1;
        var fastDecay = Create(StudioEffectKind.Audio);
        fastDecay.Boost = 1;
        fastDecay.AutoBoost = false;
        fastDecay.Decay = 2;

        var slowFrame = StudioEffectRenderer.Render(slowDecay, 2, 16, 0, new StudioEffectInput(0.8));
        var fastFrame = StudioEffectRenderer.Render(fastDecay, 2, 16, 0, new StudioEffectInput(0.8));

        Assert.False(slowFrame.SequenceEqual(fastFrame));
    }

    private static StudioEffectParams Create(StudioEffectKind effect) =>
        StudioEffectCatalog.CreateDefault(effect);

    private static int Lit(StudioRgb[] frame) =>
        frame.Count(pixel => pixel.R + pixel.G + pixel.B > 0);

    private static long Total(StudioRgb[] frame) =>
        frame.Sum(pixel => (long)pixel.R + pixel.G + pixel.B);
}
