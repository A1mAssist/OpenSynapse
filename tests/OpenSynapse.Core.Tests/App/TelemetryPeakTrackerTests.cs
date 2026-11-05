using OpenSynapse.Core.Sensors;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class TelemetryPeakTrackerTests
{
    [Fact]
    public void PeaksKeepBothEndsOfTheRange()
    {
        var tracker = new TelemetryPeakTracker();

        tracker.Apply(Snapshot(cpuTemperature: 59.3, cpuPower: 11.4, gpuTemperature: 45.8, gpuPower: 21.1));
        tracker.Apply(Snapshot(cpuTemperature: 61.0, cpuPower: 10.2, gpuTemperature: 44.0, gpuPower: 30.3));
        tracker.Apply(Snapshot(cpuTemperature: 40.0, cpuPower: 9.0, gpuTemperature: 50.3, gpuPower: 12.0));

        Assert.Equal(40.0, tracker.CpuTemperatureLowCelsius);
        Assert.Equal(61.0, tracker.CpuTemperaturePeakCelsius);
        Assert.Equal(9.0, tracker.CpuPowerLowWatts);
        Assert.Equal(11.4, tracker.CpuPowerPeakWatts);
        Assert.Equal(44.0, tracker.GpuTemperatureLowCelsius);
        Assert.Equal(50.3, tracker.GpuTemperaturePeakCelsius);
        Assert.Equal(12.0, tracker.GpuPowerLowWatts);
        Assert.Equal(30.3, tracker.GpuPowerPeakWatts);
    }

    [Fact]
    public void ClockKeepsBothEndsOfTheRange()
    {
        var tracker = new TelemetryPeakTracker();

        tracker.Apply(Snapshot(cpuClock: 3300, gpuClock: 1600));
        tracker.Apply(Snapshot(cpuClock: 5000, gpuClock: 900));
        tracker.Apply(Snapshot(cpuClock: 2000, gpuClock: 2000));

        Assert.Equal(2000, tracker.CpuClockLowMegahertz);
        Assert.Equal(5000, tracker.CpuClockPeakMegahertz);
        Assert.Equal(900, tracker.GpuClockLowMegahertz);
        Assert.Equal(2000, tracker.GpuClockPeakMegahertz);
    }

    [Fact]
    public void UnreadableSamplesAreNotRememberedAsReadings()
    {
        var tracker = new TelemetryPeakTracker();

        tracker.Apply(Snapshot(cpuTemperature: 61.0, cpuPower: 11.4, cpuClock: 3300));
        tracker.Apply(Snapshot(cpuTemperature: null, cpuPower: null, cpuClock: null));

        Assert.Equal(61.0, tracker.CpuTemperatureLowCelsius);
        Assert.Equal(61.0, tracker.CpuTemperaturePeakCelsius);
        Assert.Equal(11.4, tracker.CpuPowerPeakWatts);
        Assert.Equal(3300, tracker.CpuClockPeakMegahertz);
    }

    [Fact]
    public void AParkedCoreReportingZeroMegahertzIsIgnored()
    {
        var tracker = new TelemetryPeakTracker();

        tracker.Apply(Snapshot(cpuClock: 3300, gpuClock: 1600));
        tracker.Apply(Snapshot(cpuClock: 0, gpuClock: 0));

        Assert.Equal(3300, tracker.CpuClockLowMegahertz);
        Assert.Equal(1600, tracker.GpuClockLowMegahertz);
    }

    [Fact]
    public void EachAdapterKeepsItsOwnPeaks()
    {
        var tracker = new TelemetryPeakTracker();

        tracker.Apply(Snapshot(gpuName: "NVIDIA GeForce RTX 5070 Laptop GPU", gpuTemperature: 50.3, gpuPower: 30.3, gpuClock: 1600));
        tracker.Apply(Snapshot(gpuName: "AMD Radeon(TM) 880M Graphics", gpuTemperature: 41.0, gpuPower: 8.0, gpuClock: 900));

        Assert.Equal(41.0, tracker.GpuTemperatureLowCelsius);
        Assert.Equal(41.0, tracker.GpuTemperaturePeakCelsius);
        Assert.Equal(8.0, tracker.GpuPowerPeakWatts);
        Assert.Equal(900, tracker.GpuClockLowMegahertz);
    }

    [Fact]
    public void ReturningToAnAdapterRestoresItsEarlierPeaks()
    {
        var tracker = new TelemetryPeakTracker();

        tracker.Apply(Snapshot(gpuName: "NVIDIA GeForce RTX 5070 Laptop GPU", gpuTemperature: 50.3, gpuPower: 30.3, gpuClock: 1600));
        tracker.Apply(Snapshot(gpuName: "AMD Radeon(TM) 880M Graphics", gpuTemperature: 41.0, gpuPower: 8.0, gpuClock: 900));
        tracker.Apply(Snapshot(gpuName: "NVIDIA GeForce RTX 5070 Laptop GPU", gpuTemperature: 44.0, gpuPower: 20.0, gpuClock: 1200));

        Assert.Equal(44.0, tracker.GpuTemperatureLowCelsius);
        Assert.Equal(50.3, tracker.GpuTemperaturePeakCelsius);
        Assert.Equal(20.0, tracker.GpuPowerLowWatts);
        Assert.Equal(30.3, tracker.GpuPowerPeakWatts);
        Assert.Equal(1200, tracker.GpuClockLowMegahertz);
        Assert.Equal(1600, tracker.GpuClockPeakMegahertz);
    }

    private static PerformanceSnapshot Snapshot(
        double? cpuTemperature = null,
        double? cpuPower = null,
        int? cpuClock = null,
        string gpuName = "GPU",
        double? gpuTemperature = null,
        double? gpuPower = null,
        int? gpuClock = null) =>
        new(
            "CPU",
            CpuUsagePercent: 15,
            CpuTemperatureCelsius: cpuTemperature,
            CpuPowerWatts: cpuPower,
            CpuClockMegahertz: cpuClock,
            GpuName: gpuName,
            GpuUsagePercent: 8,
            GpuTemperatureCelsius: gpuTemperature,
            GpuPowerWatts: gpuPower,
            GpuClockMegahertz: gpuClock,
            GpuMemoryUsedMebibytes: null,
            GpuMemoryTotalMebibytes: null,
            MemoryUsedBytes: null,
            MemoryTotalBytes: null,
            StorageUsedBytes: null,
            StorageTotalBytes: null,
            CapturedAt: DateTimeOffset.UnixEpoch);
}
