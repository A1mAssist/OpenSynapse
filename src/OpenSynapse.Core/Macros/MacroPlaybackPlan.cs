namespace OpenSynapse.Core.Macros;

public enum MacroPlaybackActionKind
{
    KeyDown = 0,
    KeyUp = 1,
    MouseButtonDown = 2,
    MouseButtonUp = 3,
    MouseWheel = 4,
    MouseMove = 5,
    Text = 6,
    RunCommand = 7,
    Delay = 8,
}

public sealed class MacroPlaybackAction
{
    public MacroPlaybackActionKind Kind { get; init; }
    public MacroInput? Input { get; init; }
    public int DelayMilliseconds { get; init; }
    public string? Text { get; init; }
    public string? Command { get; init; }
    public string? Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public MacroMouseMoveMode MouseMoveMode { get; init; }
    public IReadOnlyList<MacroMousePoint> MousePath { get; init; } = [];
}

public sealed class MacroPlaybackPlan
{
    private const int MaxActions = 100_000;

    private MacroPlaybackPlan(
        IReadOnlyList<MacroPlaybackAction> actions,
        long totalDurationMilliseconds,
        IReadOnlyList<string> warnings,
        MacroPlaybackMode playbackMode,
        IReadOnlyList<MacroInput> heldInputs,
        bool hasHeldInputsInLoop)
    {
        Actions = actions;
        TotalDurationMilliseconds = totalDurationMilliseconds;
        Warnings = warnings;
        PlaybackMode = playbackMode;
        HeldInputs = heldInputs;
        HasHeldInputsInLoop = hasHeldInputsInLoop;
    }

    public IReadOnlyList<MacroPlaybackAction> Actions { get; }
    public long TotalDurationMilliseconds { get; }
    public IReadOnlyList<string> Warnings { get; }
    public MacroPlaybackMode PlaybackMode { get; }

    /// <summary>
    /// Inputs the plan presses and never releases. The runtime owns their
    /// release; a plan on its own only describes the actions.
    /// </summary>
    public IReadOnlyList<MacroInput> HeldInputs { get; }

    public bool ContainsHeldInputs => HeldInputs.Count > 0;

    /// <summary>A held input cannot be re-pressed every cycle safely.</summary>
    public bool HasHeldInputsInLoop { get; }

    /// <summary>
    /// Held inputs only release on trigger-up or stop, so repeating playback
    /// would leave them down for the whole run.
    /// </summary>
    public bool RequiresSinglePlayback =>
        ContainsHeldInputs && PlaybackMode != MacroPlaybackMode.Once;

    public bool HasUnsupportedHoldPlacement =>
        RequiresSinglePlayback || HasHeldInputsInLoop;

    public static MacroPlaybackPlan Build(
        MacroDefinition macro,
        IReadOnlyDictionary<string, MacroDefinition>? macros = null,
        MacroPlaybackMode? playbackMode = null,
        int? repeatCount = null)
    {
        ArgumentNullException.ThrowIfNull(macro);
        EnsureValidSource(macro);
        var normalized = macro.Clone();
        if (playbackMode is { } mode)
        {
            normalized.PlaybackMode = mode;
            normalized.RepeatCount = mode == MacroPlaybackMode.Repeat
                ? Math.Clamp(repeatCount ?? 1, 1, MacroDefinition.MaxRepeatCount)
                : 1;
        }
        var builder = new Builder(macros ?? new Dictionary<string, MacroDefinition>(StringComparer.Ordinal));
        builder.ExpandMacro(
            normalized,
            depth: 0,
            speed: normalized.PlaybackSpeed,
            stack: [],
            insideLoop: false);
        return builder.Build(normalized.PlaybackMode);
    }

    private static void EnsureValidSource(MacroDefinition macro)
    {
        var count = 0;
        if (macro.Steps is null ||
            !ValidateSteps(macro.Steps, depth: 0, ref count))
        {
            throw new InvalidOperationException(
                "The macro contains an incomplete action. Finish or remove it before playing.");
        }
    }

