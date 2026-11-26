using System.Diagnostics;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

internal sealed partial class MacroRuntime : IDisposable
{
    private const int DefaultStopPlaybackVirtualKey = 0x79;
    private const int HoldReleaseCheckMilliseconds = 100;
    private readonly object _gate = new();
    private readonly IMacroInputSink _sink;
    private readonly bool _installInputHooks;
    private readonly Action<string>? _log;
    private readonly Func<MacroInput, bool> _isPhysicallyDown;
    private readonly Dictionary<InputKey, SuppressedInput> _suppressedInputs = [];
    private readonly HashSet<int> _physicalModifierKeys = [];
    private readonly Dictionary<string, int> _sequencePositions = new(StringComparer.Ordinal);
    private IReadOnlyList<MacroDefinition> _macros = [];
    private IReadOnlyDictionary<string, MacroDefinition> _macroMap =
        new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);
    private MacroInputHook? _hook;
    private MacroRecordingSession? _recording;
    private MacroRecordingOptions _recordingOptions = new();
    private MacroInput? _recordingStopInput;
    private TaskCompletionSource<MacroTrigger?>? _triggerCapture;
    private TaskCompletionSource<MacroInput?>? _stepCapture;
    private MacroInput? _suppressedCapture;
    private bool _triggerCaptureAllowsModifiers;
    private bool _triggerCaptureAllowsNagaSideButton;
    private bool _triggerCaptureAllowsDeviceButton;
    private Func<bool>? _isOwnWindowForeground;
    private CancellationTokenSource? _playbackCancellation;
    private TaskCompletionSource? _resumePlayback;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _isHolding;
    private IReadOnlyList<MacroInput>? _heldInputs;
    private string? _activePlaybackMacroId;
    private bool _disposed;
    private bool _recordingPending;
    private MacroInput? _playbackStartShortcut;
    private MacroInput _playbackStopShortcut = CreateDefaultStopShortcut();
    private bool _playbackStartHotkeyHeld;
    private bool _playbackStopHotkeyHeld;
    private bool _hyperShiftHeld;

    internal MacroRuntime(
        Func<MacroInput, bool>? isPhysicallyDown = null,
        IMacroInputSink? sink = null, bool installInputHooks = true,
        Action<string>? log = null)
    {
        _isPhysicallyDown = isPhysicallyDown ?? MacroInputHook.IsPhysicallyDown;
        _sink = sink ?? new WindowsMacroInputSink();
        _installInputHooks = installInputHooks;
        _log = log;
    }

    public event Action<string>? Error;

    public event Action? RecordingStopRequested;

    public event Action? PlaybackStartRequested;

    /// <summary>
    /// Raised for a private key no macro claimed. The app uses it to keep the
    /// firmware's default action alive after the device enters driver mode.
    /// </summary>
    internal Action<MacroInput>? UnhandledRazerKeyHandler { get; set; }

    public event Action<MacroDefinition, MacroTriggerMode?, MacroPlaybackMode>? PlaybackStarted;

    public event Action? PlaybackStateChanged;

    /// <summary>
    /// Raised on the hook thread when the pointer moves after a pause. The app
    /// uses it to re-arm a mouse that slept through its driver-mode sync.
    /// </summary>
    internal event Action? Activity;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _recording is not null;
            }
        }
    }

    public bool IsCapturing
    {
        get
        {
            lock (_gate)
            {
                return _triggerCapture is not null || _stepCapture is not null;
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _isPlaying;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _isPaused;
            }
        }
    }

    /// <summary>
    /// Playback finished but deliberately left inputs down. The next trigger
    /// release or <see cref="StopPlayback"/> releases them.
    /// </summary>
    public bool IsHolding
    {
        get
        {
            lock (_gate)
            {
                return _isHolding;
            }
        }
    }

    public void SetOwnWindowForegroundPredicate(Func<bool>? predicate)
    {
        lock (_gate)
        {
            _isOwnWindowForeground = predicate;
        }
    }

    public void SetRecordingPending(bool pending)
    {
        lock (_gate)
        {
            _recordingPending = pending;
        }
    }

    public void UpdateMacros(IEnumerable<MacroDefinition> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);
        var snapshot = macros.Where(macro => macro is not null).ToArray();
        var map = new Dictionary<string, MacroDefinition>(StringComparer.Ordinal);
        foreach (var macro in snapshot)
        {
            map.TryAdd(macro.Id, macro);
        }

        MacroInputHook? hookToDispose = null;
        lock (_gate)
        {
            _macros = snapshot;
            _macroMap = map;
            _sequencePositions.Clear();
            if (CanDetachHookLocked())
            {
                hookToDispose = DetachHookLocked();
            }
            else
            {
                EnsureHookLocked();
                if (_hook is not null)
                {
                    _hook.CaptureMouseMoves =
                        _recording is not null &&
                        _recordingOptions.MouseTrackingMode != MacroMouseTrackingMode.None;
                }
            }
        }

        DisposeHook(hookToDispose);
    }

    public void StartRecording(MacroRecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        StopPlayback();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recording is not null)
            {
                throw new InvalidOperationException("A macro recording is already running.");
            }

            _recordingOptions = options.Clone();
            _recording = new MacroRecordingSession(_recordingOptions);
            _recordingPending = false;
            _recordingStopInput = null;
            _triggerCapture?.TrySetResult(null);
            _triggerCapture = null;
            _stepCapture?.TrySetResult(null);
            _stepCapture = null;
            _suppressedInputs.Clear();
            _suppressedCapture = null;
            if (!EnsureHookLocked())
            {
                _recording = null;
                throw new InvalidOperationException("The macro input hook could not be started.");
            }

            if (_hook is not null) _hook.CaptureMouseMoves =
                _recordingOptions.MouseTrackingMode != MacroMouseTrackingMode.None;
        }
    }

    public IReadOnlyList<MacroStep> StopRecording()
    {
        IReadOnlyList<MacroStep> steps;
        MacroInputHook? hookToDispose = null;
        lock (_gate)
        {
            var recording = _recording;
            _recording = null;
            _recordingPending = false;
            if (_hook is not null)
            {
                _hook.CaptureMouseMoves = false;
            }
            if (CanDetachHookLocked())
            {
                hookToDispose = DetachHookLocked();
            }
            steps = recording?.Snapshot() ?? [];
        }

        DisposeHook(hookToDispose);
        return steps;
    }

    public Task<MacroTrigger?> CaptureNextTriggerAsync(
        CancellationToken cancellationToken = default) =>
        CaptureNextTriggerAsync(
            allowModifierCombo: false,
            cancellationToken: cancellationToken);

    public Task<MacroTrigger?> CaptureNextTriggerAsync(
        bool allowModifierCombo,
        CancellationToken cancellationToken = default) =>
        CaptureNextTriggerAsync(
            allowModifierCombo,
            allowNagaSideButton: true,
            cancellationToken: cancellationToken);

    public async Task<MacroTrigger?> CaptureNextTriggerAsync(
        bool allowModifierCombo,
        bool allowNagaSideButton,
        bool allowDeviceButton = false,
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<MacroTrigger?> completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _triggerCapture?.TrySetResult(null);
            _stepCapture?.TrySetResult(null);
            _stepCapture = null;
            _triggerCaptureAllowsModifiers = allowModifierCombo;
            _triggerCaptureAllowsNagaSideButton = allowNagaSideButton;
            _triggerCaptureAllowsDeviceButton = allowDeviceButton;
            completion = new TaskCompletionSource<MacroTrigger?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _triggerCapture = completion;
            _suppressedCapture = null;
            if (!EnsureHookLocked())
            {
                _triggerCapture = null;
                completion.TrySetResult(null);
            }
        }

        MacroInputHook? hookToDispose = null;
        try
        {
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_triggerCapture, completion))
                {
                    _triggerCapture = null;
                    _triggerCaptureAllowsNagaSideButton = false;
                    _triggerCaptureAllowsDeviceButton = false;
                }
                if (CanDetachHookLocked())
                {
                    hookToDispose = DetachHookLocked();
                }
            }

            DisposeHook(hookToDispose);
        }
    }

    public async Task<MacroInput?> CaptureNextInputAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<MacroInput?> completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _triggerCapture?.TrySetResult(null);
            _triggerCapture = null;
            _stepCapture?.TrySetResult(null);
            completion = new TaskCompletionSource<MacroInput?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _stepCapture = completion;
            _suppressedCapture = null;
            if (!EnsureHookLocked())
            {
                _stepCapture = null;
                completion.TrySetResult(null);
            }
        }

        MacroInputHook? hookToDispose = null;
        try
        {
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_stepCapture, completion))
                {
                    _stepCapture = null;
                }
                if (CanDetachHookLocked())
                {
                    hookToDispose = DetachHookLocked();
                }
            }

            DisposeHook(hookToDispose);
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? playback;
        MacroInputHook? hookToDispose;
        var heldInputs = 0;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _triggerCapture?.TrySetResult(null);
            _triggerCapture = null;
            _stepCapture?.TrySetResult(null);
            _stepCapture = null;
            _recording = null;
            _hyperShiftHeld = false;
            _isPaused = false;
            _isHolding = false;
            heldInputs = _heldInputs?.Count ?? 0;
            _heldInputs = null;
            _resumePlayback?.TrySetResult();
            _resumePlayback = null;
            playback = _playbackCancellation;
            _playbackCancellation = null;
            hookToDispose = DetachHookLocked();
        }
        StopAllTurbo();
        playback?.Cancel(); playback?.Dispose();
        DisposeHook(hookToDispose);
        if (heldInputs > 0)
        {
            // Shutdown is the one path where the playback task may never run
            // its own cleanup, so the sink drops whatever it still holds.
            _log?.Invoke($"hold released on dispose keys={heldInputs}");
            _ = _sink.ReleaseAllAsync();
        }
    }

    private static bool HasTrigger(IReadOnlyList<MacroDefinition> macros) =>
        macros.Any(macro =>
            macro.Enabled &&
            macro.Steps.Count > 0 &&
            macro.AllTriggers.Any(trigger => trigger.Input is not null));

    private static MacroInput CreateDefaultStopShortcut() => new()
    {
        Kind = MacroInputKind.Keyboard,
        VirtualKey = DefaultStopPlaybackVirtualKey,
        Extended = false,
    };
}
