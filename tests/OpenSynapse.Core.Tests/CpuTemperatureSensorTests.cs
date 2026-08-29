using OpenSynapse.Windows.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class CpuTemperatureSensorTests
{
    [Fact]
    public void SelectTemperatureConvertsKelvinAndUsesHottestValidZone()
    {
        var selected = CpuHardwareMonitor.SelectTemperatureCelsius(
            [273.15, 333.15, 199, 501, double.NaN]);

        Assert.Equal(60, selected);
    }

    [Fact]
    public void SelectTemperatureReturnsNullWhenNoZoneIsInHardwareRange()
    {
        var selected = CpuHardwareMonitor.SelectTemperatureCelsius([0, 199, 500.1]);

        Assert.Null(selected);
    }
}
