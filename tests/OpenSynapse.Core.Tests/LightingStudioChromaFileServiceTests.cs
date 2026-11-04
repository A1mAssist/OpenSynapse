using System.IO.Compression;
using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioChromaFileServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "opensynapse-chroma-file-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void ExportWritesChromaJsonAndCreatesParentDirectory()
    {
        var project = ProjectWithRegion();
        project.Name = "Night Desk";
        var path = Path.Combine(_directory, "nested", "studio.ChromaEffects");

        StudioChromaFileService.Export(project, path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("Night Desk", root.GetProperty("openSynapse").GetProperty("name").GetString());
        var profile = Assert.Single(root.GetProperty("profiles").EnumerateArray());
        Assert.Equal("Default", profile.GetProperty("title").GetString());
        var layer = Assert.Single(profile.GetProperty("layers").EnumerateArray());
        Assert.Equal("Wave", layer.GetProperty("title").GetString());
    }

    [Fact]
    public void ExportKeepsOnlySelectedProfiles()
    {
        var project = StudioProject.CreateDefault();
        var second = project.AddProfile("Second");
        var first = project.Profiles.First(profile => profile != second);
        var path = Path.Combine(_directory, "one.ChromaEffects");

        StudioChromaFileService.Export(project, path, [first.Id]);

        var imported = StudioChromaFileService.Import(path, Array.Empty<StudioDeviceMatch>());
        var profile = Assert.Single(imported.Project.Profiles);
        Assert.Equal(first.Title, profile.Title);
    }

    [Fact]
    public void ExportAppendsExtensionWhenMissing()
    {
        var project = ProjectWithRegion();
        var withoutExtension = Path.Combine(_directory, "studio");

        StudioChromaFileService.Export(project, withoutExtension);

        var appended = withoutExtension + StudioChromaFileService.Extension;
        Assert.True(File.Exists(appended));
        Assert.False(File.Exists(withoutExtension));
    }

    [Fact]
    public void ExportAcceptsExistingExtensionInAnyCase()
    {
        var project = ProjectWithRegion();
        var upper = Path.Combine(_directory, "studio.CHROMAEFFECTS");

        StudioChromaFileService.Export(project, upper);

        Assert.True(File.Exists(upper));
        Assert.False(File.Exists(upper + StudioChromaFileService.Extension));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void ExportOverwritesExistingFileWithoutLeavingTempFiles()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "studio.ChromaEffects");
        File.WriteAllText(path, "stale content that must not survive");
        var project = ProjectWithRegion();
        project.Name = "Replacement";

        StudioChromaFileService.Export(project, path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("Replacement", document.RootElement.GetProperty("openSynapse").GetProperty("name").GetString());
        Assert.Single(Directory.GetFiles(_directory, "*.ChromaEffects"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void ImportOfExportedFileKeepsTitlesAndMatchesConnectedDevice()
    {
        var project = ProjectWithRegion();
        project.Name = "Exported";
        var path = Path.Combine(_directory, "studio.ChromaEffects");
        StudioChromaFileService.Export(project, path);

        var result = StudioChromaFileService.Import(path, [new StudioDeviceMatch(736, "kbd-live")]);

        Assert.Equal("Exported", result.Project.Name);
        var profile = Assert.Single(result.Project.Profiles);
        Assert.Equal("Default", profile.Title);
        var layer = Assert.Single(profile.Items);
        Assert.Equal("Wave", layer.Title);
        var device = Assert.Single(Assert.Single(layer.Regions).Devices);
        Assert.Equal((ushort)736, device.ProductId);
        Assert.Equal("kbd-live", device.InstanceId);
        Assert.DoesNotContain(result.Issues, issue => issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
    }

    [Fact]
    public void ImportWithNoConnectedDevicesKeepsLayersAndDropsRegionDevices()
    {
        var path = Path.Combine(_directory, "studio.ChromaEffects");
        StudioChromaFileService.Export(ProjectWithRegion(), path);

        var result = StudioChromaFileService.Import(path, []);

        var layer = Assert.Single(Assert.Single(result.Project.Profiles).Items);
        Assert.Equal("Wave", layer.Title);
        Assert.Empty(Assert.Single(layer.Regions).Devices);
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.MissingDevice));
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.DeviceDropped));
    }

    [Fact]
    public void MissingFileThrowsFileNotFoundException()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "missing.ChromaEffects");

        Assert.Throws<FileNotFoundException>(() => StudioChromaFileService.Import(path, []));
    }

    [Fact]
    public void MalformedJsonThrowsInvalidDataException()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "broken.ChromaEffects");
        File.WriteAllText(path, "{ \"profiles\": [");

        Assert.Throws<InvalidDataException>(() => StudioChromaFileService.Import(path, []));
    }

    [Fact]
    public void NullOrEmptyPathIsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => StudioChromaFileService.Import(null!, []));
        Assert.Throws<ArgumentException>(() => StudioChromaFileService.Import("   ", []));
        Assert.Throws<ArgumentException>(() => StudioChromaFileService.Export(ProjectWithRegion(), ""));
    }

    [Fact]
    public void ImportDetectsLegacyZipWithChromaEffectsExtension()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "legacy.ChromaEffects");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("profile.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(LegacyXml);
        }

        var result = StudioChromaFileService.Import(path, []);

        var profile = Assert.Single(result.Project.Profiles);
        Assert.Equal("Legacy", profile.Title);
        Assert.Equal(StudioEffectKind.Solid, Assert.Single(profile.Items).Params.Effect);
    }

    [Fact]
    public void ImportDetectsLegacyXmlWithChromaEffectsExtension()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "legacy.ChromaEffects");
        File.WriteAllText(path, LegacyXml);

        var result = StudioChromaFileService.Import(path, []);

        var profile = Assert.Single(result.Project.Profiles);
        Assert.Equal("Legacy", profile.Title);
        Assert.Single(profile.Items);
    }

    [Fact]
    public void ImportRejectsUnsupportedBinaryWithClearError()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "broken.ChromaEffects");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0xFF, 0x00]);

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaFileService.Import(path, []));

        Assert.Contains("Unsupported", error.Message);
    }

    [Fact]
    public void ImportRejectsCorruptZipWithClearError()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "corrupt.ChromaEffects");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00]);

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaFileService.Import(path, []));

        Assert.Contains("archive", error.Message);
    }

    [Fact]
    public void ExportAndImportKeepTwoProfilesAndReconcileEveryPlacement()
    {
        var project = new StudioProject();
        foreach (var title in new[] { "First", "Second" })
        {
            var profile = new StudioProfile(title);
            AddWaveRegion(profile);
            project.Profiles.Add(profile);
        }

        var path = Path.Combine(_directory, "two.ChromaEffects");
        StudioChromaFileService.Export(project, path);

        var result = StudioChromaFileService.Import(path, [new StudioDeviceMatch(736, "kbd-live")]);

        Assert.Equal(2, result.Project.Profiles.Count);
        Assert.Equal(["First", "Second"], result.Project.Profiles.Select(profile => profile.Title));
        foreach (var profile in result.Project.Profiles)
        {
            Assert.Equal("kbd-live", Assert.Single(profile.Devices).InstanceId);
            var device = Assert.Single(Assert.Single(Assert.Single(profile.Items).Regions).Devices);
            Assert.Equal("kbd-live", device.InstanceId);
        }

        Assert.Equal(2, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.RemappedDevice));
        Assert.Equal(2, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.RemappedLeds));
        Assert.DoesNotContain(result.Issues,
            issue => issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
    }

    [Fact]
    public void ImportRejectsFutureOpenSynapseSchemaVersion()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "future.ChromaEffects");
        StudioChromaFileService.Export(ProjectWithRegion(), path);
        var current = File.ReadAllText(path);
        var future = current.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 3");
        Assert.NotEqual(current, future);
        File.WriteAllText(path, future);

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaFileService.Import(path, []));

        Assert.Contains("3", error.Message);
    }

    [Fact]
    public void ImportRejectsVersionTwoChromaDocument()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "version-two.ChromaEffects");
        File.WriteAllText(
            path,
            """{"version":2,"profiles":[{"id":"aaaaaaaaaaaa","title":"Future","devices":[],"layers":[]}]}""");

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaFileService.Import(path, []));

        Assert.Contains("version", error.Message);
    }

    [Fact]
    public void RoundTripDoesNotDuplicateDevicePlacements()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        profile.Devices.Add(new StudioDevicePlacement
        {
            ProductId = 736,
            InstanceId = "kbd-old",
            DeviceId = "KBD1",
        });
        project.Profiles.Add(profile);
        var path = Path.Combine(_directory, "placement.ChromaEffects");
        StudioChromaFileService.Export(project, path);

        var result = StudioChromaFileService.Import(path, [new StudioDeviceMatch(736, "kbd-live")]);

        var imported = Assert.Single(result.Project.Profiles);
        Assert.Equal("kbd-live", Assert.Single(imported.Devices).InstanceId);
        Assert.Equal(1, result.Issues.Count(issue => issue.Kind == StudioImportIssueKind.RemappedDevice));
        Assert.DoesNotContain(result.Issues,
            issue => issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
    }

    [Fact]
    public void ExportMarksExactlyOneDefaultAndKeepsSelection()
    {
        var project = new StudioProject();
        var first = new StudioProfile("First");
        var second = new StudioProfile("Second") { IsDefault = true };
        var third = new StudioProfile("Third");
        project.Profiles.Add(first);
        project.Profiles.Add(second);
        project.Profiles.Add(third);
        project.SelectedProfileId = third.Id;
        var path = Path.Combine(_directory, "selected.ChromaEffects");

        StudioChromaFileService.Export(project, path, [first.Id, third.Id]);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var profiles = document.RootElement.GetProperty("profiles").EnumerateArray().ToList();
        Assert.Equal(["First", "Third"], profiles.Select(profile => profile.GetProperty("title").GetString()));
        Assert.Single(profiles, profile =>
            profile.TryGetProperty("default", out var value) && value.GetBoolean());
        Assert.True(profiles[1].GetProperty("default").GetBoolean());
        Assert.Equal(
            third.Id,
            document.RootElement.GetProperty("openSynapse").GetProperty("selectedProfileId").GetString());
    }

    [Fact]
    public void ExportRejectsUnknownProfileSelection()
    {
        var project = ProjectWithRegion();
        var path = Path.Combine(_directory, "missing-profile.ChromaEffects");

        var error = Assert.Throws<ArgumentException>(
            () => StudioChromaFileService.Export(project, path, ["not-a-profile"]));

        Assert.Contains("profile", error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ImportRejectsLegacyXmlWithoutLightingEffects()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "empty.ChromaEffects");
        File.WriteAllText(path, "<root />");

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaFileService.Import(path, []));

        Assert.Contains("LightingEffects", error.Message);
    }

    [Fact]
    public void ImportRejectsLegacyZipWithOnlyUnreadableEntries()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "unreadable.ChromaEffects");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("thumbnail.bin");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not xml");
        }

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaFileService.Import(path, []));

        Assert.Contains("no readable", error.Message);
    }

    [Fact]
    public void ImportRejectsMalformedLegacyXml()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "malformed.ChromaEffects");
        File.WriteAllText(path, "<LightingEffects><Name>Broken");

        var error = Assert.Throws<InvalidDataException>(() => StudioChromaFileService.Import(path, []));

        Assert.Contains("not valid XML", error.Message);
    }

    [Fact]
    public void ArgbControllerRoundTripKeepsLegacyChannelRows()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("ARGB");
        profile.Devices.Add(new StudioDevicePlacement
        {
            Id = "device-3871-29",
            ProductId = StudioArgbDevice.ProductId,
            InstanceId = "0",
            DeviceId = "ARGB1",
            Layout = "0",
        });
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        var device = new StudioRegionDevice
        {
            Id = "device-3871-29",
            ProductId = StudioArgbDevice.ProductId,
            InstanceId = "29",
            DeviceId = "ARGB1",
        };
        device.Leds.Add(new StudioLedRef(40, 7));
        device.Leds.Add(new StudioLedRef(41, 8));
        region.Devices.Add(device);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);
        var path = Path.Combine(_directory, "argb.ChromaEffects");

        StudioChromaFileService.Export(project, path);
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var exported = document.RootElement.GetProperty("profiles")[0];
            Assert.Equal(0, exported.GetProperty("devices")[0].GetProperty("instanceId").GetInt32());
            Assert.Equal(
                29,
                exported.GetProperty("layers")[0].GetProperty("regions")[0]
                    .GetProperty("devices")[0].GetProperty("instanceId").GetInt32());
        }

        var result = StudioChromaFileService.Import(
            path, [new StudioDeviceMatch(StudioArgbDevice.ProductId, "argb-live")]);
        var imported = Assert.Single(result.Project.Profiles);
        Assert.Equal("argb-live", Assert.Single(imported.Devices).InstanceId);
        var importedDevice = Assert.Single(Assert.Single(Assert.Single(imported.Items).Regions).Devices);
        Assert.Equal("argb-live", importedDevice.InstanceId);
        Assert.Equal([3, 3], importedDevice.Leds.Select(led => led.Row));
        Assert.DoesNotContain(result.Issues,
            issue => issue.Kind is StudioImportIssueKind.MissingDevice or StudioImportIssueKind.DeviceDropped);
    }

    private static void AddWaveRegion(StudioProfile profile)
    {
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "kbd-old" };
        device.Leds.Add(new StudioLedRef(0, 1));
        region.Devices.Add(device);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
    }

    private const string LegacyXml =
        "<LightingEffects><Name>Legacy</Name><EffectLayers>" +
        "<EffectLayer><Effect>static</Effect><Visible>true</Visible></EffectLayer>" +
        "</EffectLayers></LightingEffects>";

    private static StudioProject ProjectWithRegion()
    {
        var project = new StudioProject();
        var profile = new StudioProfile("Default");
        var layer = StudioLayerItem.CreateLayer("Wave", StudioEffectKind.Wave);
        var region = new StudioRegion();
        var device = new StudioRegionDevice { ProductId = 736, InstanceId = "kbd-old" };
        device.Leds.Add(new StudioLedRef(0, 1, InputType: "0", InputData: "Esc"));
        region.Devices.Add(device);
        layer.Regions.Add(region);
        profile.Items.Add(layer);
        project.Profiles.Add(profile);
        return project;
    }
}
