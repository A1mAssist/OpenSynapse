using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly MacroRuntime _macroRuntime = new();
    private MacroRowViewModel? _selectedMacro;
    private string _macroStatusText = AppStrings.Text("MacroStatusIdle");
    private bool _isMacroRecording;
    private bool _isMacroRecordingDelays = true;
    private bool _isMacroTriggerCapturing;
    private bool _isMacroStepCapturing;
    private CancellationTokenSource? _macroCaptureCancellation;
    private CancellationTokenSource? _macroStepCaptureCancellation;
    private MacroStepRowViewModel? _selectedMacroStep;
    private double _newMacroDelayMilliseconds = 100;
    private DispatcherQueue? _macroDispatcherQueue;

    public ObservableCollection<MacroRowViewModel> Macros { get; } = new();
    public ObservableCollection<MacroStepRowViewModel> MacroSteps { get; } = new();

    /// <summary>
    /// The on-screen key picker, so a step can be added without capturing input.
    /// </summary>
    public MacroKeyPickerViewModel MacroKeyPicker { get; } = new();

    public MacroRowViewModel? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (ReferenceEquals(_selectedMacro, value))
            {
                return;
            }

            CancelMacroCapture();
            CancelMacroStepCapture();
            _selectedMacro = value;
            _selectedMacroStep = null;
            RefreshMacroSteps(0);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMacroStep));
            NotifySelectedMacroChanged();
        }
    }

    public Visibility MacroEditorVisibility =>
        SelectedMacro is null ? Visibility.Collapsed : Visibility.Visible;

    public bool CanEditMacro => SelectedMacro is not null;

    public MacroStepRowViewModel? SelectedMacroStep
    {
        get => _selectedMacroStep;
        set
        {
            if (ReferenceEquals(_selectedMacroStep, value))
            {
                return;
            }

            _selectedMacroStep = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacroStepEditorVisibility));
            OnPropertyChanged(nameof(CanEditMacroStep));
        }
    }

    public Visibility MacroStepEditorVisibility =>
        SelectedMacroStep is null ? Visibility.Collapsed : Visibility.Visible;

    public bool CanEditMacroStep => SelectedMacroStep is not null;

    public Visibility MacroNoSelectionVisibility =>
        SelectedMacro is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MacroListEmptyVisibility =>
        Macros.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MacroTriggerUnsetVisibility =>
        SelectedMacro is null || SelectedMacro.Definition.Trigger is null
            ? Visibility.Visible
            : Visibility.Collapsed;
    public Visibility MacroTriggerBoundVisibility =>
        SelectedMacro is null || SelectedMacro.Definition.Trigger is null
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility MacroEmptyStepsVisibility =>
        SelectedMacro is null || SelectedMacro.Definition.Steps.Count > 0
            ? Visibility.Collapsed
            : Visibility.Visible;

    /// <summary>
    /// Playback time of the whole macro, the number Razer shows below the step
    /// list.
    /// </summary>
    public string MacroTotalDurationText => SelectedMacro is { } selected
        ? AppStrings.FormatText(
            "MacroDurationText",
            (int)Math.Round(MeasureDurationMilliseconds(selected.Definition)))
        : string.Empty;

    private static double MeasureDurationMilliseconds(MacroDefinition macro)
    {
        var total = 0d;
        foreach (var step in macro.Steps)
        {
            total += step.DelayMilliseconds;
            if (step.Kind == MacroStepKind.Input && step.IsDown)
            {
                total += step.HoldMilliseconds;
            }
        }

        return total * Math.Max(1, macro.PlaybackMode == MacroPlaybackMode.Repeat
            ? macro.RepeatCount
            : 1);
    }

    public bool IsMacroRecordingDelays
    {
        get => _isMacroRecordingDelays;
        set => SetField(ref _isMacroRecordingDelays, value);
    }

    public double NewMacroDelayMilliseconds
    {
        get => _newMacroDelayMilliseconds;
        set => SetField(
            ref _newMacroDelayMilliseconds,
            Math.Clamp(value, 0, MacroDefinition.MaxDelayMilliseconds));
    }

    public bool IsMacroStepCapturing
    {
        get => _isMacroStepCapturing;
        private set => SetField(ref _isMacroStepCapturing, value);
    }


    public string MacroStatusText
    {
        get => _macroStatusText;
        private set => SetField(ref _macroStatusText, value);
    }

    public string MacroRecordButtonText => AppStrings.Text(
        _isMacroRecording ? "MacroStopRecording" : "MacroStartRecording");

    public string MacroCaptureButtonText => AppStrings.Text(
        _isMacroTriggerCapturing ? "MacroCancelCapture" : "MacroCaptureTrigger");
    public string MacroPlayButtonText => AppStrings.Text("MacroPlayButton");

    public bool IsMacroRecording
    {
        get => _isMacroRecording;
        private set
        {
            if (SetField(ref _isMacroRecording, value))
            {
                OnPropertyChanged(nameof(MacroRecordButtonText));
            }
        }
    }



    public bool IsMacroTriggerCapturing
    {
        get => _isMacroTriggerCapturing;
        private set
        {
            if (SetField(ref _isMacroTriggerCapturing, value))
            {
                OnPropertyChanged(nameof(MacroCaptureButtonText));
            }
        }
    }

    internal void InitializeMacroRuntime()
    {
        RefreshMacroState();
    }

    internal void SetMacroDispatcher(DispatcherQueue dispatcherQueue) =>
        _macroDispatcherQueue = dispatcherQueue;

    internal void SetMacroOwnWindowForegroundPredicate(Func<bool>? predicate) =>
        _macroRuntime.SetOwnWindowForegroundPredicate(predicate);

    internal void RefreshMacroState()
    {
        CancelMacroCapture();
        CancelMacroStepCapture();
        if (IsMacroRecording)
        {
            var steps = _macroRuntime.StopRecording();
            if (SelectedMacro is { } recording)
            {
                recording.SetSteps(steps);
            }
            IsMacroRecording = false;
        }

        var selectedId = SelectedMacro?.Id;
        Macros.Clear();
        foreach (var macro in GetActiveProfile().Macros)
        {
            Macros.Add(CreateMacroRow(macro));
        }

        SelectedMacro = Macros.FirstOrDefault(row =>
                StringComparer.Ordinal.Equals(row.Id, selectedId))
            ?? Macros.FirstOrDefault();
        RefreshMacroRuntime();
        if (SelectedMacro is null)
        {
            MacroStatusText = AppStrings.Text("MacroStatusIdle");
        }
        OnPropertyChanged(nameof(MacroEditorVisibility));
        NotifySelectedMacroChanged();
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
    }

    public async Task CreateMacroAsync(CancellationToken cancellationToken = default)
    {
        CancelMacroCapture();
        CancelMacroStepCapture();
        var macro = new MacroDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = AppStrings.FormatText("MacroDefaultName", Macros.Count + 1),
            Enabled = true,
            Steps = [],
        };
        var profile = GetActiveProfile();
        profile.Macros.Add(macro);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            profile.Macros.Remove(macro);
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        var row = CreateMacroRow(macro);
        Macros.Add(row);
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        SelectedMacro = row;
        RefreshMacroRuntime();
        MacroStatusText = AppStrings.Text("MacroCreated");
    }

    public async Task DeleteSelectedMacroAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        CancelMacroCapture();
        CancelMacroStepCapture();
        var profile = GetActiveProfile();
        var index = profile.Macros.IndexOf(selected.Definition);
        if (index < 0)
        {
            return;
        }

        profile.Macros.RemoveAt(index);
        Macros.Remove(selected);
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        SelectedMacro = Macros.FirstOrDefault();
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            profile.Macros.Insert(index, selected.Definition);
            Macros.Insert(Math.Min(index, Macros.Count), selected);
            SelectedMacro = selected;
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        RefreshMacroRuntime();
        MacroStatusText = AppStrings.Text("MacroDeleted");
    }

    public async Task SaveSelectedMacroAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        CancelMacroCapture();
        CancelMacroStepCapture();
        selected.Definition.ApplySafeDefaults();
        if (string.IsNullOrWhiteSpace(selected.Definition.Name))
        {
            selected.Definition.Name = AppStrings.FormatText(
                "MacroDefaultName",
                Math.Max(1, Macros.IndexOf(selected) + 1));
        }
        selected.Refresh();
        RefreshMacroSteps();
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        RefreshMacroRuntime();
        MacroStatusText = AppStrings.Text("MacroSaved");
    }

    public async Task ToggleMacroRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        if (IsMacroRecording)
        {
            var previousSteps = selected.Definition.Steps.ToList();
            var steps = _macroRuntime.StopRecording();
            selected.SetSteps(steps);
            IsMacroRecording = false;
            RefreshMacroSteps();
            if (!await TrySaveMacroProfileAsync(cancellationToken))
            {
                selected.SetSteps(previousSteps);
                RefreshMacroSteps();
                MacroStatusText = AppStrings.Text("MacroSaveFailed");
                return;
            }

            RefreshMacroRuntime();
            MacroStatusText = AppStrings.FormatText("MacroRecorded", steps.Count);
            return;
        }

        CancelMacroCapture();
        CancelMacroStepCapture();
        try
        {
            _macroRuntime.StartRecording(IsMacroRecordingDelays);
        }
        catch (Exception exception)
        {
            MacroStatusText = AppStrings.FormatText("MacroRuntimeError", exception.Message);
            return;
        }

        IsMacroRecording = true;
        MacroStatusText = AppStrings.Text("MacroRecording");
    }

    public async Task CaptureMacroTriggerAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        if (IsMacroRecording)
        {
            MacroStatusText = AppStrings.Text("MacroRecording");
            return;
        }

        CancelMacroStepCapture();
        if (_macroCaptureCancellation is not null)
        {
            _macroCaptureCancellation.Cancel();
            return;
        }

        using var capture = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _macroCaptureCancellation = capture;
        IsMacroTriggerCapturing = true;
        MacroStatusText = AppStrings.Text("MacroCapturePrompt");
        try
        {
            var trigger = await _macroRuntime.CaptureNextTriggerAsync(capture.Token);
            if (trigger is null)
            {
                MacroStatusText = AppStrings.Text("MacroCaptureCanceled");
                return;
            }

            var previousTrigger = selected.Definition.Trigger;
            selected.SetTrigger(trigger);
            if (!await TrySaveMacroProfileAsync(cancellationToken))
            {
                selected.SetTrigger(previousTrigger);
                MacroStatusText = AppStrings.Text("MacroSaveFailed");
                return;
            }

            RefreshMacroRuntime();
            MacroStatusText = AppStrings.FormatText(
                "MacroTriggerCaptured",
                MacroRowViewModel.DescribeInput(trigger.Input));
        }
        catch (OperationCanceledException)
        {
            MacroStatusText = AppStrings.Text("MacroCaptureCanceled");
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

    public async Task AddMacroDelayAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        CancelMacroStepCapture();
        if (selected.Definition.Steps.Count >= MacroDefinition.MaxSteps)
        {
            MacroStatusText = AppStrings.Text("MacroStepLimit");
            return;
        }

        var index = selected.Definition.Steps.Count;
        selected.Definition.Steps.Add(new MacroStep
        {
            Kind = MacroStepKind.Delay,
            DelayMilliseconds = (int)Math.Round(NewMacroDelayMilliseconds),
        });
        RefreshMacroSteps(index);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selected.Definition.Steps.RemoveAt(index);
            RefreshMacroSteps(Math.Max(0, index - 1));
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        RefreshMacroRuntime();
        MacroStatusText = AppStrings.Text("MacroStepAdded");
    }

    public async Task AddMacroInputStepAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        if (IsMacroRecording)
        {
            MacroStatusText = AppStrings.Text("MacroRecording");
            return;
        }

        if (_macroStepCaptureCancellation is not null)
        {
            _macroStepCaptureCancellation.Cancel();
            return;
        }

        CancelMacroCapture();
        var input = await CaptureMacroInputAsync(cancellationToken);
        if (input is null)
        {
            MacroStatusText = AppStrings.Text("MacroStepCaptureCanceled");
            return;
        }

        // One step is one key press. The runtime releases the key again, so a
        // press and its release can never drift apart.
        var newSteps = new List<MacroStep>
        {
            new()
            {
                Kind = MacroStepKind.Input,
                Input = input.Clone(),
                IsDown = input.Kind != MacroInputKind.MouseWheel,
                HoldMilliseconds = MacroStep.DefaultHoldMilliseconds,
            },
        };
        if (selected.Definition.Steps.Count + newSteps.Count > MacroDefinition.MaxSteps)
        {
            MacroStatusText = AppStrings.Text("MacroStepLimit");
            return;
        }

        var index = selected.Definition.Steps.Count;
        selected.Definition.Steps.AddRange(newSteps);
        RefreshMacroSteps(index);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selected.Definition.Steps.RemoveRange(index, newSteps.Count);
            RefreshMacroSteps(Math.Max(0, index - 1));
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        RefreshMacroRuntime();
        MacroStatusText = AppStrings.Text("MacroStepAdded");
    }

    public async Task AddMacroStepFromPickerAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected ||
            MacroKeyPicker.Selected is not { } option)
        {
            return;
        }

        if (selected.Definition.Steps.Count >= MacroDefinition.MaxSteps)
        {
            MacroStatusText = AppStrings.Text("MacroStepLimit");
            return;
        }

        var index = selected.Definition.Steps.Count;
        selected.Definition.Steps.Add(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = option.Input.Clone(),
            IsDown = option.Input.Kind != MacroInputKind.MouseWheel,
            HoldMilliseconds = MacroStep.DefaultHoldMilliseconds,
        });
        RefreshMacroSteps(index);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selected.Definition.Steps.RemoveAt(index);
            RefreshMacroSteps(Math.Max(0, index - 1));
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        RefreshMacroRuntime();
        MacroStatusText = AppStrings.Text("MacroStepAdded");
    }

    public async Task RecaptureMacroStepInputAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacroStep is not { } selectedStep || selectedStep.IsDelay)
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
        if (input is null)
        {
            MacroStatusText = AppStrings.Text("MacroStepCaptureCanceled");
            return;
        }

        var previous = selectedStep.Step.Input;
        selectedStep.Step.Input = input.Clone();
        if (input.Kind == MacroInputKind.MouseWheel)
        {
            // A wheel event has no press or release, so the step becomes one.
            selectedStep.Step.IsDown = false;
        }
        selectedStep.Refresh();
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selectedStep.Step.Input = previous;
            selectedStep.Refresh();
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        MacroStatusText = AppStrings.FormatText(
            "MacroStepCaptured",
            MacroRowViewModel.DescribeInput(input));
    }

    public async Task DeleteSelectedMacroStepAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected ||
            SelectedMacroStep is not { } selectedStep)
        {
            return;
        }

        CancelMacroStepCapture();
        var index = selected.Definition.Steps.IndexOf(selectedStep.Step);
        if (index < 0)
        {
            return;
        }

        var removed = selected.Definition.Steps[index];
        selected.Definition.Steps.RemoveAt(index);
        RefreshMacroSteps(index);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selected.Definition.Steps.Insert(index, removed);
            RefreshMacroSteps(index);
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        RefreshMacroRuntime();
        MacroStatusText = AppStrings.Text("MacroStepDeleted");
    }

    public async Task MoveSelectedMacroStepAsync(int offset, CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected ||
            SelectedMacroStep is not { } selectedStep)
        {
            return;
        }

        CancelMacroStepCapture();
        var index = selected.Definition.Steps.IndexOf(selectedStep.Step);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= selected.Definition.Steps.Count)
        {
            return;
        }

        var step = selected.Definition.Steps[index];
        selected.Definition.Steps.RemoveAt(index);
        selected.Definition.Steps.Insert(target, step);
        RefreshMacroSteps(target);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            selected.Definition.Steps.RemoveAt(target);
            selected.Definition.Steps.Insert(index, step);
            RefreshMacroSteps(index);
            MacroStatusText = AppStrings.Text("MacroSaveFailed");
            return;
        }

        MacroStatusText = AppStrings.Text("MacroStepMoved");
    }

    private async Task<MacroInput?> CaptureMacroInputAsync(CancellationToken cancellationToken)
    {
        using var capture = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _macroStepCaptureCancellation = capture;
        IsMacroStepCapturing = true;
        MacroStatusText = AppStrings.Text("MacroStepCapturePrompt");
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

    private void CancelMacroStepCapture()
    {
        _macroStepCaptureCancellation?.Cancel();
        _macroStepCaptureCancellation = null;
        IsMacroStepCapturing = false;
    }

    public async Task PlaySelectedMacroAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected || selected.Definition.Steps.Count == 0)
        {
            MacroStatusText = AppStrings.Text("MacroEmpty");
            return;
        }

        CancelMacroCapture();
        CancelMacroStepCapture();
        MacroStatusText = AppStrings.Text("MacroPlaying");
        try
        {
            await _macroRuntime.PlayAsync(selected.Definition, cancellationToken);
            MacroStatusText = AppStrings.Text("MacroPlayed");
        }
        catch (OperationCanceledException)
        {
            MacroStatusText = AppStrings.Text("MacroPlayCanceled");
        }
        catch (Exception exception)
        {
            MacroStatusText = AppStrings.FormatText("MacroRuntimeError", exception.Message);
        }
    }

    private async Task<bool> TrySaveMacroProfileAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await SaveProfileAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void SetMacroRuntimeError(string message)
    {
        if (_macroDispatcherQueue is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(() =>
                MacroStatusText = AppStrings.FormatText("MacroRuntimeError", message));
            return;
        }

        MacroStatusText = AppStrings.FormatText("MacroRuntimeError", message);
    }

    private MacroRowViewModel CreateMacroRow(MacroDefinition macro)
    {
        var row = new MacroRowViewModel(macro);
        row.EnabledChanged += (_, _) => RefreshMacroRuntime();
        return row;
    }

    private void RefreshMacroRuntime()
    {
        _macroRuntime.UpdateMacros(GetActiveProfile().Macros);
    }

    private void RefreshMacroSteps(int? selectedIndex = null)
    {
        var indexToSelect = selectedIndex ?? SelectedMacroStep?.Index ?? -1;
        MacroSteps.Clear();
        if (SelectedMacro is not { } selected)
        {
            SelectedMacroStep = null;
            return;
        }

        selected.Refresh();
        for (var index = 0; index < selected.Definition.Steps.Count; index++)
        {
            MacroSteps.Add(new MacroStepRowViewModel(index, selected.Definition.Steps[index]));
        }

        SelectedMacroStep = MacroSteps.Count == 0
            ? null
            : MacroSteps.ElementAtOrDefault(
                Math.Clamp(indexToSelect >= 0 ? indexToSelect : 0, 0, MacroSteps.Count - 1));
        OnPropertyChanged(nameof(MacroEmptyStepsVisibility));
        OnPropertyChanged(nameof(MacroTotalDurationText));
    }

    private void NotifySelectedMacroChanged()
    {
        OnPropertyChanged(nameof(MacroEditorVisibility));
        OnPropertyChanged(nameof(CanEditMacro));
        OnPropertyChanged(nameof(MacroNoSelectionVisibility));
        OnPropertyChanged(nameof(MacroTriggerBoundVisibility));
        OnPropertyChanged(nameof(MacroTriggerUnsetVisibility));
        OnPropertyChanged(nameof(MacroEmptyStepsVisibility));
        OnPropertyChanged(nameof(MacroTotalDurationText));
    }
    private void CancelMacroCapture()
    {
        _macroCaptureCancellation?.Cancel();
        _macroCaptureCancellation = null;
        IsMacroTriggerCapturing = false;
    }
}



