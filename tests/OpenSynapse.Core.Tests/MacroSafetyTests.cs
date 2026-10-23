using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroSafetyTests
{
    [Fact]
    public void PlaybackRejectsAnIncompleteStepWithoutRemovingItFromTheSource()
    {
        var source = new MacroDefinition
        {
            Steps =
            [
                new MacroStep { Kind = MacroStepKind.Text, Text = string.Empty },
                new MacroStep { Kind = MacroStepKind.Delay, DelayMilliseconds = 5 },
            ],
        };

        Assert.Throws<InvalidOperationException>(() => MacroPlaybackPlan.Build(source));

        Assert.Equal(2, source.Steps.Count);
    }

    [Fact]
    public void ModifierOnlyTriggerIsRejected()
    {
        var macro = new MacroDefinition
        {
            Trigger = new MacroTrigger
            {
                Input = new MacroInput
                {
                    Kind = MacroInputKind.Keyboard,
                    VirtualKey = 0x11,
                },
            },
        };

        macro.ApplySafeDefaults();

        Assert.Null(macro.Trigger);
    }

    [Fact]
    public void ProfileCloneKeepsAnIncompleteStepInTheLiveMacro()
    {
        var macro = new MacroDefinition
        {
            Steps =
            [
                new MacroStep { Kind = MacroStepKind.Text, Text = string.Empty },
                new MacroStep { Kind = MacroStepKind.Delay, DelayMilliseconds = 10 },
            ],
        };
        var document = new ProfileDocument
        {
            ActiveProfileName = "Working",
            Profiles =
            {
                ["Working"] = new ProfileDefinition { Macros = [macro] },
            },
        };

        var clone = document.Clone();

        Assert.Equal(2, macro.Steps.Count);
        var clonedMacro = Assert.Single(
            clone.Profiles["Working"].Macros);
        Assert.Equal(2, clonedMacro.Steps.Count);
    }

    [Fact]
    public void IncompleteReferencedMacroIsRejectedInsteadOfPartiallyExpanded()
    {
        var referenced = new MacroDefinition
        {
            Id = "referenced",
            Steps =
            [
                new MacroStep { Kind = MacroStepKind.Text, Text = string.Empty },
                new MacroStep { Kind = MacroStepKind.Delay, DelayMilliseconds = 10 },
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
            ],
        };

        Assert.Throws<InvalidOperationException>(() => MacroPlaybackPlan.Build(
            root,
            new Dictionary<string, MacroDefinition> { [referenced.Id] = referenced }));
    }

    [Fact]
    public void StartPointPathIsConvertedToConsecutiveRelativeDeltas()
    {
        var macro = new MacroDefinition
        {
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.MouseMove,
                    MouseMoveMode = MacroMouseMoveMode.RelativeToStart,
                    MousePath =
                    [
                        new MacroMousePoint { X = 0, Y = 0 },
                        new MacroMousePoint { X = 60, Y = 40 },
                        new MacroMousePoint { X = 100, Y = 0 },
                    ],
                },
            ],
        };

        var plan = MacroPlaybackPlan.Build(macro);
        var action = Assert.Single(plan.Actions);

        Assert.Equal(MacroPlaybackActionKind.MouseMove, action.Kind);
        Assert.Equal(MacroMouseMoveMode.Relative, action.MouseMoveMode);
        Assert.Collection(
            action.MousePath,
            point => Assert.Equal((0, 0), (point.X, point.Y)),
            point => Assert.Equal((60, 40), (point.X, point.Y)),
            point => Assert.Equal((40, -40), (point.X, point.Y)));
    }
}
