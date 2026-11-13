using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Mapping groups the middleware editor exposes. Each group maps to one function id in the
/// 0x02:0x0C button-assignment frame.
/// </summary>
public enum RazerHidMappingGroup
{
    Keep = 0,
    Off,
    MouseButton,
    KeyboardKey,
    Dpi,
    Profile,
    MediaKey,
    PowerKey,
    DoubleClick,
    ScrollWheelMode,
    RazerKey,
    WindowsShortcut,
    HyperShiftToggle,
    Macro,
    TurboKey,
    TurboButton,
    DpiClutch,
    ProfileSpecific,
}

public sealed record RazerHidMappingAction(string Name, byte FunctionId, byte[] Data);

/// <summary>Raw assignment decoded back into the shared editor fields.</summary>
public sealed record RazerHidDecodedMapping(
    RazerHidMappingGroup Group,
    int OptionIndex,
    int ModifierIndex = 0,
    double? TurboRate = null,
    double? DpiClutchX = null,
    double? DpiClutchY = null,
    double? ProfileId = null,
    int? MacroSlot = null,
    RazerHidMacroPlayback? MacroPlayback = null,
    int? MacroRepeatCount = null);

public sealed record RazerHidMacroOption(byte Id, string Name);

public enum RazerHidMacroPlayback
{
    Once,
    Repeat,
    Hold,
    Toggle,
    Sequence,
}

/// <summary>
/// Selectable onboard mappings, transcribed from the middleware's fnId switch
/// (chunk 6120, module 56415). DPI-clutch thresholds, turbo rates and profile ids are
/// explicit caller inputs; controller buttons are not listed because their ids are not
/// available from this protocol. Macro slots are listed only when the caller supplies the
/// ids read from the device.
/// </summary>
public static partial class RazerHidMappingActions
{
    public static IReadOnlyList<string> ModifierNames { get; } = ["None", "Ctrl", "Shift", "Alt", "Win"];

    public static IReadOnlyList<byte> ModifierMasks { get; } = [0x00, 0x01, 0x02, 0x04, 0x08];

    private static readonly (string Name, byte Id)[] MouseButtons =
    [
        ("Left", 0x01), ("Right", 0x02), ("Middle", 0x03), ("Back", 0x04), ("Forward", 0x05),
        ("Button 6", 0x06), ("Button 7", 0x07), ("Button 8", 0x08), ("Scroll up", 0x09),
        ("Scroll down", 0x0A), ("Scroll left", 0x34), ("Scroll right", 0x35), ("DPI up", 0x0B), ("DPI down", 0x0C), ("User select", 0x0D),
        ("Profile switch", 0x0E), ("Sniper", 0x0F), ("DPI cycle up", 0x60),
        ("DPI cycle down", 0x61), ("Previous page", 0x63), ("Next page", 0x64),
        ("Volume up", 0x65), ("Volume down", 0x66), ("Mute", 0x67), ("Scroll left", 0x68),
        ("Scroll right", 0x69), ("Scroll mode switch", 0x6A),
    ];

