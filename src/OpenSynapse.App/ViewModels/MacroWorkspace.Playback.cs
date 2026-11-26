using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    /// <summary>Rebuilds the input hook and its private-key readers after a suspend.</summary>
    internal void RestartInputHook() => _macroRuntime.RestartInputHook();

    private void OnMacroPlaybackStartRequested()
    {
        if (_macroDispatcherQueue is not { } dispatcher)
        {
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            _ = PlaySelectedMacroAsync(useCountdown: false, CancellationToken.None);
            return;
        }

        dispatcher.TryEnqueue(() =>
            _ = PlaySelectedMacroAsync(useCountdown: false, CancellationToken.None));
    }

    private void OnMacroPlaybackStarted(
        MacroDefinition macro,
        MacroTriggerMode? triggerMode,
        MacroPlaybackMode playbackMode)
    {
        _diagnosticLog.TryWrite(
            "macro",
            $"started id={macro.Id} trigger={triggerMode?.ToString() ?? "manual"} playback={playbackMode} steps={MacroStepTree.Enumerate(macro.Steps).Count()}");
        if (playbackMode != MacroPlaybackMode.RepeatUntilStopped ||
            triggerMode is (MacroTriggerMode.Hold or MacroTriggerMode.HoldRepeat))
        {
            return;
        }

        if (_macroDispatcherQueue is not { } dispatcher)
        {
            return;
        }

        void Notify()
        {
            var name = string.IsNullOrWhiteSpace(macro.Name)
                ? AppStrings.Text("MacroUntitled")
                : macro.Name.Trim();
            var stopShortcut = _getActiveProfile().Shortcuts.MacroPlaybackStop is { } input
                ? MacroRowViewModel.DescribeInput(input)
                : AppStrings.Text("MacroPlaybackStopShortcutDefault");
            MacroContinuousPlaybackStarted?.Invoke(
                AppStrings.Text("MacroContinuousPlaybackTitle"),
                AppStrings.FormatText("MacroContinuousPlaybackBody", name, stopShortcut));
        }

        dispatcher.TryEnqueue(Notify);
    }

    internal void CancelMacroPlaybackCountdown()
    {
        var countdown = _macroPlaybackCountdownCancellation;
        IsMacroPlaybackCountdown = false;
        countdown?.Cancel();
    }

    public Task PlaySelectedMacroAsync(CancellationToken cancellationToken = default) =>
        PlaySelectedMacroAsync(useCountdown: true, cancellationToken);

    private async Task PlaySelectedMacroAsync(
        bool useCountdown,
        CancellationToken cancellationToken)
    {
        if (IsMacroPlaybackCountdown)
        {
            CancelMacroPlaybackCountdown();
            StatusText = AppStrings.Text("MacroPlaybackCountdownCanceled");
            return;
        }

        if (IsMacroRecording || IsMacroCountdown)
        {
            StatusText = AppStrings.Text("MacroRecording");
            return;
        }

        if (SelectedMacro is not { } selected || selected.Definition.Steps.Count == 0)
        {
            StatusText = AppStrings.Text("MacroEmpty");
            return;
        }

        if (IsMacroPlaying)
        {
            StatusText = AppStrings.Text("MacroAlreadyPlaying");
            return;
        }

        if (!ValidateMacroTree(selected.Definition))
        {
            StatusText = AppStrings.Text("MacroStepIncomplete");
            return;
        }

        if (!ValidateMacroReferences(selected.Definition))
        {
            StatusText = AppStrings.Text("MacroReferenceInvalid");
            return;
        }

        if (MacroValidator.HasUnsupportedHoldPlayback(selected.Definition))
        {
            StatusText = AppStrings.Text("MacroHoldRequiresSinglePlayback");
            return;
        }

        CancelMacroStepCapture();
        CancelMacroPlaybackShortcutCapture();
        if (!useCountdown)
        {
            await PlaySelectedMacroCoreAsync(selected, cancellationToken);
            return;
        }

        using var countdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _macroPlaybackCountdownCancellation = countdown;
        IsMacroPlaybackCountdown = true;
        try
        {
            // Give the user time to focus the target window. Sending input
            // immediately would put keystrokes into the macro editor itself.
            for (var remaining = 3; remaining > 0; remaining--)
            {
                StatusText = AppStrings.FormatText(
                    "MacroPlaybackCountdown",
                    remaining);
                await Task.Delay(TimeSpan.FromSeconds(1), countdown.Token);
            }

            IsMacroPlaybackCountdown = false;
            await PlaySelectedMacroCoreAsync(selected, cancellationToken);
        }
        catch (OperationCanceledException) when (!IsMacroRecording && !IsMacroCountdown)
        {
            StatusText = AppStrings.Text("MacroPlaybackCountdownCanceled");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_macroPlaybackCountdownCancellation, countdown))
            {
                _macroPlaybackCountdownCancellation = null;
            }
            IsMacroPlaybackCountdown = false;
        }
    }

    private async Task PlaySelectedMacroCoreAsync(
        MacroRowViewModel selected,
        CancellationToken cancellationToken)
    {
        IsMacroPlaying = true;
        IsMacroPaused = false;
        StatusText = AppStrings.Text("MacroPlaying");
        try
        {
            await _macroRuntime.PlayAsync(selected.Definition, cancellationToken);
            StatusText = AppStrings.Text("MacroPlayed");
        }
        catch (OperationCanceledException)
        {
            StatusText = AppStrings.Text("MacroPlayCanceled");
        }
        catch (Exception exception)
        {
            StatusText = AppStrings.FormatText("MacroRuntimeError", exception.Message);
        }
        finally
        {
            SyncMacroPlaybackState();
        }
    }

    public void StopMacroPlayback()
    {
        var wasCountingDown = IsMacroPlaybackCountdown;
        CancelMacroPlaybackCountdown();
        if (wasCountingDown)
        {
            StatusText = AppStrings.Text("MacroPlaybackCountdownCanceled");
            return;
        }

        var wasPlaying = IsMacroPlaying || _macroRuntime.IsPlaying;
        _macroRuntime.StopPlayback();
        if (wasPlaying)
        {
            StatusText = AppStrings.Text("MacroPlayStopping");
        }
    }

    public void PauseMacroPlayback()
    {
        _macroRuntime.PausePlayback();
        SyncMacroPlaybackState();
        if (IsMacroPaused)
        {
            StatusText = AppStrings.Text("MacroPaused");
        }
    }

    public void ResumeMacroPlayback()
    {
        _macroRuntime.ResumePlayback();
        SyncMacroPlaybackState();
        if (IsMacroPlaying && !IsMacroPaused)
        {
            StatusText = AppStrings.Text("MacroResumed");
        }
    }

    public void ToggleMacroPlaybackPause()
    {
        SyncMacroPlaybackState();
        if (IsMacroPaused)
        {
            ResumeMacroPlayback();
        }
        else
        {
            PauseMacroPlayback();
        }
    }

    private void OnMacroPlaybackStateChanged()
    {
        if (_macroDispatcherQueue is not { } dispatcher)
        {
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            SyncMacroPlaybackState();
            return;
        }

        dispatcher.TryEnqueue(SyncMacroPlaybackState);
    }

    private void SyncMacroPlaybackState()
    {
        var wasPlaying = IsMacroPlaying;
        IsMacroPlaying = _macroRuntime.IsPlaying;
        IsMacroPaused = _macroRuntime.IsPaused;
        var wasHolding = IsMacroHolding;
        IsMacroHolding = _macroRuntime.IsHolding;
        if (IsMacroHolding && !wasHolding)
        {
            StatusText = AppStrings.Text("MacroHoldPlaying");
        }
        if (wasPlaying && !IsMacroPlaying)
        {
            _diagnosticLog.TryWrite("macro", "playback stopped");
        }
    }
}
