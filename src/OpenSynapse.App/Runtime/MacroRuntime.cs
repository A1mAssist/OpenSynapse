using System.Diagnostics;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.Runtime;

internal sealed class MacroRuntime : IDisposable
{
    private const int StopPlaybackVirtualKey = 0x79;
    private readonly object _gate = new();
    private readonly WindowsMacroInputSink _sink = new();
    private readonly Dictionary<InputKey, SuppressedInput> _suppressedInputs = [];
    private readonly HashSet<int> _physicalModifierKeys = [];
    private readonly Dictionary<string, int> _sequencePositions = new(StringComparer.Ordinal);
    private IReadOnlyList<MacroDefinition> _macros = [];
    private IReadOnlyDictionary<string, MacroDefinition> _macroMap =
        new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);
    private MacroInputHook? _hook;
    private MacroRecordingSession? _recording;
    private MacroRecordingOptions _recordingOptions = new();
    private MacroInput? _recordingStopInput;
    private TaskCompletionSource<MacroTrigger?>? _triggerCapture;
    private TaskCompletionSource<MacroInput?>? _stepCapture;
    private MacroInput? _suppressedCapture;
    private bool _triggerCaptureAllowsModifiers;
    private Func<bool>? _isOwnWindowForeground;
    private CancellationTokenSource? _playbackCancellation;
    private TaskCompletionSource? _resumePlayback;
    private bool _isPlaying;
    private bool _isPaused;
    private string? _activePlaybackMacroId;
    private bool _disposed;
    private bool _recordingPending;
    private bool _stopPlaybackHotkeyHeld;

    public event Action<string>? Error;

    public event Action? RecordingStopRequested;

    public event Action? PlaybackStateChanged;

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

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _isPlaying;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _isPaused;
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

    public void SetRecordingPending(bool pending)
    {
        lock (_gate)
        {
            _recordingPending = pending;
        }
    }

    public void UpdateMacros(IEnumerable<MacroDefinition> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);
        var snapshot = macros.Where(macro => macro is not null).ToArray();
        var map = new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);
        foreach (var macro in snapshot)
        {
            map.TryAdd(macro.Id, macro);
        }

        MacroInputHook? hookToDispose = null;
        lock (_gate)
        {
            _macros = snapshot;
            _macroMap = map;
            _sequencePositions.Clear();
            if (CanDetachHookLocked())
            {
                hookToDispose = DetachHookLocked();
            }
            else
            {
                EnsureHookLocked();
                if (_hook is not null)
                {
                    _hook.CaptureMouseMoves =
                        _recording is not null &&
                        _recordingOptions.MouseTrackingMode != MacroMouseTrackingMode.None;
                }
            }
        }

        DisposeHook(hookToDispose);
    }

    public void StartRecording(MacroRecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        StopPlayback();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recording is not null)
            {
                throw new InvalidOperationException("A macro recording is already running.");
            }

            _recordingOptions = options.Clone();
            _recording = new MacroRecordingSession(_recordingOptions);
            _recordingPending = false;
            _recordingStopInput = null;
            _triggerCapture?.TrySetResult(null);
            _triggerCapture = null;
            _stepCapture?.TrySetResult(null);
            _stepCapture = null;
            _suppressedInputs.Clear();
            _suppressedCapture = null;
            if (!EnsureHookLocked())
            {
                _recording = null;
                throw new InvalidOperationException("The macro input hook could not be started.");
            }

            _hook!.CaptureMouseMoves =
                _recordingOptions.MouseTrackingMode != MacroMouseTrackingMode.None;
        }
    }

    public IReadOnlyList<MacroStep> StopRecording()
    {
        IReadOnlyList<MacroStep> steps;
        MacroInputHook? hookToDispose = null;
        lock (_gate)
        {
            var recording = _recording;
            _recording = null;
            _recordingPending = false;
            if (_hook is not null)
            {
                _hook.CaptureMouseMoves = false;
            }
            if (CanDetachHookLocked())
            {
                hookToDispose = DetachHookLocked();
            }
            steps = recording?.Snapshot() ?? [];
        }

        DisposeHook(hookToDispose);
        return steps;
    }

    public Task<MacroTrigger?> CaptureNextTriggerAsync(
        CancellationToken cancellationToken = default) =>
        CaptureNextTriggerAsync(allowModifierCombo: false, cancellationToken);

    public async Task<MacroTrigger?> CaptureNextTriggerAsync(
        bool allowModifierCombo,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<MacroTrigger?> completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _triggerCapture?.TrySetResult(null);
            _stepCapture?.TrySetResult(null);
            _stepCapture = null;
            _triggerCaptureAllowsModifiers = allowModifierCombo;
            completion = new TaskCompletionSource<MacroTrigger?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _triggerCapture = completion;
            _suppressedCapture = null;
            if (!EnsureHookLocked())
            {
                _triggerCapture = null;
                completion.TrySetResult(null);
            }
        }

        MacroInputHook? hookToDispose = null;
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
                if (CanDetachHookLocked())
                {
                    hookToDispose = DetachHookLocked();
                }
            }

            DisposeHook(hookToDispose);
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
            _suppressedCapture = null;
            if (!EnsureHookLocked())
            {
                _stepCapture = null;
                completion.TrySetResult(null);
            }
        }

        MacroInputHook? hookToDispose = null;
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
                if (CanDetachHookLocked())
                {
                    hookToDispose = DetachHookLocked();
                }
            }

            DisposeHook(hookToDispose);
        }
    }

    public Task PlayAsync(MacroDefinition macro, CancellationToken cancellationToken = default) =>
        PlayAsync(macro, MacroModifierKeys.None, cancellationToken);

    public async Task PlayAsync(
        MacroDefinition macro,
        MacroModifierKeys triggerModifiers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        var repeatUntilStopped = macro.PlaybackMode == MacroPlaybackMode.RepeatUntilStopped;
        IReadOnlyDictionary<string, MacroDefinition> macroMap;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            macroMap = _macroMap;
        }
        var plan = MacroPlaybackPlan.Build(macro, macroMap);

        if (plan.Actions.Count == 0)
        {
            return;
        }

        if (plan.Warnings.Count > 0)
        {
            // A missing or cyclic reference must not play a half-executed
            // macro. Surface it as a failure and leave the desktop untouched.
            throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Warnings));
        }

        CancellationTokenSource playback;
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _playbackCancellation;
            playback = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _playbackCancellation = playback;
            _isPlaying = true;
            _isPaused = false;
            _resumePlayback?.TrySetResult();
            _resumePlayback = null;
            _activePlaybackMacroId = macro.Id;
            // Manual playback also needs the hook so F10 can stop it even
            // when no macro has a trigger.
            EnsureHookLocked();
        }
        PlaybackStateChanged?.Invoke();
        previous?.Cancel();
        previous?.Dispose();
        var releasedModifiers = ReleaseTriggerModifiers(triggerModifiers);

        try
        {
            do
            {
                await ExecutePlanAsync(plan, playback.Token).ConfigureAwait(false);
                if (!repeatUntilStopped)
                {
                    break;
                }

                // Yield between cycles so a macro with no delays cannot pin a
                // core while still repeating immediately.
                await Task.Delay(10, playback.Token).ConfigureAwait(false);
            }
            while (true);
        }
        finally
        {
            RestoreTriggerModifiers(releasedModifiers);
            MacroInputHook? hookToDispose = null;
            lock (_gate)
            {
                if (ReferenceEquals(_playbackCancellation, playback))
                {
                    _playbackCancellation = null;
                    _isPlaying = false;
                    _isPaused = false;
                    _activePlaybackMacroId = null;
                    _resumePlayback?.TrySetResult();
                    _resumePlayback = null;
                    if (CanDetachHookLocked())
                    {
                        hookToDispose = DetachHookLocked();
                    }
                }
            }
            DisposeHook(hookToDispose);
            playback.Dispose();
            PlaybackStateChanged?.Invoke();
        }
    }

    public void StopPlayback()
    {
        CancellationTokenSource? playback;
        lock (_gate)
        {
            playback = _playbackCancellation;
            _isPaused = false;
            _resumePlayback?.TrySetResult();
            _resumePlayback = null;
        }
        playback?.Cancel();
        PlaybackStateChanged?.Invoke();
    }

    public void PausePlayback()
    {
        var changed = false;
        lock (_gate)
        {
            if (!_isPlaying || _isPaused)
            {
                return;
            }

            _isPaused = true;
            _resumePlayback = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            changed = true;
        }
        if (changed)
        {
            PlaybackStateChanged?.Invoke();
        }
    }

    public void ResumePlayback()
    {
        var changed = false;
        lock (_gate)
        {
            changed = _isPaused;
            _isPaused = false;
            _resumePlayback?.TrySetResult();
            _resumePlayback = null;
        }
        if (changed)
        {
            PlaybackStateChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? playback;
        MacroInputHook? hookToDispose;
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
            _isPaused = false;
            _resumePlayback?.TrySetResult();
            _resumePlayback = null;
            playback = _playbackCancellation;
            _playbackCancellation = null;
            hookToDispose = DetachHookLocked();
        }
        playback?.Cancel();
        playback?.Dispose();
        DisposeHook(hookToDispose);
    }

    private bool HandleInput(MacroInputEvent value)
    {
        TrackPhysicalModifier(value);
        if (value.IsMouseMove)
        {
            MacroRecordingSession? mouseRecording;
            lock (_gate)
            {
                mouseRecording = _recording;
            }
            if (mouseRecording is not null &&
                _isOwnWindowForeground?.Invoke() != true)
            {
                mouseRecording.TryAddMouseMove(
                    value.X,
                    value.Y,
                    value.TimestampMilliseconds,
                    value.WindowLeft,
                    value.WindowTop,
                    value.WindowWidth,
                    value.WindowHeight);
            }
            return false;
        }

        MacroDefinition? playMacro = null;
        var playModifiers = MacroModifierKeys.None;
        var suppress = false;
        var stopRequested = false;
        var stopPlayback = false;

        // F10 is reserved while a macro is playing. Keep this check ahead of
        // trigger matching, but behind recording and capture modes.
        if (IsStopPlaybackInput(value.Input))
        {
            var stopHotkeyHandled = false;
            lock (_gate)
            {
                var canUseStopHotkey =
                    _recording is null &&
                    _stepCapture is null &&
                    _triggerCapture is null &&
                    !_recordingPending;
                if (value.IsDown && _isPlaying && canUseStopHotkey)
                {
                    _stopPlaybackHotkeyHeld = true;
                    stopHotkeyHandled = true;
                }
                else if (!value.IsDown && _stopPlaybackHotkeyHeld && canUseStopHotkey)
                {
                    _stopPlaybackHotkeyHeld = false;
                    stopHotkeyHandled = true;
                }
            }

            if (stopHotkeyHandled)
            {
                if (value.IsDown)
                {
                    StopPlayback();
                }
                return true;
            }
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (_recordingStopInput is { } heldStop &&
                heldStop.Matches(value.Input))
            {
                if (!value.IsDown)
                {
                    _recordingStopInput = null;
                }
                return true;
            }

            if (_recording is { } recording)
            {
                if (_recordingOptions.StopShortcut is { } stopShortcut &&
                    stopShortcut.Matches(value.Input))
                {
                    _recordingStopInput = value.Input.Clone();
                    if (value.IsDown)
                    {
                        stopRequested = true;
                    }
                    return true;
                }

                // Button clicks and typing in our own editor are not part of
                // the user's target application sequence.
                if (_isOwnWindowForeground?.Invoke() == true)
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
                    _suppressedCapture = value.Input.Clone();
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

                if (value.Input.IsModifierKey)
                {
                    return false;
                }

                var modifiers = _triggerCaptureAllowsModifiers
                    ? value.Modifiers
                    : MacroModifierKeys.None;
                _triggerCapture = null;
                _suppressedCapture = value.Input.Clone();
                capture.TrySetResult(new MacroTrigger
                {
                    Input = value.Input.Clone(),
                    Modifiers = modifiers,
                });
                return true;
            }

            if (_recordingPending)
            {
                return false;
            }

            if (_suppressedCapture is { } captureSuppressed &&
                captureSuppressed.Matches(value.Input))
            {
                if (!value.IsDown)
                {
                    _suppressedCapture = null;
                }
                return true;
            }

            var key = KeyOf(value.Input);
            if (_suppressedInputs.TryGetValue(key, out var suppressed))
            {
                if (!value.IsDown)
                {
                    _suppressedInputs.Remove(key);
                    if (suppressed.Mode == MacroTriggerMode.Hold &&
                        StringComparer.Ordinal.Equals(
                            _activePlaybackMacroId,
                            suppressed.MacroId))
                    {
                        stopPlayback = true;
                    }
                }
                return true;
            }

            if (!value.IsDown)
            {
                return false;
            }

            foreach (var macro in _macros)
            {
                if (!macro.Enabled ||
                    macro.Steps.Count == 0 ||
                    macro.Trigger is not { } trigger ||
                    !trigger.Matches(value.Input, value.Modifiers))
                {
                    continue;
                }

                suppress = true;
                playModifiers = trigger.Modifiers;
                _suppressedInputs[key] = new SuppressedInput(trigger.Mode, macro.Id);
                switch (trigger.Mode)
                {
                    case MacroTriggerMode.Hold:
                        if (!_isPlaying ||
                            !StringComparer.Ordinal.Equals(_activePlaybackMacroId, macro.Id))
                        {
                            playMacro = macro;
                        }
                        break;

                    case MacroTriggerMode.Toggle:
                        if (_isPlaying &&
                            StringComparer.Ordinal.Equals(_activePlaybackMacroId, macro.Id))
                        {
                            stopPlayback = true;
                        }
                        else
                        {
                            playMacro = macro;
                        }
                        break;

                    case MacroTriggerMode.Sequence:
                        if (macro.Steps.Count > 0)
                        {
                            var index = _sequencePositions.GetValueOrDefault(macro.Id) %
                                macro.Steps.Count;
                            _sequencePositions[macro.Id] = (index + 1) % macro.Steps.Count;
                            playMacro = new MacroDefinition
                            {
                                Id = macro.Id,
                                Name = macro.Name,
                                PlaybackMode = MacroPlaybackMode.Once,
                                PlaybackSpeed = macro.PlaybackSpeed,
                                MouseTrackingMode = macro.MouseTrackingMode,
                                Steps = [macro.Steps[index]],
                            };
                        }
                        break;

                    default:
                        playMacro = macro;
                        break;
                }
                break;
            }
        }

        if (stopRequested)
        {
            RecordingStopRequested?.Invoke();
        }
        if (stopPlayback)
        {
            StopPlayback();
        }
        if (playMacro is not null)
        {
            // Never build a playback plan or send the first input on the
            // low-level hook thread. Windows drops slow hooks and the desktop
            // would stall until the plan finishes.
            _ = Task.Run(() => ObservePlaybackAsync(playMacro, playModifiers));
        }
        return suppress;
    }

    private async Task ObservePlaybackAsync(
        MacroDefinition macro,
        MacroModifierKeys triggerModifiers)
    {
        try
        {
            await PlayAsync(macro, triggerModifiers).ConfigureAwait(false);
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

    private async Task ExecutePlanAsync(
        MacroPlaybackPlan plan,
        CancellationToken cancellationToken)
    {
        var pressed = new List<MacroInput>();
        try
        {
            foreach (var action in plan.Actions)
            {
                await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (action.DelayMilliseconds > 0)
                {
                    await DelayWithPauseAsync(action.DelayMilliseconds, cancellationToken)
                        .ConfigureAwait(false);
                }

                await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                switch (action.Kind)
                {
                    case MacroPlaybackActionKind.KeyDown:
                    case MacroPlaybackActionKind.MouseButtonDown:
                        SendInput(action.Input!, isDown: true);
                        if (action.Input is { } downInput)
                        {
                            pressed.RemoveAll(item => item.Matches(downInput));
                            pressed.Add(downInput);
                        }
                        break;

                    case MacroPlaybackActionKind.KeyUp:
                    case MacroPlaybackActionKind.MouseButtonUp:
                        SendInput(action.Input!, isDown: false);
                        if (action.Input is { } upInput)
                        {
                            pressed.RemoveAll(item => item.Matches(upInput));
                        }
                        break;

                    case MacroPlaybackActionKind.MouseWheel:
                        SendInput(action.Input!, isDown: false);
                        break;

                    case MacroPlaybackActionKind.MouseMove:
                        await SendMousePathAsync(action, cancellationToken).ConfigureAwait(false);
                        break;

                    case MacroPlaybackActionKind.Text:
                        _sink.SendText(action.Text ?? string.Empty);
                        break;

                    case MacroPlaybackActionKind.RunCommand:
                        RunCommand(action);
                        break;
                }
            }
        }
        finally
        {
            foreach (var input in pressed)
            {
                try
                {
                    SendInput(input, isDown: false);
                }
                catch
                {
                    // Best effort release; the original error is more actionable.
                }
            }
        }
    }

    private async Task SendMousePathAsync(
        MacroPlaybackAction action,
        CancellationToken cancellationToken)
    {
        var previousOffset = 0;
        foreach (var point in action.MousePath)
        {
            await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var delay = point.OffsetMilliseconds - previousOffset;
            if (delay > 0)
            {
                await DelayWithPauseAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            previousOffset = point.OffsetMilliseconds;
            _sink.SendMouseMove(action.MouseMoveMode, point.X, point.Y);
        }
    }

    private void SendInput(MacroInput input, bool isDown)
    {
        _sink.Send(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = input,
            IsDown = isDown,
        });
    }

    private void TrackPhysicalModifier(MacroInputEvent value)
    {
        if (value.Input.Kind != MacroInputKind.Keyboard ||
            !value.Input.IsModifierKey)
        {
            return;
        }

        lock (_gate)
        {
            if (value.IsDown)
            {
                _physicalModifierKeys.Add(value.Input.VirtualKey);
            }
            else
            {
                _physicalModifierKeys.Remove(value.Input.VirtualKey);
            }
        }
    }

    private IReadOnlyList<MacroInput> ReleaseTriggerModifiers(MacroModifierKeys modifiers)
    {
        var released = new List<MacroInput>(4);
        if (modifiers == MacroModifierKeys.None)
        {
            return released;
        }

        // The physical modifiers were already delivered before the trigger key.
        // Record what was actually held, because GetAsyncKeyState will report
        // them as up after our synthetic releases; a later query cannot restore
        // the user's still-held physical keys.
        foreach (var input in WindowsMacroInputSink.GetPressedModifierInputs())
        {
            if ((modifiers & ModifierFlag(input.VirtualKey)) == 0)
            {
                continue;
            }

            lock (_gate)
            {
                _physicalModifierKeys.Add(input.VirtualKey);
            }

            try
            {
                SendInput(input, isDown: false);
                released.Add(input.Clone());
            }
            catch
            {
                // Best effort; playback continues even if a release is refused.
            }
        }

        return released;
    }

    private void RestoreTriggerModifiers(IReadOnlyList<MacroInput> released)
    {
        foreach (var input in released)
        {
            // Do not re-press a modifier the user let go of during a long
            // macro. The hook tracks physical modifier events separately from
            // our synthetic releases.
            lock (_gate)
            {
                if (!_physicalModifierKeys.Contains(input.VirtualKey))
                {
                    continue;
                }
            }

            try
            {
                SendInput(input, isDown: true);
            }
            catch
            {
                // Best effort; the physical key is still held.
            }
        }
    }

    private static MacroModifierKeys ModifierFlag(int virtualKey) => virtualKey switch
    {
        0xA0 or 0xA1 => MacroModifierKeys.Shift,
        0xA2 or 0xA3 => MacroModifierKeys.Control,
        0xA4 or 0xA5 => MacroModifierKeys.Alt,
        0x5B or 0x5C => MacroModifierKeys.Windows,
        _ => MacroModifierKeys.None,
    };

    private static void RunCommand(MacroPlaybackAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Command))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = action.Command,
            UseShellExecute = true,
        };
        if (!string.IsNullOrWhiteSpace(action.Arguments))
        {
            startInfo.Arguments = action.Arguments;
        }
        if (!string.IsNullOrWhiteSpace(action.WorkingDirectory))
        {
            startInfo.WorkingDirectory = action.WorkingDirectory;
        }
        using var process = Process.Start(startInfo);
    }

    private async Task DelayWithPauseAsync(
        int milliseconds,
        CancellationToken cancellationToken)
    {
        var remaining = milliseconds;
        while (remaining > 0)
        {
            await WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
            var slice = Math.Min(remaining, 50);
            await Task.Delay(slice, cancellationToken).ConfigureAwait(false);
            remaining -= slice;
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? wait;
            lock (_gate)
            {
                if (!_isPaused)
                {
                    return;
                }
                wait = _resumePlayback?.Task;
            }

            if (wait is null)
            {
                return;
            }
            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private MacroInputHook? DetachHookLocked()
    {
        var hook = _hook;
        _hook = null;
        if (hook is not null)
        {
            hook.CaptureMouseMoves = false;
        }
        _suppressedCapture = null;
        _suppressedInputs.Clear();
        _stopPlaybackHotkeyHeld = false;
        return hook;
    }

    private static void DisposeHook(MacroInputHook? hook)
    {
        // Never dispose the hook while holding _gate: its callback may be
        // waiting for the same lock, and Dispose joins the hook thread.
        hook?.Dispose();
    }

    private bool CanDetachHookLocked() =>
        _recording is null &&
        _triggerCapture is null &&
        _stepCapture is null &&
        !_isPlaying &&
        !HasTrigger(_macros);

    private static bool HasTrigger(IReadOnlyList<MacroDefinition> macros) =>
        macros.Any(macro =>
            macro.Enabled &&
            macro.Steps.Count > 0 &&
            macro.Trigger?.Input is not null);

    private static bool IsStopPlaybackInput(MacroInput input) =>
        input.Kind == MacroInputKind.Keyboard &&
        input.VirtualKey == StopPlaybackVirtualKey;

    private static InputKey KeyOf(MacroInput input) =>
        input.Kind == MacroInputKind.MouseButton
            ? new InputKey(input.Kind, input.MouseButton, false)
            : new InputKey(input.Kind, input.VirtualKey, input.Extended);

    private readonly record struct InputKey(MacroInputKind Kind, int Code, bool Extended);

    private readonly record struct SuppressedInput(MacroTriggerMode Mode, string? MacroId);
}
