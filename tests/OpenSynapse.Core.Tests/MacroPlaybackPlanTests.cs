using OpenSynapse.Core.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroPlaybackPlanTests
{
    private static MacroInput Key(int virtualKey) => new()
    {
        Kind = MacroInputKind.Keyboard,
        VirtualKey = virtualKey,
        ScanCode = 0,
    };

    [Fact]
    public void TapBecomesDownHoldAndUp()
    {
        var macro = new MacroDefinition
        {
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = Key(0x41),
                    IsDown = true,
                    AutoRelease = true,
                    DelayMilliseconds = 100,
                    HoldMilliseconds = 50,
                },
            ],
        };

        var plan = MacroPlaybackPlan.Build(macro);

        Assert.Collection(
            plan.Actions,
            action =>
            {
                Assert.Equal(MacroPlaybackActionKind.KeyDown, action.Kind);
                Assert.Equal(100, action.DelayMilliseconds);
            },
            action =>
            {
                Assert.Equal(MacroPlaybackActionKind.Delay, action.Kind);
                Assert.Equal(50, action.DelayMilliseconds);
            },
            action =>
            {
                Assert.Equal(MacroPlaybackActionKind.KeyUp, action.Kind);
            });
        Assert.Equal(150, plan.TotalDurationMilliseconds);
    }

    [Fact]
    public void ExplicitDownAndUpStaySeparate()
    {
        var macro = new MacroDefinition
        {
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = Key(0xA0),
                    IsDown = true,
                    AutoRelease = false,
                },
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = Key(0x41),
                    IsDown = true,
                    AutoRelease = true,
                    HoldMilliseconds = 25,
                },
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = Key(0xA0),
                    IsDown = false,
                },
            ],
        };

        var plan = MacroPlaybackPlan.Build(macro);

        Assert.Equal(
            [
                MacroPlaybackActionKind.KeyDown,
                MacroPlaybackActionKind.KeyDown,
                MacroPlaybackActionKind.Delay,
                MacroPlaybackActionKind.KeyUp,
                MacroPlaybackActionKind.KeyUp,
            ],
            plan.Actions.Select(action => action.Kind));
    }

    [Fact]
    public void PlaybackSpeedScalesDelayAndHold()
    {
        var macro = new MacroDefinition
        {
            PlaybackSpeed = 2.0,
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = Key(0x41),
                    IsDown = true,
                    AutoRelease = true,
                    DelayMilliseconds = 100,
                    HoldMilliseconds = 50,
                },
            ],
        };

        var plan = MacroPlaybackPlan.Build(macro);

        Assert.Equal(50, plan.Actions[0].DelayMilliseconds);
        Assert.Equal(25, plan.Actions[1].DelayMilliseconds);
        Assert.Equal(75, plan.TotalDurationMilliseconds);
    }

    [Fact]
    public void LoopExpandsChildren()
    {
        var macro = new MacroDefinition
        {
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.Loop,
                    LoopCount = 3,
                    Children =
                    [
                        new MacroStep
                        {
                            Kind = MacroStepKind.Input,
                            Input = Key(0x41),
                            IsDown = true,
                            AutoRelease = true,
                            HoldMilliseconds = 10,
                        },
                    ],
                },
            ],
        };

        var plan = MacroPlaybackPlan.Build(macro);

        Assert.Equal(3, plan.Actions.Count(action => action.Kind == MacroPlaybackActionKind.KeyDown));
        Assert.Equal(3, plan.Actions.Count(action => action.Kind == MacroPlaybackActionKind.KeyUp));
    }

    [Fact]
    public void MacroReferenceExpandsAndCycleIsSkipped()
    {
        var referenced = new MacroDefinition
        {
            Id = "referenced",
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = Key(0x42),
                    IsDown = true,
                    AutoRelease = true,
                    HoldMilliseconds = 10,
                },
            ],
        };
        var root = new MacroDefinition
        {
            Id = "root",
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.MacroReference,
                    MacroId = "referenced",
                },
                new MacroStep
                {
                    Kind = MacroStepKind.MacroReference,
                    MacroId = "root",
                },
            ],
        };

        var plan = MacroPlaybackPlan.Build(
            root,
            new Dictionary<string, MacroDefinition> { [referenced.Id] = referenced, [root.Id] = root });

        Assert.Contains(plan.Actions, action => action.Kind == MacroPlaybackActionKind.KeyDown);
        Assert.Contains(plan.Warnings, warning => warning.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoopAndReferenceKeepTheirConfiguredDelay()
    {
        var referenced = new MacroDefinition
        {
            Id = "referenced",
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = Key(0x42),
                    IsDown = true,
                    AutoRelease = true,
                    HoldMilliseconds = 10,
                },
            ],
        };
        var root = new MacroDefinition
        {
            Id = "root",
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.Loop,
                    DelayMilliseconds = 25,
                    LoopCount = 2,
                    Children =
                    [
                        new MacroStep
                        {
                            Kind = MacroStepKind.Input,
                            Input = Key(0x41),
                            IsDown = true,
                            AutoRelease = true,
                            HoldMilliseconds = 10,
                        },
                    ],
                },
                new MacroStep
                {
                    Kind = MacroStepKind.MacroReference,
                    DelayMilliseconds = 40,
                    MacroId = "referenced",
                },
            ],
        };

        var plan = MacroPlaybackPlan.Build(
            root,
            new Dictionary<string, MacroDefinition> { [referenced.Id] = referenced });

        Assert.Equal(
            2,
            plan.Actions.Count(action =>
                action.Kind == MacroPlaybackActionKind.Delay &&
                action.DelayMilliseconds == 25));
        Assert.Contains(
            plan.Actions,
            action => action.Kind == MacroPlaybackActionKind.Delay &&
                action.DelayMilliseconds == 40);
        Assert.Equal(120, plan.TotalDurationMilliseconds);
    }

    [Fact]
    public void TextCommandAndMousePathBecomeActions()
    {
        var macro = new MacroDefinition
        {
            Steps =
            [
                new MacroStep { Kind = MacroStepKind.Text, Text = "hello" },
                new MacroStep
                {
                    Kind = MacroStepKind.RunCommand,
                    Command = "notepad.exe",
                    Arguments = "test.txt",
                    WorkingDirectory = "C:\\",
                },
                new MacroStep
                {
                    Kind = MacroStepKind.MouseMove,
                    MouseMoveMode = MacroMouseMoveMode.Absolute,
                    MousePath =
                    [
                        new MacroMousePoint { X = 10, Y = 20, OffsetMilliseconds = 0 },
                        new MacroMousePoint { X = 30, Y = 40, OffsetMilliseconds = 100 },
                    ],
                },
            ],
        };

        var plan = MacroPlaybackPlan.Build(macro);

        Assert.Contains(plan.Actions, action => action.Kind == MacroPlaybackActionKind.Text && action.Text == "hello");
        Assert.Contains(plan.Actions, action => action.Kind == MacroPlaybackActionKind.RunCommand);
        var move = Assert.Single(plan.Actions, action => action.Kind == MacroPlaybackActionKind.MouseMove);
        Assert.Equal(2, move.MousePath.Count);
        Assert.Equal(100, plan.TotalDurationMilliseconds);
    }
}