    // USB HID usage page 0x07. Modifier keys carry the usage in the first byte and 0 in the
    // second, which is what the middleware's keyboardGroupToFnDataByte writes.
    private static readonly (string Name, byte Hid)[] Keys =
    [
        ("A", 4), ("B", 5), ("C", 6), ("D", 7), ("E", 8), ("F", 9), ("G", 10), ("H", 11),
        ("I", 12), ("J", 13), ("K", 14), ("L", 15), ("M", 16), ("N", 17), ("O", 18), ("P", 19),
        ("Q", 20), ("R", 21), ("S", 22), ("T", 23), ("U", 24), ("V", 25), ("W", 26), ("X", 27),
        ("Y", 28), ("Z", 29),
        ("1", 30), ("2", 31), ("3", 32), ("4", 33), ("5", 34), ("6", 35), ("7", 36), ("8", 37),
        ("9", 38), ("0", 39),
        ("Enter", 40), ("Esc", 41), ("Backspace", 42), ("Tab", 43), ("Space", 44),
        ("-", 45), ("=", 46), ("[", 47), ("]", 48), ("\\", 49), (";", 51), ("'", 52),
        ("`", 53), (",", 54), (".", 55), ("/", 56), ("Caps Lock", 57),
        ("F1", 58), ("F2", 59), ("F3", 60), ("F4", 61), ("F5", 62), ("F6", 63), ("F7", 64),
        ("F8", 65), ("F9", 66), ("F10", 67), ("F11", 68), ("F12", 69),
        ("Print Screen", 70), ("Scroll Lock", 71), ("Pause", 72), ("Insert", 73), ("Home", 74),
        ("Page Up", 75), ("Delete", 76), ("End", 77), ("Page Down", 78),
        ("Right Arrow", 79), ("Left Arrow", 80), ("Down Arrow", 81), ("Up Arrow", 82),
        ("Num Lock", 83), ("KP /", 84), ("KP *", 85), ("KP -", 86), ("KP +", 87), ("KP Enter", 88),
        ("KP 1", 89), ("KP 2", 90), ("KP 3", 91), ("KP 4", 92), ("KP 5", 93), ("KP 6", 94),
        ("KP 7", 95), ("KP 8", 96), ("KP 9", 97), ("KP 0", 98), ("KP .", 99),
        ("Left Ctrl", 224), ("Left Shift", 225), ("Left Alt", 226), ("Left Win", 227),
        ("Right Ctrl", 228), ("Right Shift", 229), ("Right Alt", 230), ("Right Win", 231),
    ];

    private static readonly (string Name, byte Code)[] DpiActions =
    [
        ("DPI up", 0x01), ("DPI down", 0x02), ("DPI cycle up", 0x06), ("DPI cycle down", 0x07),
    ];

    private static readonly (string Name, byte Code)[] ProfileActions =
    [
        ("Profile up", 0x01), ("Profile down", 0x02), ("Profile cycle up", 0x04),
        ("Profile cycle down", 0x05),
    ];

    private static readonly (string Name, ushort Code)[] MediaKeys =
    [
        ("Mute", 226), ("Volume up", 233), ("Volume down", 234), ("Play / Pause", 205),
        ("Next track", 181), ("Previous track", 182), ("Stop", 183), ("Eject", 184),
        ("Cycle apps", 671), ("Brightness up", 111), ("Brightness down", 112), ("Spotlight", 545),
    ];

    private static readonly (string Name, byte Code)[] PowerKeys =
    [
        ("Power", 129), ("Sleep", 130), ("Wake", 131),
    ];

    private static readonly (string Name, byte Code)[] ScrollWheelModes =
    [
        ("Toggle scroll mode", 0x01), ("Scroll up", 0x02), ("Scroll down", 0x03),
        ("Scroll cycle up", 0x04), ("Scroll cycle down", 0x05),
    ];

    private static readonly (string Name, byte Code)[] RazerKeys =
    [
        ("Fn", 1), ("Razer key", 2), ("Game mode", 3), ("Macro record", 4),
        ("Switch internal display", 5), ("Display brightness up", 6), ("Display brightness down", 7),
        ("Keyboard backlight up", 8), ("Keyboard backlight down", 9), ("Low power mode", 11),
        ("DPI up", 32), ("DPI down", 33), ("Back", 34), ("Forward", 35), ("Macro keys", 36),
        ("Print Screen", 70), ("System sleep", 76), ("Profile switch", 80), ("Sniper", 81),
        ("DPI cycle up", 82), ("DPI cycle down", 83), ("Scroll mode switch", 84),
        ("Haptic scroll up", 85), ("Haptic scroll down", 86), ("Haptic scroll cycle up", 87),
        ("Haptic scroll cycle down", 88), ("HyperShift", 89), ("Actuation adjustment", 112),
        ("Rapid trigger adjustment", 113), ("SnapTap toggle", 117), ("Mic mute", 216),
    ];

    private static readonly (string Name, byte Code)[] WindowsShortcuts =
    [
        ("Screenshot", 99), ("Xbox Game Bar", 100), ("Cycle apps", 101),
    ];

