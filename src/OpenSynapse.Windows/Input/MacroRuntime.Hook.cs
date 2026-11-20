namespace OpenSynapse.Windows.Input;

internal sealed partial class MacroRuntime
{
    private bool EnsureHookLocked()
    {
        if (!_installInputHooks) return !_disposed;
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
            var hook = new MacroInputHook(HandleInput, _log);
            hook.IsPlaybackActive = () => _isPlaying;
            hook.Activity = () => Activity?.Invoke();
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
        foreach (var suppressed in _suppressedInputs.Values)
        {
            suppressed.HoldCancellation?.Cancel();
        }
        _suppressedInputs.Clear();
        _playbackStartHotkeyHeld = false;
        _playbackStopHotkeyHeld = false;
        return hook;
    }

    /// <summary>
    /// Rebuilds the input hook after a suspend: a pending HID read can outlive the
    /// sleep and never deliver another report, which silently stops every
    /// device-scoped trigger until the app restarts.
    /// </summary>
    internal void RestartInputHook()
    {
        MacroInputHook? hook;
        lock (_gate)
        {
            hook = DetachHookLocked();
            // Nothing to restart when no macro needed the hook in the first place.
            if (hook is not null)
            {
                EnsureHookLocked();
            }
        }

        DisposeHook(hook);
    }

    // Outside _gate: Dispose joins a hook thread that may be waiting for that lock.
    private static void DisposeHook(MacroInputHook? hook) => hook?.Dispose();

    private bool CanDetachHookLocked() =>
        _recording is null &&
        _triggerCapture is null &&
        _stepCapture is null &&
        !_isPlaying &&
        _playbackStartShortcut is null &&
        !HasTrigger(_macros);
}
