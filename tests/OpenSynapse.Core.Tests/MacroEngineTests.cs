using OpenSynapse.Core.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroEngineTests
{
    private static MacroInput Key(int virtualKey, int scanCode = 0) => new()
    {
        Kind = MacroInputKind.Keyboard,
        VirtualKey = virtualKey,
        ScanCode = scanCode,
    };

    private static MacroInput Wheel(int delta) => new()
    {
        Kind = MacroInputKind.MouseWheel,
        WheelDelta = delta,
    };

    [Fact]
    public void SingleTapBecomesOneStepWithItsHold()
    {
        var session = new MacroRecordingSession();
        var key = Key(0x41, 0x1E);

        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000));
        Assert.True(session.TryAdd(key, isDown: false, timestampMilliseconds: 1_250));

        var step = Assert.Single(session.Snapshot());
        Assert.Equal(MacroStepKind.Input, step.Kind);
        Assert.True(step.IsDown);
        Assert.True(step.AutoRelease);
        Assert.Equal(0, step.DelayMilliseconds);
        Assert.Equal(250, step.HoldMilliseconds);
        Assert.Null(step.GroupId);
    }

    [Fact]
    public void AutoRepeatDoesNotCreateExtraTapsAndKeepsTheOriginalPressTime()
    {
        var session = new MacroRecordingSession();
        var key = Key(0x57, 0x11);

        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000));
        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_040));
        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_080));
        Assert.True(session.TryAdd(key, isDown: false, timestampMilliseconds: 1_500));

        var step = Assert.Single(session.Snapshot());
        Assert.True(step.IsDown);
        Assert.True(step.AutoRelease);
        Assert.Equal(500, step.HoldMilliseconds);
    }

    [Fact]
    public void ChordHoldsEveryInputUntilItsExplicitRelease()
    {
        var session = new MacroRecordingSession();
        var shift = Key(0x10, 0x2A);
        var a = Key(0x41, 0x1E);

        Assert.True(session.TryAdd(shift, isDown: true, timestampMilliseconds: 1_000));
        Assert.True(session.TryAdd(a, isDown: true, timestampMilliseconds: 1_100));
        Assert.True(session.TryAdd(a, isDown: false, timestampMilliseconds: 1_150));

        var three = session.Snapshot();
        Assert.Equal(3, three.Count);
        var downs = three.Where(step => step.IsDown).ToList();
        Assert.Equal(2, downs.Count);
        Assert.All(downs, step =>
        {
            Assert.False(step.AutoRelease);
            Assert.False(string.IsNullOrEmpty(step.GroupId));
        });
        Assert.Equal(downs[0].GroupId, downs[1].GroupId);
        var firstUp = Assert.Single(three, step => !step.IsDown);
        Assert.Equal(downs[0].GroupId, firstUp.GroupId);
        Assert.Equal(0, three[0].DelayMilliseconds);
        Assert.Equal(100, three[1].DelayMilliseconds);
        Assert.Equal(50, three[2].DelayMilliseconds);

        Assert.True(session.TryAdd(shift, isDown: false, timestampMilliseconds: 1_200));

        var four = session.Snapshot();
        Assert.Equal(4, four.Count);
        Assert.All(four, step => Assert.Equal(four[0].GroupId, step.GroupId));
        Assert.False(four[3].IsDown);
        Assert.Equal(50, four[3].DelayMilliseconds);
        Assert.DoesNotContain(four, step => step.AutoRelease && step.IsDown);
    }

    [Fact]
    public void SecondTapDelayStartsAtThePreviousReleaseNotItsPress()
    {
        var session = new MacroRecordingSession();
        var key = Key(0x41, 0x1E);

        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000));
        Assert.True(session.TryAdd(key, isDown: false, timestampMilliseconds: 1_100));
        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 2_000));
        Assert.True(session.TryAdd(key, isDown: false, timestampMilliseconds: 2_050));

        var steps = session.Snapshot();
        Assert.Equal(2, steps.Count);
        Assert.Equal(0, steps[0].DelayMilliseconds);
        Assert.Equal(100, steps[0].HoldMilliseconds);
        Assert.Equal(900, steps[1].DelayMilliseconds);
        Assert.Equal(50, steps[1].HoldMilliseconds);
    }

    [Fact]
    public void FixedDelayIsUsedAfterTheFirstStepAndIsClamped()
    {
        var session = new MacroRecordingSession(new MacroRecordingOptions
        {
            DelayMode = MacroDelayMode.Fixed,
            FixedDelayMilliseconds = 250,
        });
        var key = Key(0x41, 0x1E);

        session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000);
        session.TryAdd(key, isDown: false, timestampMilliseconds: 1_050);
        session.TryAdd(key, isDown: true, timestampMilliseconds: 5_000);
        session.TryAdd(key, isDown: false, timestampMilliseconds: 5_050);

        var steps = session.Snapshot();
        Assert.Equal(0, steps[0].DelayMilliseconds);
        Assert.Equal(250, steps[1].DelayMilliseconds);

        var clamped = new MacroRecordingSession(new MacroRecordingOptions
        {
            DelayMode = MacroDelayMode.Fixed,
            FixedDelayMilliseconds = 120_000,
        });
        clamped.TryAdd(key, isDown: true, timestampMilliseconds: 1_000);
        clamped.TryAdd(key, isDown: false, timestampMilliseconds: 1_010);
        clamped.TryAdd(key, isDown: true, timestampMilliseconds: 2_000);
        clamped.TryAdd(key, isDown: false, timestampMilliseconds: 2_010);

        Assert.Equal(
            MacroDefinition.MaxDelayMilliseconds,
            clamped.Snapshot()[1].DelayMilliseconds);
    }

    [Fact]
    public void NoneDelayModeRecordsNoDelays()
    {
        var session = new MacroRecordingSession(new MacroRecordingOptions
        {
            DelayMode = MacroDelayMode.None,
        });
        var key = Key(0x41, 0x1E);

        session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000);
        session.TryAdd(key, isDown: false, timestampMilliseconds: 1_900);
        session.TryAdd(key, isDown: true, timestampMilliseconds: 2_000);
        session.TryAdd(key, isDown: false, timestampMilliseconds: 2_900);

        Assert.All(session.Snapshot(), step => Assert.Equal(0, step.DelayMilliseconds));
    }

    [Fact]
    public void AbsoluteMousePathAggregatesThrottlesAndKeepsTheLastPoint()
    {
        var session = new MacroRecordingSession(new MacroRecordingOptions
        {
            MouseTrackingMode = MacroMouseTrackingMode.Absolute,
        });

        Assert.True(session.TryAddMouseMove(10, 20, 1_000, 0, 0, 0, 0));
        Assert.True(session.TryAddMouseMove(11, 21, 1_005, 0, 0, 0, 0));
        Assert.True(session.TryAddMouseMove(12, 22, 1_010, 0, 0, 0, 0));
        Assert.True(session.TryAddMouseMove(13, 23, 1_016, 0, 0, 0, 0));
        Assert.True(session.TryAddMouseMove(14, 24, 1_020, 0, 0, 0, 0));

        var step = Assert.Single(session.Snapshot());
        Assert.Equal(MacroStepKind.MouseMove, step.Kind);
        Assert.Equal(MacroMouseMoveMode.Absolute, step.MouseMoveMode);
        Assert.Equal(0, step.DelayMilliseconds);
        Assert.Collection(
            step.MousePath,
            point => Assert.Equal((10, 20, 0), (point.X, point.Y, point.OffsetMilliseconds)),
            point => Assert.Equal((13, 23, 16), (point.X, point.Y, point.OffsetMilliseconds)),
            point => Assert.Equal((14, 24, 20), (point.X, point.Y, point.OffsetMilliseconds)));
    }

    [Fact]
    public void NonMoveEventStartsANewMouseStep()
    {
        var session = new MacroRecordingSession(new MacroRecordingOptions
        {
            MouseTrackingMode = MacroMouseTrackingMode.Absolute,
        });
        var key = Key(0x41, 0x1E);

        session.TryAddMouseMove(10, 20, 1_000, 0, 0, 0, 0);
        session.TryAddMouseMove(11, 21, 1_008, 0, 0, 0, 0);
        session.TryAdd(key, isDown: true, timestampMilliseconds: 2_000);
        session.TryAdd(key, isDown: false, timestampMilliseconds: 2_100);
        session.TryAddMouseMove(30, 40, 2_200, 0, 0, 0, 0);

        var steps = session.Snapshot();
        Assert.Equal(3, steps.Count);
        Assert.Equal(MacroStepKind.MouseMove, steps[0].Kind);
        Assert.Equal(MacroStepKind.Input, steps[1].Kind);
        Assert.Equal(MacroStepKind.MouseMove, steps[2].Kind);
        var secondPath = Assert.Single(steps[2].MousePath);
        Assert.Equal((30, 40, 0), (secondPath.X, secondPath.Y, secondPath.OffsetMilliseconds));
        Assert.Equal(100, steps[2].DelayMilliseconds);
        // The throttled point was kept when the mouse step was closed.
        Assert.Equal(2, steps[0].MousePath.Count);
        Assert.Equal((11, 21, 8), (
            steps[0].MousePath[1].X,
            steps[0].MousePath[1].Y,
            steps[0].MousePath[1].OffsetMilliseconds));
    }

    [Fact]
    public void RelativeMousePathUsesDeltasFromThePreviousAcceptedPoint()
    {
        var session = new MacroRecordingSession(new MacroRecordingOptions
        {
            MouseTrackingMode = MacroMouseTrackingMode.Relative,
        });
        var key = Key(0x41, 0x1E);

        session.TryAddMouseMove(100, 100, 1_000, 0, 0, 0, 0);
        session.TryAddMouseMove(130, 150, 1_016, 0, 0, 0, 0);
        session.TryAdd(key, isDown: true, timestampMilliseconds: 2_000);
        session.TryAdd(key, isDown: false, timestampMilliseconds: 2_010);
        session.TryAddMouseMove(200, 200, 2_100, 0, 0, 0, 0);

        var steps = session.Snapshot();
        Assert.Equal(3, steps.Count);
        Assert.Equal(MacroMouseMoveMode.Relative, steps[0].MouseMoveMode);
        Assert.Collection(
            steps[0].MousePath,
            point => Assert.Equal((0, 0, 0), (point.X, point.Y, point.OffsetMilliseconds)),
            point => Assert.Equal((30, 50, 16), (point.X, point.Y, point.OffsetMilliseconds)));
        var next = Assert.Single(steps[2].MousePath);
        Assert.Equal((70, 50, 0), (next.X, next.Y, next.OffsetMilliseconds));
    }

    [Fact]
    public void StartPointMousePathUsesTheFirstAcceptedPointAsOrigin()
    {
        var session = new MacroRecordingSession(new MacroRecordingOptions
        {
            MouseTrackingMode = MacroMouseTrackingMode.StartPoint,
        });
        var key = Key(0x41, 0x1E);

        session.TryAddMouseMove(1_000, 500, 1_000, 0, 0, 0, 0);
        session.TryAddMouseMove(1_060, 540, 1_016, 0, 0, 0, 0);
        session.TryAdd(key, isDown: true, timestampMilliseconds: 2_000);
        session.TryAdd(key, isDown: false, timestampMilliseconds: 2_010);
        session.TryAddMouseMove(1_100, 500, 2_100, 0, 0, 0, 0);

        var steps = session.Snapshot();
        Assert.Equal((0, 0), (steps[0].MousePath[0].X, steps[0].MousePath[0].Y));
        Assert.Equal((60, 40), (steps[0].MousePath[1].X, steps[0].MousePath[1].Y));
        Assert.Equal((100, 0), (steps[2].MousePath[0].X, steps[2].MousePath[0].Y));
    }

    [Fact]
    public void ForegroundMousePathIsNormalizedToTheWindowRectangle()
    {
        var session = new MacroRecordingSession(new MacroRecordingOptions
        {
            MouseTrackingMode = MacroMouseTrackingMode.Foreground,
        });

        Assert.True(session.TryAddMouseMove(600, 450, 1_000, 100, 200, 1_000, 500));
        Assert.True(session.TryAddMouseMove(-100, -100, 1_020, 100, 200, 1_000, 500));
        Assert.True(session.TryAddMouseMove(2_000, 1_000, 1_040, 100, 200, 1_000, 500));

        var step = Assert.Single(session.Snapshot());
        Assert.Equal(MacroMouseMoveMode.ForegroundRelative, step.MouseMoveMode);
        Assert.Collection(
            step.MousePath,
            point => Assert.Equal((5_000, 5_000, 0), (point.X, point.Y, point.OffsetMilliseconds)),
            point => Assert.Equal((0, 0, 20), (point.X, point.Y, point.OffsetMilliseconds)),
            point => Assert.Equal((10_000, 10_000, 40), (point.X, point.Y, point.OffsetMilliseconds)));

        var invalid = new MacroRecordingSession(new MacroRecordingOptions
        {
            MouseTrackingMode = MacroMouseTrackingMode.Foreground,
        });
        Assert.False(invalid.TryAddMouseMove(1, 1, 1_000, 0, 0, 0, 500));
        Assert.Empty(invalid.Snapshot());
    }

    [Fact]
    public void DisabledMouseTrackingRecordsNothing()
    {
        var session = new MacroRecordingSession();

        Assert.False(session.TryAddMouseMove(10, 20, 1_000, 0, 0, 100, 100));
        Assert.Empty(session.Snapshot());
        Assert.Equal(0, session.Count);
    }

    [Fact]
    public void UnrelatedReleaseKeepsThePendingPressInEventOrder()
    {
        var session = new MacroRecordingSession();
        var a = Key(0x41, 0x1E);
        var b = Key(0x42, 0x30);

        session.TryAdd(a, isDown: true, timestampMilliseconds: 1_000);
        session.TryAdd(b, isDown: false, timestampMilliseconds: 1_100);
        session.TryAdd(a, isDown: false, timestampMilliseconds: 1_200);

        var steps = session.Snapshot();
        Assert.Equal(3, steps.Count);
        Assert.Equal(a.VirtualKey, steps[0].Input!.VirtualKey);
        Assert.True(steps[0].IsDown);
        Assert.False(steps[0].AutoRelease);
        Assert.Equal(b.VirtualKey, steps[1].Input!.VirtualKey);
        Assert.False(steps[1].IsDown);
        Assert.Equal(100, steps[1].DelayMilliseconds);
        Assert.Equal(a.VirtualKey, steps[2].Input!.VirtualKey);
        Assert.False(steps[2].IsDown);
        Assert.Equal(steps[0].GroupId, steps[2].GroupId);
        Assert.Equal(100, steps[2].DelayMilliseconds);
    }

    [Fact]
    public void WheelWhileKeyIsHeldKeepsTheKeyDownAndInsertsOneStep()
    {
        var session = new MacroRecordingSession();
        var key = Key(0x41, 0x1E);
        var wheel = Wheel(120);

        session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000);
        Assert.True(session.TryAdd(wheel, isDown: false, timestampMilliseconds: 1_050));
        session.TryAdd(key, isDown: false, timestampMilliseconds: 1_100);

        var steps = session.Snapshot();
        Assert.Equal(3, steps.Count);
        Assert.True(steps[0].IsDown);
        Assert.False(steps[0].AutoRelease);
        Assert.False(string.IsNullOrEmpty(steps[0].GroupId));
        Assert.Equal(0, steps[0].DelayMilliseconds);
        Assert.Equal(MacroInputKind.MouseWheel, steps[1].Input!.Kind);
        Assert.False(steps[1].IsDown);
        Assert.Equal(50, steps[1].DelayMilliseconds);
        Assert.False(steps[2].IsDown);
        Assert.Equal(steps[0].GroupId, steps[2].GroupId);
        Assert.Equal(50, steps[2].DelayMilliseconds);
    }

    [Fact]
    public void SnapshotLeavesAnUnreleasedPressHeldWithoutInventingARelease()
    {
        var session = new MacroRecordingSession();
        var key = Key(0x41, 0x1E);

        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000));

        Assert.Equal(0, session.Count);
        var step = Assert.Single(session.Snapshot());
        Assert.True(step.IsDown);
        Assert.False(step.AutoRelease);
        Assert.Equal(0, step.DelayMilliseconds);
        Assert.True(step.Input!.Matches(key));
    }

    [Fact]
    public void MousePathStopsAtItsPointCapButKeepsTheLastPosition()
    {
        var session = new MacroRecordingSession(new MacroRecordingOptions
        {
            MouseTrackingMode = MacroMouseTrackingMode.Absolute,
        });

        var count = MacroStep.MaxMousePathPoints + 100;
        for (var index = 0; index < count; index++)
        {
            session.TryAddMouseMove(index, index, 1_000 + index * 20L, 0, 0, 0, 0);
        }

        var step = Assert.Single(session.Snapshot());
        Assert.Equal(MacroStep.MaxMousePathPoints, step.MousePath.Count);
        Assert.Equal(count - 1, step.MousePath[^1].X);
    }

    [Fact]
    public void RecordingStopsAtTheTotalStepLimit()
    {
        var session = new MacroRecordingSession();
        var key = Key(0x41, 0x1E);

        for (var index = 0; index < MacroDefinition.MaxSteps + 10; index++)
        {
            var timestamp = 1_000 + index * 100L;
            session.TryAdd(key, isDown: true, timestampMilliseconds: timestamp);
            session.TryAdd(key, isDown: false, timestampMilliseconds: timestamp + 10);
        }

        Assert.Equal(MacroDefinition.MaxSteps, session.Count);
        Assert.Equal(MacroDefinition.MaxSteps, session.Snapshot().Count);
    }

    [Fact]
    public void EventsAfterTheMaximumDurationAreRejected()
    {
        var session = new MacroRecordingSession();
        var key = Key(0x41, 0x1E);

        session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000);
        session.TryAdd(key, isDown: false, timestampMilliseconds: 1_010);

        Assert.False(session.TryAdd(
            key,
            isDown: true,
            timestampMilliseconds: 1_000 + MacroRecordingSession.MaxDurationMilliseconds + 1));
        Assert.Single(session.Snapshot());
    }
}
