using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanCurveSamplingTests
{
    [Fact]
    public void DefaultCurveIsValidAndQuieterWhenCool()
    {
        var curve = BladeFanCurve.CreateDefault();

        Assert.Equal(BladeFanCurveTemperatureMode.Both, curve.TemperatureMode);
        Assert.NotEmpty(curve.CpuPoints);
        Assert.Equal(curve.CpuPoints.Count, curve.GpuPoints.Count);

        // The whole point of the default: near-idle when cool, full speed before throttling.
        Assert.True(curve.Evaluate(40, 40).CpuRpm <= 2400);
        Assert.Equal(5000, curve.Evaluate(95, 95).CpuRpm);
        Assert.True(curve.Evaluate(70, 70).CpuRpm < curve.Evaluate(85, 85).CpuRpm);
    }

    [Theory]
    [InlineData(BladeFanCurveTemperatureMode.Cpu, true, false)]
    [InlineData(BladeFanCurveTemperatureMode.Gpu, false, true)]
    [InlineData(BladeFanCurveTemperatureMode.Both, true, true)]
    public async Task CurveRequestsOnlyRequiredTemperatures(
        BladeFanCurveTemperatureMode mode,
        bool cpuExpected,
        bool gpuExpected)
    {
        var requested = new TaskCompletionSource<(bool Cpu, bool Gpu)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new BladeFanControlSnapshot(default, BladeFanMode.Automatic, 0, 0);
        await using var runtime = new BladeFanCurveRuntime(
            (_, _) => ValueTask.FromResult(original),
            (_, _, _, _, _) => ValueTask.FromResult(original),
            (_, _, _, _, _) => ValueTask.CompletedTask,
            (cpu, gpu, _) =>
            {
                requested.TrySetResult((cpu, gpu));
                return ValueTask.FromResult<(double?, double?)>((60, 60));
            },
            TimeSpan.FromHours(1));
        var point = new BladeFanCurvePoint(60, 3000, 3000);
        var curve = new BladeFanCurve(mode, [point], [point]);

        await runtime.StartAsync([], curve);
        var actual = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal((cpuExpected, gpuExpected), actual);
        await runtime.StopAsync();
    }

    [Fact]
    public async Task FixedControlUsesTheFixedSetterAndRestoresBothZones()
    {
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Balanced, BladeFanMode.Manual, 3200, 3300);
        var writes = new List<(BladeFanMode Mode, int? CpuRpm, int? GpuRpm)>();
        await using var runtime = new BladeFanCurveRuntime(
            (_, _) => ValueTask.FromResult(original),
            (_, _, _, _, _) => throw new InvalidOperationException("The curve setter must not drive fixed fan control."),
            (_, mode, cpu, gpu, _) =>
            {
                writes.Add((mode, cpu, gpu));
                return ValueTask.CompletedTask;
            },
            (_, _, _) => ValueTask.FromResult<(double?, double?)>((null, null)),
            TimeSpan.FromHours(1));

        await runtime.StartFixedAsync([], BladeFanMode.Manual, 3000);
        Assert.Equal([(BladeFanMode.Manual, (int?)3000, (int?)3000)], writes);

        await runtime.StopAsync();
        Assert.Equal(
            [
                (BladeFanMode.Manual, (int?)3000, (int?)3000),
                (BladeFanMode.Manual, (int?)3200, (int?)3300),
            ],
            writes);
    }
}
