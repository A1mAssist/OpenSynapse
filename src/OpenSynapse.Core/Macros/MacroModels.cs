namespace OpenSynapse.Core.Macros;

public enum MacroInputKind
{
    Keyboard = 0,
    MouseButton = 1,
    MouseWheel = 2,
    NagaSideButton = 3,
    DeviceButton = 4,
    RazerKey = 5,
}

public enum MacroStepKind
{
    Input = 0,
    Delay = 1,
    Text = 2,
    RunCommand = 3,
    Loop = 4,
    MacroReference = 5,
    MouseMove = 6,
}

public enum MacroPlaybackMode
{
    Once = 0,
    Repeat = 1,
    RepeatUntilStopped = 2,
}

public enum MacroTriggerMode
{
    Press = 0,
    Toggle = 1,
    Hold = 2,
    Sequence = 3,
    HoldRepeat = 4,
}

public enum MacroDelayMode
{
    Recorded = 0,
    Fixed = 1,
    None = 2,
}

public enum MacroMouseTrackingMode
{
    None = 0,
    Absolute = 1,
    Relative = 2,
    StartPoint = 3,
    Foreground = 4,
}

public enum MacroMouseMoveMode
{
    Absolute = 0,
    Relative = 1,
    ForegroundRelative = 2,
    RelativeToStart = 3,
}

[Flags]
public enum MacroModifierKeys
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8,
    All = Control | Shift | Alt | Windows,
}

public sealed class MacroInput
{
    public MacroInputKind Kind { get; set; }
    public int VirtualKey { get; set; }
    public int ScanCode { get; set; }
    public int MouseButton { get; set; }
    public int SideButton { get; set; }
    public int DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public int RazerKeyCode { get; set; }
    public int WheelDelta { get; set; }
    public bool WheelHorizontal { get; set; }
    public bool Extended { get; set; }

    public bool Matches(MacroInput other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            MacroInputKind.Keyboard => VirtualKey == other.VirtualKey && Extended == other.Extended,
            MacroInputKind.MouseButton => MouseButton == other.MouseButton,
            MacroInputKind.NagaSideButton => SideButton == other.SideButton,
            MacroInputKind.DeviceButton => DeviceId == other.DeviceId &&
                VirtualKey == other.VirtualKey &&
                ScanCode == other.ScanCode &&
                Extended == other.Extended,
            MacroInputKind.RazerKey => DeviceId == other.DeviceId &&
                RazerKeyCode == other.RazerKeyCode,
            MacroInputKind.MouseWheel => WheelDelta == other.WheelDelta &&
                WheelHorizontal == other.WheelHorizontal,
            _ => false,
        };
    }

    public bool IsModifierKey =>
        Kind is MacroInputKind.Keyboard or MacroInputKind.DeviceButton &&
        VirtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or
            0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    public bool IsValid(
        bool allowPrimaryMouseButton,
        bool allowWheel = true,
        bool allowNagaSideButton = false,
        bool allowDeviceButton = false)
    {
        return Kind switch
        {
            MacroInputKind.Keyboard => VirtualKey is > 0 and <= 0xFF && ScanCode is >= 0 and <= 0xFFFF,
            MacroInputKind.MouseButton => allowPrimaryMouseButton
                ? MouseButton is >= 1 and <= 5
                : MouseButton is >= 3 and <= 5,
            MacroInputKind.NagaSideButton => allowNagaSideButton && SideButton is >= 1 and <= 12,
            MacroInputKind.DeviceButton => allowDeviceButton &&
                DeviceId is > 0 and <= 0xFFFF &&
                VirtualKey is > 0 and <= 0xFF &&
                ScanCode is >= 0 and <= 0xFFFF,
            MacroInputKind.RazerKey => allowDeviceButton &&
                DeviceId is > 0 and <= 0xFFFF &&
                RazerKeyCode is > 0 and <= 0xFF,
            MacroInputKind.MouseWheel => allowWheel && WheelDelta is not 0 and >= -1200 and <= 1200,
            _ => false,
        };
    }

    public MacroInput Clone() => new()
    {
        Kind = Kind,
        VirtualKey = VirtualKey,
        ScanCode = ScanCode,
        MouseButton = MouseButton,
        SideButton = SideButton,
        DeviceId = DeviceId,
        DeviceName = DeviceName,
        RazerKeyCode = RazerKeyCode,
        WheelDelta = WheelDelta,
        WheelHorizontal = WheelHorizontal,
        Extended = Extended,
    };

    internal void ApplySafeDefaults()
    {
        var name = DeviceName?.Trim();
        DeviceName = string.IsNullOrEmpty(name) ? null : name[..Math.Min(name.Length, 128)];
    }
}

public sealed class MacroTrigger
{
    public MacroInput Input { get; set; } = new();
    public MacroModifierKeys Modifiers { get; set; }
    public MacroTriggerMode Mode { get; set; }
    public string? DeviceId { get; set; }
    public string? ControlId { get; set; }

