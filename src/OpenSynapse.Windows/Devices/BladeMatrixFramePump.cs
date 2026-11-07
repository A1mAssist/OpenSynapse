using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Sends only the newest complete declared-matrix frame and restores a known persistent
/// effect after the software-lighting session ends or faults.
/// </summary>
public sealed class BladeMatrixFramePump : IAsyncDisposable
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(1);
    // The EC drops a custom frame after its idle window. A static Studio frame
    // therefore still needs a periodic re-latch even when its pixels do not change.
    private static readonly TimeSpan IdenticalFrameKeepAlive = TimeSpan.FromSeconds(1);

    private readonly IRazerFeatureTransport _transport;
    private readonly string _devicePath;
    private readonly Func<CancellationToken, Task> _restorePersistentEffect;
    private readonly Func<IReadOnlyList<RazerRgb>, int, int, byte[][]> _createFrameRequests;
    private readonly int _matrixRows;
    private readonly int _matrixColumns;
    private readonly TimeSpan _identicalFrameKeepAlive;
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
    private RazerRgb[]? _lastSentFrame;
    private long _lastFrameSentAt;
    private int _stopped;
    private int _turnOffOnStop;
    private long _framesSent;
    private long _framesSkipped;

    public BladeMatrixFramePump(
        IRazerFeatureTransport transport,
        string devicePath,
        Func<CancellationToken, Task> restorePersistentEffect,
        int matrixRows = BladeLightingProtocol.Rows,
        int matrixColumns = BladeLightingProtocol.Columns,
        Func<IReadOnlyList<RazerRgb>, int, int, byte[][]>? createFrameRequests = null,
        TimeSpan? identicalFrameKeepAlive = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentNullException.ThrowIfNull(restorePersistentEffect);
        if (matrixRows is < 1 or > 6 || matrixColumns is < 1 or > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(matrixRows));
        }

        _transport = transport;
        _devicePath = devicePath;
        _restorePersistentEffect = restorePersistentEffect;
        _createFrameRequests = createFrameRequests ?? BladeLightingProtocol.CreateMatrixFrameRequests;
        _matrixRows = matrixRows;
        _matrixColumns = matrixColumns;
        _identicalFrameKeepAlive = identicalFrameKeepAlive ?? IdenticalFrameKeepAlive;
        if (_identicalFrameKeepAlive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(identicalFrameKeepAlive));
        }
        _worker = RunAsync();
    }

    public Task Completion => _worker;
    public Task FirstFrameApplied => _firstFrameApplied.Task;
    public long FramesSent => Interlocked.Read(ref _framesSent);
    public long FramesSkipped => Interlocked.Read(ref _framesSkipped);

    public bool TryPublish(IReadOnlyList<RazerRgb> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        // Studio renders the manifest matrix directly for non-710 dialects; legacy
        // 710-family sources still publish the canonical 6 x 17 frame and scale.
        if (frame.Count != _matrixRows * _matrixColumns &&
            frame.Count != BladeLightingProtocol.Rows * BladeLightingProtocol.Columns)
        {
            throw new ArgumentException(
                $"A Blade lighting frame must contain exactly {_matrixRows} x {_matrixColumns} colors.",
                nameof(frame));
        }

        return Volatile.Read(ref _stopped) == 0 &&
            Volatile.Read(ref _turnOffOnStop) == 0 &&
            _frames.Writer.TryWrite(frame.ToArray());
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

    internal void MarkTurnOffOnStop()
    {
        Interlocked.Exchange(ref _turnOffOnStop, 1);
        _stop.Cancel();
        while (_frames.Reader.TryRead(out _))
        {
        }
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
        IRazerFeatureSession? session = null;
        try
        {
            session = await TryOpenSessionAsync(_stop.Token).ConfigureAwait(false);
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

                if (Volatile.Read(ref _turnOffOnStop) != 0)
                {
                    break;
                }

                if (_lastSentFrame is not null &&
                    _lastSentFrame.AsSpan().SequenceEqual(frame) &&
                    Stopwatch.GetElapsedTime(_lastFrameSentAt) < _identicalFrameKeepAlive)
                {
                    Interlocked.Increment(ref _framesSkipped);
                    continue;
                }

                restoreRequired = true;
                await SendFrameAsync(frame, session, _stop.Token).ConfigureAwait(false);
                _lastSentFrame = frame;
                _lastFrameSentAt = Stopwatch.GetTimestamp();
                Interlocked.Increment(ref _framesSent);

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

        if (Volatile.Read(ref _turnOffOnStop) != 0 && restoreRequired)
        {
            try
            {
                await SendFrameAsync(
                    new RazerRgb[BladeLightingProtocol.Rows * BladeLightingProtocol.Columns],
                    session,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
        }

        if (session is not null)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
        }

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

    private async Task SendFrameAsync(
        IReadOnlyList<RazerRgb> frame,
        IRazerFeatureSession? session,
        CancellationToken cancellationToken)
    {
        var requests = _createFrameRequests(
            ScaleFrame(frame),
            _matrixRows,
            _matrixColumns);
        // Every row write is answered by the EC. Sending the batch without reading
        // those replies leaves them queued, and the next telemetry query decodes the
        // stale 03:0B frame instead of its own response. Consume each ack before the
        // next row so the single feature-report queue stays in step.
        if (session is null)
        {
            foreach (var request in requests)
            {
                await QueryAsync(_transport, _devicePath, request, cancellationToken).ConfigureAwait(false);
                if (DeviceWait > TimeSpan.Zero)
                {
                    await Task.Delay(DeviceWait, cancellationToken).ConfigureAwait(false);
                }
            }
            return;
        }

        foreach (var request in requests)
        {
            await QueryAsync(session, request, cancellationToken).ConfigureAwait(false);
            if (DeviceWait > TimeSpan.Zero)
            {
                await Task.Delay(DeviceWait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task<byte[]> QueryAsync(
        IRazerFeatureTransport transport,
        string devicePath,
        byte[] request,
        CancellationToken cancellationToken) =>
        transport.QueryAsync(
            devicePath,
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            DeviceWait,
            cancellationToken,
            allowRemainingPacketsMismatch: true);

    private static Task<byte[]> QueryAsync(
        IRazerFeatureSession session,
        byte[] request,
        CancellationToken cancellationToken) =>
        session.QueryAsync(
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            DeviceWait,
            responseReportId: request[0],
            cancellationToken,
            allowRemainingPacketsMismatch: true);

    private RazerRgb[] ScaleFrame(IReadOnlyList<RazerRgb> frame) =>
        ScaleFrame(frame, _matrixRows, _matrixColumns);

    internal static RazerRgb[] ScaleFrame(
        IReadOnlyList<RazerRgb> frame,
        int matrixRows,
        int matrixColumns)
    {
        if (frame.Count == matrixRows * matrixColumns ||
            (matrixRows == BladeLightingProtocol.Rows &&
             matrixColumns == BladeLightingProtocol.Columns))
        {
            return frame.ToArray();
        }

        var scaled = new RazerRgb[matrixRows * matrixColumns];
        for (var row = 0; row < matrixRows; row++)
        {
            var sourceRow = row * BladeLightingProtocol.Rows / matrixRows;
            for (var column = 0; column < matrixColumns; column++)
            {
                var sourceColumn = column * BladeLightingProtocol.Columns / matrixColumns;
                scaled[row * matrixColumns + column] =
                    frame[sourceRow * BladeLightingProtocol.Columns + sourceColumn];
            }
        }
        return scaled;
    }

    private async Task<IRazerFeatureSession?> TryOpenSessionAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _transport.OpenSessionAsync(_devicePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
