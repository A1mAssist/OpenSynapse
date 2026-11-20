using System.Diagnostics;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

internal sealed partial class MacroRuntime
{
    public void SetPlaybackShortcuts(MacroInput? start, MacroInput? stop)
    {
        MacroInputHook? hookToDispose = null;
        lock (_gate)
        {
            _playbackStartShortcut = NormalizePlaybackShortcut(start);
            _playbackStopShortcut = NormalizePlaybackShortcut(stop) ?? CreateDefaultStopShortcut();
            _playbackStartHotkeyHeld = false;
            _playbackStopHotkeyHeld = false;
            if (CanDetachHookLocked())
            {
                hookToDispose = DetachHookLocked();
            }
            else
            {
                EnsureHookLocked();
            }
        }

        DisposeHook(hookToDispose);
    }

    public Task PlayAsync(MacroDefinition macro, CancellationToken cancellationToken = default) =>
        PlayAsync(macro, MacroModifierKeys.None, cancellationToken);

    public async Task PlayAsync(
        MacroDefinition macro,
        MacroModifierKeys triggerModifiers,
        CancellationToken cancellationToken = default,
        MacroTrigger? trigger = null)
    {
        ArgumentNullException.ThrowIfNull(macro);
        cancellationToken.ThrowIfCancellationRequested();
        var playbackMode = macro.EffectivePlaybackMode(trigger);
        var repeatUntilStopped = playbackMode == MacroPlaybackMode.RepeatUntilStopped;
        IReadOnlyDictionary<string, MacroDefinition> macroMap;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            macroMap = _macroMap;
        }
        var plan = MacroPlaybackPlan.Build(
            macro,
            macroMap,
            playbackMode,
            macro.EffectiveRepeatCount(trigger));

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
            // Manual playback also needs the hook so the stop shortcut can
            // stop it even when no macro has a trigger.
            EnsureHookLocked();
        }
        PlaybackStateChanged?.Invoke();
        PlaybackStarted?.Invoke(macro, trigger?.Mode, playbackMode);
        previous?.Cancel();
        previous?.Dispose();
        var releasedModifiers = await ReleaseTriggerModifiersAsync(triggerModifiers)
            .ConfigureAwait(false);

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
            await RestoreTriggerModifiersAsync(releasedModifiers).ConfigureAwait(false);
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

    private async Task ObservePlaybackAsync(
        MacroDefinition macro,
        MacroModifierKeys triggerModifiers,
        CancellationTokenSource? holdCancellation,
        MacroInput? holdInput,
        MacroTrigger? trigger)
    {
        // A filter mouse's XBUTTON state can be up; poll only when it is actually down.
        var holdMonitor = holdCancellation is { } hold && holdInput is { } input &&
            MacroInputHook.SupportsPhysicalState(input) && _isPhysicallyDown(input)
                ? MonitorHoldReleaseAsync(input, hold)
                : Task.CompletedTask;
        try
        {
            await PlayAsync(
                macro,
                triggerModifiers,
                holdCancellation?.Token ?? CancellationToken.None,
                trigger).ConfigureAwait(false);
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
        finally
        {
            holdCancellation?.Cancel();
            await holdMonitor.ConfigureAwait(false);
            if (holdCancellation is not null)
            {
                lock (_gate)
                {
                    foreach (var key in _suppressedInputs
                        .Where(pair => ReferenceEquals(
                            pair.Value.HoldCancellation,
                            holdCancellation))
                        .Select(pair => pair.Key)
                        .ToArray())
                    {
                        _suppressedInputs.Remove(key);
                    }
                }
                holdCancellation.Dispose();
            }
        }
    }

    private async Task MonitorHoldReleaseAsync(
        MacroInput input,
        CancellationTokenSource holdCancellation)
    {
        try
        {
            while (!holdCancellation.IsCancellationRequested)
            {
                await Task.Delay(
                    HoldReleaseCheckMilliseconds,
                    holdCancellation.Token).ConfigureAwait(false);
                if (!_isPhysicallyDown(input))
                {
                    holdCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch
        {
            // A failed state probe must not leave a hold macro running forever.
            holdCancellation.Cancel();
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
                        await SendInputAsync(action.Input!, isDown: true, cancellationToken)
                            .ConfigureAwait(false);
                        if (action.Input is { } downInput)
                        {
                            pressed.RemoveAll(item => item.Matches(downInput));
                            pressed.Add(downInput);
                        }
                        break;

                    case MacroPlaybackActionKind.KeyUp:
                    case MacroPlaybackActionKind.MouseButtonUp:
                        await SendInputAsync(action.Input!, isDown: false, cancellationToken)
                            .ConfigureAwait(false);
                        if (action.Input is { } upInput)
                        {
                            pressed.RemoveAll(item => item.Matches(upInput));
                        }
                        break;

                    case MacroPlaybackActionKind.MouseWheel:
                        await SendInputAsync(action.Input!, isDown: false, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case MacroPlaybackActionKind.MouseMove:
                        await SendMousePathAsync(action, cancellationToken).ConfigureAwait(false);
                        break;

                    case MacroPlaybackActionKind.Text:
                        await _sink.SendTextAsync(action.Text ?? string.Empty, cancellationToken)
                            .ConfigureAwait(false);
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
                    await SendInputAsync(input, isDown: false, CancellationToken.None)
                        .ConfigureAwait(false);
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
            await _sink.SendMouseMoveAsync(action.MouseMoveMode, point.X, point.Y, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private Task SendInputAsync(
        MacroInput input,
        bool isDown,
        CancellationToken cancellationToken)
    {
        return _sink.SendAsync(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = input,
            IsDown = isDown,
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<MacroInput>> ReleaseTriggerModifiersAsync(
        MacroModifierKeys modifiers)
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
                await SendInputAsync(input, isDown: false, CancellationToken.None)
                    .ConfigureAwait(false);
                released.Add(input.Clone());
            }
            catch
            {
                // Best effort; playback continues even if a release is refused.
            }
        }

        return released;
    }

    private async Task RestoreTriggerModifiersAsync(IReadOnlyList<MacroInput> released)
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
                await SendInputAsync(input, isDown: true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best effort; the physical key is still held.
            }
        }
    }

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

    private void TraceSkipped(MacroDefinition macro, string reason)
    {
        if (MacroInputTrace.Enabled)
        {
            _log?.Invoke($"trigger skipped macro={macro.Name} id={macro.Id} reason={reason}");
        }
    }

    private static MacroInput? NormalizePlaybackShortcut(MacroInput? input) =>
        input is not null &&
        !input.IsModifierKey &&
        input.IsValid(allowPrimaryMouseButton: false, allowWheel: false)
            ? input.Clone()
            : null;
}
