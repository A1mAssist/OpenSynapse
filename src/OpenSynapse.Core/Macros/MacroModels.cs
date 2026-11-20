using System.Text.Json.Serialization;

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

public enum MacroExecutionTarget
{
    Software = 0,
    Onboard = 1,
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
    public string? InstanceId { get; set; }
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
                Extended == other.Extended &&
                MatchesInstance(other),
            MacroInputKind.RazerKey => DeviceId == other.DeviceId &&
                RazerKeyCode == other.RazerKeyCode &&
                MatchesInstance(other),
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
        bool allowDeviceButton = false,
        bool allowExtraMouseButton = false)
    {
        return Kind switch
        {
            MacroInputKind.Keyboard => VirtualKey is > 0 and <= 0xFF && ScanCode is >= 0 and <= 0xFFFF,
            MacroInputKind.MouseButton => allowPrimaryMouseButton
                ? MouseButton >= 1 && MouseButton <= (allowExtraMouseButton ? 10 : 5)
                : MouseButton >= 3 && MouseButton <= (allowExtraMouseButton ? 10 : 5),
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
        InstanceId = InstanceId,
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
        InstanceId = NormalizeOptional(InstanceId, 256);
    }

    private bool MatchesInstance(MacroInput other) =>
        string.IsNullOrWhiteSpace(InstanceId) ||
        string.IsNullOrWhiteSpace(other.InstanceId) ||
        StringComparer.OrdinalIgnoreCase.Equals(InstanceId.Trim(), other.InstanceId.Trim());

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

public sealed class MacroTrigger
{
    public MacroInput Input { get; set; } = new();
    public MacroModifierKeys Modifiers { get; set; }
    public MacroTriggerMode Mode { get; set; }
    // A key can override how the shared macro plays. Null keeps the
    // definition's setting, so two keys can run the same macro differently.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MacroPlaybackMode? PlaybackMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RepeatCount { get; set; }
    public bool HyperShiftLayer { get; set; }
    public string? DeviceId { get; set; }
    public string? ControlId { get; set; }
    public MacroSoftwareBinding? SoftwareBinding { get; set; }

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

        if (PlaybackMode is { } playback)
        {
            PlaybackMode = Enum.IsDefined(playback)
                ? MacroPlaybackChoices.Normalize(Mode, playback)
                : null;
        }
        RepeatCount = PlaybackMode == MacroPlaybackMode.Repeat
            ? Math.Clamp(RepeatCount ?? 1, 1, MacroDefinition.MaxRepeatCount)
            : null;

        DeviceId = NormalizeOptional(DeviceId, 128);
        ControlId = NormalizeOptional(ControlId, 128);
    }

    public MacroTrigger Clone() => new()
    {
        Input = Input.Clone(),
        Modifiers = Modifiers,
        Mode = Mode,
        PlaybackMode = PlaybackMode,
        RepeatCount = RepeatCount,
        HyperShiftLayer = HyperShiftLayer,
        DeviceId = DeviceId,
        ControlId = ControlId,
        SoftwareBinding = SoftwareBinding?.Clone(),
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

/// <summary>
/// The device assignment a software macro replaced when it had to switch the key
/// off. It is restored when the macro is deleted, so the key never stays dead.
/// </summary>
public sealed class MacroSoftwareBinding
{
    public ushort ProductId { get; set; }
    public string? InstanceId { get; set; }
    public byte Layer { get; set; }
    public byte ButtonId { get; set; }
    public byte OriginalFunctionId { get; set; }
    public List<byte> OriginalData { get; set; } = [];

    public MacroSoftwareBinding Clone() => new()
    {
        ProductId = ProductId,
        InstanceId = InstanceId,
        Layer = Layer,
        ButtonId = ButtonId,
        OriginalFunctionId = OriginalFunctionId,
        OriginalData = OriginalData.ToList(),
    };
}


public sealed class MacroDefinition
{
    public const int MaxSteps = 1024;
    public const int MaxTriggers = 64;
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
    public List<MacroTrigger> AdditionalTriggers { get; set; } = [];
    public MacroSoftwareBinding? SoftwareBinding { get; set; }
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

        if (!IsValidTrigger(Trigger))
        {
            Trigger = null;
        }
        else if (Trigger is { } trigger)
        {
            trigger.ApplySafeDefaults();
        }

        AdditionalTriggers = (AdditionalTriggers ?? [])
            .Where(trigger => IsValidTrigger(trigger))
            .Take(MaxTriggers)
            .ToList();
        foreach (var trigger in AdditionalTriggers)
        {
            trigger.ApplySafeDefaults();
        }

        if (Trigger is null && AdditionalTriggers.Count > 0)
        {
            Trigger = AdditionalTriggers[0];
            AdditionalTriggers.RemoveAt(0);
        }

        for (var index = AdditionalTriggers.Count - 1; index >= 0; index--)
        {
            var trigger = AdditionalTriggers[index];
            if ((Trigger is not null && SameTrigger(Trigger, trigger)) ||
                AdditionalTriggers.Take(index).Any(item => SameTrigger(item, trigger)))
            {
                AdditionalTriggers.RemoveAt(index);
            }
        }

        if (AdditionalTriggers.Count > MaxTriggers - (Trigger is null ? 0 : 1))
        {
            AdditionalTriggers.RemoveRange(
                MaxTriggers - (Trigger is null ? 0 : 1),
                AdditionalTriggers.Count - (MaxTriggers - (Trigger is null ? 0 : 1)));
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
        Steps ??= [];
        Steps.RemoveAll(step => step is null);
        if (Steps.Count > MaxSteps)
        {
            Steps.RemoveRange(MaxSteps, Steps.Count - MaxSteps);
        }
        foreach (var step in Steps)
        {
            step.ApplySafeDefaults();
        }
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
            AdditionalTriggers = (AdditionalTriggers ?? [])
                .Where(trigger => trigger is not null)
                .Select(trigger => trigger.Clone())
                .ToList(),
            SoftwareBinding = SoftwareBinding?.Clone(),
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

    public IEnumerable<MacroTrigger> AllTriggers
    {
        get
        {
            if (Trigger is not null)
            {
                yield return Trigger;
            }

            foreach (var trigger in AdditionalTriggers ?? [])
            {
                if (trigger is not null)
                {
                    yield return trigger;
                }
            }
        }
    }

    public MacroTrigger? FindTrigger(
        MacroInput input,
        MacroModifierKeys modifiers,
        bool hyperShiftLayer)
    {
        foreach (var trigger in AllTriggers)
        {
            if (trigger.Modifiers == modifiers &&
                trigger.HyperShiftLayer == hyperShiftLayer &&
                trigger.Input.Matches(input))
            {
                return trigger;
            }
        }

        return null;
    }

    public MacroTrigger? FindTrigger(MacroInput input, bool hyperShiftLayer) =>
        FindTrigger(input, MacroModifierKeys.None, hyperShiftLayer);

    /// <summary>Playback a specific trigger runs with; the definition is the default.</summary>
    public MacroPlaybackMode EffectivePlaybackMode(MacroTrigger? trigger) =>
        trigger is null
            ? PlaybackMode
            : trigger.PlaybackMode ??
              (trigger.Mode == MacroTriggerMode.HoldRepeat
                  ? MacroPlaybackMode.RepeatUntilStopped
                  : PlaybackMode);

    public int EffectiveRepeatCount(MacroTrigger? trigger) =>
        EffectivePlaybackMode(trigger) == MacroPlaybackMode.Repeat
            ? Math.Clamp(trigger?.RepeatCount ?? RepeatCount, 1, MaxRepeatCount)
            : 1;

    internal static bool IsValidTrigger(MacroTrigger? trigger) =>
        trigger is { Input: { } input } &&
        Enum.IsDefined(trigger.Mode) &&
        (trigger.Modifiers & ~MacroModifierKeys.All) == 0 &&
        !input.IsModifierKey &&
        input.IsValid(
            allowPrimaryMouseButton: true,
            // The mapping page can bind a macro to the wheel directions; the
            // runtime already dispatches wheel triggers.
            allowWheel: true,
            allowNagaSideButton: true,
            allowDeviceButton: true,
            allowExtraMouseButton: true);

    private static bool SameTrigger(MacroTrigger left, MacroTrigger right) =>
        left.Input.Matches(right.Input) &&
        left.Modifiers == right.Modifiers &&
        left.HyperShiftLayer == right.HyperShiftLayer &&
        StringComparer.Ordinal.Equals(left.DeviceId, right.DeviceId) &&
        StringComparer.Ordinal.Equals(left.ControlId, right.ControlId);
}

public sealed class MacroRecordingOptions
{
    public bool RecordDelays { get; set; } = true;
    public bool RecordOwnWindow { get; set; }
    public MacroDelayMode DelayMode { get; set; }
    public int FixedDelayMilliseconds { get; set; } = 100;
    public MacroMouseTrackingMode MouseTrackingMode { get; set; }
    public MacroInput? StopShortcut { get; set; }

    public MacroRecordingOptions Clone() => new()
    {
        RecordDelays = RecordDelays,
        RecordOwnWindow = RecordOwnWindow,
        DelayMode = DelayMode,
        FixedDelayMilliseconds = FixedDelayMilliseconds,
        MouseTrackingMode = MouseTrackingMode,
        StopShortcut = StopShortcut?.Clone(),
    };
}
