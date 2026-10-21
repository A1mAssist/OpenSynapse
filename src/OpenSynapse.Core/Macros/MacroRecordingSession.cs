namespace OpenSynapse.Core.Macros;

public sealed class MacroRecordingSession
{
    public const int MaxDurationMilliseconds = 300_000;

    private readonly object _gate = new();
    private readonly List<MacroStep> _steps = [];
    private readonly bool _recordDelays;
    private long _startedAt;
    private long _lastAt;
    private bool _hasStarted;
    private MacroStep? _pendingPress;
    private long _pendingPressAt;

    public MacroRecordingSession(bool recordDelays = true)
    {
        _recordDelays = recordDelays;
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
            if (_steps.Count >= MacroDefinition.MaxSteps)
            {
                return false;
            }

            if (!_hasStarted)
            {
                _hasStarted = true;
                _startedAt = timestampMilliseconds;
                _lastAt = timestampMilliseconds;
            }
            else if (timestampMilliseconds - _startedAt > MaxDurationMilliseconds)
            {
                return false;
            }

            // A press and its matching release form one step, so the recorded
            // macro can never leave a key stuck down and the user never has to
            // delete a lone release.
            if (isDown)
            {
                if (_pendingPress is not null)
                {
                    // The previous key was never released; keep it as a step so
                    // the recording still reflects what happened.
                    _steps.Add(_pendingPress);
                }

                _pendingPress = new MacroStep
                {
                    Kind = MacroStepKind.Input,
                    Input = input.Clone(),
                    IsDown = true,
                };
                _pendingPressAt = timestampMilliseconds;
                return true;
            }

            if (_pendingPress is not null &&
                _pendingPress.Input?.Matches(input) == true)
            {
                _pendingPress.HoldMilliseconds = (int)Math.Clamp(
                    timestampMilliseconds - _pendingPressAt,
                    0,
                    MacroStep.MaxHoldMilliseconds);
                _pendingPress.DelayMilliseconds = NextDelay(timestampMilliseconds);
                _steps.Add(_pendingPress);
                _pendingPress = null;
                return true;
            }

            if (_pendingPress is not null)
            {
                _steps.Add(_pendingPress);
                _pendingPress = null;
            }

            var delay = NextDelay(timestampMilliseconds);
            _steps.Add(new MacroStep
            {
                Kind = MacroStepKind.Input,
                Input = input.Clone(),
                IsDown = false,
                DelayMilliseconds = delay,
            });
            return true;
        }
    }

    /// <summary>
    /// The gap since the previous recorded event, used as the delay before the
    /// next step.
    /// </summary>
    private int NextDelay(long timestampMilliseconds)
    {
        var delay = _steps.Count == 0 || !_recordDelays
            ? 0
            : checked((int)Math.Clamp(
                timestampMilliseconds - _lastAt,
                0,
                MacroDefinition.MaxDelayMilliseconds));
        _lastAt = timestampMilliseconds;
        return delay;
    }

    public IReadOnlyList<MacroStep> Snapshot()
    {
        lock (_gate)
        {
            var steps = _steps.Select(step => step.Clone()).ToList();
            if (_pendingPress is not null)
            {
                // The key was never released, so the step still carries the gap
                // that came before it.
                var pending = _pendingPress.Clone();
                pending.DelayMilliseconds = _steps.Count == 0 || !_recordDelays
                    ? 0
                    : checked((int)Math.Clamp(
                        _pendingPressAt - _lastAt,
                        0,
                        MacroDefinition.MaxDelayMilliseconds));
                steps.Add(pending);
            }

            return steps;
        }
    }
}