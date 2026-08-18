using OpenSynapse.Windows.Protocols;
using System.Text.Json.Nodes;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMappingInputRuntimeTests
{
    [Fact]
    public void HyperShiftUsesLayerAtPressAndReleasesTheSameOutput()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.RazerKey, 3, false, BladeMappingOutputKind.HyperShift, 0),
            new(BladeMappingInputKind.Keyboard, 30, false, BladeMappingOutputKind.Keyboard, 31),
            new(BladeMappingInputKind.Keyboard, 30, true, BladeMappingOutputKind.Keyboard, 32),
        ]);

        Assert.Empty(runtime.Process(new(BladeMappingInputKind.RazerKey, 3, true)));
        Assert.Equal([new BladeMappingOutputEvent(32, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, true)));
        Assert.Equal([new BladeMappingOutputEvent(32, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, false)));
        Assert.Empty(runtime.Process(new(BladeMappingInputKind.RazerKey, 3, false)));
        Assert.False(runtime.HyperShiftEnabled);
    }

    [Fact]
    public void SnapTapRestoresThePreviousHeldKeyWhenNewestKeyReleases()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.Keyboard, 30, false, BladeMappingOutputKind.Keyboard, 30, 1),
            new(BladeMappingInputKind.Keyboard, 32, false, BladeMappingOutputKind.Keyboard, 32, 1),
        ]);
        runtime.SetSnapTapEnabled(true);

        Assert.Equal([new BladeMappingOutputEvent(30, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, true)));
        Assert.Equal(
            [new BladeMappingOutputEvent(30, false), new BladeMappingOutputEvent(32, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 32, true)));
        Assert.Equal(
            [new BladeMappingOutputEvent(32, false), new BladeMappingOutputEvent(30, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 32, false)));
        Assert.Equal([new BladeMappingOutputEvent(30, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, false)));
    }

    [Fact]
    public void StopReleasesEverySyntheticKeyAndClearsState()
    {
        using var runtime = new BladeMappingInputRuntime(
        [new(BladeMappingInputKind.Keyboard, 30, false, BladeMappingOutputKind.Keyboard, 31)]);
        Assert.Single(runtime.Process(new(BladeMappingInputKind.Keyboard, 30, true)));

        Assert.Equal([new BladeMappingOutputEvent(31, false)], runtime.Stop());
        Assert.False(runtime.HyperShiftEnabled);
        Assert.Empty(runtime.Process(new(BladeMappingInputKind.Keyboard, 30, false)));
    }

    [Fact]
    public void SharedOutputStaysHeldUntilEveryMappedInputReleases()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.Keyboard, 30, false, BladeMappingOutputKind.Keyboard, 31),
            new(BladeMappingInputKind.Keyboard, 32, false, BladeMappingOutputKind.Keyboard, 31),
        ]);

        Assert.Equal([new BladeMappingOutputEvent(31, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, true)));
        Assert.Empty(runtime.Process(new(BladeMappingInputKind.Keyboard, 32, true)));
        Assert.Empty(runtime.Process(new(BladeMappingInputKind.Keyboard, 30, false)));
        Assert.Equal([new BladeMappingOutputEvent(31, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 32, false)));
    }

    [Fact]
    public void ExtendedAndOrdinaryVersionsOfTheSameScanCodeStayIndependent()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.Keyboard, 30, false, BladeMappingOutputKind.Keyboard, 93),
            new(BladeMappingInputKind.Keyboard, 32, false, BladeMappingOutputKind.Keyboard, 93, OutputExtended: true),
        ]);

        Assert.Equal([new BladeMappingOutputEvent(93, true, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, true)));
        Assert.Equal([new BladeMappingOutputEvent(93, true, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 32, true)));
        Assert.Equal([new BladeMappingOutputEvent(93, false, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, false)));
        Assert.Equal([new BladeMappingOutputEvent(93, false, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 32, false)));
    }

    [Fact]
    public void ParsesExistingCompilerGraphIncludingSnapTapToggle()
    {
        var mappings = new JsonArray();
        var ordinary = BladeMappingEngineProtocol.CreateKeyboardToKeyboardPair(30, 0, false, 93, 2);
        var toggle = BladeMappingEngineProtocol.CreateSnapTapTogglePair(42, 0, false);
        mappings.Add(ordinary[0]!.DeepClone());
        mappings.Add(ordinary[1]!.DeepClone());
        mappings.Add(toggle[0]!.DeepClone());
        mappings.Add(toggle[1]!.DeepClone());

        using var runtime = BladeMappingInputRuntime.FromGraph(
            new JsonObject { ["mappings"] = mappings });

        Assert.Equal([new BladeMappingOutputEvent(93, true, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, true)));
        Assert.Equal([new BladeMappingOutputEvent(93, false, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 30, false)));
        Assert.False(runtime.SnapTapEnabled);
        runtime.Process(new(BladeMappingInputKind.Keyboard, 42, true));
        runtime.Process(new(BladeMappingInputKind.Keyboard, 42, false));
        Assert.True(runtime.SnapTapEnabled);
    }
}