    public static IReadOnlyList<string> OptionNames(
        RazerHidMappingGroup group,
        IReadOnlyList<RazerHidMacroOption>? macros = null) => group switch
    {
        RazerHidMappingGroup.Off => ["Off"],
        RazerHidMappingGroup.MouseButton => Names(MouseButtons),
        RazerHidMappingGroup.KeyboardKey => Names(Keys),
        RazerHidMappingGroup.Dpi => Names(DpiActions),
        RazerHidMappingGroup.Profile => Names(ProfileActions),
        RazerHidMappingGroup.MediaKey => Names(MediaKeys),
        RazerHidMappingGroup.PowerKey => Names(PowerKeys),
        RazerHidMappingGroup.DoubleClick => ["Double click"],
        RazerHidMappingGroup.ScrollWheelMode => Names(ScrollWheelModes),
        RazerHidMappingGroup.RazerKey => Names(RazerKeys),
        RazerHidMappingGroup.WindowsShortcut => Names(WindowsShortcuts),
        RazerHidMappingGroup.HyperShiftToggle => ["HyperShift"],
        RazerHidMappingGroup.Macro when macros is { Count: > 0 } =>
            macros.Select(macro => macro.Name).ToArray(),
        RazerHidMappingGroup.Macro => ["Create a macro in the macro editor first"],
        RazerHidMappingGroup.TurboKey => Names(Keys).Select(name => $"Turbo {name}").ToArray(),
        RazerHidMappingGroup.TurboButton =>
            Names(MouseButtons).Select(name => $"Turbo {name}").ToArray(),
        RazerHidMappingGroup.DpiClutch => ["DPI clutch"],
        RazerHidMappingGroup.ProfileSpecific => ["Specific profile"],
        _ => [],
    };

