using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>Real third-party .ChromaEffects files, not synthetic ones.</summary>
public sealed class LightingStudioRealChromaFileTests
{
    public static TheoryData<string> Files => new()
    {
        "Matrix.ChromaEffects",
        "LightningStrike.ChromaEffects",
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void ImportReadsRealFileAndSurvivesExportRoundTrip(string name)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Chroma", name);
        Assert.True(File.Exists(source), $"missing fixture {source}");

        var imported = StudioChromaFileService.Import(source, Array.Empty<StudioDeviceMatch>());
        var profile = imported.Project.SelectedProfile;
        Assert.NotNull(profile);
        Assert.False(string.IsNullOrWhiteSpace(profile.Title));
        Assert.NotEmpty(profile.Items);
        Assert.Single(imported.Project.Profiles, candidate => candidate.IsDefault);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(profile.Devices).LedRefJson));

        var directory = Path.Combine(Path.GetTempPath(), "opensynapse-chroma-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, name);
            StudioChromaFileService.Export(imported.Project, path);

            var again = StudioChromaFileService.Import(path, Array.Empty<StudioDeviceMatch>());
            var roundTripped = Assert.Single(again.Project.Profiles);
            Assert.Equal(profile.Title, roundTripped.Title);
            Assert.Equal(profile.Items.Count, roundTripped.Items.Count);
            Assert.False(string.IsNullOrWhiteSpace(Assert.Single(roundTripped.Devices).LedRefJson));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ImportedRealFileRendersOnADeviceMatrix()
    {
        var source = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Chroma", "Matrix.ChromaEffects");
        var imported = StudioChromaFileService.Import(source, [new StudioDeviceMatch(664, "0")]);
        var profile = Assert.IsType<StudioProfile>(imported.Project.SelectedProfile);
        const int rows = 6;
        const int columns = 17;
        var layers = new List<StudioLayerRender>();
        foreach (var layer in profile.EnumerateLayers())
        {
            if (layer.IsGroup) continue;
            if (layer.Regions.Count == 0)
            {
                layers.Add(new StudioLayerRender(
                    layer.Params, null, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
                continue;
            }

            foreach (var region in layer.Regions)
            {
                var mask = StudioMaskBuilder.BuildRegion(region, 664, "0", rows, columns);
                layers.Add(new StudioLayerRender(
                    region.Params, mask, layer.Opacity, layer.Blend, layer.Visible, layer.Id));
            }
        }

        var frame = StudioCompositor.RenderLayers(layers, rows, columns, 1.0);

        Assert.Equal(rows * columns, frame.Length);
        Assert.Contains(frame, color => color != StudioRgb.Black);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void RealFileReconcilesItsProfileDeviceAndRegions(string name)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Chroma", name);

        var imported = StudioChromaFileService.Import(source, [new StudioDeviceMatch(664, "0")]);

        Assert.Empty(imported.Issues);
        var profile = Assert.Single(imported.Project.Profiles);
        Assert.Equal("0", Assert.Single(profile.Devices).InstanceId);
        foreach (var layer in profile.EnumerateLayers())
        foreach (var region in layer.Regions)
            Assert.Equal("0", Assert.Single(region.Devices).InstanceId);
    }
}
