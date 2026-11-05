using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioArgbDeviceTests
{
    private const ushort ArgbProductId = 0x0F1F;

    [Fact]
    public void LayoutIncludesConfiguredChannelCounts()
    {
        var info = StudioArgbDevice.Create(
            "argb-controller",
            "Razer Chroma Addressable RGB Controller",
            [80, 3, 2, 1, 1, 1]);

        Assert.Equal(ArgbProductId, info.ProductId);
        Assert.Equal(6, info.Rows);
        Assert.Equal(80, info.Columns);
        Assert.Equal([80, 3, 2, 1, 1, 1], info.LedCounts);
        Assert.Equal(88, info.Layout.Leds.Count);
        Assert.Equal(80, info.Layout.MatrixColumns);
        Assert.Equal((5, 0), info.Layout.ToDevice(5, 0));
        Assert.Equal(80, info.Layout.Leds.Count(led => led.MatrixRow == 0));
    }

    [Fact]
    public void OfficialManifestMapsSixChannelsToRows()
    {
        var path = Path.Combine(
            RepoLayout.Root,
            "artifacts", "chroma-studio-2026-11-03", "catalog", "manifests",
            "DeviceManifest_3871_0.json");
        if (!File.Exists(path)) return; // The catalog is a local artifact snapshot.

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var config = document.RootElement.GetProperty("ledConfig").GetProperty("ledConfig_0_0");
        Assert.Equal(6, config.GetProperty("MatrixMaxRow").GetInt32());
        Assert.Equal(80, config.GetProperty("DeviceMaxCol").GetInt32());

        var rows = config.GetProperty("LedMatrix").EnumerateArray()
            .Select(item => item.GetProperty("MatrixPos")[0].GetInt32())
            .GroupBy(row => row)
            .OrderBy(group => group.Key)
            .ToArray();
        Assert.Equal(6, rows.Length);
        Assert.All(rows, group => Assert.Equal(80, group.Count()));
        Assert.Equal(
            [26, 27, 28, 29, 30, 31],
            document.RootElement.GetProperty("chromaStudioRegionIdList")
                .EnumerateArray().Select(item => item.GetInt32()));

        var info = StudioArgbDevice.Create(
            "argb-controller", "ARGB", Enumerable.Repeat(80, 6).ToArray());
        Assert.Equal(6, info.Rows);
        Assert.Equal(80, info.Columns);
        Assert.Equal(480, info.Layout.Leds.Count);
        Assert.Equal(6, info.Layout.Leds.Select(led => led.MatrixRow).Distinct().Count());
        Assert.All(info.Layout.Leds, led => Assert.Equal(led.MatrixRow, led.DeviceRow));
    }

    [Fact]
    public void LegacyRegions26To31MapToChannelsZeroToFive()
    {
        for (var channel = 0; channel < StudioArgbDevice.ChannelCount; channel++)
        {
            Assert.Equal(channel, StudioArgbDevice.ChannelFromLegacyInstance((26 + channel).ToString()));
        }

        Assert.Null(StudioArgbDevice.ChannelFromLegacyInstance("25"));
        Assert.Null(StudioArgbDevice.ChannelFromLegacyInstance("32"));
        Assert.Null(StudioArgbDevice.ChannelFromLegacyInstance("argb-test"));
    }

    [Fact]
    public void SliceChannelKeepsEachConfiguredLedCount()
    {
        var info = StudioArgbDevice.Create("argb-controller", "ARGB", [80, 3, 2, 1, 1, 1]);
        var mask = new byte[info.Rows * info.Columns];
        Array.Fill(mask, StudioMaskBuilder.Full);
        var layers = new[]
        {
            new StudioLayerRender(
                new StudioEffectParams { Effect = StudioEffectKind.Solid },
                mask,
                100,
                StudioBlendMode.Normal,
                true,
                Guid.NewGuid()),
        };

        for (var channel = 0; channel < StudioArgbDevice.ChannelCount; channel++)
        {
            var sliced = Assert.Single(StudioArgbDevice.SliceChannel(
                layers, info.Rows, info.Columns, channel, info.LedCounts[channel]));
            Assert.Equal(info.LedCounts[channel], sliced.Mask!.Length);
            Assert.All(sliced.Mask, value => Assert.Equal(StudioMaskBuilder.Full, value));
        }
    }

    [Fact]
    public void GridVisualDrawsOnePathPerConfiguredLed()
    {
        var info = StudioArgbDevice.Create(
            "argb-controller",
            "ARGB",
            [4, 2, 1, 1, 1, 1]);

        var visual = StudioDeviceVisualBuilder.BuildGrid(
            info.ProductId, info.InstanceId, info.Name, info.Layout);

        Assert.False(visual.UsesSvg);
        Assert.Equal(10, visual.Leds.Count);
        Assert.Equal(10, visual.Paths.Count);
        Assert.Equal(10, visual.Paths.Select(path => path.Id).Distinct().Count());
        Assert.Equal(4, visual.Leds.Count(led => led.MatrixRow == 0));
        Assert.Equal(2, visual.Leds.Count(led => led.MatrixRow == 1));
    }

    [Fact]
    public void WholeControllerImportMatchesAndKeepsRows()
    {
        var device = new StudioRegionDevice { ProductId = ArgbProductId, InstanceId = "0" };
        device.Leds.Add(new StudioLedRef(2, 7));
        var json = JsonWithDevice(device);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(ArgbProductId, "argb-test")]);

        var imported = Assert.Single(Assert.Single(Assert.Single(
            result.Project.Profiles[0].Items).Regions).Devices);
        Assert.Equal("argb-test", imported.InstanceId);
        Assert.Equal(2, Assert.Single(imported.Leds).Row);
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.RemappedLeds));
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
    }

    [Fact]
    public void LegacyStripInstanceMapsToItsChannelRow()
    {
        var device = new StudioRegionDevice { ProductId = ArgbProductId, InstanceId = "29" };
        device.Leds.Add(new StudioLedRef(40, 7));
        device.Leds.Add(new StudioLedRef(41, 8));
        var json = JsonWithDevice(device);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(ArgbProductId, "argb-test")]);

        var imported = Assert.Single(Assert.Single(Assert.Single(
            result.Project.Profiles[0].Items).Regions).Devices);
        Assert.All(imported.Leds, led => Assert.Equal(3, led.Row));
    }

    [Fact]
    public void GlobalRowsAreNotCollapsedForLegacyInstance()
    {
        var device = new StudioRegionDevice { ProductId = ArgbProductId, InstanceId = "27" };
        for (var row = 0; row < 6; row++) device.Leds.Add(new StudioLedRef(row, 0));

        var result = StudioChromaImport.Import(
            JsonWithDevice(device), [new StudioDeviceMatch(ArgbProductId, "argb-test")]);

        var imported = Assert.Single(Assert.Single(Assert.Single(
            result.Project.Profiles[0].Items).Regions).Devices);
        Assert.Equal([0, 1, 2, 3, 4, 5], imported.Leds.Select(led => led.Row));
    }

    [Fact]
    public void MissingArgbControllerUsesTheExistingIssueRules()
    {
        var device = new StudioRegionDevice { ProductId = ArgbProductId, InstanceId = "0" };
        device.Leds.Add(new StudioLedRef(1, 2));
        var json = JsonWithDevice(device);

        var result = StudioChromaImport.Import(json, [new StudioDeviceMatch(736, "kbd")]);

        Assert.Empty(Assert.Single(Assert.Single(
            result.Project.Profiles[0].Items).Regions).Devices);
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.MissingDevice));
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.DeviceDropped));
    }

    private static string JsonWithDevice(StudioRegionDevice device)
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        region.Devices.Add(device);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);
        return StudioProjectCodec.Serialize(project);
    }
}
