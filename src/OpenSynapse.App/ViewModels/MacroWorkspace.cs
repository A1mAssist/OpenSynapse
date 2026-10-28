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

public sealed class MacroWorkspace : INotifyPropertyChanged
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
        _macroRuntime = new MacroRuntime(sink: macroInputSink);
        _macroRuntime.Error += SetMacroRuntimeError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action<string, string>? MacroContinuousPlaybackStarted;

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

    public bool CanEditMacroPlaybackMode =>
        CanEditMacro && SelectedMacro?.IsPlaybackModeEditable == true;

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
            OnPropertyChanged(nameof(CanEditMacroPlaybackMode));
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
            OnPropertyChanged(nameof(CanEditMacroPlaybackMode));
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

    internal void SetMacroOwnWindowForegroundPredicate(Func<bool>? predicate) =>
        _macroRuntime.SetOwnWindowForegroundPredicate(predicate);

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

    internal async Task<bool> CommitMacroEditsBeforeProfileSwitchAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsMacroCountdown)
        {
            CancelMacroCountdown();
        }

        if (IsMacroRecording &&
            !await StopMacroRecordingAsync(cancellationToken))
        {
            return false;
        }

        return !HasUnsavedMacroChanges ||
            await SaveMacroChangesCoreAsync(forceSelected: false, cancellationToken);
    }

    public async Task SaveSelectedMacroAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is null)
        {
            return;
        }

        CancelMacroCapture();
        CancelMacroStepCapture();
        if (!await SaveMacroChangesCoreAsync(forceSelected: true, cancellationToken))
        {
            return;
        }

        StatusText = AppStrings.Text("MacroSaved");
    }

    public async Task SaveMacroEnabledAsync(
        MacroRowViewModel row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_isSavingMacroEnabled)
        {
            return;
        }

        _isSavingMacroEnabled = true;
        try
        {
            var enabled = row.Enabled;
            var wasDirty = _dirtyMacroIds.Contains(row.Id);
            if (IsMacroRecording || IsMacroCountdown)
            {
                row.Enabled = !enabled;
                RestoreMacroDirtyState(row.Id, wasDirty);
                return;
            }

            MarkMacroDirty(row);
            if (!await SaveMacroChangesCoreAsync(forceSelected: false, cancellationToken))
            {
                row.Enabled = !enabled;
                RestoreMacroDirtyState(row.Id, wasDirty);
                return;
            }

            StatusText = AppStrings.Text("MacroSaved");
        }
        finally
        {
            _isSavingMacroEnabled = false;
        }
    }

    public async Task<bool> SaveMacroChangesOnCloseAsync(
        CancellationToken cancellationToken = default)
    {
        if (!HasUnsavedMacroChanges)
        {
            return true;
        }

        return await SaveMacroChangesCoreAsync(forceSelected: false, cancellationToken);
    }

    private async Task<bool> SaveMacroChangesCoreAsync(
        bool forceSelected,
        CancellationToken cancellationToken)
    {
        var rows = Items
            .Where(row => _dirtyMacroIds.Contains(row.Id) ||
                (forceSelected && ReferenceEquals(row, SelectedMacro)))
            .Distinct()
            .ToList();
        if (rows.Count == 0)
        {
            return true;
        }

        if (!PrepareMacrosForSave(rows))
        {
            return false;
        }

        if (!await TrySaveMacroProfileAsync(cancellationToken, prepareDirtyMacros: false))
        {
            return false;
        }

        foreach (var row in rows)
        {
            row.Refresh();
        }

        if (SelectedMacro is not null)
        {
            RefreshMacroSteps(selectStep: SelectedStep?.Step);
        }

        RefreshMacroRuntime();
        return true;
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

    private void OnMacroPlaybackStarted(MacroDefinition macro)
    {
        _diagnosticLog.TryWrite(
            "macro",
            $"started id={macro.Id} trigger={macro.Trigger?.Mode.ToString() ?? "manual"} playback={macro.PlaybackMode}");
        if (macro.PlaybackMode != MacroPlaybackMode.RepeatUntilStopped ||
            macro.Trigger?.Mode is (MacroTriggerMode.Hold or MacroTriggerMode.HoldRepeat))
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
                StatusText = AppStrings.Text("MacroCaptureCanceled");
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

    public Task SetMacroMouseSideButtonTriggerAsync(
        int mouseButton,
        CancellationToken cancellationToken = default) =>
        SetMacroButtonTriggerAsync(
            mouseButton is 4 or 5
                ? new MacroInput
                {
                    Kind = MacroInputKind.MouseButton,
                    MouseButton = mouseButton,
                }
                : null,
            cancellationToken);

    public Task SetMacroNagaSideButtonTriggerAsync(
        int sideButton,
        CancellationToken cancellationToken = default) =>
        SetMacroButtonTriggerAsync(
            sideButton is >= 1 and <= 12
                ? new MacroInput
                {
                    Kind = MacroInputKind.NagaSideButton,
                    SideButton = sideButton,
                }
                : null,
            cancellationToken);

    private async Task SetMacroButtonTriggerAsync(
        MacroInput? input,
        CancellationToken cancellationToken)
    {
        if (SelectedMacro is not { } selected || !CanEditMacro || input is null)
        {
            return;
        }

        CancelMacroCapture();
        CancelMacroStepCapture();
        CancelMacroPlaybackShortcutCapture();
        await ApplyMacroTriggerAsync(
            selected,
            new MacroTrigger { Input = input },
            cancellationToken);
    }

    private async Task ApplyMacroTriggerAsync(
        MacroRowViewModel selected,
        MacroTrigger trigger,
        CancellationToken cancellationToken)
    {
        trigger.Mode = selected.Definition.Trigger?.Mode ?? MacroTriggerMode.Press;
        if (FindTriggerConflict(selected.Definition, trigger, selected) is { } conflict)
        {
            StatusText = AppStrings.FormatText("MacroTriggerConflict", conflict.Name);
            return;
        }

        selected.SetTrigger(trigger);
        MarkMacroDirty(selected);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            return;
        }

        RefreshMacroRuntime();
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
        selected.SetTrigger(null);
        MarkMacroDirty(selected);
        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            return;
        }

        RefreshMacroRuntime();
        NotifySelectedMacroChanged();
        StatusText = AppStrings.Text("MacroTriggerCleared");
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
            if (!MoveGroup(owner, groupId, offset))
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

    private void CancelMacroCountdown()
    {
        _macroCountdownCancellation?.Cancel();
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

        CancelMacroCapture();
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
        if (wasPlaying && !IsMacroPlaying)
        {
            _diagnosticLog.TryWrite("macro", "playback stopped");
        }
    }

    public async Task ExportSelectedMacroAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (SelectedMacro is not { } selected)
        {
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

        try
        {
            await MacroDocumentService.ExportAsync(
                selected.Definition,
                ActiveMacroMap(),
                filePath,
                cancellationToken);
            StatusText = AppStrings.FormatText(
                "MacroExported",
                Path.GetFileName(filePath));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException)
        {
            StatusText = AppStrings.FormatText("MacroExportFailed", exception.Message);
        }
    }

    public async Task ImportMacroAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MacroDefinition> imported;
        try
        {
            imported = await MacroDocumentService.ImportAsync(filePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText = AppStrings.FormatText("MacroImportFailed", exception.Message);
            return;
        }

        var profile = _getActiveProfile();
        if (profile.Macros.Count + imported.Count > 64)
        {
            StatusText = AppStrings.Text("MacroCountLimit");
            return;
        }

        MacroDocumentService.PrepareImported(
            imported,
            () => Guid.NewGuid().ToString("N"),
            AppStrings.Text("MacroImportedName"));

        var importedRows = new List<MacroRowViewModel>(imported.Count);
        foreach (var macro in imported)
        {
            profile.Macros.Add(macro);
            var row = CreateMacroRow(macro);
            Items.Add(row);
            importedRows.Add(row);
            MarkMacroDirtyId(macro.Id);
        }

        var importedIds = imported.Select(macro => macro.Id).ToArray();
        if (imported.Any(macro => !ValidateMacroTree(macro)))
        {
            StatusText = AppStrings.Text("MacroStepIncomplete");
            RollbackImportedMacros(profile, importedIds, importedRows);
            return;
        }

        if (imported.Any(macro => !ValidateMacroReferences(macro)))
        {
            StatusText = AppStrings.Text("MacroReferenceInvalid");
            RollbackImportedMacros(profile, importedIds, importedRows);
            return;
        }

        foreach (var macro in imported)
        {
            if (macro.Trigger is { } trigger &&
                FindTriggerConflict(macro, trigger) is { } conflict)
            {
                StatusText = AppStrings.FormatText("MacroTriggerConflict", conflict.Name);
                RollbackImportedMacros(profile, importedIds, importedRows);
                return;
            }
        }

        if (!await TrySaveMacroProfileAsync(cancellationToken))
        {
            RollbackImportedMacros(profile, importedIds, importedRows);
            return;
        }

        var firstRow = importedRows.FirstOrDefault();
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
        if (firstRow is not null)
        {
            SelectedMacro = firstRow;
        }
        RefreshMacroRuntime();
        StatusText = imported.Count == 1
            ? AppStrings.Text("MacroImported")
            : AppStrings.FormatText("MacroBundleImported", imported.Count);
    }

    private void RollbackImportedMacros(
        ProfileDefinition profile,
        IReadOnlyList<string> macroIds,
        IReadOnlyList<MacroRowViewModel> rows)
    {
        var ids = macroIds.ToHashSet(StringComparer.Ordinal);
        profile.Macros.RemoveAll(macro => ids.Contains(macro.Id));
        foreach (var row in rows)
        {
            Items.Remove(row);
        }

        if (SelectedMacro is { } selected && rows.Contains(selected))
        {
            SelectedMacro = Items.FirstOrDefault();
        }

        foreach (var id in ids)
        {
            _dirtyMacroIds.Remove(id);
        }

        OnPropertyChanged(nameof(HasUnsavedMacroChanges));
        OnPropertyChanged(nameof(MacroUnsavedVisibility));
        OnPropertyChanged(nameof(MacroListEmptyVisibility));
        OnPropertyChanged(nameof(MacroReferenceCandidates));
    }

    private async Task<bool> TrySaveMacroProfileAsync(
        CancellationToken cancellationToken,
        bool prepareDirtyMacros = true)
    {
        if (prepareDirtyMacros)
        {
            var dirtyRows = Items
                .Where(row => _dirtyMacroIds.Contains(row.Id))
                .ToList();
            if (!PrepareMacrosForSave(dirtyRows))
            {
                return false;
            }
        }

        // Only clear the dirty flags that existed when this save started. A
        // field edit made while the file is being written must stay dirty.
        var dirtyAtStart = _dirtyMacroIds.ToDictionary(
            id => id,
            GetMacroEditVersion,
            StringComparer.Ordinal);
        try
        {
            await _macroSaveGate.WaitAsync(cancellationToken);
            try
            {
                if (!await _saveProfileAsync(cancellationToken))
                {
                    StatusText = AppStrings.Text("MacroSaveFailed");
                    return false;
                }
            }
            finally
            {
                _macroSaveGate.Release();
            }

            MarkMacroChangesSaved(dirtyAtStart);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            StatusText = AppStrings.FormatText("MacroRuntimeError", exception.Message);
            return false;
        }
    }

    private bool PrepareMacrosForSave(IReadOnlyList<MacroRowViewModel> rows)
    {
        foreach (var row in rows)
        {
            if (!ValidateMacroTree(row.Definition))
            {
                StatusText = AppStrings.Text("MacroStepIncomplete");
                return false;
            }

            if (!ValidateMacroReferences(row.Definition))
            {
                StatusText = AppStrings.Text("MacroReferenceInvalid");
                return false;
            }

            row.Definition.ApplySafeDefaults();
            EnsureMacroName(row);
            row.Refresh();
            if (FindTriggerConflict(row) is { } conflict)
            {
                StatusText = AppStrings.FormatText("MacroTriggerConflict", conflict.Name);
                return false;
            }
        }

        return true;
    }

    private bool ValidateMacroTree(MacroDefinition macro) =>
        MacroValidator.ValidateTree(macro);

    private bool ValidateMacroReferences(MacroDefinition macro) =>
        MacroValidator.ValidateReferences(macro, ActiveMacroMap());

    private IReadOnlyDictionary<string, MacroDefinition> ActiveMacroMap() =>
        MacroDocumentService.IndexById(_getActiveProfile().Macros);

    private void EnsureMacroName(MacroRowViewModel row)
    {
        if (!string.IsNullOrWhiteSpace(row.Definition.Name))
        {
            row.Definition.Name = row.Definition.Name.Trim();
            return;
        }

        row.Definition.Name = AppStrings.FormatText(
            "MacroDefaultName",
            Math.Max(1, Items.IndexOf(row) + 1));
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
            StringComparer.Ordinal.Equals(otherTrigger.DeviceId, trigger.DeviceId) &&
            StringComparer.Ordinal.Equals(otherTrigger.ControlId, trigger.ControlId));

    private static bool MoveGroup(IList<MacroStep> owner, string groupId, int offset)
    {
        var indices = Enumerable.Range(0, owner.Count)
            .Where(index => StringComparer.Ordinal.Equals(owner[index].GroupId, groupId))
            .ToList();
        if (indices.Count == 0 ||
            (offset < 0 && indices[0] == 0) ||
            (offset > 0 && indices[^1] == owner.Count - 1))
        {
            return false;
        }

        var group = indices.Select(index => owner[index]).ToList();
        var anchor = offset < 0 ? owner[indices[0] - 1] : owner[indices[^1] + 1];
        for (var index = indices.Count - 1; index >= 0; index--)
        {
            owner.RemoveAt(indices[index]);
        }

        var anchorIndex = owner.IndexOf(anchor);
        var target = offset < 0 ? anchorIndex + 1 : anchorIndex;
        foreach (var step in group)
        {
            owner.Insert(target++, step);
        }

        return true;
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

    private MacroRecordingOptions BuildRecordingOptions() => new()
    {
        RecordDelays = _macroRecordingDelayMode != MacroDelayMode.None,
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

    public void MarkMacroDirty() => MarkMacroDirty(SelectedMacro);

    public void MarkMacroDirty(MacroRowViewModel? row)
    {
        if (row is not null)
        {
            MarkMacroDirtyId(row.Id);
        }
    }

    private void RestoreMacroDirtyState(string macroId, bool wasDirty)
    {
        if (wasDirty)
        {
            MarkMacroDirtyId(macroId);
            return;
        }

        if (_dirtyMacroIds.Remove(macroId))
        {
            OnPropertyChanged(nameof(HasUnsavedMacroChanges));
            OnPropertyChanged(nameof(MacroUnsavedVisibility));
        }
    }

    private long GetMacroEditVersion(string macroId) =>
        _macroEditVersions.GetValueOrDefault(macroId);

    private void MarkMacroDirtyId(string macroId)
    {
        // Bump on every edit, including edits made while a save is in flight.
        _macroEditVersions[macroId] = unchecked(GetMacroEditVersion(macroId) + 1);
        if (!_dirtyMacroIds.Add(macroId))
        {
            return;
        }

        OnPropertyChanged(nameof(HasUnsavedMacroChanges));
        OnPropertyChanged(nameof(MacroUnsavedVisibility));
    }

    private void MarkMacroChangesSaved(IReadOnlyDictionary<string, long> savedVersions)
    {
        var changed = false;
        foreach (var (id, savedVersion) in savedVersions)
        {
            if (GetMacroEditVersion(id) == savedVersion)
            {
                changed |= _dirtyMacroIds.Remove(id);
            }
        }

        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(HasUnsavedMacroChanges));
        OnPropertyChanged(nameof(MacroUnsavedVisibility));
    }

    private void OnMacroStepChanged()
    {
        MarkMacroDirty();
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
            OnPropertyChanged(nameof(CanEditMacroPlaybackMode));
            OnPropertyChanged(nameof(CanPlayMacro));
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
            var row = new MacroStepRowViewModel(
                step,
                steps,
                parentLoop,
                depth,
                OnMacroStepChanged,
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
        OnPropertyChanged(nameof(MacroEditorVisibility));
        OnPropertyChanged(nameof(CanEditMacro));
        OnPropertyChanged(nameof(CanEditMacroStep));
        OnPropertyChanged(nameof(CanEditMacroPlaybackMode));
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



