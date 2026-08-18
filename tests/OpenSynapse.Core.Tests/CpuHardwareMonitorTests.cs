using OpenSynapse.Windows.Sensors;
using Xunit.Abstractions;

namespace OpenSynapse.Core.Tests;

public sealed class CpuHardwareMonitorTests(ITestOutputHelper output)
{
    [Fact]
    public void SelectsHighestValidAcpiThermalZone()
    {
        var temperature = CpuHardwareMonitor.SelectTemperatureCelsius([318, 330, 0]);

        Assert.Equal(56.85, temperature!.Value, 2);
    }

    [Fact]
    public void SumsOnlyRaplPackagePower()
    {
        PdhSample[] samples =
        [
            new("rapl_package0_core0_core", 1640),
            new("rapl_package0_pkg", 17844.24),
            new("rapl_package1_pkg", 10000),
            new("_total", 0),
        ];

        var power = CpuHardwareMonitor.SelectPackagePowerWatts(samples);

        Assert.Equal(27.84424, power!.Value, 5);
    }

    [Fact]
    public void SelectsFastestValidCoreClock()
    {
        var clock = CpuHardwareMonitor.SelectFastestCoreClock([0u, 2400u, 5100u, 10001u]);

        Assert.Equal(5100, clock);
        Assert.Null(CpuHardwareMonitor.SelectFastestCoreClock([0u, 10001u]));
    }

    [Fact]
    public void NativeSamplingReturnsOnlyValidCpuMetrics()
    {
        using var monitor = new CpuHardwareMonitor();

        var sample = monitor.Read();

        output.WriteLine(
            $"CPU sensors: temp={sample.TemperatureCelsius?.ToString("0.0") ?? "--"} C, " +
            $"power={sample.PowerWatts?.ToString("0.0") ?? "--"} W, " +
            $"clock={sample.ClockMegahertz?.ToString() ?? "--"} MHz");

        AssertInRangeOrNull(sample.TemperatureCelsius, 1, 125);
        AssertInRangeOrNull(sample.PowerWatts, 0.1, 500);
        AssertInRangeOrNull(sample.ClockMegahertz, 1, 10000);
    }

    private static void AssertInRangeOrNull(double? value, double minimum, double maximum)
    {
        if (value is double actual)
        {
            Assert.InRange(actual, minimum, maximum);
        }
    }
}