    public static RazerHidMappingAction? CreateAction(
        RazerHidMappingGroup group,
        int optionIndex,
        byte modifierMask = 0,
        IReadOnlyList<RazerHidMacroOption>? macros = null,
        int turboRate = 7,
        int profileId = 1,
        int dpiClutchX = 800,
        int dpiClutchY = 800,
        RazerHidMacroPlayback macroPlayback = RazerHidMacroPlayback.Once,
        int macroRepeatCount = 1)
    {
        if (optionIndex < 0)
        {
            return null;
        }

        switch (group)
        {
            case RazerHidMappingGroup.Off:
                return new("Off", RazerHidMappingFunctions.Off, []);
            case RazerHidMappingGroup.MouseButton when optionIndex < MouseButtons.Length:
                var mouse = MouseButtons[optionIndex];
                return new(mouse.Name, RazerHidMappingFunctions.ButtonCode, [mouse.Id]);
            case RazerHidMappingGroup.KeyboardKey when optionIndex < Keys.Length:
                var key = Keys[optionIndex];
                return new(key.Name, RazerHidMappingFunctions.KeyCode, [modifierMask, key.Hid]);
            case RazerHidMappingGroup.Dpi when optionIndex < DpiActions.Length:
                var dpi = DpiActions[optionIndex];
                return new(dpi.Name, RazerHidMappingFunctions.Dpi, [dpi.Code]);
            case RazerHidMappingGroup.Profile when optionIndex < ProfileActions.Length:
                var profile = ProfileActions[optionIndex];
                return new(profile.Name, RazerHidMappingFunctions.Profile, [profile.Code]);
            case RazerHidMappingGroup.MediaKey when optionIndex < MediaKeys.Length:
                var media = MediaKeys[optionIndex];
                return new(media.Name, RazerHidMappingFunctions.MediaKeys,
                    [(byte)(media.Code >> 8), (byte)media.Code]);
            case RazerHidMappingGroup.PowerKey when optionIndex < PowerKeys.Length:
                var power = PowerKeys[optionIndex];
                return new(power.Name, RazerHidMappingFunctions.PowerKeys, [power.Code]);
            case RazerHidMappingGroup.DoubleClick:
                return new("Double click", RazerHidMappingFunctions.DoubleClick, [0x01]);
            case RazerHidMappingGroup.ScrollWheelMode when optionIndex < ScrollWheelModes.Length:
                var scroll = ScrollWheelModes[optionIndex];
                return new(scroll.Name, RazerHidMappingFunctions.ScrollWheelMode, [scroll.Code]);
            case RazerHidMappingGroup.RazerKey when optionIndex < RazerKeys.Length:
                var razer = RazerKeys[optionIndex];
                return new(razer.Name, RazerHidMappingFunctions.RazerKey, [razer.Code]);
            case RazerHidMappingGroup.WindowsShortcut when optionIndex < WindowsShortcuts.Length:
                var shortcut = WindowsShortcuts[optionIndex];
                return new(shortcut.Name, RazerHidMappingFunctions.Win8ShortcutsKey,
                    [shortcut.Code]);
            case RazerHidMappingGroup.HyperShiftToggle:
                return new("HyperShift", RazerHidMappingFunctions.ModeButtonkey, [0x01]);
            case RazerHidMappingGroup.Macro when macros is not null && optionIndex < macros.Count:
                var macro = macros[optionIndex];
                return macroPlayback switch
                {
                    RazerHidMacroPlayback.Once => new(
                        macro.Name, RazerHidMappingFunctions.MacroTypeI, [0x00, macro.Id, 0x01]),
                    RazerHidMacroPlayback.Repeat => new(
                        macro.Name, RazerHidMappingFunctions.MacroTypeI,
                        [0x00, macro.Id, (byte)Math.Clamp(macroRepeatCount, 1, byte.MaxValue)]),
                    RazerHidMacroPlayback.Hold => new(
                        macro.Name, RazerHidMappingFunctions.MacroTypeII, [0x00, macro.Id]),
                    RazerHidMacroPlayback.Toggle => new(
                        macro.Name, RazerHidMappingFunctions.MacroTypeIII, [0x00, macro.Id]),
                    RazerHidMacroPlayback.Sequence => new(
                        macro.Name, RazerHidMappingFunctions.MacroTypeIV, [0x00, macro.Id]),
                    _ => null,
                };
            case RazerHidMappingGroup.TurboKey when optionIndex < Keys.Length:
                var turboKey = Keys[optionIndex];
                var turboKeyInterval = Interval(turboRate);
                return new($"Turbo {turboKey.Name}", RazerHidMappingFunctions.TurboModeKey,
                    [modifierMask, turboKey.Hid, High(turboKeyInterval), Low(turboKeyInterval)]);
            case RazerHidMappingGroup.TurboButton when optionIndex < MouseButtons.Length:
                var turboButton = MouseButtons[optionIndex];
                var turboButtonInterval = Interval(turboRate);
                return new($"Turbo {turboButton.Name}", RazerHidMappingFunctions.TurboModeButton,
                    [turboButton.Id, High(turboButtonInterval), Low(turboButtonInterval)]);
            case RazerHidMappingGroup.DpiClutch:
                var clutchX = ClampDpi(dpiClutchX);
                var clutchY = ClampDpi(dpiClutchY);
                return new("DPI clutch", RazerHidMappingFunctions.Dpi,
                    [0x05, High(clutchX), Low(clutchX), High(clutchY), Low(clutchY)]);
            case RazerHidMappingGroup.ProfileSpecific:
                return new("Specific profile", RazerHidMappingFunctions.Profile,
                    [0x03, (byte)Math.Clamp(profileId, 1, 5)]);
            default:
                return null;
        }
    }

    public static RazerHidMappingAction CreateMacroAction(
        byte slotId,
        RazerHidMacroPlayback playback,
        int repeatCount = 1) => playback switch
    {
        RazerHidMacroPlayback.Once => new(
            $"Macro {slotId}", RazerHidMappingFunctions.MacroTypeI, [0x00, slotId, 0x01]),
        RazerHidMacroPlayback.Repeat => new(
            $"Macro {slotId}", RazerHidMappingFunctions.MacroTypeI,
            [0x00, slotId, (byte)Math.Clamp(repeatCount, 1, byte.MaxValue)]),
        RazerHidMacroPlayback.Hold => new(
            $"Macro {slotId}", RazerHidMappingFunctions.MacroTypeII, [0x00, slotId]),
        RazerHidMacroPlayback.Toggle => new(
            $"Macro {slotId}", RazerHidMappingFunctions.MacroTypeIII, [0x00, slotId]),
        RazerHidMacroPlayback.Sequence => new(
            $"Macro {slotId}", RazerHidMappingFunctions.MacroTypeIV, [0x00, slotId]),
        _ => throw new ArgumentOutOfRangeException(nameof(playback)),
    };

