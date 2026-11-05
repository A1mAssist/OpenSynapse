using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioChromaInteropTests
{
    [Fact]
    public void ExportWritesProfileDevicesEffectNamesAndRegionDeviceIds()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        profile.Devices.Add(new StudioDevicePlacement
        {
            ProductId = 736,
            InstanceId = "0",
            DeviceId = "PM123",
            Layout = "0",
            Width = 760,
            Height = 320,
            X = 1200,
            Y = 3400,
            Rotation = 90,
        });

        var wave = StudioLayerItem.CreateLayer("My Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        region.Devices.Add(new StudioRegionDevice
        {
            ProductId = 736,
            InstanceId = "0",
            DeviceId = "PM123",
        });
        region.Devices[0].Leds.Add(new StudioLedRef(0, 1, InputType: "kbd", InputData: "[1,0]"));
        wave.Regions.Add(region);
        var gradient = StudioLayerItem.CreateLayer("My Gradient", StudioEffectKind.Gradient);
        profile.Items.Add(wave);
        profile.Items.Add(gradient);
        project.Profiles.Add(profile);

        using var document = JsonDocument.Parse(StudioProjectCodec.Serialize(project));
        var profileElement = document.RootElement.GetProperty("profiles")[0];
        var device = Assert.Single(profileElement.GetProperty("devices").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(device.GetProperty("id").GetString()));
        Assert.Equal(736, device.GetProperty("productId").GetInt32());
        Assert.Equal(JsonValueKind.Number, device.GetProperty("instanceId").ValueKind);
        Assert.Equal(0, device.GetProperty("instanceId").GetInt32());
        Assert.Equal(0, device.GetProperty("layout").GetInt32());
        Assert.Equal(760, device.GetProperty("width").GetDouble());
        Assert.Equal(320, device.GetProperty("height").GetDouble());
        Assert.Equal(1200, device.GetProperty("x").GetDouble());
        Assert.Equal(3400, device.GetProperty("y").GetDouble());
        Assert.Equal(90, device.GetProperty("orientation").GetInt32());

        var layers = profileElement.GetProperty("layers").EnumerateArray().ToList();
        var waveElement = layers.Single(layer => layer.GetProperty("title").GetString() == "My Wave");
        Assert.Equal("wave", waveElement.GetProperty("name").GetString());
        Assert.Equal(2, waveElement.GetProperty("value").GetInt32());
        var regionDevice = Assert.Single(
            waveElement.GetProperty("regions")[0].GetProperty("devices").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(regionDevice.GetProperty("id").GetString()));
        Assert.Equal("kbd", regionDevice.GetProperty("leds")[0].GetProperty("inputType").GetString());

        var gradientElement = layers.Single(layer => layer.GetProperty("title").GetString() == "My Gradient");
        Assert.Equal("spectrum", gradientElement.GetProperty("name").GetString());
        Assert.Equal(4, gradientElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void ImportReadsNumericIdentitiesAndEffectNameFallback()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [
            {
              "id": "abcdef123456",
              "title": "Numeric",
              "devices": [
                {
                  "id": "device-664-0-PM123",
                  "productId": 664,
                  "instanceId": 0,
                  "deviceId": 12345,
                  "layout": 0,
                  "x": 4520,
                  "y": 4840,
                  "orientation": 90
                }
              ],
              "layers": [
                {
                  "id": "111111111111",
                  "type": "layer",
                  "title": "Pulse",
                  "name": "reactive",
                  "params": {},
                  "regions": [
                    {
                      "params": {},
                      "devices": [
                        {
                          "id": "device-664-0-PM123",
                          "productId": 664,
                          "instanceId": 0,
                          "deviceId": 12345,
                          "leds": [
                            { "row": 1, "col": 2, "inputType": "kbd", "inputData": "[1,0]" }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var project = StudioProjectCodec.Deserialize(json);
        var profile = Assert.Single(project.Profiles);
        Assert.Equal(StudioEffectKind.Reactive, Assert.Single(profile.Items).Params.Effect);
        var placement = Assert.Single(profile.Devices);
        Assert.Equal("0", placement.InstanceId);
        Assert.Equal("12345", placement.DeviceId);
        Assert.Equal("0", placement.Layout);
        Assert.Equal(90, placement.Rotation);
        Assert.Equal(4520, placement.X);
        Assert.Equal(4840, placement.Y);

        var device = Assert.Single(Assert.Single(Assert.Single(profile.Items).Regions).Devices);
        Assert.Equal("0", device.InstanceId);
        Assert.Equal("12345", device.DeviceId);
        Assert.Equal("kbd", Assert.Single(device.Leds).InputType);
    }

    [Fact]
    public void GeneratePresetFieldsRoundTrip()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Generate", StudioEffectKind.ChromaGenerate);
        layer.Params.CurrentConfigJson = """{"pattern":1}""";
        layer.Params.EffectConfigListJson = """[{"name":"one"},{"name":"two"}]""";
        layer.Params.CurrentConfigId = 1;
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var json = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(json))
        {
            var parameters = document.RootElement.GetProperty("profiles")[0]
                .GetProperty("layers")[0].GetProperty("params");
            Assert.Equal(2, parameters.GetProperty("effectConfigList").GetArrayLength());
            Assert.Equal(1, parameters.GetProperty("currentConfigId").GetInt32());
        }

        var restoredParams = Assert.Single(Assert.Single(StudioProjectCodec.Deserialize(json).Profiles).Items).Params;
        Assert.Equal(2, JsonDocument.Parse(restoredParams.EffectConfigListJson!).RootElement.GetArrayLength());
        Assert.Equal(1, restoredParams.CurrentConfigId);
        Assert.Equal(json, StudioProjectCodec.Serialize(StudioProjectCodec.Deserialize(json)));

        var clone = layer.Params.Clone();
        Assert.Equal(layer.Params.EffectConfigListJson, clone.EffectConfigListJson);
        Assert.Equal(1, clone.CurrentConfigId);
    }

    [Fact]
    public void ChromaExportCollapsesGenerateHistoryButInternalSaveKeepsIt()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Generate", StudioEffectKind.ChromaGenerate);
        layer.Params.CurrentConfigJson = """{"pattern":1}""";
        layer.Params.EffectConfigListJson = """[{"pattern":1},{"pattern":2}]""";
        layer.Params.CurrentConfigId = 1;
        var region = new StudioRegion { Params = layer.Params.Clone() };
        region.Params.CurrentConfigJson = """{"pattern":3}""";
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var internalJson = StudioProjectCodec.Serialize(project);
        using (var internalDocument = JsonDocument.Parse(internalJson))
        {
            var internalParams = internalDocument.RootElement.GetProperty("profiles")[0]
                .GetProperty("layers")[0].GetProperty("params");
            Assert.Equal(2, internalParams.GetProperty("effectConfigList").GetArrayLength());
            Assert.Equal(1, internalParams.GetProperty("currentConfigId").GetInt32());
        }

        var directory = Path.Combine(Path.GetTempPath(), "opensynapse-chroma-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Generate.ChromaEffects");
            StudioChromaFileService.Export(project, path);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var layerParams = document.RootElement.GetProperty("profiles")[0]
                .GetProperty("layers")[0].GetProperty("params");
            Assert.Equal(1, layerParams.GetProperty("effectConfigList").GetArrayLength());
            Assert.Equal(0, layerParams.GetProperty("currentConfigId").GetInt32());
            Assert.Equal(1, layerParams.GetProperty("currentConfig").GetProperty("pattern").GetInt32());

            var regionParams = document.RootElement.GetProperty("profiles")[0]
                .GetProperty("layers")[0].GetProperty("regions")[0].GetProperty("params");
            Assert.Equal(1, regionParams.GetProperty("effectConfigList").GetArrayLength());
            Assert.Equal(0, regionParams.GetProperty("currentConfigId").GetInt32());
            Assert.Equal(3, regionParams.GetProperty("currentConfig").GetProperty("pattern").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SyntheticChromaRoundTripKeepsDevicesRegionsAndRequiredShape()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [
            {
              "id": "abcdef123456",
              "title": "Synthetic",
              "devices": [
                {
                  "id": "device-664-0-SN1",
                  "productId": 664,
                  "instanceId": 0,
                  "deviceId": "SN1",
                  "category": "keyboard",
                  "productName": "Test Keyboard",
                  "width": 760,
                  "height": 320,
                  "x": 4520,
                  "y": 4840,
                  "orientation": 0,
                  "config": { "MatrixMaxRow": 6, "MatrixMaxCol": 18 },
                  "leds": [ { "row": 0, "col": 0 }, { "row": 0, "col": 1 } ]
                }
              ],
              "layers": [
                {
                  "id": "111111111111",
                  "type": "layer",
                  "title": "Wave",
                  "name": "wave",
                  "value": 2,
                  "visible": true,
                  "params": {},
                  "regions": [
                    {
                      "params": {
                        "colorStops": [
                          { "Stop": 0, "Color": 16711680 },
                          { "Stop": 50, "Color": 65280 },
                          { "Stop": 100, "Color": 255 }
                        ],
                        "colorStopsCustom": true,
                        "speed": 15,
                        "split": true
                      },
                      "devices": [
                        {
                          "id": "device-664-0-SN1",
                          "productId": 664,
                          "instanceId": 0,
                          "deviceId": "SN1",
                          "leds": [
                            { "row": 0, "col": 0, "inputType": "kbd", "inputData": "[1,0]" },
                            { "row": 0, "col": 1, "inputType": "kbd", "inputData": "[2,0]" }
                          ]
                        }
                      ]
                    },
                    {
                      "params": {
                        "colorStops": [
                          { "Stop": 0, "Color": 255 },
                          { "Stop": 50, "Color": 65280 },
                          { "Stop": 100, "Color": 16711680 }
                        ],
                        "colorStopsCustom": true
                      },
                      "devices": [
                        {
                          "id": "device-664-0-SN1",
                          "productId": 664,
                          "instanceId": 0,
                          "deviceId": "SN1",
                          "leds": [
                            { "row": 1, "col": 0, "inputType": "kbd", "inputData": "[3,0]" },
                            { "row": 1, "col": 1, "inputType": "kbd", "inputData": "[4,0]" }
                          ]
                        }
                      ]
                    }
                  ]
                },
                {
                  "id": "222222222222",
                  "type": "group",
                  "title": "Group",
                  "visible": true,
                  "layers": [
                    {
                      "id": "333333333333",
                      "type": "layer",
                      "title": "Static",
                      "name": "static",
                      "value": 6,
                      "visible": true,
                      "params": { "color": 65280 },
                      "regions": []
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(664, "0")]);

        Assert.DoesNotContain(result.Issues,
            issue => issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
        var stats = Count(result.Project);
        Assert.Equal((2, 1, 2, 4), stats);
        Assert.Equal(6, CountColorStops(result.Project));

        var profile = Assert.Single(result.Project.Profiles);
        foreach (var layer in profile.EnumerateLayers().Where(layer => !layer.IsGroup))
        foreach (var region in layer.Regions)
        {
            Assert.Equal(layer.Params.Effect, region.Params.Effect);
            if (layer.Params.Effect == StudioEffectKind.Wave)
                Assert.True(region.Params.ColorStopsCustom);
        }
        var placement = Assert.Single(profile.Devices);
        Assert.Equal("0", placement.InstanceId);
        Assert.Equal("SN1", placement.DeviceId);
        Assert.Equal("keyboard", placement.Category);
        Assert.Equal("Test Keyboard", placement.ProductName);
        Assert.False(string.IsNullOrWhiteSpace(placement.ConfigJson));
        Assert.False(string.IsNullOrWhiteSpace(placement.LedsJson));
        Assert.Equal(4520, placement.X);
        Assert.Equal(4840, placement.Y);
        Assert.Equal(760, placement.Width);
        Assert.Equal(320, placement.Height);

        var exported = StudioProjectCodec.Serialize(result.Project);
        AssertChromaShape(exported);

        var reimported = StudioChromaImport.Import(exported, [new StudioDeviceMatch(664, "0")]);
        Assert.DoesNotContain(reimported.Issues,
            issue => issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
        Assert.Equal(stats, Count(reimported.Project));
        Assert.Equal(profile.Devices.Count, Assert.Single(reimported.Project.Profiles).Devices.Count);
        Assert.Equal(exported, StudioProjectCodec.Serialize(reimported.Project));
    }

    [Fact]
    public void ImportRemapsProfilePlacementToConnectedInstance()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        profile.Devices.Add(new StudioDevicePlacement
        {
            ProductId = 664,
            InstanceId = "0",
            DeviceId = "SN1",
        });
        var layer = StudioLayerItem.CreateLayer("Layer", StudioEffectKind.Solid);
        var region = new StudioRegion();
        var device = new StudioRegionDevice
        {
            ProductId = 664,
            InstanceId = "0",
            DeviceId = "SN1",
        };
        device.Leds.Add(new StudioLedRef(1, 2));
        region.Devices.Add(device);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var result = StudioChromaImport.Import(
            StudioProjectCodec.Serialize(project),
            [new StudioDeviceMatch(664, "usb-1")]);

        var placement = Assert.Single(Assert.Single(result.Project.Profiles).Devices);
        Assert.Equal("usb-1", placement.InstanceId);
        Assert.Contains(result.Issues, issue => issue.Kind == StudioImportIssueKind.RemappedDevice);
        var importedRegion = Assert.Single(Assert.Single(
            Assert.Single(result.Project.Profiles).EnumerateLayers()).Regions);
        Assert.Equal("usb-1", Assert.Single(importedRegion.Devices).InstanceId);
    }

    [Fact]
    public void RegionParams2KeepsTidalCenterAndDirection()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Tidal", StudioEffectKind.Tidal);
        var region = new StudioRegion { Params = layer.Params.Clone() };
        region.Params.CenterPointX = 1_234;
        region.Params.CenterPointY = 5_678;
        region.Params.Counterclockwise = true;
        region.Params.DeviceWithCenterPointJson = "[\"device-1\"]";
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var json = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(json))
        {
            var params2 = document.RootElement.GetProperty("profiles")[0].GetProperty("layers")[0]
                .GetProperty("regions")[0].GetProperty("params2");
            Assert.Equal(1_234d, params2.GetProperty("centerPointX2").GetDouble());
            Assert.Equal(5_678d, params2.GetProperty("centerPointY2").GetDouble());
            Assert.True(params2.GetProperty("counterclockwise").GetBoolean());
        }

        var restored = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0].Regions[0].Params;
        Assert.Equal(1_234d, restored.CenterPointX);
        Assert.Equal(5_678d, restored.CenterPointY);
        Assert.True(restored.Counterclockwise);
        Assert.Contains("device-1", restored.DeviceWithCenterPointJson);
    }

    [Fact]
    public void ImportKeepsGenerateHistoryFromAChromaFile()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [{
            "id": "aaaaaaaaaaaa",
            "title": "Generate",
            "devices": [],
            "layers": [{
              "id": "111111111111", "type": "layer", "title": "Generate",
              "name": "chroma-generate", "value": 18,
              "params": {
                "mediaSrc": "C:\\\\media\\\\clip.mp4",
                "currentConfig": { "pattern": "3" },
                "effectConfigList": [{ "pattern": "1" }, { "pattern": "3" }],
                "currentConfigId": 1
              },
              "regions": []
            }]
          }]
        }
        """;

        var project = StudioChromaImport.Import(json, []).Project;
        var parameters = Assert.Single(Assert.Single(project.Profiles).Items).Params;

        Assert.Equal(StudioEffectKind.ChromaGenerate, parameters.Effect);
        Assert.Equal(2, StudioGeneratePresets.Read(parameters.EffectConfigListJson).Count);
        Assert.Equal(1, parameters.CurrentConfigId);
        using var document = JsonDocument.Parse(StudioProjectCodec.Serialize(project));
        var saved = document.RootElement.GetProperty("profiles")[0]
            .GetProperty("layers")[0].GetProperty("params");
        Assert.Equal(2, saved.GetProperty("effectConfigList").GetArrayLength());
        Assert.Equal(1, saved.GetProperty("currentConfigId").GetInt32());
    }

    private static (int Layers, int Groups, int Regions, int Leds) Count(StudioProject project)
    {
        var layers = 0;
        var groups = 0;
        var regions = 0;
        var leds = 0;
        foreach (var profile in project.Profiles)
        foreach (var item in profile.EnumerateLayers())
        {
            if (item.IsGroup)
            {
                groups++;
                continue;
            }

            layers++;
            foreach (var region in item.Regions)
            {
                regions++;
                leds += region.Devices.Sum(device => device.Leds.Count);
            }
        }

        return (layers, groups, regions, leds);
    }

    private static int CountColorStops(StudioProject project) =>
        project.Profiles
            .SelectMany(profile => profile.EnumerateLayers())
            .SelectMany(layer => layer.Regions)
            .Sum(region => region.Params.ColorStops.Count);

    private static void AssertChromaShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        foreach (var profile in root.GetProperty("profiles").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.GetProperty("id").GetString()));
            Assert.Equal(JsonValueKind.String, profile.GetProperty("title").ValueKind);
            foreach (var device in profile.GetProperty("devices").EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(device.GetProperty("id").GetString()));
                Assert.Equal(JsonValueKind.Number, device.GetProperty("productId").ValueKind);
                if (device.TryGetProperty("category", out var category))
                    Assert.Equal(JsonValueKind.String, category.ValueKind);
                if (device.TryGetProperty("productName", out var productName))
                    Assert.Equal(JsonValueKind.String, productName.ValueKind);
                Assert.Equal(JsonValueKind.Object, device.GetProperty("config").ValueKind);
                Assert.Equal(JsonValueKind.Array, device.GetProperty("leds").ValueKind);
                if (device.TryGetProperty("instanceId", out var instanceId))
                    Assert.Equal(JsonValueKind.Number, instanceId.ValueKind);
                Assert.Contains(device.GetProperty("orientation").GetInt32(), new[] { 0, 90, 180, 270 });
            }

            foreach (var item in profile.GetProperty("layers").EnumerateArray())
                AssertLayerShape(item);
        }
    }

    private static void AssertLayerShape(JsonElement item)
    {
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("id").GetString()));
        Assert.Equal(JsonValueKind.String, item.GetProperty("title").ValueKind);
        var type = item.GetProperty("type").GetString();
        if (type == "group")
        {
            Assert.Equal(JsonValueKind.Array, item.GetProperty("layers").ValueKind);
            foreach (var child in item.GetProperty("layers").EnumerateArray()) AssertLayerShape(child);
            return;
        }

        Assert.Equal("layer", type);
        Assert.Contains(item.GetProperty("name").GetString(), new[]
        {
            "ambient", "audio", "breathing", "chroma-generate", "fire", "reactive", "ripple",
            "spectrum", "starlight", "static", "tidal", "wave", "wheel",
        });
        Assert.Equal(JsonValueKind.Number, item.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Object, item.GetProperty("params").ValueKind);
        AssertParamsShape(item.GetProperty("params"));
        foreach (var region in item.GetProperty("regions").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Object, region.GetProperty("params").ValueKind);
            AssertParamsShape(region.GetProperty("params"));
            foreach (var device in region.GetProperty("devices").EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(device.GetProperty("id").GetString()));
                if (device.TryGetProperty("instanceId", out var instanceId))
                    Assert.Equal(JsonValueKind.Number, instanceId.ValueKind);
                Assert.Equal(JsonValueKind.Array, device.GetProperty("leds").ValueKind);
            }
        }
    }

    private static void AssertParamsShape(JsonElement parameters)
    {
        if (parameters.TryGetProperty("split", out var split))
            Assert.True(split.ValueKind is JsonValueKind.True or JsonValueKind.False);
        if (parameters.TryGetProperty("colorStopsCustom", out var custom))
            Assert.True(custom.ValueKind is JsonValueKind.True or JsonValueKind.False);
    }
}
