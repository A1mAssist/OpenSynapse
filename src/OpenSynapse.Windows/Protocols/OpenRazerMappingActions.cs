using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Mapping groups the middleware editor exposes. Each group maps to one function id in the
/// 0x02:0x0C button-assignment frame.
/// </summary>
public enum OpenRazerMappingGroup
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

public sealed record OpenRazerMappingAction(string Name, byte FunctionId, byte[] Data);

public sealed record OpenRazerMacroOption(byte Id, string Name);

public enum OpenRazerMacroPlayback
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
public static class OpenRazerMappingActions
{
    public static IReadOnlyList<string> MacroPlaybackNames { get; } =
        ["Once", "N times", "Hold", "Toggle", "Sequence"];

    public static IReadOnlyList<string> ModifierNames { get; } = ["None", "Ctrl", "Shift", "Alt", "Win"];

    public static IReadOnlyList<byte> ModifierMasks { get; } = [0x00, 0x01, 0x02, 0x04, 0x08];

    private static readonly (string Name, byte Id)[] MouseButtons =
    [
        ("Left", 0x01), ("Right", 0x02), ("Middle", 0x03), ("Back", 0x04), ("Forward", 0x05),
        ("Button 6", 0x06), ("Button 7", 0x07), ("Button 8", 0x08), ("Scroll up", 0x09),
        ("Scroll down", 0x0A), ("DPI up", 0x0B), ("DPI down", 0x0C), ("User select", 0x0D),
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
        OpenRazerMappingGroup group,
        IReadOnlyList<OpenRazerMacroOption>? macros = null) => group switch
    {
        OpenRazerMappingGroup.Off => ["Off"],
        OpenRazerMappingGroup.MouseButton => Names(MouseButtons),
        OpenRazerMappingGroup.KeyboardKey => Names(Keys),
        OpenRazerMappingGroup.Dpi => Names(DpiActions),
        OpenRazerMappingGroup.Profile => Names(ProfileActions),
        OpenRazerMappingGroup.MediaKey => Names(MediaKeys),
        OpenRazerMappingGroup.PowerKey => Names(PowerKeys),
        OpenRazerMappingGroup.DoubleClick => ["Double click"],
        OpenRazerMappingGroup.ScrollWheelMode => Names(ScrollWheelModes),
        OpenRazerMappingGroup.RazerKey => Names(RazerKeys),
        OpenRazerMappingGroup.WindowsShortcut => Names(WindowsShortcuts),
        OpenRazerMappingGroup.HyperShiftToggle => ["HyperShift"],
        OpenRazerMappingGroup.Macro when macros is { Count: > 0 } =>
            macros.Select(macro => macro.Name).ToArray(),
        OpenRazerMappingGroup.Macro => ["No readable macro slots"],
        OpenRazerMappingGroup.TurboKey => Names(Keys).Select(name => $"Turbo {name}").ToArray(),
        OpenRazerMappingGroup.TurboButton =>
            Names(MouseButtons).Select(name => $"Turbo {name}").ToArray(),
        OpenRazerMappingGroup.DpiClutch => ["DPI clutch"],
        OpenRazerMappingGroup.ProfileSpecific => ["Specific profile"],
        _ => [],
    };

