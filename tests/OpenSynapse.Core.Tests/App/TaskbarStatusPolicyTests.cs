using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class TaskbarStatusPolicyTests
{
    [Fact]
    public void NoBatteryHidesTheStrip()
    {
        Assert.False(TaskbarStatusPolicy.ShouldShow(new(null, null, "性能"), enabled: true));
    }

    [Fact]
    public void DisabledSettingHidesTheStrip()
    {
        Assert.False(TaskbarStatusPolicy.ShouldShow(new(87, true, "性能"), enabled: false));
    }

    [Fact]
    public void FormatsBatteryAndTooltipWithoutHardCodedLanguage()
    {
        var snapshot = new TaskbarStatusSnapshot(87, true, "性能");

        Assert.True(TaskbarStatusPolicy.ShouldShow(snapshot, enabled: true));
        Assert.Equal("87%", TaskbarStatusPolicy.FormatBattery(snapshot.BatteryPercent));
        Assert.Equal(
            "87% · 充电中 · 性能",
            TaskbarStatusPolicy.FormatTooltip(snapshot, "充电中", snapshot.PerformanceMode));
        Assert.Equal(
            "87% · Charging · Performance",
            TaskbarStatusPolicy.FormatTooltip(snapshot, "Charging", "Performance"));
    }

    [Fact]
    public void TooltipNamesThePeripheralBeforeItsPercentage()
    {
        var snapshot = new TaskbarStatusSnapshot(
            87, null, "性能", "\uE962", "Razer Basilisk V3 Pro");

        Assert.Equal(
            "Razer Basilisk V3 Pro · 87% · 性能",
            TaskbarStatusPolicy.FormatTooltip(snapshot, string.Empty, snapshot.PerformanceMode));
    }

    [Fact]
    public void MissingPerformanceOmitsSegmentFromTooltip()
    {
        var snapshot = new TaskbarStatusSnapshot(87, false, string.Empty);

        Assert.Equal("87% · 放电中", TaskbarStatusPolicy.FormatTooltip(snapshot, "放电中", string.Empty));
    }

    [Fact]
    public void PerformanceSegmentDropsWhenWidthIsTight()
    {
        Assert.True(TaskbarStatusPolicy.ShowPerformanceSegment(180, 150));
        Assert.False(TaskbarStatusPolicy.ShowPerformanceSegment(120, 150));
        Assert.False(TaskbarStatusPolicy.ShowPerformanceSegment(180, 0));
    }
}
