using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioCompositionTests
{
    [Fact]
    public void RandomPlaybackStartsOnFirstSample()
    {
        var clock = new StudioPlaybackClock(0.5);
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Breathing,
            PlaybackStart = StudioPlaybackStart.Random,
            DurationMs = 1000,
        };

        var state = clock.Sample(5, parameters);

        Assert.True(state.Running);
        // Breathing's visible cycle is half of DurationMs, so a 1s duration is a 0.5s cycle.
        Assert.InRange(state.LocalSeconds, 0.2, 0.3);
    }

    [Fact]
    public void BreathingStopsAfterTheVisibleCycle()
    {
        var clock = new StudioPlaybackClock();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Breathing,
            PlaybackStart = StudioPlaybackStart.OnPress,
            PlaybackEnd = StudioPlaybackEnd.After,
            DurationMs = 14_000,
            Cycles = 1,
        };
        clock.Start(0);

        Assert.True(clock.Sample(6.9, parameters, 6, 17).Running);
        Assert.False(clock.Sample(7.1, parameters, 6, 17).Running);
    }

    [Fact]
    public void AfterPlaybackStopsAtCycleLimit()
    {
        var clock = new StudioPlaybackClock();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Reactive,
            PlaybackStart = StudioPlaybackStart.OnPress,
            PlaybackEnd = StudioPlaybackEnd.After,
            DurationMs = 1000,
            Cycles = 2,
        };
        clock.Start(0);

        Assert.True(clock.Sample(1.5, parameters).Running);
        Assert.False(clock.Sample(2.5, parameters).Running);
    }

    [Fact]
    public void WheelAfterOneCycleStopsAtAFullRotation()
    {
        var clock = new StudioPlaybackClock();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Wheel,
            PlaybackStart = StudioPlaybackStart.OnPress,
            PlaybackEnd = StudioPlaybackEnd.After,
            Speed = 60,
            Cycles = 1,
        };
        clock.Start(0);

        Assert.True(clock.Sample(5.9, parameters).Running);
        Assert.False(clock.Sample(6.1, parameters).Running);
    }

    [Fact]
    public void MissingDurationUsesTheRendererFallbackCycle()
    {
        var clock = new StudioPlaybackClock();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Reactive,
            PlaybackStart = StudioPlaybackStart.OnPress,
            PlaybackEnd = StudioPlaybackEnd.After,
            DurationMs = 0,
            Cycles = 1,
        };
        clock.Start(0);

        Assert.True(clock.Sample(0.9, parameters).Running);
        Assert.False(clock.Sample(1.1, parameters).Running);
    }

    [Fact]
    public void PauseHoldsTheLastFrame()
    {
        var clock = new StudioPlaybackClock();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Wave,
            PlaybackStart = StudioPlaybackStart.OnPress,
            PlaybackEnd = StudioPlaybackEnd.After,
            DurationMs = 1000,
            PauseMs = 500,
            Cycles = 2,
        };
        clock.Start(0);

        var state = clock.Sample(1.25, parameters);

        Assert.True(state.Running);
        Assert.True(state.Paused);
        Assert.Equal(1.0, state.LocalSeconds, 3);
    }

    [Fact]
    public void ToggleStartsAndStops()
    {
        var clock = new StudioPlaybackClock();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Ripple,
            PlaybackStart = StudioPlaybackStart.OnPress,
            PlaybackEnd = StudioPlaybackEnd.Toggle,
        };

        Assert.False(clock.Sample(0.1, parameters).Running);
        clock.Toggle(1);
        Assert.True(clock.Sample(1.1, parameters).Running);
        clock.Toggle(2);
        Assert.False(clock.Sample(2.1, parameters).Running);
    }

    [Fact]
    public void LayerWithoutRegionsFillsTheMask()
    {
        var layer = StudioLayerItem.CreateLayer("Base", StudioEffectKind.Solid);

        var mask = StudioMaskBuilder.Build(layer, 736, "abc", 2, 3);

        Assert.All(mask, value => Assert.Equal(StudioMaskBuilder.Full, value));
    }

    [Fact]
    public void RegionMasksOnlyListedLeds()
    {
        var layer = StudioLayerItem.CreateLayer("Keys", StudioEffectKind.Solid);
        var region = new StudioRegion();
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "abc" };
        device.Leds.Add(new StudioLedRef(0, 0));
        device.Leds.Add(new StudioLedRef(1, 2));
        region.Devices.Add(device);
        layer.Regions.Add(region);

        var mask = StudioMaskBuilder.Build(layer, 736, "abc", 2, 3);

        Assert.Equal(StudioMaskBuilder.Full, mask[0]);
        Assert.Equal(StudioMaskBuilder.Full, mask[1 * 3 + 2]);
        Assert.Equal(0, mask[1]);
    }

    [Fact]
    public void RegionIgnoresOtherProductsAndInstances()
    {
        var layer = StudioLayerItem.CreateLayer("Keys", StudioEffectKind.Solid);
        var region = new StudioRegion();
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "abc" };
        device.Leds.Add(new StudioLedRef(0, 0));
        region.Devices.Add(device);
        layer.Regions.Add(region);

        Assert.All(StudioMaskBuilder.Build(layer, 184, "abc", 2, 3), value => Assert.Equal(0, value));
        Assert.All(StudioMaskBuilder.Build(layer, 736, "other", 2, 3), value => Assert.Equal(0, value));
    }
    [Fact]
    public void BuildRegionOnlyCoversItsOwnLeds()
    {
        var region = new StudioRegion();
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "abc" };
        device.Leds.Add(new StudioLedRef(1, 2));
        region.Devices.Add(device);

        var mask = StudioMaskBuilder.BuildRegion(region, 736, "abc", 2, 3);

        Assert.Equal(0, mask[0]);
        Assert.Equal(StudioMaskBuilder.Full, mask[1 * 3 + 2]);
    }

    [Fact]
    public void RegionParamsRenderIndependently()
    {
        var red = new StudioEffectParams { Effect = StudioEffectKind.Solid, Primary = new StudioRgb(255, 0, 0) };
        var blue = new StudioEffectParams { Effect = StudioEffectKind.Solid, Primary = new StudioRgb(0, 0, 255) };
        var layers = new[]
        {
            new StudioLayerRender(red, new byte[] { 100, 0, 0 }, 100, StudioBlendMode.Normal, true, Guid.NewGuid()),
            new StudioLayerRender(blue, new byte[] { 0, 0, 100 }, 100, StudioBlendMode.Normal, true, Guid.NewGuid()),
        };

        var frame = StudioCompositor.RenderLayers(layers, 1, 3, 0);

        Assert.Equal(red.Primary, frame[0]);
        Assert.Equal(StudioRgb.Black, frame[1]);
        Assert.Equal(blue.Primary, frame[2]);
    }
}
