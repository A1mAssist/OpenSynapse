namespace OpenSynapse.Core.Macros;

public sealed class MacroRecordingSession
{
    public const int MaxDurationMilliseconds = 300_000;

    private const int MouseSampleIntervalMilliseconds = 15;
    private const int MaxMouseCoordinate = 100_000;

    private readonly object _gate = new();
    private readonly List<MacroStep> _steps = [];
    private readonly List<MacroInput> _heldInputs = [];
    private readonly bool _recordDelays;
    private readonly MacroDelayMode _delayMode;
    private readonly int _fixedDelayMilliseconds;
    private readonly MacroMouseTrackingMode _mouseTrackingMode;

    private long _startedAt;
    private long _lastEventTime;
    private bool _hasStarted;

    private MacroStep? _pendingPress;
    private long _pendingPressAt;
    private string? _groupId;

    private MacroStep? _mouseStep;
    private long _mouseStepStartedAt;
    private long _lastMouseSampleAt;
    private int _lastMouseX;
    private int _lastMouseY;
    private bool _hasLastMousePoint;
    private int _firstMouseX;
    private int _firstMouseY;
    private bool _hasFirstMousePoint;
    private PendingMousePoint? _pendingMousePoint;

    public MacroRecordingSession(bool recordDelays = true)
        : this(new MacroRecordingOptions { RecordDelays = recordDelays })
    {
    }

