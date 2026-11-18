namespace OpenSynapse.Core.Macros;

/// <summary>
/// Synapse plays these shortcuts in software: the device firmware only has
/// native ids for three of them and keeps the modifier down when the rest are
/// written as raw chords.
/// </summary>
public static class SoftwareShortcutCatalog
{
    /// <summary>Macro ids of the built-in actions, kept out of the user's library.</summary>
    public const string MacroIdPrefix = "sys-shortcut:";

    public static IReadOnlyList<SoftwareShortcutAction> Actions { get; } =
    [
        new("screenshot", "Screenshot",
        [
            Down(Keys.LeftWin),
            Down(Keys.LeftShift),
            Down(Keys.S),
            Up(Keys.S, delayMilliseconds: 10),
            Up(Keys.LeftShift),
            Up(Keys.LeftWin),
        ]),
        new("xbox-game-bar", "Xbox Game Bar",
        [
            Down(Keys.LeftWin),
            Down(Keys.G),
            Up(Keys.G, delayMilliseconds: 10),
            Up(Keys.LeftWin),
        ]),
        new("cycle-apps", "Cycle apps",
        [
            Down(Keys.LeftWin),
            Down(Keys.Tab),
            Up(Keys.Tab, delayMilliseconds: 10),
            Up(Keys.LeftWin),
        ]),
        new("task-manager", "Task Manager", [Launch("taskmgr.exe")]),
        new("close-app", "Close app",
        [
            Down(Keys.LeftAlt),
            Down(Keys.F4),
            Up(Keys.F4, delayMilliseconds: 10),
            Up(Keys.LeftAlt),
        ]),
        new("switch-apps", "Switch apps",
        [
            Down(Keys.LeftAlt),
            Down(Keys.Tab),
            Up(Keys.Tab, delayMilliseconds: 10),
            Up(Keys.LeftAlt),
        ]),
        new("cut", "Cut",
        [
            Down(Keys.LeftCtrl),
            Down(Keys.X),
            Up(Keys.X, delayMilliseconds: 10),
            Up(Keys.LeftCtrl),
        ]),
        new("copy", "Copy",
        [
            Down(Keys.LeftCtrl),
            Down(Keys.C),
            Up(Keys.C, delayMilliseconds: 10),
            Up(Keys.LeftCtrl),
        ]),
        new("paste", "Paste",
        [
            Down(Keys.LeftCtrl),
            Down(Keys.V),
            Up(Keys.V, delayMilliseconds: 10),
            Up(Keys.LeftCtrl),
        ]),
    ];

    /// <summary>
    /// The action behind a built-in macro id; null for user macros.
    /// </summary>
    public static SoftwareShortcutAction? ActionOf(string? macroId)
    {
        if (string.IsNullOrEmpty(macroId) ||
            !macroId.StartsWith(MacroIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var id = macroId[MacroIdPrefix.Length..];
        return Actions.FirstOrDefault(action =>
            string.Equals(action.Id, id, StringComparison.Ordinal));
    }

    public static int IndexOf(string actionId)
    {
        for (var index = 0; index < Actions.Count; index++)
        {
            if (string.Equals(Actions[index].Id, actionId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Rebuilds a stored entry with the current sequence but its saved triggers,
    /// so a sequence fix ships without dropping the user's bindings.
    /// </summary>
    public static MacroDefinition Refresh(MacroDefinition stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var action = ActionOf(stored.Id) ?? throw new InvalidOperationException(
            $"Macro '{stored.Id}' is not a software shortcut.");
        var fresh = action.CreateMacro();
        fresh.Trigger = stored.Trigger;
        fresh.AdditionalTriggers = stored.AdditionalTriggers;
        fresh.SoftwareBinding = stored.SoftwareBinding;
        return fresh;
    }

    /// <summary>The stored shortcut entries, ready for the runtime and the mapping page.</summary>
    public static IReadOnlyList<MacroDefinition> Bindings(IEnumerable<MacroDefinition> stored) =>
        stored.Where(macro => ActionOf(macro.Id) is not null).Select(Refresh).ToArray();

    private static MacroStep Down(Key key) => Input(key, isDown: true);

    private static MacroStep Up(Key key, int delayMilliseconds = 0) =>
        Input(key, isDown: false, delayMilliseconds);

    private static MacroStep Input(Key key, bool isDown, int delayMilliseconds = 0) => new()
    {
        Kind = MacroStepKind.Input,
        IsDown = isDown,
        // The sequence spells out both halves; an auto release would close
        // each key before the chord is complete.
        AutoRelease = false,
        DelayMilliseconds = delayMilliseconds,
        Input = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = key.VirtualKey,
            ScanCode = key.ScanCode,
            Extended = key.Extended,
        },
    };

    private static MacroStep Launch(string command) => new()
    {
        Kind = MacroStepKind.RunCommand,
        Command = command,
    };

    private readonly record struct Key(ushort VirtualKey, ushort ScanCode, bool Extended = false);

    private static class Keys
    {
        public static readonly Key LeftCtrl = new(0x11, 0x1D);
        public static readonly Key LeftShift = new(0x10, 0x2A);
        public static readonly Key LeftAlt = new(0x12, 0x38);
        public static readonly Key LeftWin = new(0x5B, 0x5B, Extended: true);
        public static readonly Key Tab = new(0x09, 0x0F);
        public static readonly Key S = new(0x53, 0x1F);
        public static readonly Key G = new(0x47, 0x22);
        public static readonly Key X = new(0x58, 0x2D);
        public static readonly Key C = new(0x43, 0x2E);
        public static readonly Key V = new(0x56, 0x2F);
        public static readonly Key F4 = new(0x73, 0x3E);
    }
}

public sealed record SoftwareShortcutAction(
    string Id,
    string Name,
    IReadOnlyList<MacroStep> Steps)
{
    public string MacroId => SoftwareShortcutCatalog.MacroIdPrefix + Id;

    public MacroDefinition CreateMacro() => new()
    {
        Id = MacroId,
        Name = Name,
        Enabled = true,
        PlaybackMode = MacroPlaybackMode.Once,
        RepeatCount = 1,
        Steps = Steps.Select(CloneStep).ToList(),
    };

    private static MacroStep CloneStep(MacroStep step) => new()
    {
        Kind = step.Kind,
        Input = step.Input?.Clone(),
        IsDown = step.IsDown,
        AutoRelease = step.AutoRelease,
        DelayMilliseconds = step.DelayMilliseconds,
        HoldMilliseconds = step.HoldMilliseconds,
        Command = step.Command,
        Arguments = step.Arguments,
        WorkingDirectory = step.WorkingDirectory,
    };
}
