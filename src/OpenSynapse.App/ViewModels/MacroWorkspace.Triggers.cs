using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    internal void RefreshMacroState()
    {
        CancelMacroCapture();
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

    public Task CaptureMacroTriggerAsync(CancellationToken cancellationToken = default) =>
        CaptureMacroTriggerCoreAsync(
            allowModifierCombo: false,
            allowDeviceButton: false,
            cancellationToken);

    public Task CaptureMacroTriggerComboAsync(CancellationToken cancellationToken = default) =>
        CaptureMacroTriggerCoreAsync(
            allowModifierCombo: true,
            allowDeviceButton: false,
            cancellationToken);

    public Task CaptureMacroDeviceTriggerAsync(CancellationToken cancellationToken = default) =>
        CaptureMacroTriggerCoreAsync(
            allowModifierCombo: false,
            allowDeviceButton: true,
            cancellationToken);

    private async Task CaptureMacroTriggerCoreAsync(
        bool allowModifierCombo,
        bool allowDeviceButton,
        CancellationToken cancellationToken)
    {
        if (SelectedMacro is null)
        {
            return;
        }

        if (IsMacroRecording)
        {
            StatusText = AppStrings.Text("MacroRecordingBlocked");
            return;
        }

        if (IsMacroCountdown)
        {
            StatusText = AppStrings.Text("MacroCountdownBlocked");
            return;
        }

        var selected = SelectedMacro;
        CancelMacroStepCapture();
        CancelMacroPlaybackShortcutCapture();
        if (_macroCaptureCancellation is not null)
        {
            _macroCaptureCancellation.Cancel();
            return;
        }

        using var capture = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _macroCaptureCancellation = capture;
        IsMacroTriggerCapturing = true;
        StatusText = AppStrings.Text(
            allowDeviceButton ? "MacroCapturePromptDevice" : "MacroCapturePrompt");
        try
        {
            var trigger = await _macroRuntime.CaptureNextTriggerAsync(
                allowModifierCombo,
                allowNagaSideButton: !allowDeviceButton,
                allowDeviceButton: allowDeviceButton,
                cancellationToken: capture.Token);
            if (trigger is null)
            {
                StatusText = AppStrings.Text("MacroCaptureDeviceHint");
                return;
            }

            await ApplyMacroTriggerAsync(selected, trigger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusText = AppStrings.Text("MacroCaptureCanceled");
        }
        finally
        {
            if (ReferenceEquals(_macroCaptureCancellation, capture))
            {
                _macroCaptureCancellation = null;
                IsMacroTriggerCapturing = false;
            }
        }
    }

    private async Task ApplyMacroTriggerAsync(
        MacroRowViewModel selected,
        MacroTrigger trigger,
        CancellationToken cancellationToken)
    {
        trigger.Mode = ReferenceEquals(selected, SelectedMacro)
            ? MacroTriggerMode
            : selected.Definition.Trigger?.Mode ?? MacroTriggerMode.Press;
        if (FindTriggerConflict(selected.Definition, trigger, selected) is { } conflict)
        {
            StatusText = AppStrings.FormatText("MacroTriggerConflict", conflict.Name);
            return;
        }

        if (RazerSoftwareMacroAssignmentPolicy.NeedsDeviceAssignment(trigger.Input))
        {
            await BindDeviceTriggerAsync(selected, trigger, cancellationToken).ConfigureAwait(true);
            return;
        }

        if (selected.Definition.SoftwareBinding is { } binding)
        {
            if (!await RestoreSoftwareBindingAsync(binding, cancellationToken).ConfigureAwait(true))
            {
                StatusText = AppStrings.Text("MacroTriggerRestoreFailed");
                return;
            }
            selected.Definition.SoftwareBinding = null;
        }

        selected.SetTrigger(trigger);
        MarkMacroDirty(selected);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            return;
        }

        RefreshMacroRuntime();
        NotifyTriggerChanged();
        StatusText = AppStrings.FormatText(
            "MacroTriggerCaptured",
            MacroRowViewModel.DescribeTrigger(trigger));
    }

    private async Task BindDeviceTriggerAsync(
        MacroRowViewModel selected,
        MacroTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (SoftwareTriggerBinder is null)
        {
            StatusText = AppStrings.Text("MacroTriggerDeviceUnavailable");
            return;
        }

        var existing = selected.Definition.SoftwareBinding;
        if (existing is not null &&
            (selected.Definition.Trigger is not { } current ||
             !current.Input.Matches(trigger.Input) ||
             current.HyperShiftLayer != trigger.HyperShiftLayer))
        {
            StatusText = AppStrings.Text("MacroTriggerDeviceRebindUnbindFirst");
            return;
        }

        StatusText = AppStrings.Text("MacroTriggerDeviceBinding");
        var error = await SoftwareTriggerBinder(
                selected.Id,
                trigger.Input,
                trigger.Mode,
                selected.Definition.PlaybackMode,
                selected.Definition.RepeatCount,
                trigger.HyperShiftLayer,
                existing,
                cancellationToken)
            .ConfigureAwait(true);
        if (error is not null)
        {
            StatusText = error;
            return;
        }

        RefreshMacroRuntime();
        NotifySelectedMacroChanged();
        NotifyTriggerChanged();
        StatusText = AppStrings.FormatText(
            "MacroTriggerCaptured",
            MacroRowViewModel.DescribeTrigger(trigger));
    }

    public async Task ClearMacroTriggerAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected || !CanEditMacro)
        {
            return;
        }

        CancelMacroCapture();
        var clearedInput = selected.Definition.Trigger?.Input;
        var binding = selected.Definition.SoftwareBinding;
        var restored = binding is not null &&
            await RestoreSoftwareBindingAsync(binding, cancellationToken);
        selected.Definition.SoftwareBinding = null;
        selected.SetTrigger(null);
        MarkMacroDirty(selected);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            return;
        }

        RefreshMacroRuntime();
        NotifySelectedMacroChanged();
        NotifyTriggerChanged();
        StatusText = AppStrings.Text(clearedInput is { } input &&
            RazerSoftwareMacroAssignmentPolicy.NeedsDeviceAssignment(input) && !restored
                ? "MacroTriggerClearedRestoreAssignment"
                : "MacroTriggerCleared");
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

        CancelMacroCapture();
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

    public void CancelMacroCapture()
    {
        _macroCaptureCancellation?.Cancel();
        _macroCaptureCancellation = null;
        IsMacroTriggerCapturing = false;
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
            other.Definition.Trigger is { } otherTrigger &&
            otherTrigger.Input.Matches(trigger.Input) &&
            otherTrigger.Modifiers == trigger.Modifiers &&
            otherTrigger.HyperShiftLayer == trigger.HyperShiftLayer &&
            StringComparer.Ordinal.Equals(otherTrigger.DeviceId, trigger.DeviceId) &&
            StringComparer.Ordinal.Equals(otherTrigger.ControlId, trigger.ControlId));
}