    private static bool ValidateSteps(
        IReadOnlyList<MacroStep> steps,
        int depth,
        ref int count)
    {
        if (depth > MacroDefinition.MaxNestingDepth)
        {
            return false;
        }

        foreach (var step in steps)
        {
            if (step is null ||
                ++count > MacroDefinition.MaxSteps ||
                (step.Kind == MacroStepKind.Loop && step.Children is null) ||
                (step.Kind == MacroStepKind.MouseMove && step.MousePath is null) ||
                !step.IsValid())
            {
                return false;
            }

            if (step.Kind == MacroStepKind.Loop &&
                !ValidateSteps(step.Children, depth + 1, ref count))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Builder(IReadOnlyDictionary<string, MacroDefinition> macros)
    {
        private readonly List<MacroPlaybackAction> _actions = [];
        private readonly List<string> _warnings = [];
        private readonly List<MacroInput> _openDowns = [];
        private long _totalDuration;
        private bool _hasHeldInputsInLoop;

        public MacroPlaybackPlan Build(MacroPlaybackMode playbackMode) =>
            new(_actions, _totalDuration, _warnings, playbackMode, _openDowns, _hasHeldInputsInLoop);

        public void ExpandMacro(
            MacroDefinition macro,
            int depth,
            double speed,
            HashSet<string> stack,
            bool insideLoop)
        {
            if (depth > MacroDefinition.MaxNestingDepth)
            {
                _warnings.Add($"Macro nesting is deeper than {MacroDefinition.MaxNestingDepth}: {macro.Name}.");
                return;
            }
            if (!stack.Add(macro.Id))
            {
                _warnings.Add($"Macro reference cycle skipped: {macro.Name}.");
                return;
            }

            try
            {
                var repeatCount = macro.PlaybackMode == MacroPlaybackMode.Repeat
                    ? Math.Clamp(macro.RepeatCount, 1, MacroDefinition.MaxRepeatCount)
                    : 1;
                for (var repetition = 0; repetition < repeatCount; repetition++)
                {
                    ExpandSteps(macro.Steps, depth, speed, stack, insideLoop);
                }
            }
            finally
            {
                stack.Remove(macro.Id);
            }
        }

        private void ExpandSteps(
            IReadOnlyList<MacroStep> steps,
            int depth,
            double speed,
            HashSet<string> stack,
            bool insideLoop)
        {
            if (depth > MacroDefinition.MaxNestingDepth)
            {
                _warnings.Add($"Macro nesting is deeper than {MacroDefinition.MaxNestingDepth}.");
                return;
            }

            foreach (var step in steps)
            {
                EnsureActionBudget();
                var delay = Scale(step.DelayMilliseconds, speed);
                switch (step.Kind)
                {
                    case MacroStepKind.Input when step.Input is { } input:
                        AddInput(step, input, delay, speed, insideLoop);
                        break;

                    case MacroStepKind.Delay:
                        Add(new MacroPlaybackAction
                        {
                            Kind = MacroPlaybackActionKind.Delay,
                            DelayMilliseconds = delay,
                        });
                        break;

                    case MacroStepKind.Text when !string.IsNullOrEmpty(step.Text):
                        Add(new MacroPlaybackAction
                        {
                            Kind = MacroPlaybackActionKind.Text,
                            DelayMilliseconds = delay,
                            Text = step.Text,
                        });
                        break;

                    case MacroStepKind.RunCommand when !string.IsNullOrEmpty(step.Command):
                        Add(new MacroPlaybackAction
                        {
                            Kind = MacroPlaybackActionKind.RunCommand,
                            DelayMilliseconds = delay,
                            Command = step.Command,
                            Arguments = step.Arguments,
                            WorkingDirectory = step.WorkingDirectory,
                        });
                        break;

                    case MacroStepKind.Loop:
                        for (var iteration = 0; iteration < step.LoopCount; iteration++)
                        {
                            AddDelay(delay);
                            ExpandSteps(step.Children, depth + 1, speed, stack, insideLoop: true);
                        }
                        break;

                    case MacroStepKind.MacroReference when !string.IsNullOrEmpty(step.MacroId):
                        if (!macros.TryGetValue(step.MacroId, out var referenced))
                        {
                            _warnings.Add($"Macro reference not found: {step.MacroId}.");
                            break;
                        }
                        if (stack.Contains(referenced.Id))
                        {
                            _warnings.Add($"Macro reference cycle skipped: {referenced.Name}.");
                            break;
                        }
                        // Cloning normalizes the copy. Validate the source first
                        // so an unfinished referenced step cannot disappear and
                        // leave the root macro running a partial sequence.
                        EnsureValidSource(referenced);
                        AddDelay(delay);
                        ExpandMacro(
                            referenced.Clone(),
                            depth + 1,
                            speed * Math.Clamp(
                                referenced.PlaybackSpeed,
                                MacroDefinition.MinPlaybackSpeed,
                                MacroDefinition.MaxPlaybackSpeed),
                            stack,
                            insideLoop);
                        break;

                    case MacroStepKind.MouseMove when step.MousePath.Count > 0:
                        var points = step.MousePath
                            .Select(point => new MacroMousePoint
                            {
                                X = point.X,
                                Y = point.Y,
                                OffsetMilliseconds = Scale(point.OffsetMilliseconds, speed),
                            })
                            .ToArray();
                        var mouseMoveMode = step.MouseMoveMode;
                        if (mouseMoveMode == MacroMouseMoveMode.RelativeToStart)
                        {
                            // Recording stores offsets from the first point. The
                            // sink sends relative deltas, so convert each point
                            // to the delta from the preceding point.
                            var previousX = 0;
                            var previousY = 0;
                            foreach (var point in points)
                            {
                                var x = point.X;
                                var y = point.Y;
                                point.X = x - previousX;
                                point.Y = y - previousY;
                                previousX = x;
                                previousY = y;
                            }
                            mouseMoveMode = MacroMouseMoveMode.Relative;
                        }
                        Add(new MacroPlaybackAction
                        {
                            Kind = MacroPlaybackActionKind.MouseMove,
                            DelayMilliseconds = delay,
                            MouseMoveMode = mouseMoveMode,
                            MousePath = points,
                        });

                        if (step.MouseClickButton is >= 1 and <= 5)
                        {
                            AddMouseClick(step.MouseClickButton, step.HoldMilliseconds, speed);
                        }
                        break;
                }
            }
        }

        private void AddMouseClick(int mouseButton, int holdMilliseconds, double speed)
        {
            var input = new MacroInput
            {
                Kind = MacroInputKind.MouseButton,
                MouseButton = mouseButton,
            };

            Add(new MacroPlaybackAction
            {
                Kind = MacroPlaybackActionKind.MouseButtonDown,
                Input = input,
            });
            Add(new MacroPlaybackAction
            {
                Kind = MacroPlaybackActionKind.Delay,
                DelayMilliseconds = Scale(holdMilliseconds, speed),
            });
            Add(new MacroPlaybackAction
            {
                Kind = MacroPlaybackActionKind.MouseButtonUp,
                Input = input,
            });
        }

        private void AddInput(
            MacroStep step,
            MacroInput input,
            int delay,
            double speed,
            bool insideLoop)
        {
            var downKind = input.Kind == MacroInputKind.MouseButton
                ? MacroPlaybackActionKind.MouseButtonDown
                : MacroPlaybackActionKind.KeyDown;
            var upKind = input.Kind == MacroInputKind.MouseButton
                ? MacroPlaybackActionKind.MouseButtonUp
                : MacroPlaybackActionKind.KeyUp;

            if (input.Kind == MacroInputKind.MouseWheel)
            {
                Add(new MacroPlaybackAction
                {
                    Kind = MacroPlaybackActionKind.MouseWheel,
                    Input = input,
                    DelayMilliseconds = delay,
                });
                return;
            }

            if (!step.IsDown)
            {
                Add(new MacroPlaybackAction
                {
                    Kind = upKind,
                    Input = input,
                    DelayMilliseconds = delay,
                });
                return;
            }

            Add(new MacroPlaybackAction
            {
                Kind = downKind,
                Input = input,
                DelayMilliseconds = delay,
            });

            if (!step.AutoRelease)
            {
                if (insideLoop)
                {
                    _hasHeldInputsInLoop = true;
                }

                return;
            }

            Add(new MacroPlaybackAction
            {
                Kind = MacroPlaybackActionKind.Delay,
                DelayMilliseconds = Scale(step.HoldMilliseconds, speed),
            });
            Add(new MacroPlaybackAction
            {
                Kind = upKind,
                Input = input,
            });
        }

        private void AddDelay(int delayMilliseconds)
        {
            if (delayMilliseconds <= 0)
            {
                return;
            }

            Add(new MacroPlaybackAction
            {
                Kind = MacroPlaybackActionKind.Delay,
                DelayMilliseconds = delayMilliseconds,
            });
        }

        private void Add(MacroPlaybackAction action)
        {
            EnsureActionBudget();
            _actions.Add(action);
            _totalDuration += action.DelayMilliseconds;
            if (action.Input is { } input)
            {
                switch (action.Kind)
                {
                    case MacroPlaybackActionKind.KeyDown:
                    case MacroPlaybackActionKind.MouseButtonDown:
                        _openDowns.RemoveAll(open => open.Matches(input));
                        _openDowns.Add(input);
                        break;

                    case MacroPlaybackActionKind.KeyUp:
                    case MacroPlaybackActionKind.MouseButtonUp:
                        _openDowns.RemoveAll(open => open.Matches(input));
                        break;
                }
            }

            if (action.Kind == MacroPlaybackActionKind.MouseMove &&
                action.MousePath.Count > 0)
            {
                _totalDuration += action.MousePath[^1].OffsetMilliseconds;
            }
        }

        private void EnsureActionBudget()
        {
            if (_actions.Count >= MaxActions)
            {
                throw new InvalidOperationException(
                    $"Macro expands to more than {MaxActions} actions.");
            }
        }

        private static int Scale(int milliseconds, double speed)
        {
            if (milliseconds <= 0 || speed <= 0)
            {
                return 0;
            }

            return (int)Math.Clamp(
                Math.Round(milliseconds / speed),
                0,
                MacroDefinition.MaxDelayMilliseconds * 10);
        }
    }
}
