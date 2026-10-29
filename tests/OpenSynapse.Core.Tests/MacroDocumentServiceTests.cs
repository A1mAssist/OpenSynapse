using OpenSynapse.Core.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroDocumentServiceTests
{
    private static MacroStep Key(byte vk = 0x41) => new()
    {
        Kind = MacroStepKind.Input,
        Input = new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = vk },
        IsDown = true,
    };

    private static MacroDefinition Macro(string id, params MacroStep[] steps) => new()
    {
        Id = id,
        Name = id,
        Steps = [.. steps],
    };

    [Fact]
    public void ValidateTreeAcceptsInputAndDelay()
    {
        var macro = Macro("a", Key(), new MacroStep { Kind = MacroStepKind.Delay });

        Assert.True(MacroValidator.ValidateTree(macro));
    }

    [Fact]
    public void ValidateTreeRejectsIncompleteStep()
    {
        var macro = Macro("a", new MacroStep { Kind = MacroStepKind.Text });

        Assert.False(MacroValidator.ValidateTree(macro));
        Assert.Equal(MacroValidationFailure.StepIncomplete, MacroValidator.Validate(macro, Map(macro)));
    }

    [Fact]
    public void ValidateTreeRejectsLoopWithoutChildren()
    {
        var macro = Macro("a", new MacroStep { Kind = MacroStepKind.Loop, LoopCount = 2 });

        Assert.False(MacroValidator.ValidateTree(macro));
    }

    [Fact]
    public void ValidateTreeRejectsMoreStepsThanTheLimit()
    {
        var steps = Enumerable.Range(0, MacroDefinition.MaxSteps + 1)
            .Select(_ => new MacroStep { Kind = MacroStepKind.Delay })
            .ToArray();

        Assert.False(MacroValidator.ValidateTree(Macro("a", steps)));
    }

    [Fact]
    public void ValidateReferencesRejectsATwoMacroCycle()
    {
        var first = Macro("a", new MacroStep { Kind = MacroStepKind.MacroReference, MacroId = "b" });
        var second = Macro("b", new MacroStep { Kind = MacroStepKind.MacroReference, MacroId = "a" });

        Assert.False(MacroValidator.ValidateReferences(first, Map(first, second)));
        Assert.Equal(MacroValidationFailure.ReferenceInvalid, MacroValidator.Validate(first, Map(first, second)));
    }

    [Fact]
    public void ValidateReferencesRejectsADanglingReference()
    {
        var macro = Macro("a", new MacroStep { Kind = MacroStepKind.MacroReference, MacroId = "missing" });

        Assert.False(MacroValidator.ValidateReferences(macro, Map(macro)));
    }

    [Fact]
    public void ValidateReferencesAcceptsANestedChain()
    {
        var c = Macro("c", Key());
        var b = Macro("b", new MacroStep { Kind = MacroStepKind.MacroReference, MacroId = "c" });
        var a = Macro("a", new MacroStep { Kind = MacroStepKind.MacroReference, MacroId = "b" });

        Assert.Equal(MacroValidationFailure.None, MacroValidator.Validate(a, Map(a, b, c)));
    }

    [Fact]
    public void CloneStepsIsADeepCopy()
    {
        var loop = new MacroStep { Kind = MacroStepKind.Loop, LoopCount = 3, Children = [Key()] };
        var original = new List<MacroStep> { loop };

        var clone = MacroDocumentService.CloneSteps(original);
        clone[0].Children[0].Input!.VirtualKey = 0x42;
        clone[0].LoopCount = 9;

        Assert.Equal(0x41, original[0].Children[0].Input!.VirtualKey);
        Assert.Equal(3, original[0].LoopCount);
    }

    [Fact]
    public void PrepareImportedRenamesAndKeepsInternalReferences()
    {
        var inner = Macro("inner", Key());
        inner.Name = "Imported macro";
        var outer = Macro("outer", new MacroStep { Kind = MacroStepKind.MacroReference, MacroId = "inner" });
        var imported = new List<MacroDefinition> { outer, inner };

        MacroDocumentService.PrepareImported(imported, () => Guid.NewGuid().ToString("N"), "Renamed");

        Assert.NotEqual("outer", outer.Id);
        Assert.NotEqual("inner", inner.Id);
        Assert.Equal(inner.Id, outer.Steps[0].MacroId);
        Assert.Equal("Renamed", inner.Name);
    }

    private static IReadOnlyDictionary<string, MacroDefinition> Map(params MacroDefinition[] macros) =>
        MacroDocumentService.IndexById(macros);
}
