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
        IReadOnlyList<string> warnings)
    {
        Actions = actions;
        TotalDurationMilliseconds = totalDurationMilliseconds;
        Warnings = warnings;
    }

    public IReadOnlyList<MacroPlaybackAction> Actions { get; }
    public long TotalDurationMilliseconds { get; }
    public IReadOnlyList<string> Warnings { get; }

    public static MacroPlaybackPlan Build(
        MacroDefinition macro,
        IReadOnlyDictionary<string, MacroDefinition>? macros = null)
    {
        ArgumentNullException.ThrowIfNull(macro);
        EnsureValidSource(macro);
        var normalized = macro.Clone();
        var builder = new Builder(macros ?? new Dictionary<string, MacroDefinition>(StringComparer.Ordinal));
        builder.ExpandMacro(normalized, depth: 0, speed: normalized.PlaybackSpeed, stack: []);
        return builder.Build();
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
        private long _totalDuration;

        public MacroPlaybackPlan Build() => new(_actions, _totalDuration, _warnings);

        public void ExpandMacro(
            MacroDefinition macro,
            int depth,
            double speed,
            HashSet<string> stack)
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
                    ExpandSteps(macro.Steps, depth, speed, stack);
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
            HashSet<string> stack)
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
                        AddInput(step, input, delay, speed);
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
                            ExpandSteps(step.Children, depth + 1, speed, stack);
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
                            stack);
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
                        break;
                }
            }
        }

        private void AddInput(MacroStep step, MacroInput input, int delay, double speed)
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
