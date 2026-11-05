using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioCanvasPlacementTests
{
    [Theory]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]
    [InlineData(19, 0)]
    [InlineData(20, 40)]
    [InlineData(39, 40)]
    [InlineData(59, 40)]
    [InlineData(60, 80)]
    [InlineData(5000, 5000)]
    [InlineData(9999, 10000)]
    public void SnapRoundsToFortyPixelGrid(double value, double expected) =>
        Assert.Equal(expected, StudioCanvasPlacement.Snap(value, 0));

    [Theory]
    [InlineData(600, 9400)]
    [InlineData(30, 9960)]
    [InlineData(40, 9960)]
    [InlineData(10000, 0)]
    public void SnapKeepsDeviceBoxInsideCanvas(double extent, double expected) =>
        Assert.Equal(expected, StudioCanvasPlacement.Snap(9999, extent));

    [Theory]
    [InlineData(30, 9960)]
    [InlineData(600, 9400)]
    [InlineData(10000, 0)]
    public void MaxStartStaysOnGridAndInsideCanvas(double extent, double expected) =>
        Assert.Equal(expected, StudioCanvasPlacement.MaxStart(extent));

    [Fact]
    public void QuarterTurnSnapUsesTheRotatedExtent()
    {
        var (width, height) = StudioCanvasPlacement.VisualExtent(600, 200, 90);
        Assert.Equal(9800, StudioCanvasPlacement.Snap(9999, width));
        Assert.Equal(9400, StudioCanvasPlacement.Snap(9999, height));
    }

    [Fact]
    public void VisualExtentSwapsQuarterTurns()
    {
        Assert.Equal((600d, 200d), StudioCanvasPlacement.VisualExtent(600, 200, 0));
        Assert.Equal((200d, 600d), StudioCanvasPlacement.VisualExtent(600, 200, 90));
        Assert.Equal((600d, 200d), StudioCanvasPlacement.VisualExtent(600, 200, 180));
        Assert.Equal((200d, 600d), StudioCanvasPlacement.VisualExtent(600, 200, 270));
    }

    [Fact]
    public void DefaultRowCentersUnplacedDevices()
    {
        var single = StudioCanvasPlacement.CenterRow([(600, 200)], 120);
        Assert.Equal((4700d, 4900d), single[0]);

        var pair = StudioCanvasPlacement.CenterRow([(600, 200), (400, 100)], 120);
        Assert.Equal((4440d, 4900d), pair[0]);
        Assert.Equal((5160d, 4950d), pair[1]);
    }
}
