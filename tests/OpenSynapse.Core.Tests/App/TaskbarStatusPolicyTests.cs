using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class TaskbarStatusPolicyTests
{
    [Fact]
    public void NoBatteryHidesTheStrip()
    {
        Assert.False(TaskbarStatusPolicy.ShouldShow(new(null, null), enabled: true));
    }

    [Fact]
    public void DisabledSettingHidesTheStrip()
    {
        Assert.False(TaskbarStatusPolicy.ShouldShow(new(87, true), enabled: false));
    }

    [Fact]
    public void FormatsBatteryAndDescriptionWithoutHardCodedLanguage()
    {
        var snapshot = new TaskbarStatusSnapshot(87, true);

        Assert.True(TaskbarStatusPolicy.ShouldShow(snapshot, enabled: true));
        Assert.Equal("87%", TaskbarStatusPolicy.FormatBattery(snapshot.BatteryPercent));
        Assert.Equal(
            "87% · 充电中",
            TaskbarStatusPolicy.FormatDescription(snapshot, "充电中"));
        Assert.Equal(
            "87% · Charging",
            TaskbarStatusPolicy.FormatDescription(snapshot, "Charging"));
    }

    [Fact]
    public void DescriptionNamesThePeripheralBeforeItsPercentage()
    {
        var snapshot = new TaskbarStatusSnapshot(
            87, null, "\uE962", "Razer Basilisk V3 Pro");

        Assert.Equal(
            "Razer Basilisk V3 Pro · 87%",
            TaskbarStatusPolicy.FormatDescription(snapshot, string.Empty));
    }

    [Fact]
    public void ChargingTextAddsContextAfterTheBatteryPercentage()
    {
        var snapshot = new TaskbarStatusSnapshot(87, false);

        Assert.Equal("87% · 放电中", TaskbarStatusPolicy.FormatDescription(snapshot, "放电中"));
    }
}