using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioProjectCodecTests
{
    [Fact]
    public void RoundTripKeepsProfilesGroupsRegionsDevicesAndGradient()
    {
        var project = new StudioProject { Name = "Tour" };
        var first = new StudioProfile("Default");
        var group = StudioLayerItem.CreateGroup("Group 1");
        var gradient = StudioLayerItem.CreateLayer("Gradient", StudioEffectKind.Gradient);
        gradient.Params.Primary = new StudioRgb(0x11, 0x22, 0x33);
        gradient.Params.Secondary = new StudioRgb(0xAA, 0xBB, 0xCC);
        gradient.Params.Speed = 2;
        gradient.Params.PlaybackStart = StudioPlaybackStart.OnSelectedKeys;
        gradient.Params.PlaybackEnd = StudioPlaybackEnd.After;
        gradient.Params.Cycles = 3;
        gradient.Params.ColorStops.Add(new StudioColorStop(0, new StudioRgb(0x01, 0x02, 0x03)));
        gradient.Params.ColorStops.Add(new StudioColorStop(100, new StudioRgb(0x04, 0x05, 0x06)));
        var solid = StudioLayerItem.CreateLayer("Solid", StudioEffectKind.Solid);
        solid.Opacity = 60;
        solid.Blend = StudioBlendMode.Screen;
        group.Children.Add(gradient);
        group.Children.Add(solid);

        var top = StudioLayerItem.CreateLayer("Keyboard", StudioEffectKind.Wave);
        top.Params.Width = 22;
        top.Params.Angle = 45;
        top.Params.Blur = 4;
        top.Params.MediaSrc = "clip.mp4";
        top.Params.CurrentConfigJson = "{\"seed\":7}";
        top.Params.AutoBoost = true;
        var region = new StudioRegion
        {
            Params = new StudioEffectParams
            {
                Effect = StudioEffectKind.Ripple,
                Primary = new StudioRgb(9, 8, 7),
                Width = 12,
            },
        };
        var regionDevice = new StudioRegionDevice
        {
            Id = "region-1",
            ProductId = 736,
            InstanceId = "instance-1",
            DeviceId = "keyboard-1",
        };
        regionDevice.Leds.Add(new StudioLedRef(0, 1, InputType: "0", InputData: "Esc", InstanceId: "instance-1"));
        regionDevice.Leds.Add(new StudioLedRef(2, 3, RegionId: 7, RegionIndex: 2));
        region.Devices.Add(regionDevice);
        top.Regions.Add(region);
        first.Items.Add(group);
        first.Items.Add(top);
        first.Devices.Add(new StudioDevicePlacement
        {
            InstanceId = "instance-1",
            ProductId = 736,
            X = 1200,
            Y = 3400,
            Rotation = 90,
        });

        var second = new StudioProfile("Second");
        second.Items.Add(StudioLayerItem.CreateLayer("Background", StudioEffectKind.Breathing));
        project.Profiles.Add(first);
        project.Profiles.Add(second);
        project.SelectedProfileId = second.Id;

        var json = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(json))
        {
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.Equal(2, root.GetProperty("openSynapse").GetProperty("schemaVersion").GetInt32());
            var gradientLayer = root.GetProperty("profiles")[0].GetProperty("layers")[0].GetProperty("layers")[0];
            var gradientParams = gradientLayer.GetProperty("params");
            Assert.Equal("onSelectedKeys", gradientParams.GetProperty("playbackStart").GetString());
            Assert.Equal("after", gradientParams.GetProperty("playbackEnd").GetString());
            var stop = gradientParams.GetProperty("colorStops")[0];
            Assert.Equal(0, stop.GetProperty("Stop").GetInt32());
            Assert.Equal(0x010203, stop.GetProperty("Color").GetInt32());
            var solidExtras = root.GetProperty("profiles")[0].GetProperty("layers")[0].GetProperty("layers")[1]
                .GetProperty("openSynapse");
            Assert.Equal(60, solidExtras.GetProperty("opacity").GetDouble());
            Assert.Equal("Screen", solidExtras.GetProperty("blend").GetString());
            var ledExtras = root.GetProperty("profiles")[0].GetProperty("layers")[1]
                .GetProperty("regions")[0].GetProperty("devices")[0].GetProperty("leds")[0]
                .GetProperty("openSynapse");
            Assert.Equal("instance-1", ledExtras.GetProperty("instanceId").GetString());
        }

        var restored = StudioProjectCodec.Deserialize(json);
        Assert.Equal(json, StudioProjectCodec.Serialize(restored));

        Assert.Equal(2, restored.Profiles.Count);
        Assert.Equal(second.Id, restored.SelectedProfileId);
        var restoredFirst = restored.Profiles[0];
        Assert.Equal(2, restoredFirst.Items.Count);
        var restoredGroup = restoredFirst.Items[0];
        Assert.True(restoredGroup.IsGroup);
        Assert.Equal(2, restoredGroup.Children.Count);
        Assert.Equal(gradient.Id, restoredGroup.Children[0].Id);
        Assert.Equal(StudioEffectKind.Gradient, restoredGroup.Children[0].Params.Effect);
        Assert.Equal(2, restoredGroup.Children[0].Params.Speed);
        Assert.Equal(StudioPlaybackStart.OnSelectedKeys, restoredGroup.Children[0].Params.PlaybackStart);
        Assert.Equal(StudioPlaybackEnd.After, restoredGroup.Children[0].Params.PlaybackEnd);
        Assert.Equal(3, restoredGroup.Children[0].Params.Cycles);
        Assert.Equal(2, restoredGroup.Children[0].Params.ColorStops.Count);
        Assert.Equal(new StudioRgb(0x04, 0x05, 0x06), restoredGroup.Children[0].Params.ColorStops[1].Color);
        Assert.Equal(60, restoredGroup.Children[1].Opacity);
        Assert.Equal(StudioBlendMode.Screen, restoredGroup.Children[1].Blend);

        var restoredTop = restoredFirst.Items[1];
        Assert.Equal(StudioEffectKind.Wave, restoredTop.Params.Effect);
        Assert.Equal(22, restoredTop.Params.Width);
        Assert.Equal(45, restoredTop.Params.Angle);
        Assert.Equal(4, restoredTop.Params.Blur);
        Assert.Equal("clip.mp4", restoredTop.Params.MediaSrc);
        using var config = JsonDocument.Parse(restoredTop.Params.CurrentConfigJson!);
        Assert.Equal(7, config.RootElement.GetProperty("seed").GetInt32());
        Assert.True(restoredTop.Params.AutoBoost);
        var restoredRegion = Assert.Single(restoredTop.Regions);
        Assert.Equal(StudioEffectKind.Ripple, restoredRegion.Params.Effect);
        var restoredDevice = Assert.Single(restoredRegion.Devices);
        Assert.Equal("region-1", restoredDevice.Id);
        Assert.Equal((ushort)736, restoredDevice.ProductId);
        Assert.Equal("Esc", restoredDevice.Leds[0].InputData);
        Assert.Equal("0", restoredDevice.Leds[0].InputType);
        Assert.Equal("instance-1", restoredDevice.Leds[0].InstanceId);
        Assert.Null(restoredDevice.Leds[1].InstanceId);
        Assert.Equal(7, restoredDevice.Leds[1].RegionId!.Value);
        Assert.Equal(2, restoredDevice.Leds[1].RegionIndex!.Value);
        var placement = Assert.Single(restoredFirst.Devices);
        Assert.Equal(1200d, placement.X);
        Assert.Equal(3400d, placement.Y);
        Assert.Equal(90, placement.Rotation);
    }

    [Fact]
    public void RoundTripKeepsParentPlacementAndLedRef()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        profile.Devices.Add(new StudioDevicePlacement
        {
            Id = "device-662-0",
            ProductId = 662,
            InstanceId = "0",
            X = 4520,
            Y = 4840,
            ParentId = "664",
            ParentX = 4520,
            ParentY = 4840,
            PlacementIndex = 1,
            LedRefJson = """[{"row":0,"col":0},{"row":1,"col":2}]""",
        });
        profile.Items.Add(StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave));
        project.Profiles.Add(profile);

        var json = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(json))
        {
            var device = document.RootElement.GetProperty("profiles")[0].GetProperty("devices")[0];
            Assert.Equal(664, device.GetProperty("parentId").GetInt64());
            Assert.Equal(4520, device.GetProperty("parentX").GetDouble());
            Assert.Equal(4840, device.GetProperty("parentY").GetDouble());
            Assert.Equal(1, device.GetProperty("placementIndex").GetInt32());
            Assert.Equal(2, device.GetProperty("ledRef").GetArrayLength());
        }

        var restored = Assert.Single(StudioProjectCodec.Deserialize(json).Profiles).Devices[0];
        Assert.Equal("664", restored.ParentId);
        Assert.Equal(4520d, restored.ParentX);
        Assert.Equal(4840d, restored.ParentY);
        Assert.Equal(1, restored.PlacementIndex);
        using (var ledRef = JsonDocument.Parse(restored.LedRefJson!))
        {
            Assert.Equal(2, ledRef.RootElement.GetArrayLength());
            Assert.Equal(2, ledRef.RootElement[1].GetProperty("col").GetInt32());
        }

        Assert.Equal(json, StudioProjectCodec.Serialize(StudioProjectCodec.Deserialize(json)));
    }

    [Fact]
    public void RoundTripKeepsGenerateHistoryAndSelection()
    {
        var project = new StudioProject { Name = "Generate" };
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Generate", StudioEffectKind.ChromaGenerate);
        string? list = null;
        var current = string.Empty;
        for (var index = 0; index < StudioGeneratePresets.HistoryLimit + 1; index++)
        {
            current = StudioGeneratePresets.Format(StudioGeneratePresets.Create(new Random(index)));
            list = StudioGeneratePresets.Append(list, current, out _);
        }

        layer.Params.MediaSrc = @"C:\media\clip.mp4";
        layer.Params.CurrentConfigJson = current;
        layer.Params.EffectConfigListJson = list;
        layer.Params.CurrentConfigId = StudioGeneratePresets.HistoryLimit - 1;
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var restored = StudioProjectCodec.Deserialize(StudioProjectCodec.Serialize(project));
        var parameters = Assert.Single(Assert.Single(restored.Profiles).Items).Params;

        Assert.Equal(StudioEffectKind.ChromaGenerate, parameters.Effect);
        Assert.Equal(@"C:\media\clip.mp4", parameters.MediaSrc);
        Assert.Equal(StudioGeneratePresets.HistoryLimit - 1, parameters.CurrentConfigId);
        Assert.Equal(StudioGenerateConfig.Parse(current), parameters.GenerateConfig);
        var stored = StudioGeneratePresets.Read(parameters.EffectConfigListJson);
        Assert.Equal(StudioGeneratePresets.HistoryLimit, stored.Count);
        Assert.Equal(StudioGenerateConfig.Parse(current), StudioGenerateConfig.Parse(stored[^1]));
    }
    [Fact]
    public void ImportsChromaJsonWithUnknownFieldsAndNoVersion()
    {
        const string json = """
        {
          "unknownRoot": 42,
          "profiles": [
            {
              "id": "abcdef123456",
              "title": "League",
              "mystery": { "a": 1 },
              "layers": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "type": "layer",
                  "name": "Base",
                  "value": 6,
                  "params": { "color": 1122867, "color2": 0, "unknownParam": true },
                  "unknownLayer": "x"
                },
                {
                  "id": "22222222-2222-2222-2222-222222222222",
                  "type": "group",
                  "title": "Nested",
                  "opened": true,
                  "layers": [ { "type": "layer", "name": "Inner", "value": 4 }, { "type": "layer", "value": 5 } ]
                }
              ]
            },
            { "title": "No Id", "layers": [] }
          ]
        }
        """;

        var project = StudioProjectCodec.Deserialize(json);

        Assert.Equal(2, project.Profiles.Count);
        Assert.Equal("abcdef123456", project.Profiles[0].Id);
        Assert.Equal("League", project.Profiles[0].Title);
        Assert.Equal(project.Profiles[0].Id, project.SelectedProfileId);
        Assert.Equal(2, project.Profiles[0].Items.Count);
        var baseLayer = project.Profiles[0].Items[0];
        Assert.Equal(StudioEffectKind.Solid, baseLayer.Params.Effect);
        Assert.Equal(new StudioRgb(0x11, 0x22, 0x33), baseLayer.Params.Primary);
        Assert.Equal(StudioRgb.Black, baseLayer.Params.Secondary);
        var nested = project.Profiles[0].Items[1];
        Assert.True(nested.IsGroup);
        Assert.True(nested.Opened);
        Assert.Equal(2, nested.Children.Count);
        Assert.Equal(StudioEffectKind.Spectrum, nested.Children[0].Params.Effect);
        Assert.Equal("Inner", nested.Children[0].Title);
        Assert.Equal("Layer", nested.Children[1].Title);
        Assert.Equal(12, project.Profiles[1].Id.Length);
        Assert.Equal("No Id", project.Profiles[1].Title);
    }

    [Fact]
    public void RejectsNewerOpenSynapseSchemaVersion()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [{ "id": "abcdef123456", "title": "Future", "layers": [] }],
          "openSynapse": { "schemaVersion": 3 }
        }
        """;

        var error = Assert.Throws<InvalidOperationException>(() => StudioProjectCodec.Deserialize(json));

        Assert.Contains("3", error.Message);
        Assert.Contains(StudioProject.CurrentSchemaVersion.ToString(), error.Message);
    }

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("2.5")]
    [InlineData("{}")]
    [InlineData("-1")]
    public void RejectsMalformedOpenSynapseSchemaVersion(string value)
    {
        var json = "{\"version\":1,\"profiles\":[{\"id\":\"abcdef123456\",\"title\":\"Bad\"," +
            "\"layers\":[]}],\"openSynapse\":{\"schemaVersion\":" + value + "}}";

        var error = Assert.Throws<InvalidOperationException>(() => StudioProjectCodec.Deserialize(json));

        Assert.Contains("schemaVersion", error.Message);
    }

    [Fact]
    public void AcceptsOlderOpenSynapseSchemaVersion()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [{ "id": "abcdef123456", "title": "Older", "layers": [] }],
          "openSynapse": { "schemaVersion": 1 }
        }
        """;

        var project = StudioProjectCodec.Deserialize(json);

        Assert.Equal("Older", Assert.Single(project.Profiles).Title);
        Assert.True(project.Profiles[0].IsDefault);
    }

    [Fact]
    public void ProfileDefaultMarkerRoundTripsAndFallsBackToTheFirstProfile()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [
            { "id": "profile-one", "title": "One", "layers": [] },
            { "id": "profile-two", "title": "Two", "default": true, "layers": [] }
          ]
        }
        """;

        var project = StudioProjectCodec.Deserialize(json);

        Assert.False(project.Profiles[0].IsDefault);
        Assert.True(project.Profiles[1].IsDefault);
        Assert.Equal("profile-two", project.SelectedProfileId);

        var serialized = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(serialized))
        {
            var profiles = document.RootElement.GetProperty("profiles");
            Assert.False(profiles[0].TryGetProperty("default", out _));
            Assert.True(profiles[1].GetProperty("default").GetBoolean());
        }

        Assert.Equal(serialized, StudioProjectCodec.Serialize(StudioProjectCodec.Deserialize(serialized)));

        using (var subset = JsonDocument.Parse(StudioProjectCodec.Serialize(project, ["profile-one"])))
        {
            var only = Assert.Single(subset.RootElement.GetProperty("profiles").EnumerateArray());
            Assert.True(only.GetProperty("default").GetBoolean());
        }

        const string noMarker = """
        {
          "profiles": [
            { "id": "first", "title": "First", "layers": [] },
            { "id": "second", "title": "Second", "layers": [] }
          ]
        }
        """;
        var fallback = StudioProjectCodec.Deserialize(noMarker);
        Assert.True(fallback.Profiles[0].IsDefault);
        Assert.False(fallback.Profiles[1].IsDefault);
        Assert.Equal("first", fallback.SelectedProfileId);
        using var fallbackDocument = JsonDocument.Parse(StudioProjectCodec.Serialize(fallback));
        Assert.True(fallbackDocument.RootElement.GetProperty("profiles")[0].GetProperty("default").GetBoolean());
    }

    [Fact]
    public void MigratesV1Document()
    {
        var document = new StudioDocument { Name = "Legacy" };
        document.Layers.Add(new StudioLayer("Glow")
        {
            Effect = StudioEffectKind.Gradient,
            Enabled = false,
            Opacity = 42,
            Blend = StudioBlendMode.Multiply,
            Primary = new StudioRgb(0x10, 0x20, 0x30),
            Secondary = new StudioRgb(0x40, 0x50, 0x60),
            Speed = 2.5,
        });

        var project = StudioProjectCodec.Deserialize(StudioDocumentCodec.Serialize(document));

        Assert.Equal("Legacy", project.Name);
        var profile = Assert.Single(project.Profiles);
        Assert.Equal("Legacy", profile.Title);
        var layer = Assert.Single(profile.Items);
        Assert.Equal("Glow", layer.Title);
        Assert.False(layer.Visible);
        Assert.Equal(42, layer.Opacity);
        Assert.Equal(StudioBlendMode.Multiply, layer.Blend);
        Assert.Equal(StudioEffectKind.Gradient, layer.Params.Effect);
        Assert.Equal(new StudioRgb(0x10, 0x20, 0x30), layer.Params.Primary);
        Assert.Equal(new StudioRgb(0x40, 0x50, 0x60), layer.Params.Secondary);
        Assert.Equal(2.5, layer.Params.Speed);
        Assert.Empty(layer.Regions);
    }
    [Fact]
    public void GroupIdIsWrittenAndReimportedWithoutDuplication()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var group = StudioLayerItem.CreateGroup("Group");
        group.Children.Add(StudioLayerItem.CreateLayer("A", StudioEffectKind.Solid));
        group.Children.Add(StudioLayerItem.CreateLayer("B", StudioEffectKind.Solid));
        profile.Items.Add(group);
        project.Profiles.Add(profile);

        var json = StudioProjectCodec.Serialize(project);
        using var document = JsonDocument.Parse(json);
        var layers = document.RootElement.GetProperty("profiles")[0].GetProperty("layers");
        Assert.Equal(1, layers.GetArrayLength());
        var groupElement = layers[0];
        Assert.Equal("group", groupElement.GetProperty("type").GetString());
        var nested = groupElement.GetProperty("layers");
        Assert.Equal(2, nested.GetArrayLength());
        Assert.All(nested.EnumerateArray(), layer =>
            Assert.Equal(group.Id.ToString(), layer.GetProperty("groupId").GetString()));

        var restored = StudioProjectCodec.Deserialize(json);
        var restoredProfile = restored.Profiles[0];
        Assert.Single(restoredProfile.Items);
        Assert.Collection(restoredProfile.Items[0].Children,
            _ => { },
            _ => { });
        Assert.Equal(json, StudioProjectCodec.Serialize(restored));

        var flatJson = $$"""
        {
          "profiles": [
            {
              "id": "aaaaaaaaaaaa",
              "title": "Flat",
              "layers": [
                { "id": "{{group.Id}}", "type": "group", "title": "Group", "layers": [] },
                { "id": "{{Guid.NewGuid()}}", "type": "layer", "title": "Orphan", "value": 6, "groupId": "{{group.Id}}" }
              ]
            }
          ]
        }
        """;
        var flatProfile = StudioProjectCodec.Deserialize(flatJson).Profiles[0];
        Assert.Single(flatProfile.Items);
        Assert.Single(flatProfile.Items[0].Children);
        Assert.Equal("Orphan", flatProfile.Items[0].Children[0].Title);
    }

    [Fact]
    public void ScreenRectMapsBetweenUnitAndChromaRange()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Ambient", StudioEffectKind.Ambient);
        layer.Params.Screen = "custom";
        layer.Params.ScreenRect = new StudioScreenRect(0.25, 0.5, 0.1, 0.2);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var json = StudioProjectCodec.Serialize(project);
        using var document = JsonDocument.Parse(json);
        var rect = document.RootElement.GetProperty("profiles")[0].GetProperty("layers")[0]
            .GetProperty("params").GetProperty("screenRect");
        Assert.Equal(16384, rect.GetProperty("Left").GetInt32());
        Assert.Equal(32768, rect.GetProperty("Top").GetInt32());
        Assert.Equal(6554, rect.GetProperty("Right").GetInt32());
        Assert.Equal(13107, rect.GetProperty("Bottom").GetInt32());

        var restored = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0].Params.ScreenRect;
        Assert.True(restored.HasValue);
        Assert.InRange(restored.Value.X, 0.2499, 0.2501);
        Assert.InRange(restored.Value.Y, 0.4999, 0.5001);
        Assert.InRange(restored.Value.Width, 0.0999, 0.1001);
        Assert.InRange(restored.Value.Height, 0.1999, 0.2001);
        Assert.Equal(json, StudioProjectCodec.Serialize(StudioProjectCodec.Deserialize(json)));
    }

    [Fact]
    public void SaveReplacesFileAndLoadReadsItBack()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opensynapse-codec-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "project.ChromaEffects");
        try
        {
            var project = StudioProject.CreateDefault();
            project.Name = "Saved";
            StudioProjectCodec.Save(path, project);
            Assert.True(File.Exists(path));
            var loaded = StudioProjectCodec.Load(path);
            Assert.Equal("Saved", loaded.Name);
            Assert.Equal(project.Profiles[0].Id, loaded.Profiles[0].Id);

            project.Name = "Saved Again";
            StudioProjectCodec.Save(path, project);
            Assert.Equal("Saved Again", StudioProjectCodec.Load(path).Name);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MalformedJsonThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => StudioProjectCodec.Deserialize("{ \"profiles\": ["));
        Assert.Throws<InvalidOperationException>(() => StudioProjectCodec.Deserialize("[]"));
        Assert.Throws<InvalidOperationException>(() => StudioProjectCodec.Deserialize("{ \"version\": 1 }"));
    }

    [Fact]
    public void RoundTripKeepsTidalCenterPointAndDirection()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Tidal", StudioEffectKind.Tidal);
        layer.Params.CenterPointX = 3_200;
        layer.Params.CenterPointY = 7_400;
        layer.Params.Counterclockwise = true;
        layer.Params.DeviceWithCenterPointJson = "[\"device-1\"]";
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var json = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(json))
        {
            var params2 = document.RootElement.GetProperty("profiles")[0].GetProperty("layers")[0]
                .GetProperty("params2");
            Assert.Equal(80d, params2.GetProperty("centerPointX").GetDouble());
            Assert.Equal(3_200d, params2.GetProperty("centerPointX2").GetDouble());
            Assert.Equal(185d, params2.GetProperty("centerPointY").GetDouble());
            Assert.Equal(7_400d, params2.GetProperty("centerPointY2").GetDouble());
            Assert.True(params2.GetProperty("counterclockwise").GetBoolean());
            Assert.Equal("device-1", params2.GetProperty("deviceWithCenterPoint")[0].GetString());
        }

        var restored = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0].Params;
        Assert.Equal(3_200d, restored.CenterPointX);
        Assert.Equal(7_400d, restored.CenterPointY);
        Assert.True(restored.Counterclockwise);
        Assert.Contains("device-1", restored.DeviceWithCenterPointJson);
        Assert.Equal(json, StudioProjectCodec.Serialize(StudioProjectCodec.Deserialize(json)));
    }

    [Fact]
    public void MissingParams2FallsBackToTheCanvasCenter()
    {
        const string json = """
            {
              "version": 1,
              "profiles": [{
                "id": "profile-tidal",
                "title": "Tidal",
                "layers": [{
                  "id": "11111111-1111-1111-1111-111111111111",
                  "type": "layer",
                  "title": "Tidal",
                  "value": 19,
                  "params": { "color": 65280, "color2": 255, "speed": 10, "angle": 160, "split": true }
                }]
              }]
            }
            """;

        var parameters = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0].Params;

        Assert.Equal(StudioEffectKind.Tidal, parameters.Effect);
        Assert.Equal(5_000d, parameters.CenterPointX);
        Assert.Equal(5_000d, parameters.CenterPointY);
        Assert.False(parameters.Counterclockwise);
        Assert.Null(parameters.DeviceWithCenterPointJson);
    }

    [Fact]
    public void ScaledCenterPointIsConvertedToCanvasCoordinates()
    {
        const string json = """
            {
              "version": 1,
              "profiles": [{
                "id": "profile-tidal",
                "title": "Tidal",
                "layers": [{
                  "id": "11111111-1111-1111-1111-111111111111",
                  "type": "layer",
                  "title": "Tidal",
                  "value": 19,
                  "params": { "color": 65280, "color2": 255 },
                  "params2": { "centerPointX": 80, "centerPointY": 185, "counterclockwise": true }
                }]
              }]
            }
            """;

        var parameters = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0].Params;

        Assert.Equal(3_200d, parameters.CenterPointX);
        Assert.Equal(7_400d, parameters.CenterPointY);
        Assert.True(parameters.Counterclockwise);
    }

    [Fact]
    public void RoundTripKeepsWheelCenter()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Wheel", StudioEffectKind.Wheel);
        layer.Params.CenterRow = 37;
        layer.Params.CenterCol = 203;
        layer.Regions.Add(new StudioRegion { Params = layer.Params.Clone() });
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var json = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(json))
        {
            var layerElement = document.RootElement.GetProperty("profiles")[0].GetProperty("layers")[0];
            var params2 = layerElement.GetProperty("params2");
            Assert.Equal(203d, params2.GetProperty("centerPointX").GetDouble());
            Assert.Equal(8_120d, params2.GetProperty("centerPointX2").GetDouble());
            Assert.Equal(37d, params2.GetProperty("centerPointY").GetDouble());
            Assert.Equal(1_480d, params2.GetProperty("centerPointY2").GetDouble());
            Assert.Equal(37, params2.GetProperty("centerRow").GetInt32());
            Assert.Equal(203, params2.GetProperty("centerCol").GetInt32());
            var regionParams2 = layerElement.GetProperty("regions")[0].GetProperty("params2");
            Assert.Equal(37, regionParams2.GetProperty("centerRow").GetInt32());
            Assert.Equal(203, regionParams2.GetProperty("centerCol").GetInt32());
        }

        var restored = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0];
        Assert.Equal(37, restored.Params.CenterRow);
        Assert.Equal(203, restored.Params.CenterCol);
        var restoredRegion = Assert.Single(restored.Regions).Params;
        Assert.Equal(37, restoredRegion.CenterRow);
        Assert.Equal(203, restoredRegion.CenterCol);
        Assert.Equal(json, StudioProjectCodec.Serialize(StudioProjectCodec.Deserialize(json)));
    }

    [Fact]
    public void RoundTripKeepsRadiate()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Radiate", StudioEffectKind.Radiate);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var json = StudioProjectCodec.Serialize(project);
        using (var document = JsonDocument.Parse(json))
        {
            var layerElement = document.RootElement.GetProperty("profiles")[0].GetProperty("layers")[0];
            Assert.Equal("spectrum", layerElement.GetProperty("name").GetString());
            Assert.Equal(4, layerElement.GetProperty("value").GetInt32());
            Assert.Equal("radiate",
                layerElement.GetProperty("openSynapse").GetProperty("effect").GetString());
        }

        var restored = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0];
        Assert.Equal(StudioEffectKind.Radiate, restored.Params.Effect);
    }

    [Fact]
    public void MissingWheelCenterStaysUnsetForMatrixCenter()
    {
        const string json = """
            {
              "version": 1,
              "profiles": [{
                "id": "profile-wheel",
                "title": "Wheel",
                "layers": [{
                  "id": "22222222-2222-2222-2222-222222222222",
                  "type": "layer",
                  "title": "Wheel",
                  "value": 13,
                  "params": { "colorStops": [{ "Stop": 0, "Color": 16711680 }], "speed": 60 }
                }]
              }]
            }
            """;

        var parameters = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0].Params;

        Assert.Equal(StudioEffectKind.Wheel, parameters.Effect);
        Assert.Equal(-1, parameters.CenterRow);
        Assert.Equal(-1, parameters.CenterCol);
    }

    [Fact]
    public void WheelCenterFromJsonClampsToTheEditorRange()
    {
        const string json = """
            {
              "version": 1,
              "profiles": [{
                "id": "profile-wheel",
                "title": "Wheel",
                "layers": [{
                  "id": "33333333-3333-3333-3333-333333333333",
                  "type": "layer",
                  "title": "Wheel",
                  "value": 13,
                  "params": { "colorStops": [{ "Stop": 0, "Color": 16711680 }], "speed": 60 },
                  "params2": { "centerRow": 999, "centerCol": -10 }
                }]
              }]
            }
            """;

        var parameters = StudioProjectCodec.Deserialize(json).Profiles[0].Items[0].Params;

        Assert.Equal(StudioEffectParams.WheelCenterMaximum, parameters.CenterRow);
        Assert.Equal(0, parameters.CenterCol);
    }

    [Fact]
    public void RoundTripKeepsEffectParameterSemantics()
    {
        var project = new StudioProject { Name = "Params" };
        var profile = new StudioProfile("Default");
        var wave = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        wave.Params.Width = 400;
        wave.Params.Speed = 50;
        wave.Params.PauseMs = 120;
        wave.Params.RandomColor = true;
        wave.Params.AutoBoost = true;
        wave.Params.Decay = 1.5;
        wave.Params.PlaybackStart = StudioPlaybackStart.OnPress;
        wave.Params.PlaybackEnd = StudioPlaybackEnd.After;
        wave.Params.Cycles = 7;
        wave.Params.ColorStops.Add(new StudioColorStop(0, new StudioRgb(1, 2, 3)));
        wave.Params.ColorStops.Add(new StudioColorStop(100, new StudioRgb(4, 5, 6)));
        profile.Items.Add(wave);
        project.Profiles.Add(profile);

        var restored = StudioProjectCodec.Deserialize(StudioProjectCodec.Serialize(project))
            .Profiles[0].Items[0].Params;

        Assert.Equal(400d, restored.Width);
        Assert.Equal(50d, restored.Speed);
        Assert.Equal(120, restored.PauseMs);
        Assert.True(restored.RandomColor);
        Assert.True(restored.AutoBoost);
        Assert.Equal(1.5, restored.Decay);
        Assert.Equal(StudioPlaybackStart.OnPress, restored.PlaybackStart);
        Assert.Equal(StudioPlaybackEnd.After, restored.PlaybackEnd);
        Assert.Equal(7, restored.Cycles);
        Assert.Equal(2, restored.ColorStops.Count);
    }
}
