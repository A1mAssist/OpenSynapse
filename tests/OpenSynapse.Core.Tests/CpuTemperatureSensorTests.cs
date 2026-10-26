using Hwinfo.SharedMemory;
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

    [Fact]
    public void SuppressesOnlyLowUnchangingAcpiTemperatureAfterTenSeconds()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(CpuHardwareMonitor.ShouldSuppressAcpiTemperature(
            28, 28, now.AddSeconds(-10), now));
        Assert.False(CpuHardwareMonitor.ShouldSuppressAcpiTemperature(
            28, 28, now.AddSeconds(-9), now));
        Assert.False(CpuHardwareMonitor.ShouldSuppressAcpiTemperature(
            31, 31, now.AddSeconds(-20), now));
        Assert.False(CpuHardwareMonitor.ShouldSuppressAcpiTemperature(
            28, 27, now.AddSeconds(-20), now));
    }

    [Fact]
    public void SelectFastestCoreClockUsesBaseFrequencyAndPerformancePercentage()
    {
        var selected = CpuHardwareMonitor.SelectFastestCoreClock(
            [new("0,0", 2000), new("0,1", 2000), new("0,2", 0)],
            [new("0,0", 120), new("0,1", 230), new("0,2", 500), new("0,3", 100)]);

        Assert.Equal(4600, selected);
    }

    [Fact]
    public void SelectFastestCoreClockReturnsNullWithoutPairedValidCounters()
    {
        var selected = CpuHardwareMonitor.SelectFastestCoreClock(
            [new("0,0", 0), new("0,1", 2000)],
            [new("0,0", 500), new("0,2", 230)]);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectCpuTemperaturePrefersHwinfoCpuPackageOverCoreAndGpuSensors()
    {
        var cpu = new Sensor(1, 0, "CPU [#0]: Intel Core Ultra 5 225H", "CPU [#0]");
        var gpu = new Sensor(2, 0, "GPU [#0]: Intel Arc 130T", "GPU [#0]");
        var selected = HwinfoSensorReader.SelectCpuTemperature(
        [
            new SensorReading(1, SensorType.Temp, "Core Max", "Core Max", "°C", 70, 0, 0, 0, cpu),
            new SensorReading(2, SensorType.Temp, "CPU Package", "CPU Package", "°C", 65, 0, 0, 0, cpu),
            new SensorReading(3, SensorType.Temp, "GPU Temperature", "GPU Temperature", "°C", 80, 0, 0, 0, gpu),
        ]);

        Assert.Equal(65, selected);
    }

    [Fact]
    public void SelectCpuTemperatureUsesCoreMaxWhenPackageIsMissing()
    {
        var cpu = new Sensor(1, 0, "CPU [#0]: Intel Core Ultra 5 225H", "CPU [#0]");
        var selected = HwinfoSensorReader.SelectCpuTemperature(
        [
            new SensorReading(1, SensorType.Temp, "Core 0", "Core 0", "°C", 60, 0, 0, 0, cpu),
            new SensorReading(2, SensorType.Temp, "Core Max", "Core Max", "°C", 72, 0, 0, 0, cpu),
        ]);

        Assert.Equal(72, selected);
    }

    [Fact]
    public void SelectGpuTemperaturePrefersStandardGpuReadingOverCpuAndHotSpot()
    {
        var cpu = new Sensor(1, 0, "CPU [#0]: Intel Core Ultra 5 225H", "CPU [#0]");
        var gpu = new Sensor(2, 0, "GPU [#0]: Intel Arc 130T", "GPU [#0]");
        var selected = HwinfoSensorReader.SelectGpuTemperature(
        [
            new SensorReading(1, SensorType.Temp, "CPU Package", "CPU Package", "°C", 65, 0, 0, 0, cpu),
            new SensorReading(2, SensorType.Temp, "GPU Hot Spot", "GPU Hot Spot", "°C", 84, 0, 0, 0, gpu),
            new SensorReading(3, SensorType.Temp, "GPU Temperature", "GPU Temperature", "°C", 61, 0, 0, 0, gpu),
        ], "Intel");

        Assert.Equal(61, selected);
    }

    [Fact]
    public void SelectGpuTemperaturePrefersTheAdapterWindowsIsDriving()
    {
        var intel = new Sensor(1, 0, "GPU [#0]: Intel Arc 130T", "GPU [#0]");
        var nvidia = new Sensor(2, 0, "GPU [#1]: NVIDIA GeForce RTX 4060", "GPU [#1]");
        var selected = HwinfoSensorReader.SelectGpuTemperature(
        [
            new SensorReading(1, SensorType.Temp, "GPU Temperature", "GPU Temperature", "°C", 58, 0, 0, 0, intel),
            new SensorReading(2, SensorType.Temp, "GPU Temperature", "GPU Temperature", "°C", 72, 0, 0, 0, nvidia),
        ], "NVIDIA");

        Assert.Equal(72, selected);
    }

    [Fact]
    public void SelectGpuTemperatureReturnsNullWithoutAGpuSensor()
    {
        var cpu = new Sensor(1, 0, "CPU [#0]: Intel Core Ultra 5 225H", "CPU [#0]");
        var selected = HwinfoSensorReader.SelectGpuTemperature(
        [
            new SensorReading(1, SensorType.Temp, "CPU Package", "CPU Package", "°C", 65, 0, 0, 0, cpu),
        ], "Intel");

        Assert.Null(selected);
    }
}
