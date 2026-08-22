using OpenSynapse.Windows.Protocols;
using System.Threading.Channels;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Executes Product 710 host-side mapping actions and owns every synthetic key
/// that has actually been sent to Windows.
/// </summary>
public sealed class BladeMappingActionExecutor : IAsyncDisposable
{
    private readonly Action<IReadOnlyList<BladeMappingOutputEvent>> _sendKeyboard;
    private readonly Func<BladeMappingAction, CancellationToken, ValueTask> _leafExecutor;
    private readonly Func<int, CancellationToken, ValueTask> _delay;
    private readonly Action<IReadOnlyList<BladeMappingOutputEvent>>? _syntheticStateChanged;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<PendingLeafAction> _leafQueue =
        Channel.CreateUnbounded<PendingLeafAction>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly TaskCompletionSource _fault =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _leafConsumer;
    private readonly object _sync = new();
    private readonly Dictionary<InputOwner, OwnerState> _actionOwners = [];
    private readonly HashSet<OutputKey> _runtimeKeys = [];
    private readonly Dictionary<OutputKey, int> _physicalOwnerCounts = [];
    private readonly List<OutputKey> _physicalDownOrder = [];
    private readonly Dictionary<InputOwner, TurboRun> _turbos = [];
    private bool _stopping;
    private bool _disposed;

    public BladeMappingActionExecutor(
        WindowsKeyboardInputSink keyboardSink,
        Func<BladeMappingAction, CancellationToken, ValueTask> leafExecutor,
        Action<IReadOnlyList<BladeMappingOutputEvent>>? syntheticStateChanged = null)
        : this(
            (keyboardSink ?? throw new ArgumentNullException(nameof(keyboardSink))).Send,
            leafExecutor,
            static (milliseconds, cancellationToken) =>
                new ValueTask(Task.Delay(milliseconds, cancellationToken)),
            syntheticStateChanged)
    {
    }

    public Task Completion => _fault.Task;

    internal BladeMappingActionExecutor(
        Action<IReadOnlyList<BladeMappingOutputEvent>> sendKeyboard,
        Func<BladeMappingAction, CancellationToken, ValueTask> leafExecutor,
        Func<int, CancellationToken, ValueTask> delay,
        Action<IReadOnlyList<BladeMappingOutputEvent>>? syntheticStateChanged = null)
    {
        _sendKeyboard = sendKeyboard ?? throw new ArgumentNullException(nameof(sendKeyboard));
        _leafExecutor = leafExecutor ?? throw new ArgumentNullException(nameof(leafExecutor));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _syntheticStateChanged = syntheticStateChanged;
        _leafConsumer = ConsumeLeafActionsAsync();
    }

    /// <summary>
    /// Sends the already de-duplicated keyboard output returned by
    /// <see cref="BladeMappingInputRuntime"/>.
    /// </summary>
    public void SendRuntimeOutputs(IReadOnlyList<BladeMappingOutputEvent> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ThrowIfUnavailable();
        if (outputs.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            ThrowIfUnavailable();
            var runtimeKeys = new HashSet<OutputKey>(_runtimeKeys);
            var counts = new Dictionary<OutputKey, int>(_physicalOwnerCounts);
            var downOrder = new List<OutputKey>(_physicalDownOrder);
            var filtered = new List<BladeMappingOutputEvent>(outputs.Count);

            foreach (var output in outputs)
            {
                var key = new OutputKey(output.ScanCode, output.Extended);
                if (output.IsDown)
                {
                    if (!runtimeKeys.Add(key))
                    {
                        continue;
                    }

                    var count = counts.GetValueOrDefault(key);
                    counts[key] = count + 1;
                    if (count == 0)
                    {
                        filtered.Add(output);
                        downOrder.Add(key);
                    }
                }
                else
                {
                    if (!runtimeKeys.Remove(key))
                    {
                        continue;
                    }

                    var count = counts.GetValueOrDefault(key);
                    if (count <= 0)
                    {
                        throw new InvalidOperationException("Runtime synthetic-key ownership is inconsistent.");
                    }

                    if (count == 1)
                    {
                        counts.Remove(key);
                        downOrder.Remove(key);
                        filtered.Add(output);
                    }
                    else
                    {
                        counts[key] = count - 1;
                    }
                }
            }

            if (filtered.Count != 0)
            {
                _sendKeyboard(filtered);
            }

            Replace(_runtimeKeys, runtimeKeys);
            Replace(_physicalOwnerCounts, counts);
            _physicalDownOrder.Clear();
            _physicalDownOrder.AddRange(downOrder);
            if (filtered.Count != 0)
            {
                NotifySyntheticStateChanged();
            }
        }
    }

