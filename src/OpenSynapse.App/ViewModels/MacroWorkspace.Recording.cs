using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    private bool _macroRecordingOwnWindow;

    public bool IsMacroRecordingOwnWindow
    {
        get => _macroRecordingOwnWindow;
        set
        {
            if (_macroRecordingOwnWindow == value)
            {
                return;
            }

            _macroRecordingOwnWindow = value;
            OnPropertyChanged();
        }
    }

    public Task StartMacroRecordingAsync(CancellationToken cancellationToken = default) =>
        StartMacroRecordingAsync(append: false, cancellationToken);

    public async Task StartMacroRecordingAsync(
        bool append,
        CancellationToken cancellationToken = default)
    {
        if (IsMacroCountdown)
        {
            CancelMacroCountdown();
            StatusText = AppStrings.Text("MacroCountdownCanceled");
            return;
        }

        if (IsMacroRecording || SelectedMacro is null)
        {
            StatusText = AppStrings.Text("MacroRecording");
            return;
        }

        if (IsMacroPlaying)
        {
            StatusText = AppStrings.Text("MacroStopPlaybackFirst");
            return;
        }

        var selected = SelectedMacro;
        if (append && !ValidateMacroTree(selected.Definition))
        {
            StatusText = AppStrings.Text("MacroStepIncomplete");
            return;
        }

        CancelMacroPlaybackCountdown();
        CancelMacroCapture();
        CancelMacroStepCapture();
        var countdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _macroCountdownCancellation = countdown;
        IsMacroCountdown = true;
        _macroRuntime.SetRecordingPending(true);
        try
        {
            for (var remaining = 3; remaining > 0; remaining--)
            {
                StatusText = AppStrings.FormatText("MacroRecordingCountdown", remaining);
                await Task.Delay(TimeSpan.FromSeconds(1), countdown.Token);
            }

            countdown.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(SelectedMacro, selected))
            {
                StatusText = AppStrings.Text("MacroCountdownCanceled");
                return;
            }

            _recordingMacroId = selected.Id;
            _recordingAppend = append;
            _macroRuntime.StartRecording(BuildRecordingOptions());
            _macroRuntime.SetRecordingPending(false);
            IsMacroRecording = true;
            StatusText = AppStrings.FormatText("MacroRecordingStarted", "F9");
        }
        catch (OperationCanceledException)
        {
            StatusText = AppStrings.Text("MacroCountdownCanceled");
        }
        catch (Exception exception)
        {
            _recordingMacroId = null;
            _recordingAppend = false;
            StatusText = AppStrings.FormatText("MacroRuntimeError", exception.Message);
        }
        finally
        {
            _macroRuntime.SetRecordingPending(false);
            if (ReferenceEquals(_macroCountdownCancellation, countdown))
            {
                _macroCountdownCancellation = null;
                IsMacroCountdown = false;
            }

            countdown.Dispose();
        }
    }

    public async Task ToggleMacroRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (IsMacroRecording || IsMacroCountdown)
        {
            await StopMacroRecordingAsync(cancellationToken);
            return;
        }

        await StartMacroRecordingAsync(cancellationToken);
    }

    public async Task<bool> StopMacroRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (IsMacroCountdown)
        {
            CancelMacroCountdown();
            StatusText = AppStrings.Text("MacroCountdownCanceled");
            return true;
        }

        if (!IsMacroRecording)
        {
            return false;
        }

        var take = FinishRecordingTake(save: false);
        if (take is null)
        {
            return false;
        }

        if (take.Value.Count < 0)
        {
            return true;
        }

        if (take.Value.Count == 0)
        {
            StatusText = AppStrings.Text("MacroRecordingEmptyTake");
            return true;
        }

        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            return false;
        }

        RefreshMacroRuntime();
        StatusText = AppStrings.FormatText("MacroRecorded", take.Value.Count);
        return true;
    }

    private (MacroRowViewModel Row, int Count)? FinishRecordingTake(bool save)
    {
        var macroId = _recordingMacroId;
        var append = _recordingAppend;
        var steps = _macroRuntime.StopRecording();
        IsMacroRecording = false;
        _recordingMacroId = null;
        _recordingAppend = false;
        var row = Items.FirstOrDefault(item =>
            StringComparer.Ordinal.Equals(item.Id, macroId));
        if (row is null)
        {
            StatusText = AppStrings.Text("MacroRecordingMacroMissing");
            return null;
        }

        if (steps.Count == 0)
        {
            // An empty take is a no-op. Replacing a recorded macro with zero
            // actions is destructive and is almost always an accidental F9.
            StatusText = AppStrings.Text("MacroRecordingEmptyTake");
            if (ReferenceEquals(SelectedMacro, row))
            {
                RefreshMacroSteps();
            }
            return (row, 0);
        }

        if (append &&
            MacroStepTree.CountSteps(row.Definition.Steps) +
            MacroStepTree.CountSteps(steps) > MacroDefinition.MaxSteps)
        {
            StatusText = AppStrings.Text("MacroStepLimit");
            return (row, -1);
        }

        var committedSteps = append
            ? row.Definition.Steps.Concat(steps).ToList()
            : steps;
        row.SetSteps(committedSteps);
        MarkMacroDirty(row);
        if (ReferenceEquals(SelectedMacro, row))
        {
            RefreshMacroSteps(selectStep: steps.FirstOrDefault());
        }

        NotifySelectedMacroChanged();
        if (save)
        {
            _ = SaveCommittedRecordingAsync(row, steps.Count);
        }

        return (row, steps.Count);
    }

    private async Task SaveCommittedRecordingAsync(MacroRowViewModel row, int count)
    {
        try
        {
            if (!await TrySaveMacroProfileAsync(CancellationToken.None))
            {
                return;
            }

            RefreshMacroRuntime();
            StatusText = AppStrings.FormatText("MacroRecorded", count);
        }
        catch (Exception exception)
        {
            StatusText = AppStrings.FormatText("MacroRuntimeError", exception.Message);
        }
    }

    public void DiscardMacroRecording()
    {
        CancelMacroCountdown();
        if (!IsMacroRecording)
        {
            return;
        }

        _macroRuntime.StopRecording();
        IsMacroRecording = false;
        _recordingMacroId = null;
        _recordingAppend = false;
        StatusText = AppStrings.Text("MacroStatusIdle");
    }

    private void OnMacroRecordingStopRequested()
    {
        if (_macroDispatcherQueue is not { } dispatcher)
        {
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            _ = StopMacroRecordingFromShortcutAsync();
            return;
        }

        dispatcher.TryEnqueue(() => _ = StopMacroRecordingFromShortcutAsync());
    }

    private async Task StopMacroRecordingFromShortcutAsync()
    {
        try
        {
            await StopMacroRecordingAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            StatusText = AppStrings.FormatText("MacroRuntimeError", exception.Message);
        }
    }

    private void CancelMacroCountdown()
    {
        _macroCountdownCancellation?.Cancel();
    }

    private MacroRecordingOptions BuildRecordingOptions() => new()
    {
        RecordDelays = _macroRecordingDelayMode != MacroDelayMode.None,
        RecordOwnWindow = _macroRecordingOwnWindow,
        DelayMode = _macroRecordingDelayMode,
        FixedDelayMilliseconds = (int)Math.Round(_macroRecordingFixedDelayMilliseconds),
        MouseTrackingMode = _macroRecordingMouseTrackingMode,
        StopShortcut = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = 0x78,
            Extended = false,
        },
    };
}