    public static string Describe(
        byte functionId,
        IReadOnlyList<byte> data,
        IReadOnlyList<RazerHidMacroOption>? macros = null) => functionId switch
    {
        RazerHidMappingFunctions.Off => "Off",
        RazerHidMappingFunctions.ButtonCode when data.Count > 0 => DescribeButton(data[0]),
        RazerHidMappingFunctions.ButtonCode => "Mouse button",
        RazerHidMappingFunctions.KeyCode when data.Count >= 2 => DescribeKey(data[0], data[1]),
        RazerHidMappingFunctions.KeyCode => "Keyboard key",
        RazerHidMappingFunctions.Dpi when data.Count >= 5 && data[0] == 0x05 => "DPI clutch",
        RazerHidMappingFunctions.Dpi when data.Count > 0 => DpiActions
            .FirstOrDefault(action => action.Code == data[0]).Name ?? $"DPI {data[0]}",
        RazerHidMappingFunctions.Profile when data.Count >= 2 && data[0] == 0x03 =>
            "Specific profile",
        RazerHidMappingFunctions.Profile when data.Count > 0 => ProfileActions
            .FirstOrDefault(action => action.Code == data[0]).Name ?? $"Profile {data[0]}",
        RazerHidMappingFunctions.MediaKeys when data.Count >= 2 =>
            MediaKeys.FirstOrDefault(key => key.Code == (ushort)((data[0] << 8) | data[1])).Name
                ?? $"Media key {data[0]:X2}{data[1]:X2}",
        RazerHidMappingFunctions.PowerKeys when data.Count > 0 => PowerKeys
            .FirstOrDefault(key => key.Code == data[0]).Name ?? $"Power key {data[0]}",
        RazerHidMappingFunctions.DoubleClick => "Double click",
        RazerHidMappingFunctions.ScrollWheelMode when data.Count > 0 => ScrollWheelModes
            .FirstOrDefault(mode => mode.Code == data[0]).Name ?? $"Scroll mode {data[0]}",
        RazerHidMappingFunctions.RazerKey when data.Count > 0 => RazerKeys
            .FirstOrDefault(key => key.Code == data[0]).Name ?? $"Razer key {data[0]}",
        RazerHidMappingFunctions.Win8ShortcutsKey when data.Count > 0 => WindowsShortcuts
            .FirstOrDefault(key => key.Code == data[0]).Name ?? $"Shortcut {data[0]}",
        RazerHidMappingFunctions.ModeButtonkey => "HyperShift",
        RazerHidMappingFunctions.TurboModeButton when data.Count > 0 =>
            $"Turbo {DescribeButton(data[0])}",
        RazerHidMappingFunctions.TurboModeKey when data.Count >= 2 =>
            $"Turbo {DescribeKey(data[0], data[1])}",
        RazerHidMappingFunctions.TurboModeButton or RazerHidMappingFunctions.TurboModeKey => "Turbo",
        RazerHidMappingFunctions.MacroTypeI or RazerHidMappingFunctions.MacroTypeII or
            RazerHidMappingFunctions.MacroTypeIII or RazerHidMappingFunctions.MacroTypeIV =>
            DescribeMacro(functionId, data, macros),
        _ => $"Function {functionId}" + (data.Count == 0
            ? string.Empty
            : $" [{string.Join(' ', data.Select(value => value.ToString("X2")))}]"),
    };

