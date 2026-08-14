using System.Diagnostics;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Produces one complete Blade matrix frame for a point in the lighting timeline.
/// Capture, audio, and keyboard adapters belong outside this interface.
/// </summary>
public interface ISoftwareLightingFrameSource
{
    ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
        TimeSpan elapsed,
        CancellationToken cancellationToken);
}

/// <summary>
/// Schedules software-rendered frames at a bounded cadence and hands them to the
/// latest-frame matrix pump. It deliberately does not own any capture or UI APIs.
/// </summary>
public sealed class SoftwareLightingRuntime : IAsyncDisposable
{
    private readonly BladeMatrixFramePump _pump;
    private readonly ISoftwareLightingFrameSource _source;
    private readonly TimeSpan _frameInterval;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> _delay;
    private readonly Func<long> _timestamp;
    private readonly ILightingInputAdapter? _inputAdapter;
    private readonly long _startedAt;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private int _stopped;

    public SoftwareLightingRuntime(
        BladeMatrixFramePump pump,
        ISoftwareLightingFrameSource source,
        TimeSpan frameInterval)
        : this(pump, source, frameInterval, null, DefaultDelay, Stopwatch.GetTimestamp)
    {
    }

    internal SoftwareLightingRuntime(
        BladeMatrixFramePump pump,
        ISoftwareLightingFrameSource source,
        TimeSpan frameInterval,
        ILightingInputAdapter inputAdapter)
        : this(pump, source, frameInterval, inputAdapter, DefaultDelay, Stopwatch.GetTimestamp)
    {
    }

    internal SoftwareLightingRuntime(
        BladeMatrixFramePump pump,
        ISoftwareLightingFrameSource source,
        TimeSpan frameInterval,
        ILightingInputAdapter? inputAdapter,
        Func<TimeSpan, CancellationToken, ValueTask> delay,
        Func<long> timestamp)
    {
        ArgumentNullException.ThrowIfNull(pump);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(timestamp);
        if (frameInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(frameInterval));
        }

        _pump = pump;
        _source = source;
        _frameInterval = frameInterval;
        _delay = delay;
        _timestamp = timestamp;
        _inputAdapter = inputAdapter;
        _startedAt = timestamp();
        _worker = RunAsync();
    }

    public Task Completion => _worker;

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            _stop.Cancel();
        }

        await _worker.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private async Task RunAsync()
    {
        Exception? failure = null;
        try
        {
            if (_inputAdapter is not null)
            {
                await _inputAdapter.StartAsync(_stop.Token).ConfigureAwait(false);
            }
            while (!_stop.IsCancellationRequested)
            {
                var elapsed = Stopwatch.GetElapsedTime(_startedAt, _timestamp());
                var frame = await _source.RenderAsync(elapsed, _stop.Token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(frame);

                if (!_pump.TryPublish(frame))
                {
                    // The pump may have stopped because its transport failed. Awaiting
                    // completion preserves that hardware error instead of hiding it.
                    await _pump.Completion.ConfigureAwait(false);
                    break;
                }

                await _delay(_frameInterval, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (_inputAdapter is not null)
            {
                try
                {
                    await _inputAdapter.StopAsync().ConfigureAwait(false);
                    await _inputAdapter.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failure = failure is null
                        ? exception
                        : new AggregateException(failure, exception);
                }
            }
            try
            {
                await _pump.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Preserve the input failure as the primary cause. The pump's
                // completion remains observable independently through its API.
                failure ??= exception;
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static ValueTask DefaultDelay(TimeSpan delay, CancellationToken cancellationToken) =>
        new(Task.Delay(delay, cancellationToken));
}
