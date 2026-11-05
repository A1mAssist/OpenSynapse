using OpenSynapse.Core.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroTriggerTests
{
    [Fact]
    public void TriggerRequiresExactModifierMask()
    {
        var trigger = new MacroTrigger
        {
            Input = new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = 0x4D,
            },
            Modifiers = MacroModifierKeys.Control | MacroModifierKeys.Shift,
        };

        var input = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = 0x4D,
        };

        Assert.True(trigger.Matches(input, MacroModifierKeys.Control | MacroModifierKeys.Shift));
        Assert.False(trigger.Matches(input, MacroModifierKeys.Control));
        Assert.False(trigger.Matches(input, MacroModifierKeys.None));
    }

    [Fact]
    public void ComboFactoryCreatesOneGroupedDownUpSequence()
    {
        var steps = MacroStepFactory.CreateCombo(
            MacroModifierKeys.Control | MacroModifierKeys.Shift,
            new MacroInput
            {
                Kind = MacroInputKind.Keyboard,
                VirtualKey = 0x41,
            });

        Assert.Equal(6, steps.Count);
        var groupId = steps[0].GroupId;
        Assert.False(string.IsNullOrEmpty(groupId));
        Assert.All(steps, step => Assert.Equal(groupId, step.GroupId));
        Assert.All(steps.Take(3), step =>
        {
            Assert.True(step.IsDown);
            Assert.False(step.AutoRelease);
        });
        Assert.All(steps.Skip(3), step => Assert.False(step.IsDown));
        Assert.Equal(0x41, steps[2].Input!.VirtualKey);
        Assert.Equal(0x41, steps[3].Input!.VirtualKey);
    }
}
