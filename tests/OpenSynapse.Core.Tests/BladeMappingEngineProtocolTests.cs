using System.Text.Json.Nodes;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMappingEngineProtocolTests
{
    [Fact]
    public void KeyboardPairPreservesExtendedFlagsAndInputModifiers()
    {
        var pair = BladeMappingEngineProtocol.CreateKeyboardToKeyboardPair(
            inputScanCode: 0x4B,
            inputMakeFlag: 2,
            hyperShiftLayer: true,
            outputScanCode: 0x1D,
            outputMakeFlag: 2,
            inputModifiers: 512);

        Assert.Equal((byte)2, pair[0]!["input"]!["flag"]!.GetValue<byte>());
        Assert.Equal((byte)3, pair[1]!["input"]!["flag"]!.GetValue<byte>());
        Assert.Equal((ushort)512, pair[0]!["input"]!["modifiers"]!.GetValue<ushort>());
        Assert.Equal((byte)3, pair[1]!["output"]!["flag"]!.GetValue<byte>());
    }

    [Fact]
    public void RecognizesOnlyNormalLayerM3PressAsGameModeToggle()
    {
        Assert.True(BladeMappingEngineProtocol.IsGameModeToggleInput(
            "{\"type\":\"razerKey\",\"key\":3,\"flag\":0,\"hypershift\":false}"));
        Assert.False(BladeMappingEngineProtocol.IsGameModeToggleInput(
            "{\"type\":\"razerKey\",\"key\":3,\"flag\":1}"));
        Assert.False(BladeMappingEngineProtocol.IsGameModeToggleInput(
            "{\"type\":\"razerKey\",\"key\":3,\"flag\":0,\"hypershift\":true}"));
    }

    [Fact]
    public void HyperShiftActivatorMatchesProduct710Dkm03Graph()
    {
        var pair = BladeMappingEngineProtocol.CreateRazerKeyHyperShiftPair();

        Assert.Equal(
            "[{\"input\":{\"type\":\"razerKey\",\"key\":3,\"hypershift\":false,\"flag\":0},\"output\":{\"type\":\"hypershift\",\"flag\":0}},{\"input\":{\"type\":\"razerKey\",\"key\":3,\"hypershift\":false,\"flag\":1},\"output\":{\"type\":\"hypershift\",\"flag\":1}}]",
            pair.ToJsonString());
    }

    [Fact]
    public void RazerKeyMappingKeepsNormalAndHyperShiftLayersSeparate()
    {
        var normal = BladeMappingEngineProtocol.CreateRazerKeyToKeyboardPair(
            BladeMappingEngineProtocol.ApplicationKey,
            hyperShiftLayer: false,
            outputScanCode: 0x5D,
            outputMakeFlag: 2);
        var hyperShift = BladeMappingEngineProtocol.CreateRazerKeyToKeyboardPair(
            BladeMappingEngineProtocol.ApplicationKey,
            hyperShiftLayer: true,
            outputScanCode: 0x6E,
            outputMakeFlag: 0);

        Assert.False(normal[0]!["input"]!["hypershift"]!.GetValue<bool>());
        Assert.True(hyperShift[0]!["input"]!["hypershift"]!.GetValue<bool>());
        Assert.Equal((byte)3, normal[1]!["output"]!["flag"]!.GetValue<byte>());
    }

    [Fact]
    public void SnapTapPairUsesOneIdForAAndDPressAndRelease()
    {
        var a = BladeMappingEngineProtocol.CreateSnapTapKeyboardPassthroughPair(0x1E, 0);
        var d = BladeMappingEngineProtocol.CreateSnapTapKeyboardPassthroughPair(0x20, 0);

        foreach (var mapping in a.Concat(d))
        {
            Assert.Equal(1, mapping!["input"]!["snaptapId"]!.GetValue<int>());
        }
    }

    [Fact]
    public void SnapTapToggleRunsOnRelease()
    {
        var pair = BladeMappingEngineProtocol.CreateSnapTapTogglePair(
            scanCode: 0x2A,
            makeFlag: 0,
            hyperShiftLayer: true);

        Assert.Equal("disable", pair[0]!["output"]!["type"]!.GetValue<string>());
        Assert.Equal("snapTap", pair[1]!["output"]!["type"]!.GetValue<string>());
        Assert.Equal("toggle", pair[1]!["output"]!["id"]!.GetValue<string>());
        Assert.Equal((byte)1, pair[1]!["input"]!["flag"]!.GetValue<byte>());
    }

    [Fact]
    public void HashIgnoresRootHashAndGameModeAndSortsProperties()
    {
        var first = JsonNode.Parse(
            "{\"mappings\":[{\"output\":{\"flag\":0,\"type\":\"keyboard\"},\"input\":{\"flag\":0,\"type\":\"keyboard\",\"scancode\":30}}],\"hash\":\"stale\",\"gamemode\":true}")!
            .AsObject();
        var second = JsonNode.Parse(
            "{\"mappings\":[{\"input\":{\"scancode\":30,\"type\":\"keyboard\",\"flag\":0},\"output\":{\"type\":\"keyboard\",\"flag\":0}}]}")!
            .AsObject();

        Assert.Equal(
            "{\"mappings\":[{\"input\":{\"flag\":0,\"scancode\":30,\"type\":\"keyboard\"},\"output\":{\"flag\":0,\"type\":\"keyboard\"}}]}",
            BladeMappingEngineProtocol.SerializeCanonicalForHash(first));
        Assert.Equal(
            BladeMappingEngineProtocol.ComputeGraphHash(first),
            BladeMappingEngineProtocol.ComputeGraphHash(second));
        Assert.Equal("17b419ed86533ebcf06e2cef90ebefcf", BladeMappingEngineProtocol.ComputeGraphHash(first));
    }

    [Fact]
    public void HashEscapesLessThanExactlyLikeSynapseHelper()
    {
        var graph = JsonNode.Parse("{\"mappings\":[],\"name\":\"a<b>c&d\"}")!.AsObject();

        Assert.Equal(
            "{\"mappings\":[],\"name\":\"a\\u003Cb>c&d\"}",
            BladeMappingEngineProtocol.SerializeCanonicalForHash(graph));
    }

    [Fact]
    public void HashRemovalIsShallowLikeSynapseHelper()
    {
        var graph = JsonNode.Parse(
            "{\"hash\":\"root\",\"gamemode\":false,\"nested\":{\"hash\":\"keep\",\"gamemode\":true}}")!
            .AsObject();

        Assert.Equal(
            "{\"nested\":{\"gamemode\":true,\"hash\":\"keep\"}}",
            BladeMappingEngineProtocol.SerializeCanonicalForHash(graph));
    }

    [Fact]
    public void BladeMediaGraphMapsM5ToOfficialMicrophoneToggle()
    {
        var pair = BladeMappingEngineProtocol.CreateRazerKeyMicrophoneMutePair();

        Assert.Equal((byte)0xD4, pair[0]!["input"]!["key"]!.GetValue<byte>());
        Assert.Equal("audio", pair[0]!["output"]!["type"]!.GetValue<string>());
        Assert.Equal("mic", pair[0]!["output"]!["id"]!.GetValue<string>());
        Assert.Equal(2, pair[0]!["output"]!["mute"]!.GetValue<int>());
        Assert.Equal("disabled", pair[1]!["output"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void CompleteStorageRejectsSelfConsistentButUnofficialGraph()
    {
        var mappings = new JsonArray();
        for (var index = 0; index < 64; index++)
        {
            mappings.Add(new JsonObject());
        }
        var graph = new JsonObject { ["mappings"] = mappings };
        graph["hash"] = BladeMappingEngineProtocol.ComputeGraphHash(graph);
        var storage = new JsonObject
        {
            ["productId"] = 710,
            ["reportIDs"] = new JsonObject
            {
                ["4"] = "razerKeyReportID",
                ["5"] = "hardwareEventReportID",
            },
            ["profiles"] = new JsonArray
            {
                new JsonObject
                {
                    ["appEngine"] = new JsonObject
                    {
                        ["mappings"] = new JsonArray(),
                        ["hash"] = BladeMappingEngineProtocol.EmptyGraphHash,
                    },
                },
            },
            ["defaultMappings"] = new JsonObject { ["appEngine"] = graph },
        };

        Assert.Throws<ArgumentException>(() =>
            BladeMappingEngineProtocol.ValidateCompleteProduct710Storage(storage.ToJsonString()));
    }

    [Fact]
    public void CompleteStorageReportsMalformedFieldTypesAsInvalidInput()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BladeMappingEngineProtocol.ValidateCompleteProduct710Storage(
                "{\"productId\":\"710\",\"reportIDs\":{}}"));

        Assert.Contains("格式无效", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteStorageAcceptsCapturedOfficialDefaultGraph()
    {
        BladeMappingEngineProtocol.ValidateCompleteProduct710Storage(
            Product710MappingFixture.CompleteStorage);
    }
}
