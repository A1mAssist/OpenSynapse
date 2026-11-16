using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class MacroWorkspace : INotifyPropertyChanged
{
    private readonly MacroRuntime _macroRuntime;
    private readonly Func<ProfileDefinition> _getActiveProfile;
    private readonly Func<CancellationToken, Task<bool>> _saveProfileAsync;
    private readonly LocalDiagnosticLog _diagnosticLog;
    private readonly SemaphoreSlim _macroSaveGate = new(1, 1);
    private readonly HashSet<string> _dirtyMacroIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _macroEditVersions = new(StringComparer.Ordinal);
    private MacroRowViewModel? _selectedMacro;
    private string _macroStatusText = AppStrings.Text("MacroStatusIdle");
    private bool _isMacroRecording;
    private bool _isMacroCountdown;
    private string? _recordingMacroId;
    private bool _recordingAppend;
    private bool _recordingStopSubscribed;
    private bool _isMacroTriggerCapturing;
    private bool _isMacroStepCapturing;
    private CancellationTokenSource? _macroCaptureCancellation;
    private CancellationTokenSource? _macroPlaybackShortcutCaptureCancellation;
    private CancellationTokenSource? _macroStepCaptureCancellation;
    private CancellationTokenSource? _macroCountdownCancellation;
    private CancellationTokenSource? _macroPlaybackCountdownCancellation;
    private bool _isMacroPlaybackCountdown;
    private MacroStepRowViewModel? _selectedMacroStep;
    private double _newMacroDelayMilliseconds = 100;
    private MacroDelayMode _macroRecordingDelayMode = MacroDelayMode.Recorded;
    private double _macroRecordingFixedDelayMilliseconds = 100;
    private MacroMouseTrackingMode _macroRecordingMouseTrackingMode = MacroMouseTrackingMode.None;
    private bool _isMacroPlaying;
    private bool _isMacroPaused;
    private bool _isSavingMacroEnabled;
    private DispatcherQueue? _macroDispatcherQueue;

    public MacroWorkspace(
        Func<ProfileDefinition> getActiveProfile,
        Func<CancellationToken, Task<bool>> saveProfileAsync,
        LocalDiagnosticLog diagnosticLog,
        IMacroInputSink? macroInputSink = null)
    {
        _getActiveProfile = getActiveProfile;
        _saveProfileAsync = saveProfileAsync;
        _diagnosticLog = diagnosticLog;
        _macroRuntime = new MacroRuntime(
            sink: macroInputSink,
            log: message => _diagnosticLog.TryWrite("macro-input", message));
        _macroRuntime.Error += SetMacroRuntimeError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<string, string>? MacroContinuousPlaybackStarted;

    /// <summary>
    /// Writes a software macro's original key assignment back to its device.
    /// The device workspace owns the transport; the macro workspace only owns
    /// the lifetime of the binding.
    /// </summary>
    public Func<MacroSoftwareBinding, CancellationToken, Task<bool>>? SoftwareBindingRestorer { get; set; }

    public ObservableCollection<MacroRowViewModel> Items { get; } = new();
    public ObservableCollection<MacroStepRowViewModel> Steps { get; } = new();

    /// <summary>
    /// The on-screen key picker, so a step can be added without capturing input.
    /// </summary>
    public MacroKeyPickerViewModel KeyPicker { get; } = new();

    public MacroRowViewModel? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (ReferenceEquals(_selectedMacro, value))
            {
                return;
            }

            if (IsMacroRecording)
            {
                FinishRecordingTake(save: true);
            }

            CancelMacroCapture();
            CancelMacroStepCapture();
            CancelMacroPlaybackShortcutCapture();
            CancelMacroCountdown();
            CancelMacroPlaybackCountdown();
            _selectedMacro = value;
            StatusText = string.Empty;
            _selectedMacroStep = null;
            RefreshMacroSteps(0);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedStep));
            OnPropertyChanged(nameof(MacroReferenceCandidates));
            NotifySelectedMacroChanged();
        }
    }

    public Visibility MacroEditorVisibility =>
        SelectedMacro is null ? Visibility.Collapsed : Visibility.Visible;

    public bool CanEditMacro =>
        SelectedMacro is not null && !IsMacroRecording && !IsMacroCountdown;

    public MacroStepRowViewModel? SelectedStep
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
        SelectedStep is null ? Visibility.Collapsed : Visibility.Visible;

    public bool CanEditMacroStep => SelectedStep is not null && CanEditMacro;

    public Visibility MacroNoSelectionVisibility =>
        SelectedMacro is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MacroListEmptyVisibility =>
        Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

    public Visibility MacroUnsavedVisibility =>
        HasUnsavedMacroChanges ? Visibility.Visible : Visibility.Collapsed;

    public bool HasUnsavedMacroChanges => _dirtyMacroIds.Count > 0;

    /// <summary>
    /// Playback time of the whole macro, including nested loops, referenced
    /// macros and the playback speed the runtime will actually use.
    /// </summary>
    public string MacroTotalDurationText
    {
        get
        {
            if (SelectedMacro is not { } selected)
            {
                return string.Empty;
            }

            try
            {
                var macros = _getActiveProfile().Macros
                    .GroupBy(macro => macro.Id, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                var plan = MacroPlaybackPlan.Build(selected.Definition, macros);
                if (plan.Warnings.Count > 0)
                {
                    return AppStrings.Text("MacroDurationUnavailable");
                }

                return selected.Definition.PlaybackMode == MacroPlaybackMode.RepeatUntilStopped ||
                    selected.Definition.Trigger?.Mode == MacroTriggerMode.HoldRepeat
                        ? AppStrings.Text("MacroDurationContinuous")
                        : AppStrings.FormatText("MacroDurationText", plan.TotalDurationMilliseconds);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                return AppStrings.Text("MacroDurationUnavailable");
            }
        }
    }

    public IReadOnlyList<MacroReferenceOption> MacroReferenceCandidates
    {
        get
        {
            var selectedId = SelectedMacro?.Id;
            return Items
                .Where(macro => !StringComparer.Ordinal.Equals(macro.Id, selectedId))
                .Select(macro => new MacroReferenceOption(
                    macro.Id,
                    string.IsNullOrWhiteSpace(macro.Name)
                        ? AppStrings.FormatText("MacroDefaultName", Items.IndexOf(macro) + 1)
                        : macro.Name))
                .ToList();
        }
    }

    public int MacroRecordingDelayModeIndex
    {
        get => (int)_macroRecordingDelayMode;
        set
        {
            if (!Enum.IsDefined(typeof(MacroDelayMode), value) ||
                _macroRecordingDelayMode == (MacroDelayMode)value)
            {
                return;
            }

            _macroRecordingDelayMode = (MacroDelayMode)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMacroRecordingDelays));
            OnPropertyChanged(nameof(IsMacroRecordingFixedDelay));
            OnPropertyChanged(nameof(MacroRecordingDelayModeText));
            OnPropertyChanged(nameof(MacroRecordingDelayModeAutomationName));
        }
    }

    /// <summary>
    /// Label for the delay-mode menu button; the menu itself carries the options.
    /// </summary>
    public string MacroRecordingDelayModeText => _macroRecordingDelayMode switch
    {
        MacroDelayMode.Fixed => AppStrings.Text("MacroRecordDelayFixedLabel"),
        MacroDelayMode.None => AppStrings.Text("MacroRecordDelayNoneLabel"),
        _ => AppStrings.Text("MacroRecordDelayRecordedLabel"),
    };

    /// <summary>
    /// Screen-reader name for the delay-mode button: the option label plus its value.
    /// </summary>
    public string MacroRecordingDelayModeAutomationName =>
        $"{AppStrings.Text("MacroRecordDelayModeLabel")}: {MacroRecordingDelayModeText}";

    public bool IsMacroRecordingDelays
    {
        get => _macroRecordingDelayMode != MacroDelayMode.None;
        set
        {
            var mode = value ? MacroDelayMode.Recorded : MacroDelayMode.None;
            if (_macroRecordingDelayMode == mode)
            {
                return;
            }

            _macroRecordingDelayMode = mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacroRecordingDelayModeIndex));
            OnPropertyChanged(nameof(IsMacroRecordingFixedDelay));
            OnPropertyChanged(nameof(MacroRecordingDelayModeText));
            OnPropertyChanged(nameof(MacroRecordingDelayModeAutomationName));
        }
    }

    public double MacroRecordingFixedDelayMilliseconds
    {
        get => _macroRecordingFixedDelayMilliseconds;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var fixedDelay = Math.Clamp(
                Math.Round(value),
                0,
                MacroDefinition.MaxDelayMilliseconds);
            if (Math.Abs(_macroRecordingFixedDelayMilliseconds - fixedDelay) < 0.001)
            {
                return;
            }

            _macroRecordingFixedDelayMilliseconds = fixedDelay;
            OnPropertyChanged();
        }
    }

    public bool IsMacroRecordingFixedDelay =>
        _macroRecordingDelayMode == MacroDelayMode.Fixed;

    public int MacroRecordingMouseTrackingModeIndex
    {
        get => (int)_macroRecordingMouseTrackingMode;
        set
        {
            if (!Enum.IsDefined(typeof(MacroMouseTrackingMode), value) ||
                _macroRecordingMouseTrackingMode == (MacroMouseTrackingMode)value)
            {
                return;
            }

            _macroRecordingMouseTrackingMode = (MacroMouseTrackingMode)value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MacroRecordingMouseTrackingModeText));
            OnPropertyChanged(nameof(MacroRecordingMouseTrackingModeAutomationName));
        }
    }

    /// <summary>
    /// Label for the mouse-tracking menu button; the menu itself carries the options.
    /// </summary>
    public string MacroRecordingMouseTrackingModeText => _macroRecordingMouseTrackingMode switch
    {
        MacroMouseTrackingMode.Absolute => AppStrings.Text("MacroRecordMouseAbsoluteLabel"),
        MacroMouseTrackingMode.Relative => AppStrings.Text("MacroRecordMouseRelativeLabel"),
        MacroMouseTrackingMode.StartPoint => AppStrings.Text("MacroRecordMouseStartLabel"),
        MacroMouseTrackingMode.Foreground => AppStrings.Text("MacroRecordMouseForegroundLabel"),
        _ => AppStrings.Text("MacroRecordMouseNoneLabel"),
    };

    /// <summary>
    /// Screen-reader name for the mouse-tracking button: the option label plus its value.
    /// </summary>
    public string MacroRecordingMouseTrackingModeAutomationName =>
        $"{AppStrings.Text("MacroRecordMouseModeLabel")}: {MacroRecordingMouseTrackingModeText}";

    public double NewMacroDelayMilliseconds
    {
        get => _newMacroDelayMilliseconds;
        set
        {
            if (!double.IsFinite(value))
            {
                return;
            }

            var delay = Math.Clamp(
                Math.Round(value),
                0,
                MacroDefinition.MaxDelayMilliseconds);
            if (Math.Abs(_newMacroDelayMilliseconds - delay) < 0.001)
            {
                return;
            }

            _newMacroDelayMilliseconds = delay;
            OnPropertyChanged();
        }
    }

    public bool IsMacroStepCapturing
    {
        get => _isMacroStepCapturing;
        private set => SetField(ref _isMacroStepCapturing, value);
    }

    public string StatusText
    {
        get => _macroStatusText;
        private set => SetField(ref _macroStatusText, value);
    }

    public string MacroRecordButtonText => AppStrings.Text(
        _isMacroRecording
            ? "MacroStopRecording"
            : _isMacroCountdown
                ? "MacroCancelCountdown"
                : "MacroStartRecording");

    public string MacroCaptureButtonText => AppStrings.Text(
        _isMacroTriggerCapturing ? "MacroCancelCapture" : "MacroCaptureTrigger");

    public string MacroPlayButtonText => AppStrings.Text("MacroPlayButton");

    public string MacroPlaybackShortcutText
    {
        get
        {
            var shortcuts = _getActiveProfile().Shortcuts;
            var start = shortcuts.MacroPlaybackStart is { } startInput
                ? MacroRowViewModel.DescribeInput(startInput)
                : AppStrings.Text("MacroPlaybackShortcutUnset");
            var stop = shortcuts.MacroPlaybackStop is { } stopInput
                ? MacroRowViewModel.DescribeInput(stopInput)
                : AppStrings.Text("MacroPlaybackStopShortcutDefault");
            return AppStrings.FormatText("MacroPlaybackShortcutSummary", start, stop);
        }
    }

    public string MacroPauseButtonText => AppStrings.Text(
        _isMacroPaused ? "MacroResumeButton" : "MacroPauseButton");

    public bool CanStartMacroRecording =>
        SelectedMacro is not null && !IsMacroRecording && !IsMacroCountdown &&
        !IsMacroPlaying && !IsMacroPlaybackCountdown;

    public bool CanStopMacroRecording => IsMacroRecording || IsMacroCountdown;

    public bool IsMacroRecording
    {
        get => _isMacroRecording;
        private set
        {
            if (!SetField(ref _isMacroRecording, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MacroRecordButtonText));
            OnPropertyChanged(nameof(CanEditMacro));
            OnPropertyChanged(nameof(CanEditMacroStep));
            OnPropertyChanged(nameof(CanStartMacroRecording));
            OnPropertyChanged(nameof(CanStopMacroRecording));
            OnPropertyChanged(nameof(CanPlayMacro));
            OnPropertyChanged(nameof(CanStopMacro));
        }
    }

    public bool IsMacroCountdown
    {
        get => _isMacroCountdown;
        private set
        {
            if (!SetField(ref _isMacroCountdown, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MacroRecordButtonText));
            OnPropertyChanged(nameof(CanEditMacro));
            OnPropertyChanged(nameof(CanEditMacroStep));
            OnPropertyChanged(nameof(CanStartMacroRecording));
            OnPropertyChanged(nameof(CanStopMacroRecording));
        }
    }

    public bool IsMacroPlaybackCountdown
    {
        get => _isMacroPlaybackCountdown;
        private set
        {
            if (!SetField(ref _isMacroPlaybackCountdown, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanPlayMacro));
            OnPropertyChanged(nameof(CanStopMacro));
            OnPropertyChanged(nameof(CanStartMacroRecording));
        }
    }

    public bool IsMacroTriggerCapturing
    {
        get => _isMacroTriggerCapturing;
        private set
        {
            if (!SetField(ref _isMacroTriggerCapturing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MacroCaptureButtonText));
        }
    }

    public bool IsMacroPlaying
    {
        get => _isMacroPlaying;
        private set
        {
            if (!SetField(ref _isMacroPlaying, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanPlayMacro));
            OnPropertyChanged(nameof(CanStopMacro));
            OnPropertyChanged(nameof(CanPauseMacro));
            OnPropertyChanged(nameof(CanResumeMacro));
            OnPropertyChanged(nameof(CanStartMacroRecording));
        }
    }

    public bool IsMacroPaused
    {
        get => _isMacroPaused;
        private set
        {
            if (!SetField(ref _isMacroPaused, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MacroPauseButtonText));
            OnPropertyChanged(nameof(CanPauseMacro));
            OnPropertyChanged(nameof(CanResumeMacro));
        }
    }

    public bool CanPlayMacro =>
        SelectedMacro is not null && !IsMacroRecording && !IsMacroCountdown &&
        !IsMacroPlaying && !IsMacroPlaybackCountdown &&
        SelectedMacro.Definition.Steps.Count > 0;

    public bool CanStopMacro => IsMacroPlaying || IsMacroPlaybackCountdown;

    public bool CanPauseMacro => IsMacroPlaying && !IsMacroPaused;

    public bool CanResumeMacro => IsMacroPlaying && IsMacroPaused;

    internal void InitializeMacroRuntime()
    {
        if (!_recordingStopSubscribed)
        {
            _macroRuntime.RecordingStopRequested += OnMacroRecordingStopRequested;
            _macroRuntime.PlaybackStartRequested += OnMacroPlaybackStartRequested;
            _macroRuntime.PlaybackStarted += OnMacroPlaybackStarted;
            _macroRuntime.PlaybackStateChanged += OnMacroPlaybackStateChanged;
            _recordingStopSubscribed = true;
        }

        RefreshMacroState();
    }

    internal void Dispose() => _macroRuntime.Dispose();

    internal void SetMacroDispatcher(DispatcherQueue dispatcherQueue) =>
        _macroDispatcherQueue = dispatcherQueue;

    internal void SetUnhandledRazerKeyHandler(Action<MacroInput>? handler) =>
        _macroRuntime.UnhandledRazerKeyHandler = handler;

    /// <summary>
    /// Raised whenever the macro list or a trigger changes. The device workspace
    /// listens so a mouse can enter or leave driver mode with its bindings.
    /// </summary>
    internal event Action? MacrosChanged;

    internal void SetMacroOwnWindowForegroundPredicate(Func<bool>? predicate) =>
        _macroRuntime.SetOwnWindowForegroundPredicate(predicate);

    internal void SetHyperShiftHeld(bool held) => _macroRuntime.SetHyperShiftHeld(held);

    public async Task CreateMacroAsync(CancellationToken cancellationToken = default)
    {
        CancelMacroCapture();
        CancelMacroStepCapture();
        var profile = _getActiveProfile();
        if (profile.Macros.Count >= 64)
        {
            StatusText = AppStrings.Text("MacroCountLimit");
            return;
        }

        var macro = new MacroDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = AppStrings.FormatText("MacroDefaultName", Items.Count + 1),
            Enabled = true,
            Steps = [],
        };
        profile.Macros.Add(macro);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            profile.Macros.Remove(macro);
            return;
        }

        var row = CreateMacroRow(macro);
        Items.Add(row);
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
        SelectedMacro = row;
        RefreshMacroRuntime();
        StatusText = AppStrings.Text("MacroCreated");
    }

    public async Task CopySelectedMacroAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        if (_getActiveProfile().Macros.Count >= 64)
        {
            StatusText = AppStrings.Text("MacroCountLimit");
            return;
        }

        MacroDefinition copy;
        try
        {
            copy = MacroDocumentService.Clone(selected.Definition);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            StatusText = AppStrings.FormatText("MacroCopyFailed", exception.Message);
            return;
        }

        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = CopyName(selected.Definition.Name);
        copy.Trigger = null;
        if (!ValidateMacroTree(copy))
        {
            StatusText = AppStrings.Text("MacroStepIncomplete");
            return;
        }

        copy.ApplySafeDefaults();
        if (!ValidateMacroReferences(copy))
        {
            StatusText = AppStrings.Text("MacroReferenceInvalid");
            return;
        }

        var profile = _getActiveProfile();
        profile.Macros.Add(copy);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            profile.Macros.Remove(copy);
            return;
        }

        var row = CreateMacroRow(copy);
        Items.Add(row);
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
        SelectedMacro = row;
        RefreshMacroRuntime();
        StatusText = AppStrings.Text("MacroCopied");
    }

    public async Task DeleteSelectedMacroAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
            return;
        }

        if (IsMacroRecording)
        {
            if (!await StopMacroRecordingAsync(cancellationToken))
            {
                return;
            }
        }

        CancelMacroCountdown();
        CancelMacroCapture();
        CancelMacroStepCapture();
        StopMacroPlayback();
        var profile = _getActiveProfile();
        var index = profile.Macros.IndexOf(selected.Definition);
        if (index < 0)
        {
            return;
        }

        var wasDirty = _dirtyMacroIds.Contains(selected.Id);
        if (selected.Definition.SoftwareBinding is { } binding &&
            !await RestoreSoftwareBindingAsync(binding, cancellationToken))
        {
            StatusText = AppStrings.Text("MacroTriggerRestoreFailed");
            return;
        }

        profile.Macros.RemoveAt(index);
        Items.Remove(selected);
        _dirtyMacroIds.Remove(selected.Id);
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
        SelectedMacro = Items.FirstOrDefault();
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            profile.Macros.Insert(index, selected.Definition);
            Items.Insert(Math.Min(index, Items.Count), selected);
            RestoreMacroDirtyState(selected.Id, wasDirty);
            SelectedMacro = selected;
            return;
        }

        RefreshMacroRuntime();
        StatusText = AppStrings.Text("MacroDeleted");
    }

    private string CopyName(string name)
    {
        var baseName = string.IsNullOrWhiteSpace(name)
            ? AppStrings.FormatText("MacroDefaultName", Items.Count + 1)
            : name.Trim();
        var suffix = AppStrings.Text("MacroCopySuffix");
        var maxBaseLength = Math.Max(1, MacroDefinition.MaxNameLength - suffix.Length);
        if (baseName.Length > maxBaseLength)
        {
            baseName = baseName[..maxBaseLength];
        }

        return $"{baseName}{suffix}";
    }

    internal bool TryApplyMousePath(MacroDefinition owner, MacroStepRowViewModel row, IReadOnlyList<MacroMousePoint> points)
    {
        if (!CanEditMacro || !Steps.Contains(row) ||
            !MacroStepTree.TryApplyMousePath(owner, SelectedMacro?.Definition, row.Step, points)) return false;
        row.Refresh();
        OnMacroStepChanged(owner, row.Step);
        return true;
    }

    private void OnMacroStepChanged(MacroDefinition owner, MacroStep step)
    {
        if (!_getActiveProfile().Macros.Contains(owner) || !MacroStepTree.Enumerate(owner.Steps).Contains(step)) return;
        MarkMacroDirtyId(owner.Id);
        OnPropertyChanged(nameof(MacroTotalDurationText));
    }

    private void OnMacroRowDefinitionChanged(MacroRowViewModel row)
    {
        MarkMacroDirty(row);
        OnPropertyChanged(nameof(MacroReferenceCandidates));
        if (ReferenceEquals(row, SelectedMacro))
        {
            OnPropertyChanged(nameof(MacroTriggerBoundVisibility));
            OnPropertyChanged(nameof(MacroTriggerUnsetVisibility));
            OnPropertyChanged(nameof(MacroTotalDurationText));
            OnPropertyChanged(nameof(CanPlayMacro));
            SyncExecutionModeFromSelection();
        }
    }

    private void SetMacroRuntimeError(string message)
    {
        _diagnosticLog.TryWrite("macro", $"runtime error: {message}");
        if (_macroDispatcherQueue is { HasThreadAccess: false } dispatcher)
        {
            dispatcher.TryEnqueue(() =>
                StatusText = AppStrings.FormatText("MacroRuntimeError", message));
            return;
        }

        StatusText = AppStrings.FormatText("MacroRuntimeError", message);
    }

    private MacroRowViewModel CreateMacroRow(MacroDefinition macro)
    {
        var row = new MacroRowViewModel(macro);
        row.EnabledChanged += (_, _) => RefreshMacroRuntime();
        row.DefinitionChanged += (_, _) => OnMacroRowDefinitionChanged(row);
        return row;
    }

    private void RefreshMacroRuntime()
    {
        var profile = _getActiveProfile();
        _macroRuntime.SetPlaybackShortcuts(
            profile.Shortcuts.MacroPlaybackStart,
            profile.Shortcuts.MacroPlaybackStop);
        _macroRuntime.UpdateMacros(profile.Macros);
        OnPropertyChanged(nameof(CanPlayMacro));
        OnPropertyChanged(nameof(MacroPlaybackShortcutText));
        MacrosChanged?.Invoke();
    }

    private void RefreshMacroSteps(
        int? selectedIndex = null,
        MacroStep? selectStep = null)
    {
        var oldStep = selectStep ?? SelectedStep?.Step;
        var oldIndex = selectedIndex ?? SelectedStep?.Index ?? 0;
        Steps.Clear();
        if (SelectedMacro is not { } selected)
        {
            SelectedStep = null;
            return;
        }

        selected.Refresh();
        var index = 0;
        AppendMacroStepRows(selected.Definition.Steps, parentLoop: null, depth: 0, ref index);

        var target = oldStep is null
            ? null
            : Steps.FirstOrDefault(row => ReferenceEquals(row.Step, oldStep));
        target ??= Steps.ElementAtOrDefault(
            Math.Clamp(oldIndex, 0, Math.Max(0, Steps.Count - 1)));
        SelectedStep = target;
        OnPropertyChanged(nameof(MacroEmptyStepsVisibility));
        OnPropertyChanged(nameof(MacroTotalDurationText));
        OnPropertyChanged(nameof(CanPlayMacro));
    }

    private void AppendMacroStepRows(
        IList<MacroStep> steps,
        MacroStep? parentLoop,
        int depth,
        ref int index)
    {
        foreach (var step in steps)
        {
            var owner = SelectedMacro!.Definition;
            var row = new MacroStepRowViewModel(
                step,
                steps,
                parentLoop,
                depth,
                () => OnMacroStepChanged(owner, step),
                FindMacroName);
            row.SetIndex(index++);
            Steps.Add(row);
            if (step.Kind == MacroStepKind.Loop)
            {
                AppendMacroStepRows(step.Children, step, depth + 1, ref index);
            }
        }
    }

    private string? FindMacroName(string macroId) =>
        Items.FirstOrDefault(macro =>
            StringComparer.Ordinal.Equals(macro.Id, macroId))?.Name;

    private void NotifySelectedMacroChanged()
    {
        SyncExecutionModeFromSelection();
        OnPropertyChanged(nameof(MacroEditorVisibility));
        OnPropertyChanged(nameof(CanEditMacro));
        OnPropertyChanged(nameof(CanEditMacroStep));
        OnPropertyChanged(nameof(MacroNoSelectionVisibility));
        OnPropertyChanged(nameof(MacroTriggerBoundVisibility));
        OnPropertyChanged(nameof(MacroTriggerUnsetVisibility));
        OnPropertyChanged(nameof(MacroEmptyStepsVisibility));
        OnPropertyChanged(nameof(MacroTotalDurationText));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
        OnPropertyChanged(nameof(CanStartMacroRecording));
        OnPropertyChanged(nameof(CanPlayMacro));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
