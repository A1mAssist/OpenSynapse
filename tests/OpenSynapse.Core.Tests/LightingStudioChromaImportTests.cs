using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioChromaImportTests
{
    [Fact]
    public void ExactInstanceMatchRewritesDevice()
    {
        var json = JsonWithDevices(new StudioRegionDevice { ProductId = 0, InstanceId = "KBD-A" });

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "kbd-a")]);

        var device = SingleDevice(result.Project);
        Assert.Equal((ushort)736, device.ProductId);
        Assert.Equal("kbd-a", device.InstanceId);
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
