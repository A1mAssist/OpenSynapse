using OpenSynapse.Core.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MousePathValidationTests
{
    [Fact]
    public void MousePathRequiresAtLeastOnePoint()
    {
        var step = MouseStep();

        Assert.False(step.IsValid());
        Assert.False(MacroMousePath.IsValid([]));
    }

    [Fact]
    public void MousePathAllowsEqualTimeOffsets()
    {
        var step = MouseStep(
            new MacroMousePoint { X = 10, Y = 20, OffsetMilliseconds = 100 },
            new MacroMousePoint { X = 20, Y = 30, OffsetMilliseconds = 100 });

        Assert.True(step.IsValid());
    }

    [Fact]
    public void MousePathRejectsDecreasingTimeOffsets()
    {
        var step = MouseStep(
            new MacroMousePoint { X = 10, Y = 20, OffsetMilliseconds = 100 },
            new MacroMousePoint { X = 20, Y = 30, OffsetMilliseconds = 90 });
        var macro = new MacroDefinition { Name = "invalid", Steps = [step] };

        Assert.False(step.IsValid());
        Assert.Throws<InvalidOperationException>(() => MacroPlaybackPlan.Build(macro));
    }

    private static MacroStep MouseStep(params MacroMousePoint[] points) => new()
    {
        Kind = MacroStepKind.MouseMove,
        MouseMoveMode = MacroMouseMoveMode.Absolute,
        MousePath = [.. points],
    };
}
