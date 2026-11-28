namespace OpenSynapse.Windows.Devices;

public enum RazerSurfaceCalibrationSessionState
{
    Idle, Preparing, WaitingToStart, Starting, Sampling, Ending, Reading, Applying, Saving,
    Completed, AppliedUnsaved, Cancelling, Cancelled, TimedOut, Disconnected, Failed,
}

/// <summary>A single manual run. Waiting on user input never holds a device transaction.</summary>
public sealed class RazerSurfaceCalibrationSession : IAsyncDisposable
{
    private static readonly object ActiveGate = new();
    private static readonly HashSet<string> ActiveInstances = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ActiveControlPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AsyncLocal<string?> CurrentInstance = new();
    private readonly object _gate = new();
    private readonly IRazerSurfaceCalibrationClient _client;
    private readonly RazerSurfaceCalibrationIdentity _identity;
    private readonly IRazerSurfaceCalibrationInputSource _input;
    private readonly RazerSurfaceCalibrationStore _store;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _prepared = Signal();
    private readonly TaskCompletionSource _start = Signal();
    private readonly TaskCompletionSource _started = Signal();
    private readonly TaskCompletionSource _sampling = Signal();
    private readonly TaskCompletionSource _finish = Signal();
    private readonly TaskCompletionSource _ended = Signal();
    private readonly List<byte> _z = [];
    private readonly List<byte> _q = [];
    private Task? _run;
    private Task? _saveRetry;
    private IDisposable? _operationLease;
    private bool _disposed;
    private bool _applied;
    private RazerSurfaceCalibrationState? _appliedState;
    private int _speedNotificationPending;
    private RazerSurfaceCalibrationSessionState _cancelState = RazerSurfaceCalibrationSessionState.Cancelled;

    public RazerSurfaceCalibrationSession(IRazerSurfaceCalibrationClient client,
        RazerSurfaceCalibrationIdentity identity, IRazerSurfaceCalibrationInputSource input,
        RazerSurfaceCalibrationStore store, TimeProvider? timeProvider = null)
    {
        _client = client;
        _identity = identity;
        _input = input;
        _store = store;
        _time = timeProvider ?? TimeProvider.System;
    }

    public Guid SessionId { get; } = Guid.NewGuid();
    public string SurfaceName { get; set; } = "Custom surface";
    public RazerSurfaceCalibrationSessionState State { get; private set; }
    public RazerSurfaceCalibrationConfiguration? Candidate { get; private set; }
    public RazerSurfaceCalibrationRecord? SavedRecord { get; private set; }
    public RazerSurfaceCalibrationReadback Readback { get; private set; } = RazerSurfaceCalibrationReadback.Unavailable;
    public string? ReadbackDetail { get; private set; }
    public RazerSurfaceCalibrationEventKind? Speed { get; private set; }
    public Exception? Error { get; private set; }
    public Exception? CleanupError { get; private set; }
    public event Action<RazerSurfaceCalibrationSessionState>? StateChanged;

    public static bool IsActive(string instanceId)
    {
        lock (ActiveGate) return ActiveInstances.Contains(instanceId);
    }

    public static bool IsActivePath(string devicePath)
    {
        lock (ActiveGate)
            return ActiveControlPaths.Contains(devicePath) || RazerSurfaceCalibrationInputSource.IsActiveDevicePath(devicePath);
    }
    public static bool IsActiveDevicePath(string devicePath) => IsActivePath(devicePath);
    public static bool IsCurrentSession(string instanceId) => StringComparer.OrdinalIgnoreCase.Equals(CurrentInstance.Value, instanceId);

