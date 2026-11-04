using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioDeviceVisualTests
{
    private const string CatalogFolder = "artifacts/chroma-studio-2026-11-03/catalog/manifests";
    private const string FixtureManifest = "DeviceManifest_736_0.json";
    private const string SvgKey = "svg_0_1";

    private const string MinimalLedConfig = """
        {
          "ledConfig_0_1": {
            "MatrixMaxRow": 2,
            "MatrixMaxCol": 2,
            "DeviceMaxRow": 1,
            "DeviceMaxCol": 3,
            "MaxRegion": 1,
            "LedMatrix": [
              { "MatrixPos": [0, 0], "DevicePos": [0, 0] },
              { "MatrixPos": [0, 1], "DevicePos": [0, 1] },
              { "MatrixPos": [1, 1], "DevicePos": [0, 2] }
            ]
          }
        }
        """;

    [Fact]
    public void RealBladeManifestBuildsSvgVisual()
    {
        var manifestPath = Path.Combine(
            RepoLayout.Root,
            CatalogFolder.Replace('/', Path.DirectorySeparatorChar),
            FixtureManifest);
        if (!File.Exists(manifestPath)) return; // The catalog is a local artifact snapshot.

        var visual = StudioDeviceVisualBuilder.Build(ReadManifest(File.ReadAllText(manifestPath)), "blade-16");

        Assert.True(visual.UsesSvg);
        Assert.InRange(visual.Width, 954.9, 955.1);
        Assert.InRange(visual.Height, 589.9, 590.1);
        Assert.Equal(86, visual.Leds.Count);
        Assert.Equal("Razer Blade 16", visual.Name);
        Assert.NotNull(visual.Layout);
        Assert.Equal(7, visual.Layout.MatrixRows);
        Assert.Equal(16, visual.Layout.MatrixColumns);

        var escape = Assert.Single(visual.Leds, led => led.MatrixRow == 0 && led.MatrixCol == 0);
        Assert.Equal("keyEsc", escape.PathId);
        Assert.Equal("Esc", escape.KeyName);

        Assert.Contains(visual.Paths, item => item.Role == StudioSvgRole.Product);
        Assert.Contains(visual.Paths, item => item.Role == StudioSvgRole.Led);
        Assert.Contains(visual.Paths, item => item.Role == StudioSvgRole.Selection);
    }

    [Fact]
    public void EmptySvgBuildsLedGrid()
    {
        var visual = StudioDeviceVisualBuilder.Build(
            CreateManifest(string.Empty, MinimalLedConfig),
            "grid",
            "Fallback Grid");

        Assert.False(visual.UsesSvg);
        Assert.Equal("Fallback Grid", visual.Name);
        Assert.Equal(3, visual.Leds.Count);
        Assert.Equal(3, visual.Paths.Count);
        Assert.NotNull(visual.Layout);
        Assert.Equal(2, visual.Layout.MatrixRows);
        Assert.Equal(new[] { "led-0-0", "led-0-1", "led-1-1" }, visual.Paths.Select(item => item.Id));
        Assert.Equal("M 8,8 H 32 V 32 H 8 Z", visual.Paths[0].Data);
        Assert.All(visual.Paths, item =>
        {
            Assert.Equal(StudioSvgRole.Led, item.Role);
            Assert.Equal("#242424", item.Fill);
        });
        Assert.Equal(68d, visual.Width, 3);
        Assert.Equal(68d, visual.Height, 3);
    }

    [Fact]
    public void MalformedSvgFallsBackWithoutThrowing()
    {
        var svg = Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg><g></svg>"));
        var visual = StudioDeviceVisualBuilder.Build(CreateManifest(svg, MinimalLedConfig), "broken");

        Assert.False(visual.UsesSvg);
        Assert.Equal(3, visual.Paths.Count);
        Assert.Equal(3, visual.Leds.Count);
    }

    [Fact]
    public void MissingSvgAndLedConfigBuildsEmptyVisual()
    {
        var visual = StudioDeviceVisualBuilder.Build(CreateManifest(string.Empty, string.Empty), "empty");

        Assert.False(visual.UsesSvg);
        Assert.Equal("Device", visual.Name);
        Assert.Equal(100d, visual.Width);
        Assert.Equal(100d, visual.Height);
        Assert.Empty(visual.Paths);
        Assert.Empty(visual.Leds);
    }

    private static StudioManifestDevice ReadManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return CreateManifest(
            root.GetProperty("svg").GetProperty(SvgKey).GetString() ?? string.Empty,
            root.GetProperty("ledConfig").GetRawText(),
            root.TryGetProperty("productName", out var name) ? name.GetString() : null,
            root.GetProperty("productId").GetUInt16());
    }

    private static StudioManifestDevice CreateManifest(
        string svgBase64,
        string ledConfigJson,
        string? productName = null,
        ushort productId = 736) =>
        new(
            productId,
            0,
            productName,
            IsStudioSupported: true,
            SvgKey,
            svgBase64,
            ledConfigJson,
            ETag: null,
            LastModified: null);
}