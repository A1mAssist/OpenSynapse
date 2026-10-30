using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerCoolingPadCurveTests
{
    [Fact]
    public void TablesMatchTheBundle()
    {
        Assert.Equal(
            new[] { (30, 500), (40, 500), (75, 1000), (80, 1600), (90, 1600) },
            Gpu(RazerCoolingPadCurveMode.Quiet));
        Assert.Equal(
            new[] { (30, 1000), (80, 2200), (90, 2200) },
            Gpu(RazerCoolingPadCurveMode.Balanced));
        Assert.Equal(
            new[] { (30, 1000), (45, 2000), (70, 2700), (90, 2700) },
            Gpu(RazerCoolingPadCurveMode.Performance));
        Assert.Equal(
            new[] { (40, 500), (50, 500), (85, 1000), (90, 1600), (100, 1600) },
            Cpu(RazerCoolingPadCurveMode.Quiet));
        Assert.Equal(
            new[] { (40, 1000), (90, 2200), (100, 2200) },
            Cpu(RazerCoolingPadCurveMode.Balanced));
        Assert.Equal(
            new[] { (40, 1000), (55, 2000), (80, 2700), (100, 2700) },
            Cpu(RazerCoolingPadCurveMode.Performance));
    }

    [Fact]
    public void StepUsesTheNextStrictlyHigherBreakpoint()
    {
        var quiet = RazerCoolingPadCurve.GpuPoints(RazerCoolingPadCurveMode.Quiet);

        Assert.Equal(500, RazerCoolingPadCurve.Step(quiet, 29));
        Assert.Equal(500, RazerCoolingPadCurve.Step(quiet, 30));
        Assert.Equal(1000, RazerCoolingPadCurve.Step(quiet, 40));
        Assert.Equal(1600, RazerCoolingPadCurve.Step(quiet, 75));
    }

    [Fact]
    public void StepClampsAtOrAboveTheLastBreakpointInsteadOfStoppingTheFan()
    {
        var quiet = RazerCoolingPadCurve.GpuPoints(RazerCoolingPadCurveMode.Quiet);
        var performance = RazerCoolingPadCurve.GpuPoints(RazerCoolingPadCurveMode.Performance);

        Assert.Equal(1600, RazerCoolingPadCurve.Step(quiet, 90));
        Assert.Equal(1600, RazerCoolingPadCurve.Step(quiet, 105));
        Assert.Equal(2700, RazerCoolingPadCurve.Step(performance, 90));
        Assert.Equal(2700, RazerCoolingPadCurve.Step(performance, 120));
    }

    [Fact]
    public void CpuAndGpuUsesTheMaximumOfBothStepLookups()
    {
        // CPU quiet at 45 C -> 500; GPU quiet at 85 C -> 1600.
        Assert.Equal(
            1600,
            RazerCoolingPadCurve.TargetRpm(
                RazerCoolingPadCurveMode.Quiet,
                RazerCoolingPadTemperatureSource.CpuAndGpu,
                45,
                85));
    }

    [Fact]
    public void MissingTemperaturesDoNotFallBack()
    {
        Assert.Null(RazerCoolingPadCurve.TargetRpm(
            RazerCoolingPadCurveMode.Balanced,
            RazerCoolingPadTemperatureSource.Cpu,
            null,
            70));
        Assert.Null(RazerCoolingPadCurve.TargetRpm(
            RazerCoolingPadCurveMode.Balanced,
            RazerCoolingPadTemperatureSource.Gpu,
            70,
            null));
        Assert.Null(RazerCoolingPadCurve.TargetRpm(
            RazerCoolingPadCurveMode.Balanced,
            RazerCoolingPadTemperatureSource.CpuAndGpu,
            70,
            null));
    }

    [Fact]
    public void CpuSourceIgnoresTheGpuTemperature()
    {
        Assert.Equal(
            500,
            RazerCoolingPadCurve.TargetRpm(
                RazerCoolingPadCurveMode.Quiet,
                RazerCoolingPadTemperatureSource.Cpu,
                45,
                95));
    }

    private static (int, int)[] Gpu(RazerCoolingPadCurveMode mode) =>
        RazerCoolingPadCurve.GpuPoints(mode).ToArray().Select(point => (point.Celsius, point.Rpm)).ToArray();

    private static (int, int)[] Cpu(RazerCoolingPadCurveMode mode) =>
        RazerCoolingPadCurve.CpuPoints(mode).ToArray().Select(point => (point.Celsius, point.Rpm)).ToArray();
}
