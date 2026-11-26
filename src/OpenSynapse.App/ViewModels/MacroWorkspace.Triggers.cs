using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    internal void RefreshMacroState()
    {
        CancelMacroStepCapture();
        CancelMacroPlaybackShortcutCapture();
        CancelMacroCountdown();
        CancelMacroPlaybackCountdown();
        if (IsMacroRecording)
        {
            FinishRecordingTake(save: true);
        }

        var selectedId = SelectedMacro?.Id;
        Items.Clear();
        foreach (var macro in _getActiveProfile().Macros)
        {
            Items.Add(CreateMacroRow(macro));
        }

        SelectedMacro = Items.FirstOrDefault(row =>
                StringComparer.Ordinal.Equals(row.Id, selectedId))
            ?? Items.FirstOrDefault();
        RefreshMacroRuntime();
        if (SelectedMacro is null)
        {
            StatusText = AppStrings.Text("MacroStatusIdle");
        }
        OnPropertyChanged(nameof(MacroEditorVisibility));
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
        NotifySelectedMacroChanged();
    }

    public Task CaptureMacroPlaybackStartShortcutAsync(CancellationToken cancellationToken = default) =>
        CaptureMacroPlaybackShortcutAsync(isStart: true, cancellationToken);

    public Task CaptureMacroPlaybackStopShortcutAsync(CancellationToken cancellationToken = default) =>
        CaptureMacroPlaybackShortcutAsync(isStart: false, cancellationToken);

    public async Task ClearMacroPlaybackStartShortcutAsync(CancellationToken cancellationToken = default)
    {
        CancelMacroPlaybackShortcutCapture();
        if (await SetMacroPlaybackShortcutAsync(isStart: true, input: null, cancellationToken))
        {
            StatusText = AppStrings.Text("MacroPlaybackStartShortcutCleared");
        }
    }

    public async Task ClearMacroPlaybackStopShortcutAsync(CancellationToken cancellationToken = default)
    {
        CancelMacroPlaybackShortcutCapture();
        if (await SetMacroPlaybackShortcutAsync(isStart: false, input: null, cancellationToken))
        {
            StatusText = AppStrings.Text("MacroPlaybackStopShortcutCleared");
        }
    }

    private async Task CaptureMacroPlaybackShortcutAsync(
        bool isStart,
        CancellationToken cancellationToken)
    {
        if (IsMacroRecording || IsMacroCountdown)
        {
            StatusText = AppStrings.Text("MacroRecording");
            return;
        }

        if (_macroPlaybackShortcutCaptureCancellation is not null)
        {
            _macroPlaybackShortcutCaptureCancellation.Cancel();
            return;
        }

        CancelMacroStepCapture();
        using var capture = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _macroPlaybackShortcutCaptureCancellation = capture;
        StatusText = AppStrings.Text("MacroPlaybackShortcutCapturePrompt");
        try
        {
            var trigger = await _macroRuntime.CaptureNextTriggerAsync(
                allowModifierCombo: false,
                allowNagaSideButton: false,
                cancellationToken: capture.Token);
            if (trigger is null)
            {
                StatusText = AppStrings.Text("MacroCaptureCanceled");
                return;
            }

            if (!await SetMacroPlaybackShortcutAsync(
                    isStart,
                    trigger.Input.Clone(),
                    cancellationToken))
            {
                return;
            }

            StatusText = AppStrings.FormatText(
                isStart
                    ? "MacroPlaybackStartShortcutCaptured"
                    : "MacroPlaybackStopShortcutCaptured",
                MacroRowViewModel.DescribeInput(trigger.Input));
        }
        catch (OperationCanceledException)
        {
            StatusText = AppStrings.Text("MacroCaptureCanceled");
        }
        finally
        {
            if (ReferenceEquals(_macroPlaybackShortcutCaptureCancellation, capture))
            {
                _macroPlaybackShortcutCaptureCancellation = null;
            }
        }
    }

    private async Task<bool> SetMacroPlaybackShortcutAsync(
        bool isStart,
        MacroInput? input,
        CancellationToken cancellationToken)
    {
        var shortcuts = _getActiveProfile().Shortcuts;
        if (isStart)
        {
            shortcuts.MacroPlaybackStart = input;
        }
        else
        {
            shortcuts.MacroPlaybackStop = input;
        }

        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            return false;
        }

        RefreshMacroRuntime();
        return true;
    }

    public void CancelMacroPlaybackShortcutCapture()
    {
        _macroPlaybackShortcutCaptureCancellation?.Cancel();
        _macroPlaybackShortcutCaptureCancellation = null;
    }

    private MacroRowViewModel? FindTriggerConflict(MacroRowViewModel current) =>
        current.Definition.Trigger is { } trigger
            ? FindTriggerConflict(current.Definition, trigger, current)
            : null;

    private MacroRowViewModel? FindTriggerConflict(
        MacroDefinition macro,
        MacroTrigger trigger,
        MacroRowViewModel? current = null) =>
        Items.FirstOrDefault(other =>
            !ReferenceEquals(other.Definition, macro) &&
            !ReferenceEquals(other, current) &&
            other.Definition.AllTriggers.Any(otherTrigger =>
                otherTrigger.Input.Matches(trigger.Input) &&
                otherTrigger.Modifiers == trigger.Modifiers &&
                otherTrigger.HyperShiftLayer == trigger.HyperShiftLayer &&
                StringComparer.Ordinal.Equals(otherTrigger.DeviceId, trigger.DeviceId) &&
                StringComparer.Ordinal.Equals(otherTrigger.ControlId, trigger.ControlId)));
}
