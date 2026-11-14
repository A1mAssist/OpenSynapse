namespace OpenSynapse.Core.Macros;

public sealed class MacroStep
{
    public MacroStepKind Kind { get; set; }
    public MacroInput? Input { get; set; }
    public bool IsDown { get; set; }
    public bool AutoRelease { get; set; } = true;
    public int DelayMilliseconds { get; set; }
    public int HoldMilliseconds { get; set; } = DefaultHoldMilliseconds;
    public string? Text { get; set; }
    public string? Command { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public int LoopCount { get; set; } = 1;
    public List<MacroStep> Children { get; set; } = [];
    public string? MacroId { get; set; }
    public MacroMouseMoveMode MouseMoveMode { get; set; }
    public List<MacroMousePoint> MousePath { get; set; } = [];
    // 0 = move only; 1..5 = click that mouse button after the final path point.
    public int MouseClickButton { get; set; }
    public string? GroupId { get; set; }

    public const int DefaultHoldMilliseconds = 50;
    public const int MaxHoldMilliseconds = 5000;
    public const int MaxTextLength = 4096;
    public const int MaxCommandLength = 1024;
    public const int MaxArgumentsLength = 2048;
    public const int MaxWorkingDirectoryLength = 1024;
    public const int MaxMousePathPoints = 4096;
    public const int MaxLoopCount = 999;

    public void ApplySafeDefaults()
    {
        if (!Enum.IsDefined(Kind))
        {
            Kind = MacroStepKind.Input;
        }

        DelayMilliseconds = Math.Clamp(DelayMilliseconds, 0, MacroDefinition.MaxDelayMilliseconds);
        HoldMilliseconds = Math.Clamp(HoldMilliseconds, 0, MaxHoldMilliseconds);
        GroupId = NormalizeOptional(GroupId, 64);
        MouseClickButton =
            Kind == MacroStepKind.MouseMove &&
            MouseClickButton is >= 1 and <= 5
                ? MouseClickButton
                : 0;

        switch (Kind)
        {
            case MacroStepKind.Input:
                Input ??= new MacroInput();
                Text = null;
                Command = null;
                Arguments = null;
                WorkingDirectory = null;
                LoopCount = 1;
                Children = [];
                MacroId = null;
                MousePath = [];
                return;

            case MacroStepKind.Delay:
                Input = null;
                IsDown = false;
                AutoRelease = true;
                Text = null;
                Command = null;
                Arguments = null;
                WorkingDirectory = null;
                LoopCount = 1;
                Children = [];
                MacroId = null;
                MousePath = [];
                return;

            case MacroStepKind.Text:
                Input = null;
                IsDown = false;
                AutoRelease = true;
                Text = NormalizeText(Text);
                Command = null;
                Arguments = null;
                WorkingDirectory = null;
                LoopCount = 1;
                Children = [];
                MacroId = null;
                MousePath = [];
                return;

            case MacroStepKind.RunCommand:
                Input = null;
                IsDown = false;
                AutoRelease = true;
                Text = null;
                Command = NormalizeOptional(Command, MaxCommandLength);
                Arguments = NormalizeOptional(Arguments, MaxArgumentsLength);
                WorkingDirectory = NormalizeOptional(WorkingDirectory, MaxWorkingDirectoryLength);
                LoopCount = 1;
                Children = [];
                MacroId = null;
                MousePath = [];
                return;

            case MacroStepKind.Loop:
                Input = null;
                IsDown = false;
                AutoRelease = true;
                Text = null;
                Command = null;
                Arguments = null;
                WorkingDirectory = null;
                LoopCount = Math.Clamp(LoopCount, 1, MaxLoopCount);
                Children ??= [];
                Children.RemoveAll(child => child is null);
                if (Children.Count > MacroDefinition.MaxSteps)
                {
                    Children.RemoveRange(MacroDefinition.MaxSteps, Children.Count - MacroDefinition.MaxSteps);
                }
                foreach (var child in Children)
                {
                    child.ApplySafeDefaults();
                }
                MacroId = null;
                MousePath = [];
                return;

            case MacroStepKind.MacroReference:
                Input = null;
                IsDown = false;
                AutoRelease = true;
                Text = null;
                Command = null;
                Arguments = null;
                WorkingDirectory = null;
                LoopCount = 1;
                Children = [];
                MacroId = NormalizeOptional(MacroId, 64);
                MousePath = [];
                return;

            case MacroStepKind.MouseMove:
                Input = null;
                IsDown = false;
                AutoRelease = true;
                Text = null;
                Command = null;
                Arguments = null;
                WorkingDirectory = null;
                LoopCount = 1;
                Children = [];
                MacroId = null;
                if (!Enum.IsDefined(MouseMoveMode))
                {
                    MouseMoveMode = MacroMouseMoveMode.Absolute;
                }
                MousePath = (MousePath ?? [])
                    .Where(point => point is not null)
                    .Take(MaxMousePathPoints)
                    .Select(point =>
                    {
                        point.X = Math.Clamp(point.X, -100_000, 100_000);
                        point.Y = Math.Clamp(point.Y, -100_000, 100_000);
                        point.OffsetMilliseconds = Math.Clamp(
                            point.OffsetMilliseconds,
                            0,
                            MacroDefinition.MaxDelayMilliseconds);
                        return point;
                    })
                    .ToList();
                return;
        }
    }

    public bool IsValid() => Kind switch
    {
        MacroStepKind.Input => Input?.IsValid(allowPrimaryMouseButton: true) == true,
        MacroStepKind.Delay => true,
        MacroStepKind.Text => !string.IsNullOrEmpty(Text),
        MacroStepKind.RunCommand => !string.IsNullOrEmpty(Command),
        MacroStepKind.Loop => Children.Count > 0,
        MacroStepKind.MacroReference => !string.IsNullOrWhiteSpace(MacroId) && MacroId.Length <= 64,
        MacroStepKind.MouseMove =>
            MacroMousePath.IsValid(MousePath) &&
            MouseClickButton is >= 0 and <= 5,
        _ => false,
    };

    internal MacroStep Clone() => new()
    {
        Kind = Kind,
        Input = Input?.Clone(),
        IsDown = IsDown,
        AutoRelease = AutoRelease,
        DelayMilliseconds = DelayMilliseconds,
        HoldMilliseconds = HoldMilliseconds,
        Text = Text,
        Command = Command,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        LoopCount = LoopCount,
        Children = Children.Select(child => child.Clone()).ToList(),
        MacroId = MacroId,
        MouseMoveMode = MouseMoveMode,
        MousePath = MousePath.Select(point => point.Clone()).ToList(),
        MouseClickButton = MouseClickButton,
        GroupId = GroupId,
    };

    private static string NormalizeText(string? value)
    {
        value ??= string.Empty;
        return value.Length > MaxTextLength ? value[..MaxTextLength] : value;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length > maxLength ? value[..maxLength] : value;
    }
}
