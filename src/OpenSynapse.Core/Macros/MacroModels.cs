namespace OpenSynapse.Core.Macros;

public enum MacroInputKind
{
    Keyboard = 0,
    MouseButton = 1,
    MouseWheel = 2,
}

public enum MacroStepKind
{
    Input = 0,
    Delay = 1,
}

public enum MacroPlaybackMode
{
    Once = 0,
    Repeat = 1,
}

public sealed class MacroInput
{
    public MacroInputKind Kind { get; set; }
    public int VirtualKey { get; set; }
    public int ScanCode { get; set; }
    public int MouseButton { get; set; }
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
            MacroInputKind.MouseWheel => WheelDelta == other.WheelDelta &&
                WheelHorizontal == other.WheelHorizontal,
            _ => false,
        };
    }

    public bool IsValid(bool allowPrimaryMouseButton, bool allowWheel = true)
    {
        return Kind switch
        {
            MacroInputKind.Keyboard => VirtualKey is > 0 and <= 0xFF && ScanCode is >= 0 and <= 0xFFFF,
            MacroInputKind.MouseButton => allowPrimaryMouseButton
                ? MouseButton is >= 1 and <= 5
                : MouseButton is >= 3 and <= 5,
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
        WheelDelta = WheelDelta,
        WheelHorizontal = WheelHorizontal,
        Extended = Extended,
    };
}

public sealed class MacroTrigger
{
    public MacroInput Input { get; set; } = new();

    public MacroTrigger Clone() => new() { Input = Input.Clone() };
}

public sealed class MacroStep
{
    public MacroStepKind Kind { get; set; }
    public MacroInput? Input { get; set; }
    public bool IsDown { get; set; }
    public int DelayMilliseconds { get; set; }

    /// <summary>
    /// How long a key stays pressed before it is released again, in milliseconds.
    /// A step that owns its own key release only needs this for the gap between
    /// the press and the release.
    /// </summary>
    public int HoldMilliseconds { get; set; } = DefaultHoldMilliseconds;

    public const int DefaultHoldMilliseconds = 50;
    public const int MaxHoldMilliseconds = 5000;

    public void ApplySafeDefaults()
    {
        DelayMilliseconds = Math.Clamp(DelayMilliseconds, 0, MacroDefinition.MaxDelayMilliseconds);
        HoldMilliseconds = Math.Clamp(HoldMilliseconds, 0, MaxHoldMilliseconds);
        if (Kind == MacroStepKind.Delay)
        {
            Input = null;
            IsDown = false;
            return;
        }

        Kind = MacroStepKind.Input;
        Input ??= new MacroInput();
    }

    internal MacroStep Clone() => new()
    {
        Kind = Kind,
        Input = Input?.Clone(),
        IsDown = IsDown,
        DelayMilliseconds = DelayMilliseconds,
        HoldMilliseconds = HoldMilliseconds,
    };
}

public sealed class MacroDefinition
{
    public const int MaxSteps = 512;
    public const int MaxDelayMilliseconds = 60_000;
    public const int MaxRepeatCount = 999;
    public const int MaxNameLength = 64;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public MacroTrigger? Trigger { get; set; }
    public MacroPlaybackMode PlaybackMode { get; set; }
    public int RepeatCount { get; set; } = 1;
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
            !triggerInput.IsValid(allowPrimaryMouseButton: false, allowWheel: false))
        {
            Trigger = null;
        }

        if (!Enum.IsDefined(PlaybackMode))
        {
            PlaybackMode = MacroPlaybackMode.Once;
        }
        RepeatCount = Math.Clamp(RepeatCount, 1, MaxRepeatCount);
        if (PlaybackMode == MacroPlaybackMode.Once)
        {
            RepeatCount = 1;
        }

        Steps = (Steps ?? [])
            .Where(step => step is not null)
            .Take(MaxSteps)
            .Select(step =>
            {
                step.ApplySafeDefaults();
                return step;
            })
            .Where(step => step.Kind == MacroStepKind.Delay ||
                step.Input?.IsValid(allowPrimaryMouseButton: true) == true)
            .ToList();
    }

    internal MacroDefinition Clone()
    {
        ApplySafeDefaults();
        return new MacroDefinition
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            Trigger = Trigger?.Clone(),
            PlaybackMode = PlaybackMode,
            RepeatCount = RepeatCount,
            Steps = Steps.Select(step => step.Clone()).ToList(),
        };
    }
}