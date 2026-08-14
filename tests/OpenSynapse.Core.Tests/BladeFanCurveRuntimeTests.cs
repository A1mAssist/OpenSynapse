using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanCurveRuntimeTests
{
    [Fact]
    public async Task AppliesIndependentTargetsAndRestoresCompleteAutomaticSnapshot()
    {
        var writes = new List<BladeFanControlSnapshot>();
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);

        await using var runtime = CreateRuntime(
            original,
            _ => ValueTask.FromResult(CreateSample(60, null)),
            writes,
            firstWrite,
            TimeSpan.FromHours(1));

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await runtime.StopAsync();

        Assert.Equal(
            [
                new(BladePerformanceMode.Custom, BladeFanMode.Manual, 2100, 1900),
                original,
            ],
            writes);
    }

    [Fact]
    public async Task SensorLossAfterTakeoverFailsAndRestoresOriginalState()
    {
        var samples = new Queue<PerformanceSnapshot>([
            CreateSample(60, null),
            CreateSample(null, null),
            CreateSample(null, null),
            CreateSample(null, null),
        ]);
        var writes = new List<BladeFanControlSnapshot>();
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Manual, 3400, 3500);
        await using var runtime = CreateRuntime(
            original,
            _ => ValueTask.FromResult(samples.Dequeue()),
            writes,
            null,
            TimeSpan.FromMilliseconds(1));

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StopAsync());

        Assert.Contains("连续 3 次", failure.Message);
        Assert.Equal(
            [
                new(BladePerformanceMode.Custom, BladeFanMode.Manual, 2100, 1900),
                original,
            ],
            writes);
        Assert.True(runtime.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CpuModeDoesNotRequireSleepingGpuTemperature()
    {
        var writes = new List<BladeFanControlSnapshot>();
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3200);
        await using var runtime = CreateRuntime(
            original,
            _ => ValueTask.FromResult(CreateSample(60, null)),
            writes,
            firstWrite,
            TimeSpan.FromHours(1));

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await runtime.StopAsync();

        Assert.Equal(BladeFanMode.Manual, writes[0].Mode);
    }

    [Fact]
    public async Task RestoresAfterInitialWriteThrowsAndCanStartAgain()
    {
        var writes = new List<BladeFanControlSnapshot>();
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        var attempts = 0;
        await using var runtime = new BladeFanCurveRuntime(
            (_, _) => ValueTask.FromResult(original),
            (_, mode, cpu, gpu, _) =>
            {
                writes.Add(new(original.PerformanceMode, mode, cpu, gpu));
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("partial write");
                }
                return ValueTask.FromResult(new BladeFanControlSnapshot(
                    original.PerformanceMode, mode, cpu, gpu));
            },
            _ => ValueTask.FromResult(CreateSample(60, null)),
            TimeSpan.FromHours(1));

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StopAsync());

        Assert.Equal(original, writes[^1]);
        Assert.True(runtime.Completion.IsCompleted);

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        await runtime.StopAsync();
        Assert.Equal(original, writes[^1]);
    }

    private static BladeFanCurveRuntime CreateRuntime(
        BladeFanControlSnapshot original,
        Func<CancellationToken, ValueTask<PerformanceSnapshot>> sample,
        List<BladeFanControlSnapshot> writes,
        TaskCompletionSource? firstWrite,
        TimeSpan interval) => new(
            (_, _) => ValueTask.FromResult(original),
            (_, mode, cpu, gpu, _) =>
            {
                var state = new BladeFanControlSnapshot(
                    original.PerformanceMode, mode, cpu, gpu);
                writes.Add(state);
                firstWrite?.TrySetResult();
                return ValueTask.FromResult(state);
            },
            sample,
            interval);

    private static PerformanceSnapshot CreateSample(double? cpuTemperature, double? gpuTemperature) => new(
        "CPU", null, cpuTemperature, null, null,
        "GPU", null, gpuTemperature, null, null, null, null,
        null, null, null, null, DateTimeOffset.UtcNow);
}
