using System.Text.Json;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioMatrixMappingTests
{
    private const string CatalogFolder = "artifacts/chroma-studio-2026-11-03/catalog/manifests";
    private static readonly Guid LayerId = Guid.Parse("d0f6b6a4-3f4b-4f5e-8f4d-0b7f2f7c9e41");

    [Fact]
    public async Task CompositesOnTheManifestMatrixThenMapsToTheHidFrame()
    {
        if (!File.Exists(ManifestPath(515))) return; // The catalog is a local artifact snapshot.
        var layout = LoadCatalogLayout(515);
        var dimensions = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!.MatrixDimensions!;

        Assert.Equal(7, layout.MatrixRows);
        Assert.Equal(23, layout.MatrixColumns);
        Assert.Equal(6, dimensions.Rows);
        Assert.Equal(22, dimensions.Columns);

        var source = new StudioDeviceFrameSource(
            layout.MatrixRows, layout.MatrixColumns,
            dimensions.Rows, dimensions.Columns, layout);
        Assert.Equal((0, 3), layout.ToDevice(0, 4));
        var mask = new byte[layout.MatrixRows * layout.MatrixColumns];
        mask[4] = StudioMaskBuilder.Full; // Matrix (0,4), which maps to HID (0,3).
        source.Update([Solid(mask)]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(dimensions.Rows * dimensions.Columns, frame.Count);
        Assert.Equal(new RazerRgb(0, 255, 0), frame[3]);
        Assert.Equal(default, frame[4]);
        Assert.Equal(1, frame.Count(color => color != default));
    }

    [Fact]
    public async Task RegionMaskUsesMatrixCoordinatesBeforeDeviceMapping()
    {
        if (!File.Exists(ManifestPath(515))) return;
        var layout = LoadCatalogLayout(515);
        var dimensions = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!.MatrixDimensions!;
        var region = StudioRegionFactory.FromLeds(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Solid,
                Primary = new StudioRgb(0, 255, 0),
            },
            515,
            "keyboard",
            [new StudioLedRef(0, 4)]);

        var mask = StudioMaskBuilder.BuildRegion(
            region, 515, "keyboard", layout.MatrixRows, layout.MatrixColumns);
        var source = new StudioDeviceFrameSource(
            layout.MatrixRows, layout.MatrixColumns,
            dimensions.Rows, dimensions.Columns, layout);
        source.Update([new StudioLayerRender(
            region.Params, mask, 100, StudioBlendMode.Normal, true, LayerId)]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(new RazerRgb(0, 255, 0), frame[3]);
        Assert.Equal(1, frame.Count(color => color != default));
    }

    [Fact]
    public async Task UnmappedDeviceCellsStayBlack()
    {
        var layout = StudioLedLayout.Parse("""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 2,
                "MatrixMaxCol": 2,
                "DeviceMaxRow": 2,
                "DeviceMaxCol": 2,
                "MaxRegion": 1,
                "LedMatrix": [
                  { "MatrixPos": [0, 0], "DevicePos": [1, 1] }
                ]
              }
            }
            """);
        var source = new StudioDeviceFrameSource(2, 2, 2, 2, layout);
        source.Update([Solid(null)]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(4, frame.Count);
        Assert.Equal(default, frame[0]);
        Assert.Equal(default, frame[1]);
        Assert.Equal(default, frame[2]);
        Assert.Equal(new RazerRgb(0, 255, 0), frame[3]);
    }

    [Fact]
    public async Task WithoutLayoutKeepsTheLegacyDirectFrame()
    {
        var source = new StudioDeviceFrameSource(2, 2, 2, 2, null);
        source.Update([Solid(null)]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(4, frame.Count);
        Assert.All(frame, color => Assert.Equal(new RazerRgb(0, 255, 0), color));
    }

    [Theory]
    [InlineData(515, 0x0203, 7, 23, 6, 22)]
    [InlineData(550, 0x0226, 11, 24, 9, 22)]
    [InlineData(647, 0x0287, 9, 26, 8, 23)]
    public void CatalogLayoutFitsTheHidDeviceFrame(
        int productId,
        int hidProductId,
        int matrixRows,
        int matrixColumns,
        int deviceRows,
        int deviceColumns)
    {
        if (!File.Exists(ManifestPath(productId))) return;
        var layout = LoadCatalogLayout(productId);
        var dimensions = RazerHidDeviceCatalog.BuiltIn
            .Find(RazerHidDeviceDefinition.VendorId, (ushort)hidProductId)!.MatrixDimensions!;

        Assert.Equal(matrixRows, layout.MatrixRows);
        Assert.Equal(matrixColumns, layout.MatrixColumns);
        Assert.Equal(deviceRows, dimensions.Rows);
        Assert.Equal(deviceColumns, dimensions.Columns);
        Assert.NotEmpty(layout.Leds);
        Assert.All(layout.Leds, led =>
        {
            Assert.InRange(led.MatrixRow, 0, matrixRows - 1);
            Assert.InRange(led.MatrixCol, 0, matrixColumns - 1);
            Assert.InRange(led.DeviceRow, 0, deviceRows - 1);
            Assert.InRange(led.DeviceCol, 0, deviceColumns - 1);
        });
    }

    private static string ManifestPath(int productId) =>
        Path.Combine(
            RepoLayout.Root,
            CatalogFolder.Replace('/', Path.DirectorySeparatorChar),
            $"DeviceManifest_{productId}_0.json");

    private static StudioLedLayout LoadCatalogLayout(int productId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath(productId)));
        return StudioLedLayout.Parse(document.RootElement.GetProperty("ledConfig").GetRawText());
    }

    private static StudioLayerRender Solid(byte[]? mask) =>
        new(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Solid,
                Primary = new StudioRgb(0, 255, 0),
            },
            mask,
            100,
            StudioBlendMode.Normal,
            true,
            LayerId);
}