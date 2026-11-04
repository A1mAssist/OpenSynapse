using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioLedSelectionTests
{
    [Fact]
    public void SameCellOnDifferentInstancesIsNotEqual()
    {
        var first = new StudioLedRef(2, 3, InstanceId: "kbd-a");
        var second = new StudioLedRef(2, 3, InstanceId: "kbd-b");

        Assert.NotEqual(first, second);
        Assert.Equal(-1, StudioLedSelection.IndexOfCell([first], second));
        Assert.False(StudioLedSelection.SameSelection([first], [second]));
    }

    [Fact]
    public void CellForAttachesKeyboardInputIdentity()
    {
        var layout = StudioLedLayout.Parse("""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 1,
                "MatrixMaxCol": 1,
                "DeviceMaxRow": 1,
                "DeviceMaxCol": 1,
                "LedMatrix": [{ "MatrixPos": [0, 0], "DevicePos": [0, 0] }],
                "LedInputMap": [{ "InputType": "kbd", "InputData": [1, 0], "MatrixPos": [0, 0] }]
              }
            }
            """);
        var device = new StudioDeviceVisual(
            "kbd-a", 1, "Keyboard", 10, 10,
            [],
            [new StudioVisualLed(0, 0, null, null)],
            UsesSvg: false,
            Layout: layout);

        var cell = StudioLedSelection.CellFor(device, 0, 0);

        Assert.Equal("kbd", cell.InputType);
        Assert.Equal("[1,0]", cell.InputData);
        Assert.Equal("kbd-a", cell.InstanceId);
    }

    [Fact]
    public void CellForAttachesRegionIdentity()
    {
        var layout = StudioLedLayout.Parse("""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 1,
                "MatrixMaxCol": 2,
                "DeviceMaxRow": 1,
                "DeviceMaxCol": 2,
                "LedRegion": [{ "RegionId": 4, "RegionMatrix": [{ "MatrixPos": "0 1" }] }],
                "LedMatrix": [{ "MatrixPos": [0, 1], "DevicePos": [0, 1] }]
              }
            }
            """);
        var device = new StudioDeviceVisual(
            "mouse-a", 100, "Mouse", 10, 10,
            [],
            [new StudioVisualLed(0, 1, null, null)],
            UsesSvg: false,
            Layout: layout);

        var cell = StudioLedSelection.CellFor(device, 0, 1);

        Assert.Equal(4, cell.RegionId);
        Assert.Equal(0, cell.RegionIndex);
        Assert.Equal("mouse-a", cell.InstanceId);
    }

    [Fact]
    public void ToggleKeepsTheOtherInstanceSelected()
    {
        var first = new StudioLedRef(2, 3, InstanceId: "kbd-a");
        var second = new StudioLedRef(2, 3, InstanceId: "kbd-b");

        var next = StudioLedSelection.SelectCell([first, second], first, toggle: true);

        Assert.Equal(second, Assert.Single(next));
    }

    [Fact]
    public void DeviceCellsCarryTheInstance()
    {
        var device = new StudioDeviceVisual(
            "kbd-a", 1, "Keyboard", 10, 10,
            [],
            [new StudioVisualLed(0, 0, null, null), new StudioVisualLed(0, 1, null, null)],
            UsesSvg: false);

        var cells = StudioLedSelection.DeviceCells(device);

        Assert.Equal(2, cells.Count);
        Assert.All(cells, cell => Assert.Equal("kbd-a", cell.InstanceId));
    }

    [Fact]
    public void BucketSelectsOnlyTheClickedDeviceInsideARegion()
    {
        var first = Device("kbd-a");
        var region = new StudioRegion();
        region.Devices.Add(DeviceRegion("kbd-a", (0, 0), (0, 1)));
        region.Devices.Add(DeviceRegion("kbd-b", (0, 0), (0, 1)));

        var cells = StudioLedSelection.BucketCells([region], first, 0, 1);

        Assert.Equal(2, cells.Count);
        Assert.All(cells, cell => Assert.Equal("kbd-a", cell.InstanceId));
    }

    [Fact]
    public void BucketFallsBackToUncoveredCellsOfTheDevice()
    {
        var device = Device("kbd-a");
        var region = new StudioRegion();
        region.Devices.Add(DeviceRegion("kbd-a", (0, 0)));

        var cells = StudioLedSelection.BucketCells([region], device, 1, 1);

        Assert.Equal(3, cells.Count);
        Assert.DoesNotContain(cells, cell => cell.Row == 0 && cell.Col == 0);
    }

    [Fact]
    public void BucketWithoutRegionsSelectsTheWholeDevice()
    {
        var cells = StudioLedSelection.BucketCells([], Device("kbd-a"), 0, 0);

        Assert.Equal(4, cells.Count);
    }

    private static StudioDeviceVisual Device(string instanceId) => new(
        instanceId, 1, "Keyboard", 10, 10, [],
        [
            new StudioVisualLed(0, 0, null, null),
            new StudioVisualLed(0, 1, null, null),
            new StudioVisualLed(1, 0, null, null),
            new StudioVisualLed(1, 1, null, null),
        ],
        UsesSvg: false);

    private static StudioRegionDevice DeviceRegion(string instanceId, params (int Row, int Col)[] cells)
    {
        var device = new StudioRegionDevice { InstanceId = instanceId };
        foreach (var cell in cells)
        {
            device.Leds.Add(new StudioLedRef(cell.Row, cell.Col, InstanceId: instanceId));
        }

        return device;
    }
}