    /// <summary>Decodes a raw assignment; null means the catalog has no exact editor match.</summary>
    public static RazerHidDecodedMapping? Decode(byte functionId, IReadOnlyList<byte> data)
    {
        switch (functionId)
        {
            case RazerHidMappingFunctions.Off:
                return new(RazerHidMappingGroup.Off, 0);
            case RazerHidMappingFunctions.ButtonCode when data.Count > 0:
                return Map(RazerHidMappingGroup.MouseButton, MouseButtons, data[0]);
            case RazerHidMappingFunctions.KeyCode when data.Count >= 2:
                if (data[1] == 0)
                {
                    // [single modifier bit, 0] is the canonical bare-modifier form.
                    if (ModifierHid(data[0]) is byte modifierKey)
                    {
                        return Map(RazerHidMappingGroup.KeyboardKey, Keys, modifierKey);
                    }

                    // Builds before 1.7.2 beta 41 wrote the usage into the modifier byte.
                    // Keep those rows readable so one re-save repairs them.
                    if (data[0] is >= 224 and <= 231)
                    {
                        return Map(RazerHidMappingGroup.KeyboardKey, Keys, data[0]);
                    }
                }

                var modifier = ModifierIndex(data[0]);
                var key = KeyIndex(data[1]);
                return modifier >= 0 && key >= 0
                    ? new(RazerHidMappingGroup.KeyboardKey, key, modifier)
                    : null;
            case RazerHidMappingFunctions.Dpi when data.Count >= 5 && data[0] == 0x05:
                return new(
                    RazerHidMappingGroup.DpiClutch,
                    0,
                    DpiClutchX: (data[1] << 8) | data[2],
                    DpiClutchY: (data[3] << 8) | data[4]);
            case RazerHidMappingFunctions.Dpi when data.Count > 0:
                return MapCode(RazerHidMappingGroup.Dpi, DpiActions, data[0]);
            case RazerHidMappingFunctions.Profile when data.Count >= 2 && data[0] == 0x03:
                return new(RazerHidMappingGroup.ProfileSpecific, 0, ProfileId: data[1]);
            case RazerHidMappingFunctions.Profile when data.Count > 0:
                return MapCode(RazerHidMappingGroup.Profile, ProfileActions, data[0]);
            case RazerHidMappingFunctions.MediaKeys when data.Count >= 2:
                return MapCode(
                    RazerHidMappingGroup.MediaKey,
                    MediaKeys,
                    (ushort)((data[0] << 8) | data[1]));
            case RazerHidMappingFunctions.PowerKeys when data.Count > 0:
                return MapCode(RazerHidMappingGroup.PowerKey, PowerKeys, data[0]);
            case RazerHidMappingFunctions.DoubleClick:
                return new(RazerHidMappingGroup.DoubleClick, 0);
            case RazerHidMappingFunctions.ScrollWheelMode when data.Count > 0:
                return MapCode(RazerHidMappingGroup.ScrollWheelMode, ScrollWheelModes, data[0]);
            case RazerHidMappingFunctions.RazerKey when data.Count > 0:
                return MapCode(RazerHidMappingGroup.RazerKey, RazerKeys, data[0]);
            case RazerHidMappingFunctions.Win8ShortcutsKey when data.Count > 0:
                return MapCode(RazerHidMappingGroup.WindowsShortcut, WindowsShortcuts, data[0]);
            case RazerHidMappingFunctions.ModeButtonkey:
                return new(RazerHidMappingGroup.HyperShiftToggle, 0);
            case RazerHidMappingFunctions.TurboModeButton when data.Count > 0:
                var turboButton = Map(RazerHidMappingGroup.TurboButton, MouseButtons, data[0]);
                return turboButton is null
                    ? null
                    : turboButton with { TurboRate = IntervalRate(data, 1) };
            case RazerHidMappingFunctions.TurboModeKey when data.Count >= 2:
                if (data[1] == 0)
                {
                    var modifierHid = ModifierHid(data[0]) ??
                        (data[0] is >= 224 and <= 231 ? data[0] : (byte)0);
                    var modifierIndex = KeyIndex(modifierHid);
                    if (modifierHid != 0 && modifierIndex >= 0)
                    {
                        return new(
                            RazerHidMappingGroup.TurboKey,
                            modifierIndex,
                            0,
                            TurboRate: IntervalRate(data, 2));
                    }
                }

                var turboModifier = ModifierIndex(data[0]);
                var turboKey = KeyIndex(data[1]);
                return turboModifier >= 0 && turboKey >= 0
                    ? new(
                        RazerHidMappingGroup.TurboKey,
                        turboKey,
                        turboModifier,
                        TurboRate: IntervalRate(data, 2))
                    : null;
            case RazerHidMappingFunctions.MacroTypeI:
            case RazerHidMappingFunctions.MacroTypeII:
            case RazerHidMappingFunctions.MacroTypeIII:
            case RazerHidMappingFunctions.MacroTypeIV:
                var slot = data.Count >= 2 ? (data[0] << 8) | data[1] : 0;
                if (slot is < 1 or > byte.MaxValue)
                {
                    return null;
                }

                var playback = functionId switch
                {
                    RazerHidMappingFunctions.MacroTypeII => RazerHidMacroPlayback.Hold,
                    RazerHidMappingFunctions.MacroTypeIII => RazerHidMacroPlayback.Toggle,
                    RazerHidMappingFunctions.MacroTypeIV => RazerHidMacroPlayback.Sequence,
                    _ => data.Count >= 3 && data[2] > 1
                        ? RazerHidMacroPlayback.Repeat
                        : RazerHidMacroPlayback.Once,
                };
                return new(
                    RazerHidMappingGroup.Macro,
                    -1,
                    MacroSlot: slot,
                    MacroPlayback: playback,
                    MacroRepeatCount: playback == RazerHidMacroPlayback.Repeat && data.Count >= 3
                        ? data[2]
                        : 1);
            default:
                return null;
        }
    }

