using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeLightingLayoutTests
{
    [Theory]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 15, 0, 16)]
    [InlineData(5, 0, 5, 1)]
    [InlineData(5, 15, 5, 16)]
    [InlineData(6, 13, 5, 15)]
    public void MatchesOfficialProduct710Positions(
        int logicalRow,
        int logicalColumn,
        int deviceRow,
        int deviceColumn)
    {
        Assert.True(BladeLightingLayout.TryGetDevicePosition(
            logicalRow,
            logicalColumn,
            out var actualRow,
            out var actualColumn));
        Assert.Equal((deviceRow, deviceColumn), (actualRow, actualColumn));
    }

    [Fact]
    public void MapsExactlyEightySixPhysicalKeysAndLeavesDeviceColumnZeroEmpty()
    {
        var logical = Enumerable.Repeat(
            new RazerRgb(1, 2, 3),
            BladeLightingLayout.LogicalPixelCount).ToArray();

        var device = BladeLightingLayout.MapToDeviceFrame(logical);

        Assert.Equal(86, device.Count(color => color != default));
        for (var row = 0; row < BladeLightingLayout.DeviceRows; row++)
        {
            Assert.Equal(default, device[row * BladeLightingLayout.DeviceColumns]);
        }
    }

    [Theory]
    [InlineData(1, 13)]
    [InlineData(3, 12)]
    [InlineData(4, 1)]
    [InlineData(5, 4)]
    [InlineData(6, 12)]
    public void RejectsOfficialLogicalGaps(int row, int column) =>
        Assert.False(BladeLightingLayout.TryGetDevicePosition(row, column, out _, out _));
}