    public async Task ExecuteAsync(
        BladeMappingInputEvent input,
        BladeMappingAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfUnavailable();
        var owner = InputOwner.From(input);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetime.Token,
            cancellationToken);
        try
        {
            await ExecuteActionAsync(owner, action, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var cleanupError = TryReleaseOwner(owner);
            if (cleanupError is not null)
            {
                throw new AggregateException(exception, cleanupError);
            }

            throw;
        }
    }

    internal void QueueLeafAction(BladeMappingInputEvent input, BladeMappingAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action is not (BladeCommandMappingAction or
            BladeBacklightMappingAction or
            BladeAudioMappingAction))
        {
            throw new ArgumentException("Only direct leaf actions can be queued.", nameof(action));
        }

        ThrowIfUnavailable();
        if (!_leafQueue.Writer.TryWrite(new PendingLeafAction(input, action)))
        {
            throw new InvalidOperationException("Blade mapping leaf action queue is closed.");
        }
    }

    public async Task StopAsync()
    {
        List<TurboRun> runs;
        lock (_sync)
        {
            if (!_stopping)
            {
                _stopping = true;
                _leafQueue.Writer.TryComplete();
                _lifetime.Cancel();
            }

            runs = _turbos.Values.Distinct().ToList();
        }

        foreach (var run in runs)
        {
            run.Cancellation.Cancel();
        }

        var errors = new List<Exception>();
        try
        {
            await _leafConsumer.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }
        foreach (var run in runs)
        {
            try
            {
                await run.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
            finally
            {
                RemoveTurbo(run.Owner, run);
            }
        }

        var releaseError = TryReleaseAll();
        if (releaseError is not null)
        {
            errors.Add(releaseError);
        }

        if (errors.Count != 0)
        {
            throw new AggregateException("Blade mapping executor stop failed.", errors);
        }
    }

    private async Task ConsumeLeafActionsAsync()
    {
        try
        {
            await foreach (var pending in _leafQueue.Reader
                               .ReadAllAsync(_lifetime.Token)
                               .ConfigureAwait(false))
            {
                await ExecuteAsync(pending.Input, pending.Action, _lifetime.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _fault.TrySetException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _disposed = true;
            _lifetime.Dispose();
        }
    }

    private async ValueTask ExecuteActionAsync(
        InputOwner owner,
        BladeMappingAction action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (action)
        {
            case BladeDisabledMappingAction:
                return;
            case BladeKeyboardMappingAction keyboard:
                SendActionKey(owner, keyboard);
                return;
            case BladeDelayMappingAction delay:
                await _delay(delay.Milliseconds, cancellationToken).ConfigureAwait(false);
                return;
            case BladeMultiMappingAction multi:
                foreach (var child in multi.Actions)
                {
                    await ExecuteActionAsync(owner, child, cancellationToken).ConfigureAwait(false);
                }
                return;
            case BladeTurboMappingAction turbo:
                await ExecuteTurboAsync(owner, turbo, cancellationToken).ConfigureAwait(false);
                return;
            case BladeCommandMappingAction or BladeBacklightMappingAction or BladeAudioMappingAction:
                await _leafExecutor(action, cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new InvalidOperationException($"Unsupported mapping action: {action.GetType().Name}.");
        }
    }

    private async ValueTask ExecuteTurboAsync(
        InputOwner owner,
        BladeTurboMappingAction action,
        CancellationToken cancellationToken)
    {
        if (!action.IsDown)
        {
            await StopTurboAsync(owner, action.Id).ConfigureAwait(false);
            return;
        }

        var events = BladeProduct710TurboCatalog.Get(action.Id);
        await StopTurboAsync(owner, expectedId: null).ConfigureAwait(false);
        ThrowIfUnavailable();

        var linked = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetime.Token,
            cancellationToken);
        var run = new TurboRun(owner, action.Id, linked);
        lock (_sync)
        {
            ThrowIfUnavailable();
            _turbos.Add(owner, run);
        }

        run.Task = RunTurboAsync(run, action, events);
        if (action.Repeat is > 0)
        {
            await run.Task.ConfigureAwait(false);
        }
    }

    private async Task RunTurboAsync(
        TurboRun run,
        BladeTurboMappingAction action,
        IReadOnlyList<BladeMappingAction> events)
    {
        await Task.Yield();
        var repeat = action.Repeat ?? 0;
        var remaining = repeat;
        var delay = action.DelayMilliseconds ?? 1000;
        Exception? executionError = null;
        try
        {
            while (true)
            {
                run.Cancellation.Token.ThrowIfCancellationRequested();
                foreach (var child in events)
                {
                    await ExecuteActionAsync(run.Owner, child, run.Cancellation.Token)
                        .ConfigureAwait(false);
                }

                if (repeat != 0 && --remaining == 0)
                {
                    break;
                }

                await _delay(delay, run.Cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            executionError = exception;
        }

        var cleanupError = TryReleaseOwner(run.Owner);
        if (executionError is null)
        {
            RemoveTurbo(run.Owner, run);
        }
        if (executionError is not null && cleanupError is not null)
        {
            executionError = new AggregateException(executionError, cleanupError);
        }
        if (executionError is not null)
        {
            _fault.TrySetException(executionError);
            throw executionError;
        }
        if (cleanupError is not null)
        {
            throw cleanupError;
        }
    }

    private async Task StopTurboAsync(InputOwner owner, Guid? expectedId)
    {
        TurboRun? run;
        lock (_sync)
        {
            _turbos.TryGetValue(owner, out run);
        }

        if (run is null)
        {
            return;
        }
        if (expectedId.HasValue && run.Id != expectedId.Value)
        {
            throw new InvalidOperationException("Turbo release GUID does not match its physical input owner.");
        }

        run.Cancellation.Cancel();
        try
        {
            await run.Task.ConfigureAwait(false);
        }
        finally
        {
            RemoveTurbo(owner, run);
        }
    }

    private void SendActionKey(InputOwner owner, BladeKeyboardMappingAction action)
    {
        var key = new OutputKey(action.ScanCode, action.Extended);
        lock (_sync)
        {
            ThrowIfUnavailable();
            if (!_actionOwners.TryGetValue(owner, out var ownerState))
            {
                if (!action.IsDown)
                {
                    return;
                }
                ownerState = new OwnerState();
                _actionOwners.Add(owner, ownerState);
            }

            if (action.IsDown)
            {
                if (ownerState.Keys.Contains(key))
                {
                    return;
                }

                var count = _physicalOwnerCounts.GetValueOrDefault(key);
                if (count == 0)
                {
                    _sendKeyboard([new BladeMappingOutputEvent(key.ScanCode, true, key.Extended)]);
                    _physicalDownOrder.Add(key);
                }

                ownerState.Keys.Add(key);
                ownerState.DownOrder.Add(key);
                _physicalOwnerCounts[key] = count + 1;
                if (count == 0)
                {
                    NotifySyntheticStateChanged();
                }
                return;
            }

            if (!ownerState.Keys.Contains(key))
            {
                return;
            }

            var currentCount = _physicalOwnerCounts.GetValueOrDefault(key);
            if (currentCount <= 0)
            {
                throw new InvalidOperationException("Action synthetic-key ownership is inconsistent.");
            }
            if (currentCount == 1)
            {
                _sendKeyboard([new BladeMappingOutputEvent(key.ScanCode, false, key.Extended)]);
                _physicalOwnerCounts.Remove(key);
                _physicalDownOrder.Remove(key);
            }
            else
            {
                _physicalOwnerCounts[key] = currentCount - 1;
            }

            ownerState.Keys.Remove(key);
            ownerState.DownOrder.Remove(key);
            if (ownerState.Keys.Count == 0)
            {
                _actionOwners.Remove(owner);
            }
            if (currentCount == 1)
            {
                NotifySyntheticStateChanged();
            }
        }
    }

    private Exception? TryReleaseOwner(InputOwner owner)
    {
        try
        {
            lock (_sync)
            {
                if (!_actionOwners.TryGetValue(owner, out var state))
                {
                    return null;
                }

                var releases = new List<BladeMappingOutputEvent>();
                foreach (var key in state.DownOrder.AsEnumerable().Reverse())
                {
                    if (_physicalOwnerCounts.GetValueOrDefault(key) == 1)
                    {
                        releases.Add(new BladeMappingOutputEvent(key.ScanCode, false, key.Extended));
                    }
                }

                if (releases.Count != 0)
                {
                    _sendKeyboard(releases);
                }

                foreach (var key in state.Keys)
                {
                    var count = _physicalOwnerCounts.GetValueOrDefault(key);
                    if (count <= 1)
                    {
                        _physicalOwnerCounts.Remove(key);
                        _physicalDownOrder.Remove(key);
                    }
                    else
                    {
                        _physicalOwnerCounts[key] = count - 1;
                    }
                }
                _actionOwners.Remove(owner);
                if (releases.Count != 0)
                {
                    NotifySyntheticStateChanged();
                }
            }

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private Exception? TryReleaseAll()
    {
        try
        {
            lock (_sync)
            {
                var releases = _physicalDownOrder
                    .AsEnumerable()
                    .Reverse()
                    .Select(static key =>
                        new BladeMappingOutputEvent(key.ScanCode, false, key.Extended))
                    .ToArray();
                if (releases.Length != 0)
                {
                    _sendKeyboard(releases);
                }

                _actionOwners.Clear();
                _runtimeKeys.Clear();
                _physicalOwnerCounts.Clear();
                _physicalDownOrder.Clear();
                if (releases.Length != 0)
                {
                    NotifySyntheticStateChanged();
                }
            }

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void RemoveTurbo(InputOwner owner, TurboRun run)
    {
        lock (_sync)
        {
            if (_turbos.TryGetValue(owner, out var current) && ReferenceEquals(current, run))
            {
                _turbos.Remove(owner);
                run.Cancellation.Dispose();
            }
        }
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stopping)
        {
            throw new InvalidOperationException("Blade mapping executor is stopping.");
        }
    }

    private void NotifySyntheticStateChanged()
    {
        _syntheticStateChanged?.Invoke(
            _physicalDownOrder
                .Select(static key =>
                    new BladeMappingOutputEvent(key.ScanCode, true, key.Extended))
                .ToArray());
    }

    private static void Replace<T>(HashSet<T> target, HashSet<T> source)
    {
        target.Clear();
        target.UnionWith(source);
    }

    private static void Replace<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        Dictionary<TKey, TValue> source)
        where TKey : notnull
    {
        target.Clear();
        foreach (var pair in source)
        {
            target.Add(pair.Key, pair.Value);
        }
    }

    private readonly record struct InputOwner(
        BladeMappingInputKind Kind,
        int Code,
        bool Extended)
    {
        internal static InputOwner From(BladeMappingInputEvent input) =>
            new(input.Kind, input.Code, input.Extended);
    }

    private readonly record struct OutputKey(int ScanCode, bool Extended);

    private readonly record struct PendingLeafAction(
        BladeMappingInputEvent Input,
        BladeMappingAction Action);

    private sealed class OwnerState
    {
        internal HashSet<OutputKey> Keys { get; } = [];
        internal List<OutputKey> DownOrder { get; } = [];
    }

    private sealed class TurboRun(
        InputOwner owner,
        Guid id,
        CancellationTokenSource cancellation)
    {
        internal InputOwner Owner { get; } = owner;
        internal Guid Id { get; } = id;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal Task Task { get; set; } = Task.CompletedTask;
    }
}
