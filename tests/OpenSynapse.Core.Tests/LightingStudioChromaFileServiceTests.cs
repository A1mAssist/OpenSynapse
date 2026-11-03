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
    public void MalformedJsonThrowsInvalidOperationException()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "broken.ChromaEffects");
        File.WriteAllText(path, "{ \"profiles\": [");

        Assert.Throws<InvalidOperationException>(() => StudioChromaFileService.Import(path, []));
    }

    [Fact]
    public void NullOrEmptyPathIsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => StudioChromaFileService.Import(null!, []));
        Assert.Throws<ArgumentException>(() => StudioChromaFileService.Import("   ", []));
        Assert.Throws<ArgumentException>(() => StudioChromaFileService.Export(ProjectWithRegion(), ""));
    }

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
