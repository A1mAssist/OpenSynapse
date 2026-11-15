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
