using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace
{
    public async Task AddMacroDelayAsync(CancellationToken cancellationToken = default)
    {
        var step = new MacroStep
        {
            Kind = MacroStepKind.Delay,
            DelayMilliseconds = (int)Math.Round(NewMacroDelayMilliseconds),
        };
        await AddMacroStepsAsync([step], cancellationToken);
    }

    public async Task AddMacroInputStepAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetEditableMacro(out var selected))
        {
            return;
        }

        if (_macroStepCaptureCancellation is not null)
        {
            _macroStepCaptureCancellation.Cancel();
            return;
        }

        CancelMacroCapture();
        var input = await CaptureMacroInputAsync(cancellationToken);
        if (input is null || !ReferenceEquals(SelectedMacro, selected))
        {
            StatusText = AppStrings.Text("MacroStepCaptureCanceled");
            return;
        }

        await AddMacroStepsAsync([MacroStepFactory.CreateTap(input)], cancellationToken);
    }

    public async Task AddMacroStepFromPickerAsync(CancellationToken cancellationToken = default)
    {
        if (KeyPicker.Selected is not { } option)
        {
            return;
        }

        await AddMacroStepsAsync(
            [MacroStepFactory.CreateTap(option.Input)],
            cancellationToken);
    }

    public async Task AddMacroTextStepAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var trimmed = text.Length > MacroStep.MaxTextLength
            ? text[..MacroStep.MaxTextLength]
            : text;
        await AddMacroStepsAsync(
            [new MacroStep { Kind = MacroStepKind.Text, Text = trimmed }],
            cancellationToken);
    }

    public async Task AddMacroRunCommandStepAsync(
        string command,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        await AddMacroStepsAsync(
            [new MacroStep
            {
                Kind = MacroStepKind.RunCommand,
                Command = command.Length > MacroStep.MaxCommandLength
                    ? command[..MacroStep.MaxCommandLength]
                    : command,
                Arguments = arguments.Length > MacroStep.MaxArgumentsLength
                    ? arguments[..MacroStep.MaxArgumentsLength]
                    : arguments,
                WorkingDirectory = workingDirectory.Length > MacroStep.MaxWorkingDirectoryLength
                    ? workingDirectory[..MacroStep.MaxWorkingDirectoryLength]
                    : workingDirectory,
            }],
            cancellationToken);
    }

    public async Task AddMacroLoopStepAsync(CancellationToken cancellationToken = default)
    {
        var child = new MacroStep
        {
            Kind = MacroStepKind.Delay,
            DelayMilliseconds = (int)Math.Round(NewMacroDelayMilliseconds),
        };
        var loop = new MacroStep
        {
            Kind = MacroStepKind.Loop,
            LoopCount = 2,
            Children = [child],
        };
        await AddMacroStepsAsync([loop], cancellationToken);
    }

    public async Task AddMacroReferenceStepAsync(
        string? macroId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(macroId))
        {
            return;
        }

        await AddMacroStepsAsync(
            [new MacroStep { Kind = MacroStepKind.MacroReference, MacroId = macroId }],
            cancellationToken);
    }

    public async Task AddMacroComboStepAsync(
        MacroModifierKeys modifiers,
        MacroInput key,
        CancellationToken cancellationToken = default)
    {
        await AddMacroStepsAsync(
            MacroStepFactory.CreateCombo(modifiers, key).ToArray(),
            cancellationToken);
    }

    private async Task<bool> AddMacroStepsAsync(
        IReadOnlyList<MacroStep> newSteps,
        CancellationToken cancellationToken)
    {
        if (!TryGetEditableMacro(out var selected) || newSteps.Count == 0)
        {
            return false;
        }

        if (MacroStepTree.CountSteps(selected.Definition.Steps) + newSteps.Count >
            MacroDefinition.MaxSteps)
        {
            StatusText = AppStrings.Text("MacroStepLimit");
            return false;
        }

        if (!MacroValidator.ValidateSteps(newSteps))
        {
            StatusText = AppStrings.Text("MacroStepIncomplete");
            return false;
        }

        var previousStep = SelectedStep?.Step;
        var wasDirty = _dirtyMacroIds.Contains(selected.Id);
        var target = GetMacroInsertTarget(selected);
        var insertIndex = target.Index;
        foreach (var step in newSteps)
        {
            target.Owner.Insert(insertIndex++, step);
        }

        MarkMacroDirty(selected);
        if (ReferenceEquals(SelectedMacro, selected))
        {
            RefreshMacroSteps(selectStep: newSteps[0]);
        }

        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            for (var index = newSteps.Count - 1; index >= 0; index--)
            {
                var addedIndex = target.Owner.IndexOf(newSteps[index]);
                if (addedIndex >= 0)
                {
                    target.Owner.RemoveAt(addedIndex);
                }
            }

            RestoreMacroDirtyState(selected.Id, wasDirty);
            selected.Refresh();
            if (ReferenceEquals(SelectedMacro, selected))
            {
                RefreshMacroSteps(selectStep: previousStep);
            }

            return false;
        }

        selected.Refresh();
        RefreshMacroRuntime();
        StatusText = AppStrings.Text("MacroStepAdded");
        return true;
    }

    private MacroInsertTarget GetMacroInsertTarget(MacroRowViewModel selected)
    {
        if (SelectedStep is { } selectedStep)
        {
            // Selecting a loop means "insert inside this loop". Selecting a
            // normal action inserts immediately after it.
            if (selectedStep.Step.Kind == MacroStepKind.Loop)
            {
                return new MacroInsertTarget(
                    selectedStep.Step.Children,
                    selectedStep.Step.Children.Count);
            }

            var owner = selectedStep.ParentLoop is { } parentLoop
                ? (IList<MacroStep>)parentLoop.Children
                : selected.Definition.Steps;
            var index = owner.IndexOf(selectedStep.Step);
            return new MacroInsertTarget(
                owner,
                index < 0 ? owner.Count : index + 1);
        }

        return new MacroInsertTarget(selected.Definition.Steps, selected.Definition.Steps.Count);
    }

    private readonly record struct MacroInsertTarget(IList<MacroStep> Owner, int Index);

    public async Task RecaptureMacroStepInputAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetEditableMacro(out var selected) ||
            SelectedStep is not { } selectedStep ||
            selectedStep.Step.Kind != MacroStepKind.Input ||
            selectedStep.HasGroup)
        {
            if (SelectedStep?.HasGroup == true)
            {
                StatusText = AppStrings.Text("MacroGroupRecaptureBlocked");
            }
            return;
        }

        if (_macroStepCaptureCancellation is not null)
        {
            _macroStepCaptureCancellation.Cancel();
            return;
        }

        CancelMacroCapture();
        var input = await CaptureMacroInputAsync(cancellationToken);
        if (input is null || !ReferenceEquals(SelectedStep, selectedStep))
        {
            StatusText = AppStrings.Text("MacroStepCaptureCanceled");
            return;
        }

        var previousInput = selectedStep.Step.Input?.Clone();
        var previousIsDown = selectedStep.Step.IsDown;
        var wasDirty = _dirtyMacroIds.Contains(selected.Id);
        selectedStep.Step.Input = input.Clone();
        selectedStep.Step.IsDown = input.Kind != MacroInputKind.MouseWheel;
        selectedStep.Refresh();
        MarkMacroDirty(selected);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selectedStep.Step.Input = previousInput;
            selectedStep.Step.IsDown = previousIsDown;
            selectedStep.Refresh();
            RestoreMacroDirtyState(selected.Id, wasDirty);
            return;
        }

        RefreshMacroRuntime();
        StatusText = AppStrings.FormatText(
            "MacroStepCaptured",
            MacroRowViewModel.DescribeInput(input));
    }

    public int CountMacroStepDeleteTargets()
    {
        if (SelectedMacro is not { } selected || SelectedStep is not { } row)
        {
            return 0;
        }

        if (!row.HasGroup)
        {
            return 1;
        }

        return MacroStepTree.Enumerate(selected.Definition.Steps)
            .Count(step => StringComparer.Ordinal.Equals(step.GroupId, row.Step.GroupId));
    }

    public Task DeleteMacroStepAsync(
        MacroStepRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        if (row is null || !Steps.Contains(row))
        {
            return Task.CompletedTask;
        }

        SelectedStep = row;
        return DeleteSelectedMacroStepAsync(cancellationToken);
    }

    public async Task DeleteSelectedMacroStepAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetEditableMacro(out var selected) ||
            SelectedStep is not { } row)
        {
            return;
        }

        var index = row.Index;
        var wasDirty = _dirtyMacroIds.Contains(selected.Id);
        var snapshot = MacroDocumentService.CloneSteps(selected.Definition.Steps);
        if (row.HasGroup && row.Step.GroupId is { } groupId)
        {
            RemoveGroupSteps(selected.Definition.Steps, groupId);
        }
        else if (row.Owner.Remove(row.Step))
        {
            MacroStepTree.RemoveEmptyLoops(selected.Definition.Steps);
        }

        MarkMacroDirty(selected);
        RefreshMacroSteps(Math.Max(0, index - 1));
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selected.Definition.Steps = snapshot;
            RestoreMacroDirtyState(selected.Id, wasDirty);
            selected.Refresh();
            RefreshMacroSteps(Math.Max(0, index - 1));
            return;
        }

        selected.Refresh();
        RefreshMacroRuntime();
        StatusText = AppStrings.Text("MacroStepDeleted");
    }

    public async Task MoveSelectedMacroStepAsync(
        int offset,
        CancellationToken cancellationToken = default)
    {
        if (offset == 0 ||
            !TryGetEditableMacro(out var selected) ||
            SelectedStep is not { } row)
        {
            return;
        }

        var index = row.Index;
        var wasDirty = _dirtyMacroIds.Contains(selected.Id);
        var snapshot = MacroDocumentService.CloneSteps(selected.Definition.Steps);
        var owner = row.Owner;
        if (row.HasGroup && row.Step.GroupId is { } groupId)
        {
            if (!MacroStepTree.MoveGroup(owner, groupId, offset))
            {
                return;
            }
        }
        else
        {
            var ownerIndex = owner.IndexOf(row.Step);
            var target = ownerIndex + offset;
            if (ownerIndex < 0 || target < 0 || target >= owner.Count)
            {
                return;
            }

            owner.RemoveAt(ownerIndex);
            owner.Insert(target, row.Step);
        }

        MarkMacroDirty(selected);
        RefreshMacroSteps(selectStep: row.Step);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selected.Definition.Steps = snapshot;
            RestoreMacroDirtyState(selected.Id, wasDirty);
            selected.Refresh();
            RefreshMacroSteps(Math.Max(0, index));
            return;
        }

        selected.Refresh();
        RefreshMacroRuntime();
        StatusText = AppStrings.Text("MacroStepMoved");
    }

    private async Task<MacroInput?> CaptureMacroInputAsync(CancellationToken cancellationToken)
    {
        using var capture = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _macroStepCaptureCancellation = capture;
        IsMacroStepCapturing = true;
        StatusText = AppStrings.Text("MacroStepCapturePrompt");
        try
        {
            return await _macroRuntime.CaptureNextInputAsync(capture.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_macroStepCaptureCancellation, capture))
            {
                _macroStepCaptureCancellation = null;
                IsMacroStepCapturing = false;
            }
        }
    }

    public void CancelMacroStepCapture()
    {
        _macroStepCaptureCancellation?.Cancel();
        _macroStepCaptureCancellation = null;
        IsMacroStepCapturing = false;
    }

    private static int RemoveGroupSteps(List<MacroStep> steps, string groupId)
    {
        var removed = 0;
        for (var index = steps.Count - 1; index >= 0; index--)
        {
            var step = steps[index];
            if (step.Kind == MacroStepKind.Loop)
            {
                removed += RemoveGroupSteps(step.Children, groupId);
            }

            if (StringComparer.Ordinal.Equals(step.GroupId, groupId))
            {
                steps.RemoveAt(index);
                removed++;
            }
        }

        MacroStepTree.RemoveEmptyLoops(steps);
        return removed;
    }

    private bool TryGetEditableMacro(out MacroRowViewModel selected)
    {
        selected = SelectedMacro!;
        if (SelectedMacro is null)
        {
            return false;
        }

        if (IsMacroRecording)
        {
            StatusText = AppStrings.Text("MacroRecordingBlocked");
            return false;
        }

        if (IsMacroCountdown)
        {
            StatusText = AppStrings.Text("MacroCountdownBlocked");
            return false;
        }

        return true;
    }
}
