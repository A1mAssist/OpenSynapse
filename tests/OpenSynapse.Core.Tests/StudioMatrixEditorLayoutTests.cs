using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioMatrixEditorLayoutTests
{
    [Fact]
    public void VisualGridKeepsGapsAndMapsBackToDeviceCoordinates()
    {
        var layout = StudioLedLayout.Create(
            matrixRows: 2,
            matrixColumns: 3,
            deviceRows: 1,
            deviceColumns: 5,
            leds:
            [
                new StudioLedMap(0, 0, 0, 0),
                new StudioLedMap(0, 2, 0, 2),
                new StudioLedMap(1, 1, 0, 4),
            ]);

        var cells = StudioMatrixEditorLayout.Build(layout);

        Assert.Equal(6, cells.Count);
        Assert.False(cells[0].IsPlaceholder);
        Assert.Equal((0, 0), (cells[0].DeviceRow, cells[0].DeviceCol));
        Assert.True(cells[1].IsPlaceholder);
        Assert.False(cells[2].IsPlaceholder);
        Assert.Equal((0, 2), (cells[2].DeviceRow, cells[2].DeviceCol));
        Assert.True(cells[3].IsPlaceholder);
        Assert.Equal((0, 4), (cells[4].DeviceRow, cells[4].DeviceCol));
        Assert.True(cells[5].IsPlaceholder);
    }

    [Fact]
    public void MissingVisualGridFallsBackToTheDeviceGrid()
    {
        var layout = StudioLedLayout.Create(0, 0, 2, 3, [new StudioLedMap(0, 0, 1, 2)]);

        var cells = StudioMatrixEditorLayout.Build(layout);

        Assert.Equal(6, cells.Count);
        Assert.All(cells, cell => Assert.False(cell.IsPlaceholder));
        Assert.Equal((1, 2), (cells[5].DeviceRow, cells[5].DeviceCol));
    }

    [Fact]
    public void OneVisualKeyKeepsEveryHardwareLedThatSharesItsMatrixPosition()
    {
        var layout = StudioLedLayout.Create(
            matrixRows: 1,
            matrixColumns: 1,
            deviceRows: 1,
            deviceColumns: 3,
            leds:
            [
                new StudioLedMap(0, 0, 0, 0),
                new StudioLedMap(0, 0, 0, 1),
                new StudioLedMap(0, 0, 0, 2),
            ]);

        var cell = Assert.Single(StudioMatrixEditorLayout.Build(layout));

        Assert.False(cell.IsPlaceholder);
        Assert.Equal([(0, 0), (0, 1), (0, 2)], cell.DevicePositions);
    }

    [Fact]
    public void UniformGridIsRowMajor()
    {
        var cells = StudioMatrixEditorLayout.BuildUniform(2, 2);

        Assert.Equal(
            [(0, 0), (0, 1), (1, 0), (1, 1)],
            cells.Select(cell => (cell.DeviceRow, cell.DeviceCol)));
    }
}
