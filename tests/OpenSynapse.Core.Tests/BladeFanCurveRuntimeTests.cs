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

    [Fact]
    public async Task DisposeRestoresOriginalState()
    {
        var writes = new List<BladeFanControlSnapshot>();
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        var runtime = CreateRuntime(
            original,
            _ => ValueTask.FromResult(CreateSample(60, null)),
            writes,
            firstWrite,
            TimeSpan.FromHours(1));

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await runtime.DisposeAsync();

        Assert.Equal(original, writes[^1]);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu)));
    }

    [Fact]
    public async Task ConcurrentDisposeCallsWaitForTheSameRestore()
    {
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRestore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        var runtime = new BladeFanCurveRuntime(
            (_, _) => ValueTask.FromResult(original),
            async (_, mode, cpu, gpu, _) =>
            {
                if (mode == original.Mode)
                {
                    restoreStarted.TrySetResult();
                    await allowRestore.Task;
                    return original;
                }

                firstWrite.TrySetResult();
                return new BladeFanControlSnapshot(original.PerformanceMode, mode, cpu, gpu);
            },
            _ => ValueTask.FromResult(CreateSample(60, null)),
            TimeSpan.FromHours(1));

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var firstDispose = runtime.DisposeAsync().AsTask();
        await restoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondDispose = runtime.DisposeAsync().AsTask();

        Assert.False(firstDispose.IsCompleted);
        Assert.False(secondDispose.IsCompleted);
        allowRestore.TrySetResult();
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisconnectDuringTakeoverReportsOperationAndRestoreFailures()
    {
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        await using var runtime = new BladeFanCurveRuntime(
            (_, _) => ValueTask.FromResult(original),
            (_, _, _, _, _) => throw new InvalidOperationException("device disconnected"),
            _ => ValueTask.FromResult(CreateSample(60, null)),
            TimeSpan.FromHours(1));

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        var failure = await Assert.ThrowsAsync<AggregateException>(
            () => runtime.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        var stopFailure = await Assert.ThrowsAsync<AggregateException>(() => runtime.StopAsync());

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Contains("恢复失败", failure.Message);
        Assert.Equal(failure.Message, stopFailure.Message);
    }

    [Fact]
    public async Task CallerCancellationRestoresWithNonCancelableToken()
    {
        using var lifetime = new CancellationTokenSource();
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        var writes = 0;
        await using var runtime = new BladeFanCurveRuntime(
            (_, _) => ValueTask.FromResult(original),
            async (_, mode, _, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref writes) == 1)
                {
                    writeStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                Assert.Equal(original.Mode, mode);
                Assert.False(cancellationToken.CanBeCanceled);
                restored.TrySetResult();
                return original;
            },
            _ => ValueTask.FromResult(CreateSample(60, null)),
            TimeSpan.FromHours(1));

        await runtime.StartAsync(
            [], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu), lifetime.Token);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        lifetime.Cancel();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await runtime.StopAsync();

        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task TransientSensorLossAfterResumeIsTolerated()
    {
        var samples = new Queue<PerformanceSnapshot>([
            CreateSample(null, null),
            CreateSample(null, null),
            CreateSample(60, null),
        ]);
        var writes = new List<BladeFanControlSnapshot>();
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        await using var runtime = CreateRuntime(
            original,
            _ => ValueTask.FromResult(
                samples.TryDequeue(out var sample) ? sample : CreateSample(60, null)),
            writes,
            firstWrite,
            TimeSpan.FromMilliseconds(1));

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
    public async Task DisposeWaitsForRestoreAndConcurrentStopsRemainSafe()
    {
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRestore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        var writes = 0;
        var runtime = new BladeFanCurveRuntime(
            (_, _) => ValueTask.FromResult(original),
            async (_, mode, cpu, gpu, _) =>
            {
                if (Interlocked.Increment(ref writes) == 1)
                {
                    firstWrite.TrySetResult();
                }
                else
                {
                    Assert.Equal(original, new(original.PerformanceMode, mode, cpu, gpu));
                    restoreStarted.TrySetResult();
                    await allowRestore.Task;
                }
                return new(original.PerformanceMode, mode, cpu, gpu);
            },
            _ => ValueTask.FromResult(CreateSample(60, null)),
            TimeSpan.FromHours(1));

        await runtime.StartAsync([], BladeFanCurveTests.CreateCurve(BladeFanCurveTemperatureMode.Cpu));
        await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stop = runtime.StopAsync();
        var dispose = runtime.DisposeAsync().AsTask();
        await restoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(stop.IsCompleted);
        Assert.False(dispose.IsCompleted);
        allowRestore.TrySetResult();
        await Task.WhenAll(stop, dispose).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task FixedManualSpeedWritesRequestedTargetsAndRestoresOriginalState()
    {
        var writes = new List<BladeFanControlSnapshot>();
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        await using var runtime = CreateRuntime(
            original,
            _ => ValueTask.FromResult(CreateSample(60, null)),
            writes,
            null,
            TimeSpan.FromHours(1));

        await runtime.StartFixedAsync([], BladeFanMode.Manual, 4000);
        Assert.True(runtime.IsRunning);
        await runtime.StopAsync();

        Assert.Equal(
            [
                new(BladePerformanceMode.Custom, BladeFanMode.Manual, 4000, 4000),
                original,
            ],
            writes);
        Assert.False(runtime.IsRunning);
    }

    [Fact]
    public async Task FixedAutomaticModeUsesExistingTargetsAndRestoresOnCancellation()
    {
        using var lifetime = new CancellationTokenSource();
        var writes = new List<BladeFanControlSnapshot>();
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Manual, 3400, 3500);
        await using var runtime = CreateRuntime(
            original,
            _ => ValueTask.FromResult(CreateSample(60, null)),
            writes,
            null,
            TimeSpan.FromHours(1));

        await runtime.StartFixedAsync([], BladeFanMode.Automatic, null, lifetime.Token);
        lifetime.Cancel();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            [
                new(BladePerformanceMode.Custom, BladeFanMode.Automatic, 3400, 3500),
                original,
            ],
            writes);
    }

    [Fact]
    public async Task FixedModeRejectsMismatchedOrInvalidTargetBeforeDeviceIo()
    {
        var reads = 0;
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        await using var runtime = new BladeFanCurveRuntime(
            (_, _) =>
            {
                Interlocked.Increment(ref reads);
                return ValueTask.FromResult(original);
            },
            (_, _, _, _, _) => ValueTask.FromResult(original),
            _ => ValueTask.FromResult(CreateSample(60, null)),
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            runtime.StartFixedAsync([], BladeFanMode.Manual, null));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            runtime.StartFixedAsync([], BladeFanMode.Automatic, 3000));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runtime.StartFixedAsync([], BladeFanMode.Manual, 3050));

        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task FixedWriteFailureRestoresOriginalState()
    {
        var writes = new List<BladeFanControlSnapshot>();
        var original = new BladeFanControlSnapshot(
            BladePerformanceMode.Custom, BladeFanMode.Automatic, 3200, 3300);
        await using var runtime = new BladeFanCurveRuntime(
            (_, _) => ValueTask.FromResult(original),
            (_, mode, cpu, gpu, _) =>
            {
                var state = new BladeFanControlSnapshot(original.PerformanceMode, mode, cpu, gpu);
                writes.Add(state);
                if (writes.Count == 1)
                {
                    throw new InvalidOperationException("partial write");
                }
                return ValueTask.FromResult(state);
            },
            _ => ValueTask.FromResult(CreateSample(60, null)),
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.StartFixedAsync([], BladeFanMode.Manual, 4000));

        Assert.Equal(
            [
                new(BladePerformanceMode.Custom, BladeFanMode.Manual, 4000, 4000),
                original,
            ],
            writes);
        Assert.False(runtime.IsRunning);
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
