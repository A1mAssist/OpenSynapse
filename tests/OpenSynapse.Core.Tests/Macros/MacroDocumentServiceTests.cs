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

    [Fact]
    public async Task Synapse4ExportPassesTheFullImportPipeline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-macro-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                <Macro>
                   <Name>太极炸鱼</Name>
                   <MacroEvents>
                      <MacroEvent><Type>actionBar</Type><recordProfile><mmtSetting>0</mmtSetting></recordProfile><selected>false</selected></MacroEvent>
                      <MacroEvent><Type>1</Type><Id>3</Id><KeyEvent><Makecode>49</Makecode><State>0</State></KeyEvent><selected>false</selected><isPairing>false</isPairing></MacroEvent>
                      <MacroEvent><Type>0</Type><Number>0.050</Number><selected>false</selected></MacroEvent>
                      <MacroEvent><Type>1</Type><Id>3</Id><KeyEvent><Makecode>49</Makecode><State>1</State></KeyEvent><selected>false</selected><isPairing>false</isPairing></MacroEvent>
                      <MacroEvent><Type>0</Type><Number>2.000</Number><selected>false</selected></MacroEvent>
                   </MacroEvents>
                   <DelaySetting>0</DelaySetting>
                   <Guid>377c8a24-4ef9-435e-b6ee-2073da5ea676</Guid>
                   <Version>4</Version>
                   <MouseMoveType>none</MouseMoveType>
                </Macro>
                """);

            var imported = await MacroDocumentService.ImportAsync(path, CancellationToken.None);
            MacroDocumentService.PrepareImported(
                imported,
                () => Guid.NewGuid().ToString("N"),
                "Imported macro");
            var macro = Assert.Single(imported);

            Assert.Equal(4, macro.Steps.Count);
            Assert.Equal(49, macro.Steps[0].Input!.VirtualKey);
            Assert.True(MacroValidator.ValidateTree(macro));
            Assert.True(MacroValidator.ValidateReferences(macro, Map(macro)));
            Assert.NotEmpty(MacroPlaybackPlan.Build(macro, Map(macro)).Actions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IReadOnlyDictionary<string, MacroDefinition> Map(params MacroDefinition[] macros) =>
        MacroDocumentService.IndexById(macros);
}