    public async Task PrepareAsync(RazerSurfaceCalibrationConfiguration candidate, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) throw new InvalidOperationException("A calibration session can only be prepared once.");
            if (candidate.Mode is not (RazerSurfaceCalibrationMode.Manual or RazerSurfaceCalibrationMode.Mini))
                throw new ArgumentException("A manual session requires manual or mini mode.", nameof(candidate));
            _operationLease = RazerSurfaceCalibrationOperations.TryAcquire(_identity)
                ?? throw new InvalidOperationException("Wait for this device's calibration operation to finish before starting a manual session.");
            lock (ActiveGate)
            {
                ActiveInstances.Add(_identity.InstanceId);
                if (_identity.ControlDevicePath is { } controlPath) ActiveControlPaths.Add(controlPath);
            }
            Candidate = candidate;
            _run = Task.Run(() => RunAsync(cancellationToken));
        }
        await _prepared.Task.ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (State != RazerSurfaceCalibrationSessionState.WaitingToStart && State != RazerSurfaceCalibrationSessionState.Starting) return;
            _start.TrySetResult();
        }
        using var registration = cancellationToken.Register(() => _stop.Cancel());
        await _sampling.Task.ConfigureAwait(false);
    }

    public Task FinishAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (State is RazerSurfaceCalibrationSessionState.Sampling or RazerSurfaceCalibrationSessionState.Ending)
                _finish.TrySetResult();
            else if (State is not (RazerSurfaceCalibrationSessionState.Reading or RazerSurfaceCalibrationSessionState.Applying or
                RazerSurfaceCalibrationSessionState.Saving)) return Task.CompletedTask;
            return WaitForRunAsync(cancellationToken);
        }
    }

    public Task CancelAsync() => CancelAsync(RazerSurfaceCalibrationSessionState.Cancelled);
    public Task NotifyDisconnectedAsync() => CancelAsync(RazerSurfaceCalibrationSessionState.Disconnected);

    private Task CancelAsync(RazerSurfaceCalibrationSessionState outcome)
    {
        lock (_gate)
        {
            if (_run is null || _run.IsCompleted) return _saveRetry ?? Task.CompletedTask;
            _cancelState = outcome;
            _stop.Cancel();
            return _run;
        }
    }

    public Task RetrySaveAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_saveRetry is { IsCompleted: false }) return _saveRetry;
            if (State != RazerSurfaceCalibrationSessionState.AppliedUnsaved || !_applied) return Task.CompletedTask;
            return _saveRetry = Task.Run(async () =>
            {
                var outcome = await SaveAsync(cancellationToken).ConfigureAwait(false);
                if (outcome == RazerSurfaceCalibrationSessionState.Completed) ReleaseOperation();
                SetState(outcome);
            });
        }
    }

    private async Task WaitForRunAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => _stop.Cancel());
        await _run!.ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        CurrentInstance.Value = _identity.InstanceId;
        using var registration = cancellationToken.Register(() => _stop.Cancel());
        RazerSurfaceCalibrationState? original = null;
        IDisposable? subscription = null;
        var prepared = false;
        var startSent = false;
        var stopSent = false;
        var outcome = RazerSurfaceCalibrationSessionState.Failed;
        try
        {
            SetState(RazerSurfaceCalibrationSessionState.Preparing);
            SavedRecord = _store.Load(_identity);
            original = await _client.ReadAsync(_stop.Token).ConfigureAwait(false);
            RazerSurfaceCalibrationStore.RequireRestorableSnapshot(_client.Profile, original);
            subscription = await _input.SubscribeAsync(SessionId, _client.Profile.Kind == RazerProximityCalibrationKind.Philips,
                OnInput, _stop.Token).ConfigureAwait(false);
            _stop.Token.ThrowIfCancellationRequested();
            prepared = true; // A partially successful Prepare also needs restoration.
            await _client.PrepareAsync(Candidate!, _stop.Token).ConfigureAwait(false);
            _stop.Token.ThrowIfCancellationRequested();
            SetState(RazerSurfaceCalibrationSessionState.WaitingToStart);
            _prepared.TrySetResult();
            await WaitAsync(_start.Task, TimeSpan.FromSeconds(35)).ConfigureAwait(false);
            SetState(RazerSurfaceCalibrationSessionState.Starting);
            startSent = true;
            await _client.StartAsync(Candidate!.Mode, _stop.Token).ConfigureAwait(false);
            await WaitAsync(_started.Task, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            SetState(RazerSurfaceCalibrationSessionState.Sampling);
            _sampling.TrySetResult();
            if (Candidate.Mode == RazerSurfaceCalibrationMode.Mini)
            {
                try { await WaitAsync(_finish.Task, TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (TimeoutException) { /* Philips mini collects for five seconds, then finishes. */ }
            }
            else await WaitAsync(_finish.Task, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            SetState(RazerSurfaceCalibrationSessionState.Ending);
            if (!_ended.Task.IsCompleted)
            {
                stopSent = true;
                await _client.StopAsync(Candidate.Mode, _stop.Token).ConfigureAwait(false);
                await WaitAsync(_ended.Task, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            }
            SetState(RazerSurfaceCalibrationSessionState.Reading);
            var raw = await _client.ReadResultAsync(Candidate.Mode, _stop.Token).ConfigureAwait(false);
            if (_client.Profile.Kind == RazerProximityCalibrationKind.Philips)
                lock (_gate) raw = raw with { Z = _z.ToArray(), Q = _q.ToArray() };
            Candidate = _client.CreateCandidate(Candidate, raw);
            _stop.Token.ThrowIfCancellationRequested();
            SetState(RazerSurfaceCalibrationSessionState.Applying);
            await _client.ApplyAsync(Candidate, _stop.Token).ConfigureAwait(false);
            _applied = true;
            try
            {
                var actual = await _client.ReadAsync(_stop.Token).ConfigureAwait(false);
                _appliedState = actual;
                var compared = _client.CompareReadback(Candidate, actual);
                if (compared == RazerSurfaceCalibrationReadback.Mismatch)
                    ReadbackDetail = RazerSurfaceCalibrationReadbackPolicy.DescribeReadback(Candidate, actual);
                Readback = RazerSurfaceCalibrationReadbackPolicy.NormalizeSampledReadback(compared, Candidate, actual);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Readback = RazerSurfaceCalibrationReadback.Unavailable;
                Error = exception;
            }
            if (Readback == RazerSurfaceCalibrationReadback.Mismatch)
            {
                _applied = false;
                throw new InvalidDataException("The applied calibration does not match the device readback.");
            }
            outcome = await SaveAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            outcome = _applied ? RazerSurfaceCalibrationSessionState.AppliedUnsaved : _cancelState;
        }
        catch (TimeoutException exception)
        {
            Error = exception;
            outcome = RazerSurfaceCalibrationSessionState.TimedOut;
        }
        catch (Exception exception)
        {
            Error = exception;
            outcome = _applied ? RazerSurfaceCalibrationSessionState.AppliedUnsaved : RazerSurfaceCalibrationSessionState.Failed;
        }
        finally
        {
            if (!_applied) SetState(RazerSurfaceCalibrationSessionState.Cancelling);
            try { subscription?.Dispose(); }
            catch (Exception exception) { CleanupError = exception; }
            // User cancellation must not cancel the commands which restore the temporary state.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5), _time);
            if (prepared && original is not null)
            {
                try
                {
                    if (startSent && !stopSent && !_ended.Task.IsCompleted)
                    {
                        try { await _client.StopAsync(Candidate!.Mode, cleanup.Token).WaitAsync(cleanup.Token).ConfigureAwait(false); }
                        catch (Exception exception) { CleanupError = exception; }
                    }
                    if (_applied) await _client.RestoreDeviceModeAsync(original, cleanup.Token).WaitAsync(cleanup.Token).ConfigureAwait(false);
                    else if (startSent) await _client.AbortAsync(original, cleanup.Token).WaitAsync(cleanup.Token).ConfigureAwait(false);
                    else await _client.RestoreAsync(original, cleanup.Token).WaitAsync(cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception exception) { CleanupError = exception; }
            }
            lock (ActiveGate)
            {
                ActiveInstances.Remove(_identity.InstanceId);
                if (_identity.ControlDevicePath is { } controlPath) ActiveControlPaths.Remove(controlPath);
            }
            CurrentInstance.Value = null;
            // Keep the applied candidate protected from restoring an older disk record.
            if (outcome != RazerSurfaceCalibrationSessionState.AppliedUnsaved) ReleaseOperation();
            SetState(outcome);
            _prepared.TrySetResult();
            _sampling.TrySetResult();
        }
    }

    private async Task<RazerSurfaceCalibrationSessionState> SaveAsync(CancellationToken cancellationToken)
    {
        SetState(RazerSurfaceCalibrationSessionState.Saving);
        try
        {
            var now = _time.GetUtcNow();
            var id = SessionId.ToString("N");
            var record = new RazerSurfaceCalibrationRecord(_identity, _client.Profile.Kind, Candidate!, now)
            {
                SelectedSurfaceId = id,
                AppliedState = _appliedState,
                CustomSurfaces = [new(id, string.IsNullOrWhiteSpace(SurfaceName) ? "Custom surface" : SurfaceName.Trim(), Candidate!, now)],
            };
            await Task.Run(() => _store.Save(record), cancellationToken).ConfigureAwait(false);
            SavedRecord = _store.Load(_identity);
            Error = null;
            return RazerSurfaceCalibrationSessionState.Completed;
        }
        catch (Exception exception)
        {
            Error = exception;
            return RazerSurfaceCalibrationSessionState.AppliedUnsaved;
        }
    }

    private Task WaitAsync(Task signal, TimeSpan timeout) => signal.WaitAsync(timeout, _time, _stop.Token);

    private void OnInput(RazerSurfaceCalibrationInput input)
    {
        lock (_gate)
        {
            if (input.SessionId != SessionId || !StringComparer.OrdinalIgnoreCase.Equals(input.InstanceId, _identity.InstanceId) ||
                _stop.IsCancellationRequested || _run?.IsCompleted == true) return;
            switch (input.Kind)
            {
                case RazerSurfaceCalibrationEventKind.Escape:
                    _ = _stop.CancelAsync();
                    break;
                case RazerSurfaceCalibrationEventKind.Disconnected:
                    _cancelState = RazerSurfaceCalibrationSessionState.Disconnected;
                    _ = _stop.CancelAsync();
                    break;
                case RazerSurfaceCalibrationEventKind.LeftButtonDown when State == RazerSurfaceCalibrationSessionState.WaitingToStart:
                    _start.TrySetResult();
                    break;
                case RazerSurfaceCalibrationEventKind.LeftButtonDown when State == RazerSurfaceCalibrationSessionState.Sampling:
                    _finish.TrySetResult();
                    break;
                case RazerSurfaceCalibrationEventKind.LeftButtonDownAny when State == RazerSurfaceCalibrationSessionState.Sampling:
                    _finish.TrySetResult();
                    break;
                case RazerSurfaceCalibrationEventKind.Confirm when State == RazerSurfaceCalibrationSessionState.Sampling:
                    _finish.TrySetResult();
                    break;
                case RazerSurfaceCalibrationEventKind.Started when State == RazerSurfaceCalibrationSessionState.Starting:
                    _started.TrySetResult();
                    break;
                case RazerSurfaceCalibrationEventKind.Ended when State is RazerSurfaceCalibrationSessionState.Sampling or RazerSurfaceCalibrationSessionState.Ending ||
                    State == RazerSurfaceCalibrationSessionState.Starting && _started.Task.IsCompleted:
                    _ended.TrySetResult();
                    _finish.TrySetResult();
                    break;
                case RazerSurfaceCalibrationEventKind.Halted or RazerSurfaceCalibrationEventKind.Reset
                    when State is RazerSurfaceCalibrationSessionState.Starting or RazerSurfaceCalibrationSessionState.Sampling or RazerSurfaceCalibrationSessionState.Ending:
                    Error = new IOException("The device aborted surface calibration.");
                    _cancelState = RazerSurfaceCalibrationSessionState.Failed;
                    _ = _stop.CancelAsync();
                    break;
                case RazerSurfaceCalibrationEventKind.NormalSpeed or RazerSurfaceCalibrationEventKind.TooFast
                    when State == RazerSurfaceCalibrationSessionState.Sampling:
                    if (Speed == input.Kind) break;
                    Speed = input.Kind;
                    if (Interlocked.Exchange(ref _speedNotificationPending, 1) == 0)
                        _ = Task.Run(() =>
                        {
                            Interlocked.Exchange(ref _speedNotificationPending, 0);
                            if (State == RazerSurfaceCalibrationSessionState.Sampling) PublishState(State);
                        });
                    break;
                case RazerSurfaceCalibrationEventKind.Sample when State == RazerSurfaceCalibrationSessionState.Sampling:
                    _z.Add(input.Z);
                    _q.Add(input.Q);
                    break;
            }
        }
        // UI observers are never invoked from the Raw Input callback.
    }

    private void SetState(RazerSurfaceCalibrationSessionState state)
    {
        lock (_gate) State = state;
        PublishState(state);
    }

    private void PublishState(RazerSurfaceCalibrationSessionState state)
    {
        if (StateChanged is not { } observers) return;
        var currentInstance = CurrentInstance.Value;
        CurrentInstance.Value = null;
        try
        {
            foreach (Action<RazerSurfaceCalibrationSessionState> observer in observers.GetInvocationList())
            {
                try { observer(state); }
                catch { /* An observer cannot prevent device cleanup. */ }
            }
        }
        finally { CurrentInstance.Value = currentInstance; }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        await CancelAsync().ConfigureAwait(false);
        _stop.Dispose();
        ReleaseOperation();
    }

    private void ReleaseOperation() => Interlocked.Exchange(ref _operationLease, null)?.Dispose();

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
