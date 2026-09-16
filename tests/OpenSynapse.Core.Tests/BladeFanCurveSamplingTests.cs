using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanCurveSamplingTests
{
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
}