    public static ButtonMappingProfile ToProfileMapping(
        byte buttonId,
        byte mode,
        byte functionId,
        IReadOnlyList<byte> data,
        RazerHidDecodedMapping? decoded) => new()
    {
        ButtonId = buttonId,
        Mode = mode,
        Group = decoded?.Group.ToString(),
        OptionIndex = decoded is { OptionIndex: >= 0 } ? decoded.OptionIndex : null,
        ModifierIndex = decoded?.ModifierIndex,
        TurboRate = decoded?.TurboRate,
        ProfileId = decoded?.ProfileId,
        DpiClutchX = decoded?.DpiClutchX,
        DpiClutchY = decoded?.DpiClutchY,
        MacroPlaybackIndex = decoded?.MacroPlayback is { } playback ? (int)playback : null,
        MacroTargetIndex = decoded?.Group == RazerHidMappingGroup.Macro ? 1 : null,
        MacroRepeatCount = decoded?.MacroRepeatCount,
        FunctionId = functionId,
        FunctionData = data.ToList(),
    };

    public static List<ButtonMappingProfile> MergeButtonMappings(
        IReadOnlyCollection<ButtonMappingProfile>? existing,
        IReadOnlyCollection<ButtonMappingProfile> updates,
        byte mode)
    {
        var merged = existing?
            .Where(mapping => (mapping.Mode ?? 0) != mode)
            .Select(mapping => mapping.Clone())
            .ToList() ?? [];
        merged.AddRange(updates.Where(mapping => (mapping.Mode ?? 0) == mode)
            .Select(mapping => mapping.Clone()));
        return merged;
    }

    public static string DescribeButton(byte buttonId) =>
        MouseButtons.FirstOrDefault(button => button.Id == buttonId).Name ?? $"Button {buttonId}";

    private static string DescribeMacro(
        byte functionId,
        IReadOnlyList<byte> data,
        IReadOnlyList<RazerHidMacroOption>? macros)
    {
        var id = data.Count >= 2 ? (data[0] << 8) | data[1] : 0;
        var name = macros?.FirstOrDefault(macro => macro.Id == id)?.Name ??
            (id == 0 ? "Macro" : $"Macro {id}");
        return functionId switch
        {
            RazerHidMappingFunctions.MacroTypeI when data.Count >= 3 && data[2] > 1 =>
                $"{name} ×{data[2]}",
            RazerHidMappingFunctions.MacroTypeII => $"{name} (hold)",
            RazerHidMappingFunctions.MacroTypeIII => $"{name} (toggle)",
            RazerHidMappingFunctions.MacroTypeIV => $"{name} (sequence)",
            _ => name,
        };
    }

