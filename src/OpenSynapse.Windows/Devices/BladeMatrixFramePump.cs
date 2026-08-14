using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Sends only the newest complete 6 x 17 frame and restores a known persistent
/// effect after the software-lighting session ends or faults.
/// </summary>
public sealed class BladeMatrixFramePump : IAsyncDisposable
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(1);

    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly Func<CancellationToken, Task> _restorePersistentEffect;
    private readonly Channel<RazerRgb[]> _frames = Channel.CreateBounded<RazerRgb[]>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _firstFrameApplied =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _worker;
    private int _stopped;

    public BladeMatrixFramePump(
        IRazerFeatureTransport transport,
        string devicePath,
        Func<CancellationToken, Task> restorePersistentEffect)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(restorePersistentEffect);

        _transport = transport;
        _devicePath = devicePath;
        _restorePersistentEffect = restorePersistentEffect;
        _worker = RunAsync();
    }

    public Task Completion => _worker;
    public Task FirstFrameApplied => _firstFrameApplied.Task;

    public bool TryPublish(IReadOnlyList<RazerRgb> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Count != BladeLightingProtocol.Rows * BladeLightingProtocol.Columns)
        {
            throw new ArgumentException("Blade 灯光帧必须正好包含 6 x 17 个颜色。", nameof(frame));
        }

        return Volatile.Read(ref _stopped) == 0 && _frames.Writer.TryWrite(frame.ToArray());
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            _frames.Writer.TryComplete();
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
        var restoreRequired = false;
        try
        {
            while (await _frames.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                if (!_frames.Reader.TryRead(out var frame))
                {
                    continue;
                }

                while (_frames.Reader.TryRead(out var newerFrame))
                {
                    frame = newerFrame;
                }

                restoreRequired = true;
                await SendFrameAsync(frame, _stop.Token).ConfigureAwait(false);

                _firstFrameApplied.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Interlocked.Exchange(ref _stopped, 1);
        _frames.Writer.TryComplete(failure);

        if (!_firstFrameApplied.Task.IsCompleted)
        {
            if (failure is not null)
            {
                _firstFrameApplied.TrySetException(failure);
            }
            else
            {
                _firstFrameApplied.TrySetCanceled(_stop.Token);
            }
        }

        if (restoreRequired)
        {
            try
            {
                await _restorePersistentEffect(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private Task SendFrameAsync(IReadOnlyList<RazerRgb> frame, CancellationToken cancellationToken)
    {
        var requests = new byte[BladeLightingProtocol.Rows][];
        for (byte row = 0; row < BladeLightingProtocol.Rows; row++)
        {
            var offset = row * BladeLightingProtocol.Columns;
            requests[row] = BladeLightingProtocol.CreateMatrixRowRequest(
                (byte)(row + 1),
                row,
                0,
                frame.Skip(offset).Take(BladeLightingProtocol.Columns).ToArray());
        }

        return _transport.SendBatchAsync(_devicePath, requests, DeviceWait, cancellationToken);
    }
}
