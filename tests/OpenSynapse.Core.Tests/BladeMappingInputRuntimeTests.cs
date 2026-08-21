using System.Text.Json.Nodes;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMappingInputRuntimeTests
{
    [Fact]
    public void CompilesAllProduct710DefaultMappingsAndRejectsUnknownOutput()
    {
        var graph = LoadProduct710Graph();

        using var runtime = BladeMappingInputRuntime.FromProduct710Graph(graph);

        Assert.Equal(64, runtime.MappingCount);
        Assert.Equal(32, runtime.Rules.Count);
        Assert.Equal(6, runtime.Rules.Count(static rule => rule.InputExtended));
        var kinds = runtime.Rules
            .SelectMany(static rule => new[] { rule.PressAction, rule.ReleaseAction })
            .OfType<BladeMappingAction>()
            .SelectMany(Flatten)
            .Select(static action => action.Kind)
            .ToHashSet();
        Assert.Subset(
            new HashSet<BladeMappingOutputKind>
            {
                BladeMappingOutputKind.Keyboard,
                BladeMappingOutputKind.HyperShift,
                BladeMappingOutputKind.Disable,
                BladeMappingOutputKind.SnapTapToggle,
                BladeMappingOutputKind.BladeBattery,
                BladeMappingOutputKind.BladeTrackpad,
                BladeMappingOutputKind.BladePerformance,
                BladeMappingOutputKind.ScreenRefresh,
                BladeMappingOutputKind.Multi,
                BladeMappingOutputKind.Delay,
                BladeMappingOutputKind.Turbo,
                BladeMappingOutputKind.Display,
                BladeMappingOutputKind.Backlight,
                BladeMappingOutputKind.GameMode,
                BladeMappingOutputKind.Audio,
            },
            kinds);

        Assert.Throws<InvalidOperationException>(() => runtime.Process(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0x03, true)));

        graph["mappings"]![0]!["output"]!["type"] = "unknown";
        Assert.Throws<ArgumentException>(() => BladeMappingInputRuntime.FromGraph(graph));
    }

    [Fact]
    public void Product710EntryPointRejectsTruncatedGraph()
    {
        var graph = LoadProduct710Graph();
        graph["mappings"]!.AsArray().RemoveRange(2, 62);

        using var partial = BladeMappingInputRuntime.FromGraph(graph);
        Assert.Single(partial.Rules);
        Assert.Throws<ArgumentException>(() =>
            BladeMappingInputRuntime.FromProduct710Graph(graph));
    }

    [Fact]
    public void ReturnsCompiledAppActionWithoutExecutingIt()
    {
        using var runtime = BladeMappingInputRuntime.FromGraph(LoadProduct710Graph());

        var output = runtime.Process(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0x03, true),
            out var action);

        Assert.Empty(output);
        Assert.Equal(
            new BladeCommandMappingAction(BladeMappingOutputKind.GameMode, BladeMappingCommand.Toggle),
            action);

        runtime.Process(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0x03, false),
            out action);
        Assert.IsType<BladeDisabledMappingAction>(action);
    }

    [Fact]
    public void ReturnsReleaseEdgeCommandForM4()
    {
        using var runtime = BladeMappingInputRuntime.FromGraph(LoadProduct710Graph());

        runtime.Process(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD3, true),
            out var press);
        Assert.IsType<BladeDisabledMappingAction>(press);
        Assert.Empty(runtime.Process(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD3, true),
            out var duplicate));
        Assert.Null(duplicate);

        runtime.Process(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD3, false),
            out var release);
        Assert.Equal(
            new BladeCommandMappingAction(
                BladeMappingOutputKind.BladePerformance,
                BladeMappingCommand.NextPerformanceMode),
            release);
    }

    [Fact]
    public void KeepsHyperShiftEnabledUntilEveryOwnerIsReleased()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.RazerKey, 0x10, false, BladeMappingOutputKind.HyperShift, 0),
            new(BladeMappingInputKind.RazerKey, 0x20, false, BladeMappingOutputKind.HyperShift, 0),
            new(BladeMappingInputKind.RazerKey, 0x10, true, BladeMappingOutputKind.HyperShift, 0),
            new(BladeMappingInputKind.RazerKey, 0x20, true, BladeMappingOutputKind.HyperShift, 0),
        ]);

        runtime.Process(new(BladeMappingInputKind.RazerKey, 0x10, true));
        runtime.Process(new(BladeMappingInputKind.RazerKey, 0x20, true));
        runtime.Process(new(BladeMappingInputKind.RazerKey, 0x10, false));
        Assert.True(runtime.HyperShiftEnabled);

        runtime.Process(new(BladeMappingInputKind.RazerKey, 0x20, false));
        Assert.False(runtime.HyperShiftEnabled);
    }

    [Fact]
    public void IgnoresDuplicateKeyDownWithoutLeavingSyntheticKeyPressed()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.Keyboard, 0x1E, false, BladeMappingOutputKind.Keyboard, 0x30),
        ]);

        Assert.Equal(
            [new BladeMappingOutputEvent(0x30, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, true)));
        Assert.Empty(runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, true)));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x30, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, false)));
        Assert.Empty(runtime.Stop());
    }

    [Fact]
    public void PassesHookedKeyboardInputThroughWhenTheActiveLayerHasNoRule()
    {
        using var runtime = new BladeMappingInputRuntime([]);

        Assert.Equal(
            [new BladeMappingOutputEvent(0x13, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x13, true)));
        Assert.Empty(runtime.Process(new(BladeMappingInputKind.Keyboard, 0x13, true)));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x13, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x13, false)));
        Assert.Empty(runtime.Process(new(BladeMappingInputKind.RazerKey, 0x99, true)));
    }

    [Fact]
    public void RejectsMismatchedInputFlags()
    {
        var graph = LoadProduct710Graph();
        var release = Mappings(graph).First(mapping =>
            mapping["input"]!["flag"]!.GetValue<int>() == 1);
        release["input"]!["flag"] = 3;

        Assert.Throws<ArgumentException>(() => BladeMappingInputRuntime.FromGraph(graph));
    }

    [Fact]
    public void RejectsMismatchedTurboGuids()
    {
        var graph = LoadProduct710Graph();
        var release = Mappings(graph).First(mapping =>
            mapping["output"]!["type"]!.GetValue<string>() == "turbo" &&
            mapping["output"]!["flag"]!.GetValue<int>() == 1);
        release["output"]!["guid"] = Guid.NewGuid().ToString("D");

        Assert.Throws<ArgumentException>(() => BladeMappingInputRuntime.FromGraph(graph));
    }

    [Fact]
    public void RejectsKeyboardPressPairedWithDisabledRelease()
    {
        var graph = LoadProduct710Graph();
        var release = Mappings(graph).First(mapping =>
            mapping["output"]!["type"]!.GetValue<string>() == "keyboard" &&
            mapping["output"]!["flag"]!.GetValue<int>() is 1 or 3);
        release["output"]!["type"] = "disabled";

        Assert.Throws<ArgumentException>(() => BladeMappingInputRuntime.FromGraph(graph));
    }

    [Fact]
    public void RejectsUnsupportedInputModifiers()
    {
        var graph = LoadProduct710Graph();
        foreach (var mapping in Mappings(graph).Take(2))
        {
            mapping["input"]!["modifiers"] = 1;
        }

        Assert.Throws<ArgumentException>(() => BladeMappingInputRuntime.FromGraph(graph));
    }

    [Fact]
    public void RejectsExtendedRazerKeyFlags()
    {
        var graph = LoadProduct710Graph();
        var pair = Mappings(graph)
            .Where(mapping => mapping["input"]!["type"]!.GetValue<string>() == "razerKey")
            .Take(2)
            .ToArray();
        pair[0]["input"]!["flag"] = 2;
        pair[1]["input"]!["flag"] = 3;

        Assert.Throws<ArgumentException>(() => BladeMappingInputRuntime.FromGraph(graph));
    }

    [Fact]
    public void RejectsAdvancedRuleWithoutCompiledActions()
    {
        var rules = new[]
        {
            new BladeMappingRule(
                BladeMappingInputKind.RazerKey,
                0x03,
                false,
                BladeMappingOutputKind.GameMode,
                0),
        };

        Assert.Throws<ArgumentException>(() => new BladeMappingInputRuntime(rules));
    }

    [Fact]
    public void DistinguishesBaseAndExtendedInputScanCodes()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.Keyboard, 0x50, false, BladeMappingOutputKind.Keyboard, 0x20),
            new(BladeMappingInputKind.Keyboard, 0x50, false, BladeMappingOutputKind.Keyboard, 0x30,
                InputExtended: true),
        ]);

        Assert.Equal(
            [new BladeMappingOutputEvent(0x20, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x50, true)));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x30, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x50, true, true)));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x20, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x50, false)));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x30, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x50, false, true)));
        Assert.Empty(runtime.Stop());
    }

    [Fact]
    public void EnablingSnapTapMigratesAlreadyPressedKeysWithoutSticking()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.Keyboard, 0x1E, false, BladeMappingOutputKind.Keyboard, 0x1E,
                SnapTapId: 1),
            new(BladeMappingInputKind.Keyboard, 0x20, false, BladeMappingOutputKind.Keyboard, 0x20,
                SnapTapId: 1),
        ]);

        runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, true));
        runtime.Process(new(BladeMappingInputKind.Keyboard, 0x20, true));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x1E, false)],
            runtime.SetSnapTapEnabled(true));
        Assert.Equal(
            [
                new BladeMappingOutputEvent(0x20, false),
                new BladeMappingOutputEvent(0x1E, true),
            ],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x20, false)));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x1E, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, false)));
        Assert.Empty(runtime.Stop());
    }

    [Fact]
    public void DisablingSnapTapKeepsCurrentOwnerDownAndRestoresSuppressedKeys()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.Keyboard, 0x1E, false, BladeMappingOutputKind.Keyboard, 0x1E,
                SnapTapId: 1),
            new(BladeMappingInputKind.Keyboard, 0x20, false, BladeMappingOutputKind.Keyboard, 0x20,
                SnapTapId: 1),
        ]);

        runtime.SetSnapTapEnabled(true);
        Assert.Equal(
            [new BladeMappingOutputEvent(0x1E, true)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, true)));
        Assert.Equal(
        [
            new BladeMappingOutputEvent(0x1E, false),
            new BladeMappingOutputEvent(0x20, true),
        ], runtime.Process(new(BladeMappingInputKind.Keyboard, 0x20, true)));

        Assert.Equal(
            [new BladeMappingOutputEvent(0x1E, true)],
            runtime.SetSnapTapEnabled(false));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x20, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x20, false)));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x1E, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, false)));
        Assert.Empty(runtime.Stop());
    }

    [Fact]
    public void DisablingSnapTapDoesNotRepressSingleOwner()
    {
        using var runtime = new BladeMappingInputRuntime(
        [
            new(BladeMappingInputKind.Keyboard, 0x1E, false, BladeMappingOutputKind.Keyboard, 0x1E,
                SnapTapId: 1),
        ]);

        runtime.SetSnapTapEnabled(true);
        runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, true));
        Assert.Empty(runtime.SetSnapTapEnabled(false));
        Assert.Equal(
            [new BladeMappingOutputEvent(0x1E, false)],
            runtime.Process(new(BladeMappingInputKind.Keyboard, 0x1E, false)));
    }

    private static JsonObject LoadProduct710Graph()
    {
        var storage = JsonNode.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Product710Mapping.json")))!.AsObject();
        return storage["defaultMappings"]!["appEngine"]!.AsObject();
    }

    private static IEnumerable<JsonObject> Mappings(JsonObject graph) =>
        graph["mappings"]!.AsArray().Select(static node => node!.AsObject());

    private static IEnumerable<BladeMappingAction> Flatten(BladeMappingAction action)
    {
        yield return action;
        if (action is not BladeMultiMappingAction multi)
        {
            yield break;
        }

        foreach (var child in multi.Actions.SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