    private static string DescribeKey(byte modifierMask, byte hid)
    {
        // A bare modifier is [bit, 0]; pre-beta-41 rows carried the usage instead.
        if (hid == 0)
        {
            var modifierHid = ModifierHid(modifierMask) ??
                (modifierMask is >= 224 and <= 231 ? modifierMask : (byte)0);
            if (modifierHid != 0)
            {
                return Keys.FirstOrDefault(key => key.Hid == modifierHid).Name
                    ?? $"Key {modifierHid}";
            }
        }

        var name = Keys.FirstOrDefault(key => key.Hid == hid).Name ?? $"Key {hid}";
        if (hid >= 224 || modifierMask == 0)
        {
            return name;
        }

        var modifier = modifierMask switch
        {
            0x01 => "Ctrl",
            0x02 => "Shift",
            0x04 => "Alt",
            0x08 => "Win",
            _ => $"{modifierMask:X2}",
        };
        return $"{modifier}+{name}";
    }

    private static RazerHidDecodedMapping? Map(
        RazerHidMappingGroup group,
        IReadOnlyList<(string Name, byte Code)> entries,
        byte code)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Code == code)
            {
                return new(group, index);
            }
        }

        return null;
    }
    private static RazerHidDecodedMapping? MapCode<T>(
        RazerHidMappingGroup group,
        IReadOnlyList<(string Name, T Code)> entries,
        T code)
        where T : struct, IEquatable<T>
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Code.Equals(code))
            {
                return new(group, index);
            }
        }

        return null;
    }

    /// <summary>
    /// Pre-beta-41 builds wrote a bare modifier as [usage, 0]. Byte 0 is the modifier bitmask,
    /// so 0xE1 became LeftCtrl + RightShift + RightAlt + RightGUI. The middleware's own
    /// encoder (non-NKRO, which is what this device family reports) writes [0, usage], so
    /// rewrite stale profile data to that form on the way out.
    /// </summary>
    internal static ReadOnlySpan<byte> NormalizeKeyData(byte functionId, ReadOnlySpan<byte> data)
    {
        if (functionId == RazerHidMappingFunctions.KeyCode && data.Length >= 2 && data[1] == 0 &&
            data[0] is >= 224 and <= 231)
        {
            return new byte[] { 0, data[0] };
        }

        return data;
    }

    private static byte? ModifierHid(byte modifierMask) => modifierMask switch
    {
        0x01 => 224, 0x02 => 225, 0x04 => 226, 0x08 => 227,
        0x10 => 228, 0x20 => 229, 0x40 => 230, 0x80 => 231,
        _ => null,
    };

    private static int KeyIndex(byte hid)
    {
        for (var index = 0; index < Keys.Length; index++)
        {
            if (Keys[index].Hid == hid)
            {
                return index;
            }
        }

        return -1;
    }
    private static int ModifierIndex(byte mask)
    {
        for (var index = 0; index < ModifierMasks.Count; index++)
        {
            if (ModifierMasks[index] == mask)
            {
                return index;
            }
        }

        return -1;
    }
    private static double? IntervalRate(IReadOnlyList<byte> data, int offset)
    {
        if (data.Count < offset + 2)
        {
            return null;
        }

        var interval = (data[offset] << 8) | data[offset + 1];
        return interval > 0 ? Math.Clamp(1000d / interval, 1, 50) : null;
    }

    private static IReadOnlyList<string> Names<T>(IReadOnlyList<(string Name, T Code)> entries) =>
        entries.Select(entry => entry.Name).ToArray();

    private static ushort Interval(int keysPerSecond)
    {
        var rate = Math.Clamp(keysPerSecond, 1, 50);
        return (ushort)Math.Clamp(1000 / rate, 1, ushort.MaxValue);
    }

    private static int ClampDpi(int value) =>
        Math.Clamp((int)Math.Round(value / 50d, MidpointRounding.AwayFromZero) * 50, 100, 30000);

    private static byte High(int value) => (byte)(value >> 8);
    private static byte Low(int value) => (byte)value;
}
