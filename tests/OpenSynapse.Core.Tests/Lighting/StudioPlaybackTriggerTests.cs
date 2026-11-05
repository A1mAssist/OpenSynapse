using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioPlaybackTriggerTests
{
    [Fact]
    public void OnPressTurnsABlackLayerOn()
    {
        var harness = new Harness(1, 3);
        var layer = harness.Add(Solid(StudioPlaybackStart.OnPress));

        Assert.Equal(default, harness.Render(0)[1]);

        Assert.Equal(1, harness.Press(0, 1, 0));
        Assert.True(harness.Clock(layer).IsRunning);
        Assert.NotEqual(default, harness.Render(0)[1]);
    }

    [Fact]
    public void OnSelectedKeysOnlyTriggersTheLayerWhoseMaskContainsTheKey()
    {
        var harness = new Harness(1, 3);
        var left = harness.Add(Solid(StudioPlaybackStart.OnSelectedKeys), Mask(3, 0));
        var right = harness.Add(Solid(StudioPlaybackStart.OnSelectedKeys), Mask(3, 2));

        Assert.Equal(0, harness.Press(0, 1, 0));
        Assert.False(harness.Clock(left).IsRunning);
        Assert.False(harness.Clock(right).IsRunning);

        Assert.Equal(1, harness.Press(0, 0, 1));
        Assert.True(harness.Clock(left).IsRunning);
        Assert.False(harness.Clock(right).IsRunning);
        var frame = harness.Render(1);
        Assert.NotEqual(default, frame[0]);
        Assert.Equal(default, frame[2]);

        Assert.Equal(1, harness.Press(0, 2, 2));
        Assert.True(harness.Clock(right).IsRunning);
    }

    [Fact]
    public void OnSelectedKeysWithoutAMaskUsesTheWholeDevice()
    {
        var harness = new Harness(1, 3);
        var layer = harness.Add(Solid(StudioPlaybackStart.OnSelectedKeys));

        Assert.Equal(1, harness.Press(0, 1, 0));
        Assert.True(harness.Clock(layer).IsRunning);
    }

    [Fact]
    public void ToggleLayerStartsAndStopsOnPress()
    {
        var harness = new Harness(1, 3);
        var layer = harness.Add(Solid(StudioPlaybackStart.OnPress, StudioPlaybackEnd.Toggle));

        Assert.Equal(1, harness.Press(0, 0, 0));
        Assert.True(harness.Clock(layer).IsRunning);
        Assert.NotEqual(default, harness.Render(0)[1]);

        Assert.Equal(1, harness.Press(0, 0, 1));
        Assert.False(harness.Clock(layer).IsRunning);
        Assert.Equal(default, harness.Render(1)[1]);

        Assert.Equal(1, harness.Press(0, 0, 2));
        Assert.True(harness.Clock(layer).IsRunning);
    }

    [Fact]
    public void RandomToggleStopsUntilPressedAgain()
    {
        var harness = new Harness(1, 3);
        var layer = harness.Add(Solid(StudioPlaybackStart.Random, StudioPlaybackEnd.Toggle));

        Assert.NotEqual(default, harness.Render(0.1)[1]);
        Assert.True(harness.Clock(layer).IsRunning);

        Assert.Equal(1, harness.Press(0, 1, 1));
        Assert.False(harness.Clock(layer).IsRunning);
        Assert.Equal(default, harness.Render(1.1)[1]);

        Assert.Equal(1, harness.Press(0, 1, 2));
        Assert.True(harness.Clock(layer).IsRunning);
    }

    [Fact]
    public void AfterCyclesStopsAndTheNextPressRestarts()
    {
        var harness = new Harness(1, 3);
        var parameters = Solid(
            StudioPlaybackStart.OnPress, StudioPlaybackEnd.After, durationMs: 100, cycles: 2);
        var layer = harness.Add(parameters);
        var clock = harness.Clock(layer);

        Assert.Equal(1, harness.Press(0, 0, 0));
        Assert.True(clock.Sample(0.05, parameters).Running);
        Assert.False(clock.Sample(0.25, parameters).Running);
        Assert.False(clock.Sample(0.3, parameters).Running);

        Assert.Equal(1, harness.Press(0, 0, 0.4));
        var restarted = clock.Sample(0.4, parameters);
        Assert.True(restarted.Running);
        Assert.Equal(0, restarted.LocalSeconds, 3);
        Assert.NotEqual(default, harness.Render(0.45)[1]);
    }

    [Fact]
    public void ClockStartedAheadOfTheRenderTimelineStillAdvances()
    {
        var parameters = Solid(StudioPlaybackStart.OnPress, durationMs: 1_000);
        var clock = new StudioPlaybackClock();

        clock.Trigger(10);

        Assert.True(clock.Sample(0, parameters).Running);
        Assert.Equal(1, clock.Sample(1, parameters).LocalSeconds, 3);
    }

    [Fact]
    public void ReleaseAndOutOfRangeInputsDoNotTrigger()
    {
        var harness = new Harness(1, 3);
        var layer = harness.Add(Solid(StudioPlaybackStart.OnPress));

        Assert.Equal(0, harness.Press(0, 0, 0, isDown: false));
        Assert.Equal(0, harness.Press(-1, 0, 1));
        Assert.Equal(0, harness.Press(0, 3, 2));
        Assert.False(harness.Clock(layer).IsRunning);

        var empty = new StudioPlaybackTrigger(_ => new StudioPlaybackClock());
        empty.Update([], 1, 3);
        Assert.Equal(0, empty.Handle(new StudioTriggerInput(0, 0), 0));
    }

    [Fact]
    public void RegionSegmentsOfOneLayerToggleOnlyOnce()
    {
        var harness = new Harness(1, 3);
        var parameters = Solid(StudioPlaybackStart.OnPress, StudioPlaybackEnd.Toggle);
        var layer = harness.Add(parameters, Mask(3, 0));
        harness.Add(parameters, Mask(3, 2), layer);

        Assert.Equal(1, harness.Press(0, 0, 0));
        Assert.True(harness.Clock(layer).IsRunning);
        Assert.Equal(1, harness.Press(0, 2, 1));
        Assert.False(harness.Clock(layer).IsRunning);
    }

    private static StudioEffectParams Solid(
        StudioPlaybackStart start,
        StudioPlaybackEnd end = StudioPlaybackEnd.Never,
        int durationMs = 0,
        int cycles = -1) => new()
    {
        Effect = StudioEffectKind.Solid,
        Primary = new StudioRgb(0, 255, 0),
        PlaybackStart = start,
        PlaybackEnd = end,
        DurationMs = durationMs,
        Cycles = cycles,
    };

    private static byte[] Mask(int length, params int[] selected)
    {
        var mask = new byte[length];
        foreach (var index in selected) mask[index] = StudioMaskBuilder.Full;
        return mask;
    }

    private sealed class Harness
    {
        private readonly List<StudioLayerRender> _layers = [];
        private readonly Dictionary<Guid, StudioPlaybackClock> _clocks = [];
        private readonly int _rows;
        private readonly int _columns;

        public Harness(int rows, int columns)
        {
            _rows = rows;
            _columns = columns;
            Trigger = new StudioPlaybackTrigger(GetClock);
            Trigger.Update(_layers, rows, columns);
        }

        public StudioPlaybackTrigger Trigger { get; }

        public Guid Add(StudioEffectParams parameters, byte[]? mask = null, Guid? id = null)
        {
            var layerId = id ?? Guid.NewGuid();
            _layers.Add(new StudioLayerRender(
                parameters, mask, 100, StudioBlendMode.Normal, true, layerId));
            Trigger.Update(_layers, _rows, _columns);
            return layerId;
        }

        public StudioPlaybackClock Clock(Guid id) => GetClock(id);

        public int Press(int row, int column, double nowSeconds, bool isDown = true) =>
            Trigger.Handle(new StudioTriggerInput(row, column, isDown), nowSeconds);

        public StudioRgb[] Render(double nowSeconds)
        {
            var timed = new (StudioLayerRender Layer, double Seconds)[_layers.Count];
            for (var index = 0; index < _layers.Count; index++)
            {
                var layer = _layers[index];
                var state = GetClock(layer.Id).Sample(nowSeconds, layer.Params);
                timed[index] = (
                    layer with { Opacity = state.Running ? layer.Opacity : 0 },
                    state.LocalSeconds);
            }

            return StudioCompositor.RenderTimedLayers(timed, _rows, _columns);
        }

        private StudioPlaybackClock GetClock(Guid id)
        {
            if (!_clocks.TryGetValue(id, out var clock))
            {
                clock = new StudioPlaybackClock();
                _clocks[id] = clock;
            }

            return clock;
        }
    }
}