    public bool Matches(MacroInput input, MacroModifierKeys modifiers)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Input.Matches(input) && Modifiers == modifiers;
    }

    public void ApplySafeDefaults()
    {
        Input.ApplySafeDefaults();
        Modifiers &= MacroModifierKeys.All;
        if (!Enum.IsDefined(Mode))
        {
            Mode = MacroTriggerMode.Press;
        }

        DeviceId = NormalizeOptional(DeviceId, 128);
        ControlId = NormalizeOptional(ControlId, 128);
    }

    public MacroTrigger Clone() => new()
    {
        Input = Input.Clone(),
        Modifiers = Modifiers,
        Mode = Mode,
        DeviceId = DeviceId,
        ControlId = ControlId,
    };

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

public sealed class MacroMousePoint
{
    public int X { get; set; }
    public int Y { get; set; }
    public int OffsetMilliseconds { get; set; }

    public MacroMousePoint Clone() => new()
    {
        X = X,
        Y = Y,
        OffsetMilliseconds = OffsetMilliseconds,
    };
}

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
                Children = (Children ?? [])
                    .Where(child => child is not null)
                    .Take(MacroDefinition.MaxSteps)
                    .ToList();
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
        MacroStepKind.MouseMove => MacroMousePath.IsValid(MousePath),
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

public sealed class MacroDefinition
{
    public const int MaxSteps = 1024;
    public const int MaxDelayMilliseconds = 60_000;
    public const int MaxRepeatCount = 999;
    public const int MaxNameLength = 64;
    public const int MaxNestingDepth = 8;
    public const double MinPlaybackSpeed = 0.1;
    public const double MaxPlaybackSpeed = 4.0;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public MacroTrigger? Trigger { get; set; }
    public MacroPlaybackMode PlaybackMode { get; set; }
    public int RepeatCount { get; set; } = 1;
    public double PlaybackSpeed { get; set; } = 1.0;
    public MacroMouseTrackingMode MouseTrackingMode { get; set; }
    public List<MacroStep> Steps { get; set; } = [];

    public void ApplySafeDefaults()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64)
        {
            Id = Guid.NewGuid().ToString("N");
        }

        Name = (Name ?? string.Empty).Trim();
        if (Name.Length > MaxNameLength)
        {
            Name = Name[..MaxNameLength];
        }

        if (Trigger?.Input is not { } triggerInput ||
            triggerInput.IsModifierKey ||
            !triggerInput.IsValid(
                allowPrimaryMouseButton: false,
                allowWheel: false,
                allowNagaSideButton: true,
                allowDeviceButton: true))
        {
            Trigger = null;
        }
        else
        {
            Trigger.ApplySafeDefaults();
        }

        if (!Enum.IsDefined(PlaybackMode))
        {
            PlaybackMode = MacroPlaybackMode.Once;
        }
        RepeatCount = Math.Clamp(RepeatCount, 1, MaxRepeatCount);
        if (PlaybackMode != MacroPlaybackMode.Repeat)
        {
            RepeatCount = 1;
        }
        if (Trigger?.Mode == MacroTriggerMode.HoldRepeat)
        {
            PlaybackMode = MacroPlaybackMode.RepeatUntilStopped;
            RepeatCount = 1;
        }

        if (double.IsNaN(PlaybackSpeed) || double.IsInfinity(PlaybackSpeed))
        {
            PlaybackSpeed = 1.0;
        }
        PlaybackSpeed = Math.Clamp(PlaybackSpeed, MinPlaybackSpeed, MaxPlaybackSpeed);

        if (!Enum.IsDefined(MouseTrackingMode))
        {
            MouseTrackingMode = MacroMouseTrackingMode.None;
        }

        // Keep incomplete steps. An editor field is often empty for a moment
        // while the user types, and a clone or profile save must not delete it.
        Steps = (Steps ?? [])
            .Where(step => step is not null)
            .Take(MaxSteps)
            .Select(step =>
            {
                step.ApplySafeDefaults();
                return step;
            })
            .ToList();
    }

    internal MacroDefinition Clone()
    {
        // Normalize the copy, never the live definition. Editing a required
        // field temporarily leaves it empty; a duration preview or playback
        // must not delete that pending step from the user's macro.
        var clone = new MacroDefinition
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            Trigger = Trigger?.Clone(),
            PlaybackMode = PlaybackMode,
            RepeatCount = RepeatCount,
            PlaybackSpeed = PlaybackSpeed,
            MouseTrackingMode = MouseTrackingMode,
            Steps = (Steps ?? [])
                .Where(step => step is not null)
                .Take(MaxSteps)
                .Select(step => step.Clone())
                .ToList(),
        };
        clone.ApplySafeDefaults();
        return clone;
    }
}

public sealed class MacroRecordingOptions
{
    public bool RecordDelays { get; set; } = true;
    public MacroDelayMode DelayMode { get; set; }
    public int FixedDelayMilliseconds { get; set; } = 100;
    public MacroMouseTrackingMode MouseTrackingMode { get; set; }
    public MacroInput? StopShortcut { get; set; }

    public MacroRecordingOptions Clone() => new()
    {
        RecordDelays = RecordDelays,
        DelayMode = DelayMode,
        FixedDelayMilliseconds = FixedDelayMilliseconds,
        MouseTrackingMode = MouseTrackingMode,
        StopShortcut = StopShortcut?.Clone(),
    };
}
