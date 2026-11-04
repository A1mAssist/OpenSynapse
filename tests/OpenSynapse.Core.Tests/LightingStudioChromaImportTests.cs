using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioChromaImportTests
{
    [Fact]
    public void ExactInstanceMatchRewritesDevice()
    {
        var source = new StudioRegionDevice { ProductId = 0, InstanceId = "KBD-A" };
        source.Leds.Add(new StudioLedRef(0, 0, InstanceId: "KBD-A"));
        var json = JsonWithDevices(source);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "kbd-a")]);

        var device = SingleDevice(result.Project);
        Assert.Equal((ushort)736, device.ProductId);
        Assert.Equal("kbd-a", device.InstanceId);
        Assert.Equal("KBD-A", Assert.Single(device.Leds).InstanceId);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void ProductOnlyMatchWithOneCandidateSucceeds()
    {
        var json = JsonWithDevices(new StudioRegionDevice { ProductId = 736, InstanceId = "old-instance" });

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "new-instance")]);

        var device = SingleDevice(result.Project);
        Assert.Equal((ushort)736, device.ProductId);
        Assert.Equal("new-instance", device.InstanceId);
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.RemappedLeds));
        Assert.DoesNotContain(result.Issues, issue => issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
    }

    [Fact]
    public void ProductOnlyWithTwoCandidatesRecordsAmbiguousAndPicksFirst()
    {
        var json = JsonWithDevices(new StudioRegionDevice { ProductId = 736, InstanceId = "old-instance" });

        var result = StudioChromaImport.Import(json,
        [
            new StudioDeviceMatch(736, "first-device"),
            new StudioDeviceMatch(736, "second-device"),
        ]);

        Assert.Equal("first-device", SingleDevice(result.Project).InstanceId);
        var ambiguous = Assert.Single(result.Issues, issue => issue.Kind == StudioImportIssueKind.AmbiguousDevice);
        Assert.Equal((ushort)736, ambiguous.ProductId);
        Assert.DoesNotContain(result.Issues, issue => issue.Kind == StudioImportIssueKind.MissingDevice);
    }

    [Fact]
    public void MissingDeviceIsStrippedAndReportedOnce()
    {
        var missing = new StudioRegionDevice { ProductId = 999, InstanceId = "ghost" };
        missing.Leds.Add(new StudioLedRef(0, 0, InputType: "0", InputData: "Esc"));
        var present = new StudioRegionDevice { ProductId = 736, InstanceId = "kbd" };
        var json = JsonWithDevices(missing, present);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "kbd")]);

        var region = SingleRegion(result.Project);
        var surviving = Assert.Single(region.Devices);
        Assert.Equal("kbd", surviving.InstanceId);
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.MissingDevice));
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.DeviceDropped));
        Assert.Equal(2, result.Issues.Count);
        Assert.Single(result.Project.Profiles[0].Items);
    }

    [Fact]
    public void LedReferencesSurviveTheRemap()
    {
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "old-instance", DeviceId = "device-1" };
        device.Leds.Add(new StudioLedRef(0, 1, InputType: "0", InputData: "Esc"));
        device.Leds.Add(new StudioLedRef(2, 3, RegionId: 7, RegionIndex: 2));
        device.Leds.Add(new StudioLedRef(4, 5));
        var json = JsonWithDevices(device);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "new-instance")]);

        var remapped = SingleDevice(result.Project);
        Assert.Equal("new-instance", remapped.InstanceId);
        Assert.Equal("device-1", remapped.DeviceId);
        Assert.Equal(
            [
                new StudioLedRef(0, 1, InputType: "0", InputData: "Esc"),
                new StudioLedRef(2, 3, RegionId: 7, RegionIndex: 2),
                new StudioLedRef(4, 5),
            ],
            remapped.Leds);
    }

    [Fact]
    public void ImportRemapsLedInstanceWithItsDevice()
    {
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "old-instance" };
        device.Leds.Add(new StudioLedRef(0, 1, InstanceId: "old-instance"));
        var json = JsonWithDevices(device);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "new-instance")]);

        var remapped = SingleDevice(result.Project);
        Assert.Equal("new-instance", remapped.InstanceId);
        Assert.Equal("new-instance", Assert.Single(remapped.Leds).InstanceId);
    }

    [Fact]
    public void PlacementParentFieldsAndLedRefSurviveImport()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [{
            "id": "aaaaaaaaaaaa",
            "title": "Parented",
            "devices": [{
              "id": "device-662-0",
              "productId": 662,
              "instanceId": 0,
              "x": 4520,
              "y": 4840,
              "width": 760,
              "height": 320,
              "parentId": 664,
              "parentX": 4520,
              "parentY": 4840,
              "placementIndex": 1,
              "leds": [{ "row": 0, "col": 0 }, { "row": 1, "col": 2 }],
              "ledRef": [{ "row": 0, "col": 0 }, { "row": 1, "col": 2 }]
            }],
            "layers": []
          }]
        }
        """;

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(662, "0")]);

        var placement = Assert.Single(result.Project.Profiles[0].Devices);
        Assert.Equal("664", placement.ParentId);
        Assert.Equal(4520d, placement.ParentX);
        Assert.Equal(4840d, placement.ParentY);
        Assert.Equal(1, placement.PlacementIndex);
        using (var ledRef = JsonDocument.Parse(placement.LedRefJson!))
            Assert.Equal(2, ledRef.RootElement.GetArrayLength());
        Assert.Empty(result.Issues);

        using var document = JsonDocument.Parse(StudioProjectCodec.Serialize(result.Project));
        var device = document.RootElement.GetProperty("profiles")[0].GetProperty("devices")[0];
        Assert.Equal(664, device.GetProperty("parentId").GetInt64());
        Assert.Equal(2, device.GetProperty("ledRef").GetArrayLength());
    }

    [Fact]
    public void RejectsNegativeLedRefRow()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [{
            "id": "aaaaaaaaaaaa",
            "title": "Bad",
            "layers": [],
            "devices": [{ "id": "device-662-0", "productId": 662, "ledRef": [{ "row": -1, "col": 0 }] }]
          }]
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaImport.Import(json, []));

        Assert.Contains("ledRef[0].row", error.Message);
    }

    [Fact]
    public void ChromaStyleJsonWithGroupAndTwoProfilesImportsBoth()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [
            {
              "id": "aaaaaaaaaaaa",
              "title": "Profile A",
              "layers": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "type": "group",
                  "title": "Group 1",
                  "layers": [
                    {
                      "id": "22222222-2222-2222-2222-222222222222",
                      "title": "Wave",
                      "value": 3,
                      "regions": [
                        {
                          "devices": [
                            {
                              "productId": 736,
                              "instanceId": "old-keyboard",
                              "leds": [ { "row": 0, "col": 1, "inputType": 0, "inputData": "Esc" } ]
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            },
            {
              "id": "bbbbbbbbbbbb",
              "title": "Profile B",
              "layers": [ { "id": "33333333-3333-3333-3333-333333333333", "title": "Solid", "value": 0 } ]
            }
          ]
        }
        """;

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "kbd-a")]);

        Assert.Equal(2, result.Project.Profiles.Count);
        Assert.Equal("Profile A", result.Project.Profiles[0].Title);
        Assert.Equal("Profile B", result.Project.Profiles[1].Title);
        var group = Assert.Single(result.Project.Profiles[0].Items);
        Assert.True(group.IsGroup);
        var layer = Assert.Single(group.Children);
        var device = Assert.Single(Assert.Single(layer.Regions).Devices);
        Assert.Equal("kbd-a", device.InstanceId);
        Assert.Equal((ushort)736, device.ProductId);
        Assert.Single(result.Project.Profiles[1].Items);
        Assert.Equal(StudioEffectKind.Solid, result.Project.Profiles[1].Items[0].Params.Effect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullOrEmptyConnectedDropsEveryDeviceButKeepsLayers(bool useNull)
    {
        var json = JsonWithDevices(
            new StudioRegionDevice { ProductId = 736, InstanceId = "kbd" },
            new StudioRegionDevice { ProductId = 737, InstanceId = "mouse" });
        IReadOnlyList<StudioDeviceMatch> connected = useNull ? null! : [];

        var result = StudioChromaImport.Import(json, connected);

        var layer = Assert.Single(result.Project.Profiles[0].Items);
        var region = Assert.Single(layer.Regions);
        Assert.Empty(region.Devices);
        Assert.Equal(2, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.MissingDevice));
        Assert.Equal(2, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.DeviceDropped));
        Assert.Equal(4, result.Issues.Count);
    }

    [Fact]
    public void LegacyV1ProjectStillMigratesThroughChromaImport()
    {
        var document = new StudioDocument { Name = "Legacy" };
        document.Layers.Add(new StudioLayer("Glow") { Effect = StudioEffectKind.Solid });

        var result = StudioChromaImport.Import(StudioDocumentCodec.Serialize(document), []);

        Assert.Equal("Legacy", result.Project.Name);
        Assert.Equal("Legacy", Assert.Single(result.Project.Profiles).Title);
    }

    [Fact]
    public void MissingRootVersionIsAcceptedLikeChromasImporter()
    {
        const string json = """
        {
          "profiles": [
            {
              "id": "aaaaaaaaaaaa",
              "title": "Legacy",
              "devices": [],
              "layers": [ { "id": "111111111111", "name": "static", "params": {}, "regions": [] } ]
            }
          ]
        }
        """;

        var result = StudioChromaImport.Import(json, []);

        Assert.Equal("Legacy", Assert.Single(result.Project.Profiles).Title);
    }

    [Theory]
    [InlineData("""{"version":1,"profiles":[]}""", "at least one profile")]
    [InlineData(
        """{"version":1,"profiles":[{"id":"aaaaaaaaaaaa","title":"Bad","devices":[],"layers":[42]}]}""",
        "profiles[0].layers[0]")]
    [InlineData(
        """{"version":1,"profiles":[{"id":"aaaaaaaaaaaa","title":"Bad","devices":[],"layers":[{"id":"111111111111","name":"sparkle","params":{},"regions":[]}]}]}""",
        "unknown effect")]
    [InlineData(
        """{"version":1,"profiles":[{"id":"aaaaaaaaaaaa","title":"Bad","devices":[],"layers":[{"id":"111111111111","name":"static","regions":[{"devices":[{"productId":664,"instanceId":0,"leds":[{"row":-1,"col":0}]}]}]}]}]}""",
        "non-negative integer")]
    [InlineData(
        """{"version":1,"profiles":[{"id":"aaaaaaaaaaaa","title":"Bad","default":"yes","devices":[],"layers":[]}]}""",
        "default")]
    public void RejectsSchemaInvalidDocuments(string json, string fragment)
    {
        var error = Assert.Throws<InvalidDataException>(() => StudioChromaImport.Import(json, []));

        Assert.Contains(fragment, error.Message);
    }

    [Fact]
    public void ReportsMissingPlacementWithoutDroppingLayers()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        profile.Devices.Add(new StudioDevicePlacement { ProductId = 999, InstanceId = "ghost" });
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        var present = new StudioRegionDevice { ProductId = 736, InstanceId = "kbd" };
        present.Leds.Add(new StudioLedRef(0, 0));
        region.Devices.Add(present);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var result = StudioChromaImport.Import(StudioProjectCodec.Serialize(project),
            [new StudioDeviceMatch(736, "kbd")]);

        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.MissingDevice));
        Assert.Equal(0, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.DeviceDropped));
        Assert.Single(result.Project.Profiles[0].Items);
        Assert.Equal("kbd", Assert.Single(Assert.Single(
            Assert.Single(result.Project.Profiles[0].Items).Regions).Devices).InstanceId);
    }

    [Fact]
    public void PlacementResolutionIsSharedWithItsRegionDevice()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        profile.Devices.Add(new StudioDevicePlacement
        {
            ProductId = 736,
            InstanceId = "old",
            DeviceId = "SN1",
        });
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "old", DeviceId = "SN1" };
        device.Leds.Add(new StudioLedRef(0, 0));
        region.Devices.Add(device);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);

        var result = StudioChromaImport.Import(StudioProjectCodec.Serialize(project),
        [
            new StudioDeviceMatch(736, "first"),
            new StudioDeviceMatch(736, "second"),
        ]);

        Assert.Single(result.Issues, issue => issue.Kind == StudioImportIssueKind.AmbiguousDevice);
        Assert.Equal("first", Assert.Single(result.Project.Profiles[0].Devices).InstanceId);
        Assert.Equal("first", Assert.Single(Assert.Single(
            Assert.Single(result.Project.Profiles[0].Items).Regions).Devices).InstanceId);
    }

    [Fact]
    public void ReconcileRewritesProfileRegionAndLedInstances()
    {
        var project = new StudioProject();
        foreach (var title in new[] { "First", "Second" })
        {
            var profile = new StudioProfile(title);
            profile.Devices.Add(new StudioDevicePlacement
            {
                Id = "device-" + title,
                ProductId = 736,
                InstanceId = "authored",
                DeviceId = "SN-" + title,
            });
            var group = StudioLayerItem.CreateGroup("Group");
            var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
            var region = new StudioRegion();
            var device = new StudioRegionDevice
            {
                Id = "device-" + title,
                ProductId = 736,
                InstanceId = "authored",
                DeviceId = "SN-" + title,
            };
            device.Leds.Add(new StudioLedRef(0, 1, InstanceId: "authored"));
            device.Leds.Add(new StudioLedRef(2, 3, InstanceId: "authored"));
            region.Devices.Add(device);
            layer.Regions.Add(region);
            group.Children.Add(layer);
            profile.Items.Add(group);
            project.Profiles.Add(profile);
        }

        var result = StudioChromaImport.Import(
            StudioProjectCodec.Serialize(project),
            [new StudioDeviceMatch(736, "live")]);

        Assert.Equal(2, result.Project.Profiles.Count);
        foreach (var profile in result.Project.Profiles)
        {
            Assert.Equal("live", Assert.Single(profile.Devices).InstanceId);
            var imported = Assert.Single(Assert.Single(Assert.Single(
                profile.Items).Children).Regions).Devices.Single();
            Assert.Equal("live", imported.InstanceId);
            Assert.Equal((ushort)736, imported.ProductId);
            Assert.All(imported.Leds, led => Assert.Equal("live", led.InstanceId));
        }

        Assert.Equal(2, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.RemappedDevice));
        Assert.Equal(2, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.RemappedLeds));
        Assert.DoesNotContain(result.Issues,
            issue => issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
    }

    [Fact]
    public void ImportNormalizesMultipleDefaultMarkersToTheFirstProfile()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [
            { "id": "aaaaaaaaaaaa", "title": "One", "default": true, "layers": [] },
            { "id": "bbbbbbbbbbbb", "title": "Two", "default": true, "layers": [] }
          ]
        }
        """;

        var result = StudioChromaImport.Import(json, []);

        Assert.True(result.Project.Profiles[0].IsDefault);
        Assert.False(result.Project.Profiles[1].IsDefault);
        Assert.Equal(result.Project.Profiles[0].Id, result.Project.SelectedProfileId);
    }

    [Fact]
    public void ImportRejectsFutureSchemaVersionWithInvalidData()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [{ "id": "aaaaaaaaaaaa", "title": "Future", "devices": [], "layers": [] }],
          "openSynapse": { "schemaVersion": 3 }
        }
        """;

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaImport.Import(json, []));

        Assert.Contains("3", error.Message);
    }

    [Fact]
    public void LayerValue17ImportsAsRadiate()
    {
        const string json = """
        {
          "version": 1,
          "profiles": [{
            "id": "aaaaaaaaaaaa",
            "title": "Radiate",
            "devices": [],
            "layers": [{
              "id": "111111111111", "type": "layer", "title": "Radiate",
              "name": "spectrum", "value": 17, "params": {}, "regions": []
            }]
          }]
        }
        """;

        var result = StudioChromaImport.Import(json, []);

        Assert.Equal(StudioEffectKind.Radiate, Assert.Single(result.Project.Profiles[0].Items).Params.Effect);
    }

    [Fact]
    public void ExactInstanceWithDifferentProductIsNotRemapped()
    {
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "shared" };
        device.Leds.Add(new StudioLedRef(0, 0));
        var json = JsonWithDevices(device);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(999, "shared")]);

        Assert.Empty(SingleRegion(result.Project).Devices);
        Assert.Contains(result.Issues, issue => issue.Kind == StudioImportIssueKind.MissingDevice);
        Assert.DoesNotContain(result.Issues, issue => issue.Kind == StudioImportIssueKind.RemappedLeds);
    }

    [Fact]
    public void KeyboardRemapUsesInputTypeAndData()
    {
        var device = new StudioRegionDevice { Id = "dev", ProductId = 736, InstanceId = "old" };
        device.Leds.Add(new StudioLedRef(0, 0, InputType: "kbd", InputData: "[1,0]"));
        var json = JsonWithDevicePlacement(device, "keyboard");
        var layout = ParseLayout("""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 6,
                "MatrixMaxCol": 18,
                "DeviceMaxRow": 6,
                "DeviceMaxCol": 17,
                "LedMatrix": [{ "MatrixPos": [3, 4], "DevicePos": [3, 4] }],
                "LedInputMap": [{ "InputType": "kbd", "InputData": [1, 0], "MatrixPos": [3, 4] }]
              }
            }
            """);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "new", layout)]);

        var led = Assert.Single(SingleDevice(result.Project).Leds);
        Assert.Equal((3, 4), (led.Row, led.Col));
        Assert.Equal("new", led.InstanceId);
        Assert.Equal("kbd", led.InputType);
        Assert.Equal("[1,0]", led.InputData);
    }

    [Fact]
    public void DeviceRemapUsesRegionIdAndIndex()
    {
        var device = new StudioRegionDevice { Id = "dev", ProductId = 100, InstanceId = "old" };
        device.Leds.Add(new StudioLedRef(0, 0, RegionId: 4, RegionIndex: 0));
        var json = JsonWithDevicePlacement(device, "mouse");
        var layout = ParseLayout("""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 7,
                "MatrixMaxCol": 5,
                "DeviceMaxRow": 1,
                "DeviceMaxCol": 2,
                "LedRegion": [
                  { "RegionId": 4, "RegionMatrix": [{ "MatrixPos": "1 2" }] }
                ],
                "LedMatrix": [{ "MatrixPos": [1, 2], "DevicePos": [0, 1] }]
              }
            }
            """);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(100, "new", layout)]);

        var led = Assert.Single(SingleDevice(result.Project).Leds);
        Assert.Equal((1, 2), (led.Row, led.Col));
        Assert.Equal(4, led.RegionId);
        Assert.Equal(0, led.RegionIndex);
        Assert.Equal("new", led.InstanceId);
    }

    [Fact]
    public void UnmatchedKeyboardLedsFallBackToTheTargetLayout()
    {
        var device = new StudioRegionDevice { Id = "dev", ProductId = 736, InstanceId = "old" };
        device.Leds.Add(new StudioLedRef(0, 0, InputType: "kbd", InputData: "[99,0]"));
        var json = JsonWithDevicePlacement(device, "keyboard");
        var layout = ParseLayout("""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 6,
                "MatrixMaxCol": 18,
                "DeviceMaxRow": 6,
                "DeviceMaxCol": 17,
                "LedMatrix": [{ "MatrixPos": [2, 2], "DevicePos": [2, 2] }],
                "LedInputMap": [{ "InputType": "kbd", "InputData": [1, 0], "MatrixPos": [2, 2] }]
              }
            }
            """);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "new", layout)]);

        var led = Assert.Single(SingleDevice(result.Project).Leds);
        Assert.Equal((2, 2), (led.Row, led.Col));
        Assert.Equal("[1,0]", led.InputData);
        Assert.Equal("new", led.InstanceId);
    }

    private static StudioLedLayout ParseLayout(string json) => StudioLedLayout.Parse(json);

    private static string JsonWithDevicePlacement(StudioRegionDevice device, string category)
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        profile.Devices.Add(new StudioDevicePlacement
        {
            Id = device.Id,
            ProductId = device.ProductId,
            InstanceId = device.InstanceId ?? string.Empty,
            Category = category,
            X = 0,
            Y = 0,
        });
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        region.Devices.Add(device);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);
        return StudioProjectCodec.Serialize(project);
    }

    private static string JsonWithDevices(params StudioRegionDevice[] devices)
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        foreach (var device in devices) region.Devices.Add(device);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);
        return StudioProjectCodec.Serialize(project);
    }

    private static StudioRegionDevice SingleDevice(StudioProject project) => Assert.Single(SingleRegion(project).Devices);

    private static StudioRegion SingleRegion(StudioProject project) =>
        Assert.Single(Assert.Single(project.Profiles[0].Items).Regions);
}
