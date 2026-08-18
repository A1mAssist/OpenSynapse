using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Sensors;

namespace OpenSynapse.Windows.Devices;

public sealed class BladeFanCurveRuntime : IAsyncDisposable
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);
    private const int MaximumConsecutiveMissingSamples = 3;
    private readonly Func<IReadOnlyList<DeviceDescriptor>, CancellationToken, ValueTask<BladeFanControlSnapshot>> _read;
    private readonly Func<IReadOnlyList<DeviceDescriptor>, BladeFanMode, int, int, CancellationToken, ValueTask<BladeFanControlSnapshot>> _set;
    private readonly Func<CancellationToken, ValueTask<PerformanceSnapshot>> _sample;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private Task? _disposeTask;
    private int _disposed;

    public BladeFanCurveRuntime(
        IRazerDeviceTelemetryReader reader,
        IPerformanceMonitor monitor)
        : this(
            RequireReader(reader).ReadBladeFanControlStateAsync,
            RequireReader(reader).SetBladeFanTargetsAsync,
            RequireMonitor(monitor).SampleAsync,
            DefaultInterval)
    {
    }

    internal BladeFanCurveRuntime(
        Func<IReadOnlyList<DeviceDescriptor>, CancellationToken, ValueTask<BladeFanControlSnapshot>> read,
        Func<IReadOnlyList<DeviceDescriptor>, BladeFanMode, int, int, CancellationToken, ValueTask<BladeFanControlSnapshot>> set,
        Func<CancellationToken, ValueTask<PerformanceSnapshot>> sample,
        TimeSpan interval)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _set = set ?? throw new ArgumentNullException(nameof(set));
        _sample = sample ?? throw new ArgumentNullException(nameof(sample));
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }
        _interval = interval;
    }

    public Task Completion => _worker ?? Task.CompletedTask;

    public bool IsRunning => Volatile.Read(ref _worker) is not null;

    public async Task StartFixedAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int? targetRpm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (mode == BladeFanMode.Manual)
        {
            if (targetRpm is null)
            {
                throw new ArgumentNullException(nameof(targetRpm));
            }
            BladeFanLimits.ValidateTargetRpm(targetRpm.Value);
        }
        else if (targetRpm is not null)
        {
            throw new ArgumentException("自动风扇模式不能指定固定转速。", nameof(targetRpm));
        }
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            EnsureStopped();

            var deviceSnapshot = devices.ToArray();
            var original = await _read(deviceSnapshot, cancellationToken).ConfigureAwait(false);
            var cpuTarget = targetRpm ?? original.CpuTargetRpm;
            var gpuTarget = targetRpm ?? original.GpuTargetRpm;
            try
            {
                await _set(
                    deviceSnapshot,
                    mode,
                    cpuTarget,
                    gpuTarget,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                try
                {
                    await _set(
                        deviceSnapshot,
                        original.Mode,
                        original.CpuTargetRpm,
                        original.GpuTargetRpm,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception restoreException)
                {
                    throw new AggregateException(
                        "固定风扇写入失败，且原状态恢复失败。",
                        exception,
                        restoreException);
                }

                throw;
            }

            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = HoldFixedAsync(deviceSnapshot, original, _stop.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanCurve curve,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(curve);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            EnsureStopped();

            var deviceSnapshot = devices.ToArray();
            var original = await _read(deviceSnapshot, cancellationToken).ConfigureAwait(false);
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = RunAsync(deviceSnapshot, curve, original, _stop.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        Task? worker;
        CancellationTokenSource? stop;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            worker = _worker;
            stop = _stop;
            stop?.Cancel();
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            if (worker is not null)
            {
                await worker.ConfigureAwait(false);
            }
        }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                    _stop = null;
                    stop?.Dispose();
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanCurve curve,
        BladeFanControlSnapshot original,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        BladeFanTargets? lastTargets = null;
        var takeoverAttempted = false;
        var missingSamples = 0;
        try
        {
            using var timer = new PeriodicTimer(_interval);
            do
            {
                var sample = await _sample(cancellationToken).ConfigureAwait(false);
                BladeFanTargets targets;
                try
                {
                    targets = curve.Evaluate(
                        sample.CpuTemperatureCelsius,
                        sample.GpuTemperatureCelsius);
                    missingSamples = 0;
                }
                catch (InvalidOperationException exception)
                {
                    missingSamples++;
                    if (missingSamples >= MaximumConsecutiveMissingSamples)
                    {
                        throw new InvalidOperationException(
                            $"连续 {missingSamples} 次无法读取风扇曲线所需温度，已停止并恢复原状态。",
                            exception);
                    }
                    continue;
                }

                if (targets != lastTargets)
                {
                    takeoverAttempted = true;
                    // Keep the target before awaiting the device write so a partial HID write
                    // still enters the restore path when the transport throws.
                    lastTargets = targets;
                    await _set(
                        devices,
                        BladeFanMode.Manual,
                        targets.CpuRpm,
                        targets.GpuRpm,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (takeoverAttempted)
        {
            try
            {
                await _set(
                    devices,
                    original.Mode,
                    original.CpuTargetRpm,
                    original.GpuTargetRpm,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception restoreException)
            {
                failure = failure is null
                    ? new InvalidOperationException("风扇曲线停止后无法恢复原状态。", restoreException)
                    : new AggregateException("风扇曲线运行失败，且原状态恢复失败。", failure, restoreException);
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private async Task HoldFixedAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanControlSnapshot original,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        try
        {
            await _set(
                devices,
                original.Mode,
                original.CpuTargetRpm,
                original.GpuTargetRpm,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("固定风扇停止后无法恢复原状态。", exception);
        }
    }

    private void EnsureStopped()
    {
        if (_worker is not null)
        {
            throw new InvalidOperationException("风扇控制已经启动。");
        }
    }

    private static IRazerDeviceTelemetryReader RequireReader(IRazerDeviceTelemetryReader? reader) =>
        reader ?? throw new ArgumentNullException(nameof(reader));

    private static IPerformanceMonitor RequireMonitor(IPerformanceMonitor? monitor) =>
        monitor ?? throw new ArgumentNullException(nameof(monitor));
}