    public static OpenRazerMappingAction? CreateAction(
        OpenRazerMappingGroup group,
        int optionIndex,
        byte modifierMask = 0,
        IReadOnlyList<OpenRazerMacroOption>? macros = null,
        int turboRate = 7,
        int profileId = 1,
        int dpiClutchX = 800,
        int dpiClutchY = 800,
        OpenRazerMacroPlayback macroPlayback = OpenRazerMacroPlayback.Once,
        int macroRepeatCount = 1)
    {
        if (optionIndex < 0)
        {
            return null;
        }

        switch (group)
        {
            case OpenRazerMappingGroup.Off:
                return new("Off", OpenRazerMappingFunctions.Off, []);
            case OpenRazerMappingGroup.MouseButton when optionIndex < MouseButtons.Length:
                var mouse = MouseButtons[optionIndex];
                return new(mouse.Name, OpenRazerMappingFunctions.ButtonCode, [mouse.Id]);
            case OpenRazerMappingGroup.KeyboardKey when optionIndex < Keys.Length:
                var key = Keys[optionIndex];
                return key.Hid >= 224
                    ? new(key.Name, OpenRazerMappingFunctions.KeyCode, [key.Hid, 0x00])
                    : new(key.Name, OpenRazerMappingFunctions.KeyCode, [modifierMask, key.Hid]);
            case OpenRazerMappingGroup.Dpi when optionIndex < DpiActions.Length:
                var dpi = DpiActions[optionIndex];
                return new(dpi.Name, OpenRazerMappingFunctions.Dpi, [dpi.Code]);
            case OpenRazerMappingGroup.Profile when optionIndex < ProfileActions.Length:
                var profile = ProfileActions[optionIndex];
                return new(profile.Name, OpenRazerMappingFunctions.Profile, [profile.Code]);
            case OpenRazerMappingGroup.MediaKey when optionIndex < MediaKeys.Length:
                var media = MediaKeys[optionIndex];
                return new(media.Name, OpenRazerMappingFunctions.MediaKeys,
                    [(byte)(media.Code >> 8), (byte)media.Code]);
            case OpenRazerMappingGroup.PowerKey when optionIndex < PowerKeys.Length:
                var power = PowerKeys[optionIndex];
                return new(power.Name, OpenRazerMappingFunctions.PowerKeys, [power.Code]);
            case OpenRazerMappingGroup.DoubleClick:
                return new("Double click", OpenRazerMappingFunctions.DoubleClick, [0x01]);
            case OpenRazerMappingGroup.ScrollWheelMode when optionIndex < ScrollWheelModes.Length:
                var scroll = ScrollWheelModes[optionIndex];
                return new(scroll.Name, OpenRazerMappingFunctions.ScrollWheelMode, [scroll.Code]);
            case OpenRazerMappingGroup.RazerKey when optionIndex < RazerKeys.Length:
                var razer = RazerKeys[optionIndex];
                return new(razer.Name, OpenRazerMappingFunctions.RazerKey, [razer.Code]);
            case OpenRazerMappingGroup.WindowsShortcut when optionIndex < WindowsShortcuts.Length:
                var shortcut = WindowsShortcuts[optionIndex];
                return new(shortcut.Name, OpenRazerMappingFunctions.Win8ShortcutsKey,
                    [shortcut.Code]);
            case OpenRazerMappingGroup.HyperShiftToggle:
                return new("HyperShift", OpenRazerMappingFunctions.ModeButtonkey, [0x01]);
            case OpenRazerMappingGroup.Macro when macros is not null && optionIndex < macros.Count:
                var macro = macros[optionIndex];
                return macroPlayback switch
                {
                    OpenRazerMacroPlayback.Once => new(
                        macro.Name, OpenRazerMappingFunctions.MacroTypeI, [0x00, macro.Id, 0x01]),
                    OpenRazerMacroPlayback.Repeat => new(
                        macro.Name, OpenRazerMappingFunctions.MacroTypeI,
                        [0x00, macro.Id, (byte)Math.Clamp(macroRepeatCount, 1, byte.MaxValue)]),
                    OpenRazerMacroPlayback.Hold => new(
                        macro.Name, OpenRazerMappingFunctions.MacroTypeII, [0x00, macro.Id]),
                    OpenRazerMacroPlayback.Toggle => new(
                        macro.Name, OpenRazerMappingFunctions.MacroTypeIII, [0x00, macro.Id]),
                    OpenRazerMacroPlayback.Sequence => new(
                        macro.Name, OpenRazerMappingFunctions.MacroTypeIV, [0x00, macro.Id]),
                    _ => null,
                };
            case OpenRazerMappingGroup.TurboKey when optionIndex < Keys.Length:
                var turboKey = Keys[optionIndex];
                var turboKeyInterval = Interval(turboRate);
                return new($"Turbo {turboKey.Name}", OpenRazerMappingFunctions.TurboModeKey,
                    [modifierMask, turboKey.Hid, High(turboKeyInterval), Low(turboKeyInterval)]);
            case OpenRazerMappingGroup.TurboButton when optionIndex < MouseButtons.Length:
                var turboButton = MouseButtons[optionIndex];
                var turboButtonInterval = Interval(turboRate);
                return new($"Turbo {turboButton.Name}", OpenRazerMappingFunctions.TurboModeButton,
                    [turboButton.Id, High(turboButtonInterval), Low(turboButtonInterval)]);
            case OpenRazerMappingGroup.DpiClutch:
                var clutchX = ClampDpi(dpiClutchX);
                var clutchY = ClampDpi(dpiClutchY);
                return new("DPI clutch", OpenRazerMappingFunctions.Dpi,
                    [0x05, High(clutchX), Low(clutchX), High(clutchY), Low(clutchY)]);
            case OpenRazerMappingGroup.ProfileSpecific:
                return new("Specific profile", OpenRazerMappingFunctions.Profile,
                    [0x03, (byte)Math.Clamp(profileId, 1, 5)]);
            default:
                return null;
        }
    }

