namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Coalesces Core Audio mute notifications and serializes the Product 710 LED
/// status reports. The audio event source is intentionally kept outside this
/// class so a reconnect can replace it without changing HID ordering.
/// </summary>
public sealed class BladeAudioMuteSynchronizer : IAsyncDisposable
{
    private static readonly TimeSpan DeviceWait = TimeSpan.FromMilliseconds(5);
    private readonly IRazerFeatureSession _session;
    private readonly Dictionary<BladeAudioMuteTarget, bool> _pending = [];
    private readonly object _sync = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly List<TaskCompletionSource> _drainWaiters = [];
    private bool _signalPending;
    private bool _displayAvailable = true;
    private bool _disposed;
    private Exception? _drainError;
    private string? _lastError;

    public BladeAudioMuteSynchronizer(IRazerFeatureSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _worker = Task.Run(WorkerAsync);
    }

    public event Action<BladeAudioMuteState>? Synchronized;
    public event Action<Exception>? SynchronizationFailed;

    public string? LastError => Volatile.Read(ref _lastError);

    public bool Publish(BladeAudioMuteState state)
    {
        if (!Enum.IsDefined(state.Target))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }
            if (!_displayAvailable)
            {
                return false;
            }

            _pending[state.Target] = state.Muted;
            if (_signalPending)
            {
                return true;
            }

            _signalPending = true;
            _signal.Release();
            return true;
        }
    }

    public Task SetDisplayAvailableAsync(bool available)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _displayAvailable = available;
            if (available)
            {
                return Task.CompletedTask;
            }

            _pending.Clear();
            _pending[BladeAudioMuteTarget.Speaker] = false;
            _pending[BladeAudioMuteTarget.Microphone] = false;
            if (_drainWaiters.Count == 0)
            {
                _drainError = null;
            }
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _drainWaiters.Add(completion);
            if (!_signalPending)
            {
                _signalPending = true;
                _signal.Release();
            }
            return completion.Task;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var wakeWorker = false;
        TaskCompletionSource[] drainWaiters;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            drainWaiters = _drainWaiters.ToArray();
            _drainWaiters.Clear();
            if (!_signalPending)
            {
                _signalPending = true;
                wakeWorker = true;
            }
        }

        foreach (var waiter in drainWaiters)
        {
            waiter.TrySetCanceled();
        }

        _stop.Cancel();
        if (wakeWorker)
        {
            _signal.Release();
        }
        try
        {
            await _worker.ConfigureAwait(false);
        }
        finally
        {
            _stop.Dispose();
            _signal.Dispose();
        }
    }

    private async Task WorkerAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_stop.Token).ConfigureAwait(false);
                while (true)
                {
                    KeyValuePair<BladeAudioMuteTarget, bool>[] batch;
                    TaskCompletionSource[] drainWaiters;
                    Exception? drainError;
                    lock (_sync)
                    {
                        if (_pending.Count == 0)
                        {
                            _signalPending = false;
                            drainWaiters = _drainWaiters.ToArray();
                            _drainWaiters.Clear();
                            drainError = _drainError;
                            _drainError = null;
                            foreach (var waiter in drainWaiters)
                            {
                                if (drainError is null)
                                {
                                    waiter.TrySetResult();
                                }
                                else
                                {
                                    waiter.TrySetException(drainError);
                                }
                            }
                            break;
                        }

                        batch = _pending.ToArray();
                        _pending.Clear();
                    }

                    foreach (var (target, muted) in batch)
                    {
                        try
                        {
                            var state = await SendAsync(target, muted, _stop.Token).ConfigureAwait(false);
                            Volatile.Write(ref _lastError, null);
                            NotifySynchronized(state);
                        }
                        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                        {
                            return;
                        }
                        catch (Exception exception)
                        {
                            Volatile.Write(ref _lastError, exception.Message);
                            if (!muted)
                            {
                                lock (_sync)
                                {
                                    if (!_displayAvailable)
                                    {
                                        _drainError ??= exception;
                                    }
                                }
                            }
                            NotifySynchronizationFailed(exception);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task<BladeAudioMuteState> SendAsync(
        BladeAudioMuteTarget target,
        bool muted,
        CancellationToken cancellationToken)
    {
        var request = BladeSynapsePolicyProtocol.CreateSetAudioMuteStatusRequest(
            target,
            muted,
            _session.NextTransactionId());
        var response = await _session.QueryAsync(
            request[2],
            request[6],
            request[7],
            request[8],
            request.AsMemory(RazerFeatureReport.ArgumentsOffset, request[6]),
            DeviceWait,
            responseReportId: 0x02,
            cancellationToken).ConfigureAwait(false);
        return BladeSynapsePolicyProtocol.ParseAudioMuteCommandResult(response, request);
    }

    private void NotifySynchronized(BladeAudioMuteState state)
    {
        foreach (var handler in Synchronized?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<BladeAudioMuteState>)handler)(state);
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _lastError, exception.Message);
            }
        }
    }

    private void NotifySynchronizationFailed(Exception exception)
    {
        foreach (var handler in SynchronizationFailed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<Exception>)handler)(exception);
            }
            catch (Exception callbackException)
            {
                Volatile.Write(ref _lastError, callbackException.Message);
            }
        }
    }
}
