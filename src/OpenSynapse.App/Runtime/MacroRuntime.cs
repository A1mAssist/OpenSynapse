using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.Runtime;

internal sealed class MacroRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly WindowsMacroInputSink _sink = new();
    private IReadOnlyList<MacroDefinition> _macros = [];
    private MacroInputHook? _hook;
    private MacroRecordingSession? _recording;
    private TaskCompletionSource<MacroTrigger?>? _triggerCapture;
    private TaskCompletionSource<MacroInput?>? _stepCapture;
    private MacroInput? _suppressedInput;
    private Func<bool>? _isOwnWindowForeground;
    private CancellationTokenSource? _playbackCancellation;
    private bool _disposed;

    public event Action<string>? Error;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _recording is not null;
            }
        }
    }

    public bool IsCapturing
    {
        get
        {
            lock (_gate)
            {
                return _triggerCapture is not null || _stepCapture is not null;
            }
        }
    }

    public void SetOwnWindowForegroundPredicate(Func<bool>? predicate)
    {
        lock (_gate)
        {
            _isOwnWindowForeground = predicate;
        }
    }

    public void UpdateMacros(IEnumerable<MacroDefinition> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);
        var snapshot = macros
            .Where(macro => macro is not null)
            .ToArray();
        lock (_gate)
        {
            _macros = snapshot;
            if (_recording is null &&
                _triggerCapture is null &&
                _stepCapture is null &&
                !HasTrigger(snapshot))
            {
                StopHookLocked();
            }
            else
            {
                EnsureHookLocked();
            }
        }
    }

    public void StartRecording(bool recordDelays = true)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _recording = new MacroRecordingSession(recordDelays);
            _triggerCapture?.TrySetResult(null);
            _triggerCapture = null;
            _stepCapture?.TrySetResult(null);
            _stepCapture = null;
            if (!EnsureHookLocked())
            {
                _recording = null;
                throw new InvalidOperationException("The macro input hook could not be started.");
            }
        }
    }

    public IReadOnlyList<MacroStep> StopRecording()
    {
        lock (_gate)
        {
            var recording = _recording;
            _recording = null;
            if (!HasTrigger(_macros) &&
                _triggerCapture is null &&
                _stepCapture is null)
            {
                StopHookLocked();
            }
            return recording?.Snapshot() ?? [];
        }
    }

    public async Task<MacroTrigger?> CaptureNextTriggerAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<MacroTrigger?> completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _triggerCapture?.TrySetResult(null);
            _stepCapture?.TrySetResult(null);
            _stepCapture = null;
            completion = new TaskCompletionSource<MacroTrigger?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _triggerCapture = completion;
            if (!EnsureHookLocked())
            {
                _triggerCapture = null;
                completion.TrySetResult(null);
            }
        }

        try
        {
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_triggerCapture, completion))
                {
                    _triggerCapture = null;
                }
                if (_recording is null &&
                    _triggerCapture is null &&
                    _stepCapture is null &&
                    !HasTrigger(_macros))
                {
                    StopHookLocked();
                }
            }
        }
    }

    public async Task<MacroInput?> CaptureNextInputAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<MacroInput?> completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _triggerCapture?.TrySetResult(null);
            _triggerCapture = null;
            _stepCapture?.TrySetResult(null);
            completion = new TaskCompletionSource<MacroInput?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _stepCapture = completion;
            if (!EnsureHookLocked())
            {
                _stepCapture = null;
                completion.TrySetResult(null);
            }
        }

        try
        {
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_stepCapture, completion))
                {
                    _stepCapture = null;
                }
                if (_recording is null &&
                    _triggerCapture is null &&
                    _stepCapture is null &&
                    !HasTrigger(_macros))
                {
                    StopHookLocked();
                }
            }
        }
    }

    public async Task PlayAsync(MacroDefinition macro, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        CancellationTokenSource playback;
        CancellationTokenSource? previousPlayback;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previousPlayback = _playbackCancellation;
            playback = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _playbackCancellation = playback;
        }
        previousPlayback?.Cancel();
        previousPlayback?.Dispose();

        var pressed = new Dictionary<(MacroInputKind Kind, int Code, bool Extended), MacroInput>();
        try
        {
            var repeatCount = macro.PlaybackMode == MacroPlaybackMode.Repeat
                ? Math.Clamp(macro.RepeatCount, 1, MacroDefinition.MaxRepeatCount)
                : 1;
            for (var repetition = 0; repetition < repeatCount; repetition++)
            {
                foreach (var step in macro.Steps)
                {
                    playback.Token.ThrowIfCancellationRequested();
                    if (step.DelayMilliseconds > 0)
                    {
                        await Task.Delay(step.DelayMilliseconds, playback.Token).ConfigureAwait(false);
                    }

                    if (step.Kind == MacroStepKind.Delay)
                    {
                        continue;
                    }

                    _sink.Send(step);
                    if (step.Input is not { } input ||
                        input.Kind == MacroInputKind.MouseWheel)
                    {
                        continue;
                    }

                    // A press step owns its own release: the key is held for
                    // HoldMilliseconds and then released, so a single step can
                    // never leave a key stuck down.
                    var key = InputKey(input);
                    if (step.IsDown)
                    {
                        pressed[key] = input;
                        if (step.HoldMilliseconds > 0)
                        {
                            await Task.Delay(step.HoldMilliseconds, playback.Token)
                                .ConfigureAwait(false);
                        }

                        _sink.Send(new MacroStep
                        {
                            Input = input,
                            IsDown = false,
                            DelayMilliseconds = 0,
                        });
                        pressed.Remove(key);
                    }
                    else
                    {
                        pressed.Remove(key);
                    }
                }
            }
        }
        finally
        {
            foreach (var input in pressed.Values)
            {
                try
                {
                    _sink.Send(new MacroStep
                    {
                        Input = input,
                        IsDown = false,
                        DelayMilliseconds = 0,
                    });
                }
                catch
                {
                    // Best effort release; the original error is more actionable.
                }
            }

            lock (_gate)
            {
                if (ReferenceEquals(_playbackCancellation, playback))
                {
                    _playbackCancellation = null;
                }
            }
            playback.Dispose();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? playback;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _triggerCapture?.TrySetResult(null);
            _triggerCapture = null;
            _stepCapture?.TrySetResult(null);
            _stepCapture = null;
            _recording = null;
            playback = _playbackCancellation;
            _playbackCancellation = null;
            StopHookLocked();
        }
        playback?.Cancel();
        playback?.Dispose();
    }

    private bool HandleInput(MacroInputEvent value)
    {
        MacroDefinition? play = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (_recording is { } recording)
            {
                if (value.Input.Kind == MacroInputKind.MouseButton &&
                    value.Input.MouseButton is 1 or 2 &&
                    _isOwnWindowForeground?.Invoke() == true)
                {
                    return false;
                }

                recording.TryAdd(value.Input, value.IsDown, value.TimestampMilliseconds);
                return false;
            }

            if (_stepCapture is { } stepCapture)
            {
                if (value.Input.Kind == MacroInputKind.MouseButton &&
                    value.Input.MouseButton is 1 or 2 &&
                    _isOwnWindowForeground?.Invoke() == true)
                {
                    return false;
                }

                var isWheel = value.Input.Kind == MacroInputKind.MouseWheel;
                if ((!value.IsDown && !isWheel) ||
                    !value.Input.IsValid(allowPrimaryMouseButton: true, allowWheel: true))
                {
                    return false;
                }

                _stepCapture = null;
                if (!isWheel)
                {
                    _suppressedInput = value.Input.Clone();
                }
                stepCapture.TrySetResult(value.Input.Clone());
                return true;
            }

            if (_triggerCapture is { } capture)
            {
                if (!value.IsDown ||
                    !value.Input.IsValid(allowPrimaryMouseButton: false, allowWheel: false))
                {
                    return false;
                }

                _triggerCapture = null;
                _suppressedInput = value.Input.Clone();
                capture.TrySetResult(new MacroTrigger { Input = value.Input.Clone() });
                return true;
            }

            if (value.IsDown &&
                _suppressedInput is { } held &&
                held.Matches(value.Input))
            {
                return true;
            }

            if (!value.IsDown)
            {
                if (_suppressedInput is { } suppressed && suppressed.Matches(value.Input))
                {
                    _suppressedInput = null;
                    return true;
                }
                return false;
            }

            foreach (var macro in _macros)
            {
                if (!macro.Enabled ||
                    macro.Steps.Count == 0 ||
                    macro.Trigger?.Input is not { } trigger ||
                    !trigger.Matches(value.Input))
                {
                    continue;
                }

                play = macro;
                _suppressedInput = trigger.Clone();
                break;
            }
        }

        if (play is not null)
        {
            _ = ObservePlaybackAsync(play);
            return true;
        }
        return false;
    }

    private async Task ObservePlaybackAsync(MacroDefinition macro)
    {
        try
        {
            await PlayAsync(macro).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            Error?.Invoke(exception.Message);
        }
    }

    private bool EnsureHookLocked()
    {
        if (_hook is not null)
        {
            return true;
        }
        if (_disposed)
        {
            return false;
        }

        try
        {
            var hook = new MacroInputHook(HandleInput);
            hook.Start();
            _hook = hook;
            return true;
        }
        catch (Exception exception)
        {
            Error?.Invoke(exception.Message);
            return false;
        }
    }

    private void StopHookLocked()
    {
        _hook?.Dispose();
        _hook = null;
        _suppressedInput = null;
    }

    private static bool HasTrigger(IReadOnlyList<MacroDefinition> macros) =>
        macros.Any(macro =>
            macro.Enabled &&
            macro.Steps.Count > 0 &&
            macro.Trigger?.Input is not null);

    private static (MacroInputKind Kind, int Code, bool Extended) InputKey(MacroInput input) =>
        input.Kind == MacroInputKind.MouseButton
            ? (input.Kind, input.MouseButton, false)
            : (input.Kind, input.VirtualKey, input.Extended);
}