    public static string Describe(
        byte functionId,
        IReadOnlyList<byte> data,
        IReadOnlyList<OpenRazerMacroOption>? macros = null) => functionId switch
    {
        OpenRazerMappingFunctions.Off => "Off",
        OpenRazerMappingFunctions.ButtonCode when data.Count > 0 => DescribeButton(data[0]),
        OpenRazerMappingFunctions.ButtonCode => "Mouse button",
        OpenRazerMappingFunctions.KeyCode when data.Count >= 2 => DescribeKey(data[0], data[1]),
        OpenRazerMappingFunctions.KeyCode => "Keyboard key",
        OpenRazerMappingFunctions.Dpi when data.Count >= 5 && data[0] == 0x05 => "DPI clutch",
        OpenRazerMappingFunctions.Dpi when data.Count > 0 => DpiActions
            .FirstOrDefault(action => action.Code == data[0]).Name ?? $"DPI {data[0]}",
        OpenRazerMappingFunctions.Profile when data.Count >= 2 && data[0] == 0x03 =>
            "Specific profile",
        OpenRazerMappingFunctions.Profile when data.Count > 0 => ProfileActions
            .FirstOrDefault(action => action.Code == data[0]).Name ?? $"Profile {data[0]}",
        OpenRazerMappingFunctions.MediaKeys when data.Count >= 2 =>
            MediaKeys.FirstOrDefault(key => key.Code == (ushort)((data[0] << 8) | data[1])).Name
                ?? $"Media key {data[0]:X2}{data[1]:X2}",
        OpenRazerMappingFunctions.PowerKeys when data.Count > 0 => PowerKeys
            .FirstOrDefault(key => key.Code == data[0]).Name ?? $"Power key {data[0]}",
        OpenRazerMappingFunctions.DoubleClick => "Double click",
        OpenRazerMappingFunctions.ScrollWheelMode when data.Count > 0 => ScrollWheelModes
            .FirstOrDefault(mode => mode.Code == data[0]).Name ?? $"Scroll mode {data[0]}",
        OpenRazerMappingFunctions.RazerKey when data.Count > 0 => RazerKeys
            .FirstOrDefault(key => key.Code == data[0]).Name ?? $"Razer key {data[0]}",
        OpenRazerMappingFunctions.Win8ShortcutsKey when data.Count > 0 => WindowsShortcuts
            .FirstOrDefault(key => key.Code == data[0]).Name ?? $"Shortcut {data[0]}",
        OpenRazerMappingFunctions.ModeButtonkey => "HyperShift",
        OpenRazerMappingFunctions.TurboModeButton when data.Count > 0 =>
            $"Turbo {DescribeButton(data[0])}",
        OpenRazerMappingFunctions.TurboModeKey when data.Count >= 2 =>
            $"Turbo {DescribeKey(data[0], data[1])}",
        OpenRazerMappingFunctions.TurboModeButton or OpenRazerMappingFunctions.TurboModeKey => "Turbo",
        OpenRazerMappingFunctions.MacroTypeI or OpenRazerMappingFunctions.MacroTypeII or
            OpenRazerMappingFunctions.MacroTypeIII or OpenRazerMappingFunctions.MacroTypeIV =>
            DescribeMacro(functionId, data, macros),
        _ => $"Function {functionId}" + (data.Count == 0
            ? string.Empty
            : $" [{string.Join(' ', data.Select(value => value.ToString("X2")))}]"),
    };

    public static string DescribeButton(byte buttonId) =>
        MouseButtons.FirstOrDefault(button => button.Id == buttonId).Name ?? $"Button {buttonId}";

    private static string DescribeMacro(
        byte functionId,
        IReadOnlyList<byte> data,
        IReadOnlyList<OpenRazerMacroOption>? macros)
    {
        var id = data.Count >= 2 ? (data[0] << 8) | data[1] : 0;
        var name = macros?.FirstOrDefault(macro => macro.Id == id)?.Name ??
            (id == 0 ? "Macro" : $"Macro {id}");
        return functionId switch
        {
            OpenRazerMappingFunctions.MacroTypeI when data.Count >= 3 && data[2] > 1 =>
                $"{name} ×{data[2]}",
            OpenRazerMappingFunctions.MacroTypeII => $"{name} (hold)",
            OpenRazerMappingFunctions.MacroTypeIII => $"{name} (toggle)",
            OpenRazerMappingFunctions.MacroTypeIV => $"{name} (sequence)",
            _ => name,
        };
    }

    private static string DescribeKey(byte modifierMask, byte hid)
    {
        // Modifier-only keys are stored as [usage, 0] by the middleware encoder.
        if (hid == 0 && modifierMask >= 224)
        {
            return Keys.FirstOrDefault(key => key.Hid == modifierMask).Name
                ?? $"Key {modifierMask}";
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