    public MacroRecordingSession(MacroRecordingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _recordDelays = options.RecordDelays;
        _delayMode = Enum.IsDefined(options.DelayMode)
            ? options.DelayMode
            : MacroDelayMode.Recorded;
        _fixedDelayMilliseconds = Math.Clamp(
            options.FixedDelayMilliseconds,
            0,
            MacroDefinition.MaxDelayMilliseconds);
        _mouseTrackingMode = Enum.IsDefined(options.MouseTrackingMode)
            ? options.MouseTrackingMode
            : MacroMouseTrackingMode.None;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _steps.Count;
            }
        }
    }

    public bool TryAdd(MacroInput input, bool isDown, long timestampMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock (_gate)
        {
            if (!TryBegin(timestampMilliseconds))
            {
                return false;
            }

            FlushMousePointLocked();
            _mouseStep = null;

            if (_steps.Count >= MacroDefinition.MaxSteps)
            {
                return false;
            }

            // Wheel messages arrive as a single event with isDown set false and
            // have no matching release, so they are always one standalone step.
            if (input.Kind == MacroInputKind.MouseWheel)
            {
                return AddWheelLocked(input, timestampMilliseconds);
            }

            return isDown
                ? AddPressLocked(input, timestampMilliseconds)
                : AddReleaseLocked(input, timestampMilliseconds);
        }
    }

    public bool TryAddMouseMove(
        int screenX,
        int screenY,
        long timestampMilliseconds,
        int windowLeft,
        int windowTop,
        int windowWidth,
        int windowHeight)
    {
        lock (_gate)
        {
            if (_mouseTrackingMode == MacroMouseTrackingMode.None ||
                !TryConvertMousePoint(
                    screenX,
                    screenY,
                    windowLeft,
                    windowTop,
                    windowWidth,
                    windowHeight,
                    out var x,
                    out var y) ||
                !TryBegin(timestampMilliseconds))
            {
                return false;
            }

            // A pending tap becomes an explicit hold so the move is recorded
            // after the press it happened during, not before it.
            if (_pendingPress is not null && !PromotePendingPressLocked())
            {
                return false;
            }

            if (_mouseStep is null)
            {
                if (_steps.Count >= MacroDefinition.MaxSteps)
                {
                    return false;
                }

                _mouseStep = new MacroStep
                {
                    Kind = MacroStepKind.MouseMove,
                    MouseMoveMode = MouseMoveModeForTracking,
                    DelayMilliseconds = TakeDelay(timestampMilliseconds),
                    MousePath =
                    [
                        new MacroMousePoint { X = x, Y = y, OffsetMilliseconds = 0 },
                    ],
                };
                _steps.Add(_mouseStep);
                _mouseStepStartedAt = timestampMilliseconds;
                _lastMouseSampleAt = timestampMilliseconds;
                RememberMousePoint(screenX, screenY);
                return true;
            }

            var last = _mouseStep.MousePath[^1];
            if (timestampMilliseconds - _lastMouseSampleAt <
                MouseSampleIntervalMilliseconds)
            {
                // Throttled, but kept so the last distinct position is not lost.
                if (x != last.X || y != last.Y)
                {
                    _pendingMousePoint = new PendingMousePoint(
                        x,
                        y,
                        timestampMilliseconds,
                        screenX,
                        screenY);
                }

                return true;
            }

            _lastMouseSampleAt = timestampMilliseconds;
            _pendingMousePoint = null;
            RememberMousePoint(screenX, screenY);
            if (x == last.X && y == last.Y)
            {
                _lastEventTime = timestampMilliseconds;
                return true;
            }

            AppendMousePoint(_mouseStep.MousePath, x, y, timestampMilliseconds);
            _lastEventTime = timestampMilliseconds;
            return true;
        }
    }

    public IReadOnlyList<MacroStep> Snapshot()
    {
        lock (_gate)
        {
            var steps = _steps.Select(step => step.Clone()).ToList();
            if (_pendingMousePoint is { } pending && _mouseStep is not null &&
                steps.Count > 0 &&
                steps[^1] is { Kind: MacroStepKind.MouseMove } moveStep)
            {
                var last = moveStep.MousePath[^1];
                if (last.X != pending.X || last.Y != pending.Y)
                {
                    var offset = ClampDelay(pending.Timestamp - _mouseStepStartedAt);
                    if (moveStep.MousePath.Count < MacroStep.MaxMousePathPoints)
                    {
                        moveStep.MousePath.Add(new MacroMousePoint
                        {
                            X = pending.X,
                            Y = pending.Y,
                            OffsetMilliseconds = offset,
                        });
                    }
                    else
                    {
                        last.X = pending.X;
                        last.Y = pending.Y;
                        last.OffsetMilliseconds = offset;
                    }
                }
            }

            if (_pendingPress is not null && steps.Count < MacroDefinition.MaxSteps)
            {
                // Recording stopped while the key was held: the step must not
                // own a release, playback releases it when the macro ends.
                var pendingStep = _pendingPress.Clone();
                pendingStep.AutoRelease = false;
                steps.Add(pendingStep);
            }

            return steps;
        }
    }

    private bool AddPressLocked(MacroInput input, long timestampMilliseconds)
    {
        // Auto-repeat sends more key-down messages without a release.
        if (_pendingPress?.Input?.Matches(input) == true ||
            _heldInputs.Any(held => held.Matches(input)))
        {
            return true;
        }

        if (_pendingPress is not null)
        {
            if (!PromotePendingPressLocked())
            {
                return false;
            }
        }
        else if (_heldInputs.Count == 0)
        {
            // A lone press may still become a one-step tap.
            var tapDelay = TakeDelay(timestampMilliseconds);
            _pendingPress = new MacroStep
            {
                Kind = MacroStepKind.Input,
                Input = input.Clone(),
                IsDown = true,
                DelayMilliseconds = tapDelay,
            };
            _pendingPressAt = timestampMilliseconds;
            return true;
        }
        else
        {
            StartGroupLocked();
        }

        if (_steps.Count >= MacroDefinition.MaxSteps)
        {
            return false;
        }

        var delay = TakeDelay(timestampMilliseconds);
        _steps.Add(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = input.Clone(),
            IsDown = true,
            AutoRelease = false,
            DelayMilliseconds = delay,
            GroupId = _groupId,
        });
        _heldInputs.Add(input.Clone());
        return true;
    }

    private bool AddReleaseLocked(MacroInput input, long timestampMilliseconds)
    {
        if (_pendingPress?.Input?.Matches(input) == true)
        {
            if (_steps.Count >= MacroDefinition.MaxSteps)
            {
                return false;
            }

            var tap = _pendingPress;
            _pendingPress = null;
            tap.HoldMilliseconds = (int)Math.Clamp(
                timestampMilliseconds - _pendingPressAt,
                0L,
                MacroStep.MaxHoldMilliseconds);
            _steps.Add(tap);
            _lastEventTime = timestampMilliseconds;
            return true;
        }

        // The press was not released by this event; keep it held so the steps
        // stay in the order the events happened.
        if (_pendingPress is not null && !PromotePendingPressLocked())
        {
            return false;
        }

        var heldIndex = _heldInputs.FindIndex(held => held.Matches(input));
        var delay = TakeDelay(timestampMilliseconds);
        if (_steps.Count >= MacroDefinition.MaxSteps)
        {
            return false;
        }

        _steps.Add(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = input.Clone(),
            IsDown = false,
            DelayMilliseconds = delay,
            GroupId = heldIndex >= 0 ? _groupId : null,
        });

        if (heldIndex >= 0)
        {
            _heldInputs.RemoveAt(heldIndex);
            if (_heldInputs.Count == 0)
            {
                _groupId = null;
            }
        }

        return true;
    }

    private bool AddWheelLocked(MacroInput input, long timestampMilliseconds)
    {
        if (_pendingPress is not null && !PromotePendingPressLocked())
        {
            return false;
        }

        if (_steps.Count >= MacroDefinition.MaxSteps)
        {
            return false;
        }

        _steps.Add(new MacroStep
        {
            Kind = MacroStepKind.Input,
            Input = input.Clone(),
            IsDown = false,
            DelayMilliseconds = TakeDelay(timestampMilliseconds),
        });
        return true;
    }

    private bool PromotePendingPressLocked()
    {
        if (_pendingPress is null)
        {
            return true;
        }

        if (_steps.Count >= MacroDefinition.MaxSteps)
        {
            return false;
        }

        StartGroupLocked();
        _pendingPress.AutoRelease = false;
        _pendingPress.GroupId = _groupId;
        _steps.Add(_pendingPress);
        _heldInputs.Add(_pendingPress.Input!);
        _pendingPress = null;
        return true;
    }

    private void StartGroupLocked() => _groupId ??= Guid.NewGuid().ToString("N");

    private void RememberMousePoint(int screenX, int screenY)
    {
        if (!_hasFirstMousePoint)
        {
            _firstMouseX = screenX;
            _firstMouseY = screenY;
            _hasFirstMousePoint = true;
        }

        _lastMouseX = screenX;
        _lastMouseY = screenY;
        _hasLastMousePoint = true;
    }

    private void AppendMousePoint(
        List<MacroMousePoint> path,
        int x,
        int y,
        long timestampMilliseconds)
    {
        var offset = ClampDelay(timestampMilliseconds - _mouseStepStartedAt);
        if (path.Count < MacroStep.MaxMousePathPoints)
        {
            path.Add(new MacroMousePoint
            {
                X = x,
                Y = y,
                OffsetMilliseconds = offset,
            });
            return;
        }

        var last = path[^1];
        last.X = x;
        last.Y = y;
        last.OffsetMilliseconds = offset;
    }

    private void FlushMousePointLocked()
    {
        if (_pendingMousePoint is not { } pending || _mouseStep is null)
        {
            _pendingMousePoint = null;
            return;
        }

        var path = _mouseStep.MousePath;
        if (path.Count > 0 && (path[^1].X != pending.X || path[^1].Y != pending.Y))
        {
            AppendMousePoint(path, pending.X, pending.Y, pending.Timestamp);
            _lastMouseSampleAt = pending.Timestamp;
            _lastEventTime = pending.Timestamp;
            _lastMouseX = pending.ScreenX;
            _lastMouseY = pending.ScreenY;
            _hasLastMousePoint = true;
        }

        _pendingMousePoint = null;
    }

    private bool TryBegin(long timestampMilliseconds)
    {
        if (!_hasStarted)
        {
            _hasStarted = true;
            _startedAt = timestampMilliseconds;
            _lastEventTime = timestampMilliseconds;
            return true;
        }

        return timestampMilliseconds - _startedAt <= MaxDurationMilliseconds;
    }

    /// <summary>
    /// The delay before the step being recorded, measured from the previous
    /// event. The first step is always immediate.
    /// </summary>
    private int TakeDelay(long timestampMilliseconds)
    {
        if (_steps.Count == 0)
        {
            _lastEventTime = timestampMilliseconds;
            return 0;
        }

        var delay = _delayMode switch
        {
            MacroDelayMode.None => 0,
            MacroDelayMode.Fixed => _fixedDelayMilliseconds,
            _ => _recordDelays
                ? ClampDelay(timestampMilliseconds - _lastEventTime)
                : 0,
        };
        _lastEventTime = timestampMilliseconds;
        return delay;
    }

    private bool TryConvertMousePoint(
        int screenX,
        int screenY,
        int windowLeft,
        int windowTop,
        int windowWidth,
        int windowHeight,
        out int x,
        out int y)
    {
        switch (_mouseTrackingMode)
        {
            case MacroMouseTrackingMode.Absolute:
                x = Math.Clamp(screenX, -MaxMouseCoordinate, MaxMouseCoordinate);
                y = Math.Clamp(screenY, -MaxMouseCoordinate, MaxMouseCoordinate);
                return true;

            case MacroMouseTrackingMode.Relative:
                x = _hasLastMousePoint
                    ? (int)Math.Clamp((long)screenX - _lastMouseX, -MaxMouseCoordinate, MaxMouseCoordinate)
                    : 0;
                y = _hasLastMousePoint
                    ? (int)Math.Clamp((long)screenY - _lastMouseY, -MaxMouseCoordinate, MaxMouseCoordinate)
                    : 0;
                return true;

            case MacroMouseTrackingMode.StartPoint:
                x = _hasFirstMousePoint
                    ? (int)Math.Clamp((long)screenX - _firstMouseX, -MaxMouseCoordinate, MaxMouseCoordinate)
                    : 0;
                y = _hasFirstMousePoint
                    ? (int)Math.Clamp((long)screenY - _firstMouseY, -MaxMouseCoordinate, MaxMouseCoordinate)
                    : 0;
                return true;

            case MacroMouseTrackingMode.Foreground:
                if (windowWidth <= 0 || windowHeight <= 0)
                {
                    x = 0;
                    y = 0;
                    return false;
                }

                x = (int)Math.Clamp(
                    Math.Round((screenX - (double)windowLeft) * 10_000 / windowWidth),
                    0,
                    10_000);
                y = (int)Math.Clamp(
                    Math.Round((screenY - (double)windowTop) * 10_000 / windowHeight),
                    0,
                    10_000);
                return true;

            default:
                x = 0;
                y = 0;
                return false;
        }
    }

    private MacroMouseMoveMode MouseMoveModeForTracking => _mouseTrackingMode switch
    {
        MacroMouseTrackingMode.Foreground => MacroMouseMoveMode.ForegroundRelative,
        MacroMouseTrackingMode.Absolute => MacroMouseMoveMode.Absolute,
        MacroMouseTrackingMode.StartPoint => MacroMouseMoveMode.RelativeToStart,
        _ => MacroMouseMoveMode.Relative,
    };

    private static int ClampDelay(long milliseconds) =>
        (int)Math.Clamp(milliseconds, 0L, MacroDefinition.MaxDelayMilliseconds);

    private readonly record struct PendingMousePoint(
        int X,
        int Y,
        long Timestamp,
        int ScreenX,
        int ScreenY);
}
