using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioLedLayoutTests
{
    private const string BladeManifest =
        @"D:\Workspaces\OpenSynapse\artifacts\chroma-studio-2026-11-03\catalog\manifests\DeviceManifest_736_0.json";

    private const string MouseManifest =
        @"D:\Workspaces\OpenSynapse\artifacts\chroma-studio-2026-11-03\catalog\manifests\DeviceManifest_100_0.json";

    [Fact]
    public void ParsesTheBladeManifestLedConfig()
    {
        if (!File.Exists(BladeManifest)) return;

        using var document = JsonDocument.Parse(File.ReadAllText(BladeManifest));
        var ledConfig = document.RootElement.GetProperty("ledConfig").GetProperty("ledConfig_0_1").GetRawText();
        var layout = StudioLedLayout.Parse(ledConfig);

        Assert.Equal(7, layout.MatrixRows);
        Assert.Equal(16, layout.MatrixColumns);
        Assert.Equal(1, layout.DeviceRows);
        Assert.Equal(102, layout.DeviceColumns);
        Assert.Equal(86, layout.Leds.Count);
        Assert.Equal(88, layout.Inputs.Count);
        Assert.Equal(new StudioLedMap(0, 0, 0, 1), layout.FindMatrix(0, 0));
        Assert.Equal((0, 2), layout.ToDevice(0, 1));
        Assert.NotNull(layout.FindInput("kbd", 59));
    }

    [Fact]
    public void ParsesAMinimalConfig()
    {
        const string json = """
        {
          "ledConfig_0_1": {
            "MatrixMaxRow": 1,
            "MatrixMaxCol": 2,
            "DeviceMaxRow": 1,
            "DeviceMaxCol": 2,
            "MaxRegion": 1,
            "LedMatrix": [
              { "MatrixPos": [0, 0], "DevicePos": [0, 0] },
              { "MatrixPos": [0, 1], "DevicePos": [0, 1] }
            ],
            "LedInputMap": [
              { "InputType": "kbd", "InputData": [4, 0], "MatrixPos": [0, 0] }
            ]
          }
        }
        """;

        var layout = StudioLedLayout.Parse(json);

        Assert.Equal(1, layout.MatrixRows);
        Assert.Equal(2, layout.MatrixColumns);
        Assert.Equal(2, layout.Leds.Count);
        Assert.Equal((0, 1), layout.ToDevice(0, 1));
        Assert.Equal(4, layout.FindInput("kbd", 4)!.Value.InputCode);
    }

    [Fact]
    public void ParsesRegionIdsFromTheMouseManifest()
    {
        if (!File.Exists(MouseManifest)) return;

        using var document = JsonDocument.Parse(File.ReadAllText(MouseManifest));
        var ledConfig = document.RootElement.GetProperty("ledConfig").GetProperty("ledConfig_0_0").GetRawText();
        var layout = StudioLedLayout.Parse(ledConfig);

        Assert.Contains(layout.Leds, led =>
            led.MatrixRow == 0 && led.MatrixCol == 2 &&
            led.RegionId == 1 && led.RegionIndex == 0);
        Assert.Contains(layout.Leds, led =>
            led.MatrixRow == 6 && led.MatrixCol == 2 &&
            led.RegionId == 4 && led.RegionIndex == 0);
    }

    [Fact]
    public void LedRefJsonCarriesInputAndRegionIdentity()
    {
        var layout = StudioLedLayout.Parse("""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 1,
                "MatrixMaxCol": 2,
                "DeviceMaxRow": 1,
                "DeviceMaxCol": 2,
                "LedRegion": [{ "RegionId": 4, "RegionMatrix": [{ "MatrixPos": "0 1" }] }],
                "LedMatrix": [
                  { "MatrixPos": [0, 0], "DevicePos": [0, 0] },
                  { "MatrixPos": [0, 1], "DevicePos": [0, 1] }
                ],
                "LedInputMap": [{ "InputType": "kbd", "InputData": [1, 0], "MatrixPos": [0, 0] }]
              }
            }
            """);

        using var document = JsonDocument.Parse(StudioLedRefJson.Serialize(layout));
        var leds = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal("kbd", leds[0].GetProperty("inputType").GetString());
        Assert.Equal("[1,0]", leds[0].GetProperty("inputData").GetString());
        Assert.Equal(4, leds[1].GetProperty("regionId").GetInt32());
        Assert.Equal(0, leds[1].GetProperty("regionIndex").GetInt32());
    }

    [Fact]
    public void MissingArraysYieldEmptyLayouts()
    {
        var layout = StudioLedLayout.Parse("""{ "ledConfig_0_1": { "MatrixMaxRow": 1, "MatrixMaxCol": 1 } }""");

        Assert.Empty(layout.Leds);
        Assert.Empty(layout.Inputs);
        Assert.Null(layout.FindMatrix(0, 0));
    }
}
