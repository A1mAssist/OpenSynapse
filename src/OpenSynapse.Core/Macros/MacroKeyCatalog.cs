namespace OpenSynapse.Core.Macros;

/// <summary>
/// A key or mouse button the user can pick from the on-screen picker.
/// </summary>
public sealed record MacroKeyChoice(string Label, MacroInput Input)
{
    public override string ToString() => Label;
}

/// <summary>
/// The built-in key list offered by the macro key picker, so users can add a
/// step without going through the global input hook. Labels live here because
/// the operating system only reports raw names such as "VK 0xA1" for the
/// modifier and navigation keys.
/// </summary>
public static class MacroKeyCatalog
{
    public static IReadOnlyList<MacroKeyChoice> Keyboard { get; } =
    [
        Key("Esc", 0x1B), Key("F1", 0x70), Key("F2", 0x71), Key("F3", 0x72),
        Key("F4", 0x73), Key("F5", 0x74), Key("F6", 0x75), Key("F7", 0x76),
        Key("F8", 0x77), Key("F9", 0x78), Key("F10", 0x79), Key("F11", 0x7A),
        Key("F12", 0x7B), Key("PrtSc", 0x2C, extended: true),
        Key("Pause", 0x13, extended: true),
        Key("`", 0xC0), Key("1", 0x31), Key("2", 0x32), Key("3", 0x33),
        Key("4", 0x34), Key("5", 0x35), Key("6", 0x36), Key("7", 0x37),
        Key("8", 0x38), Key("9", 0x39), Key("0", 0x30), Key("-", 0xBD),
        Key("=", 0xBB), Key("Backspace", 0x08), Key("Tab", 0x09),
        Key("Q", 0x51), Key("W", 0x57), Key("E", 0x45), Key("R", 0x52),
        Key("T", 0x54), Key("Y", 0x59), Key("U", 0x55), Key("I", 0x49),
        Key("O", 0x4F), Key("P", 0x50), Key("[", 0xDB), Key("]", 0xDD),
        Key("Enter", 0x0D), Key("Caps", 0x14),
        Key("A", 0x41), Key("S", 0x53), Key("D", 0x44), Key("F", 0x46),
        Key("G", 0x47), Key("H", 0x48), Key("J", 0x4A), Key("K", 0x4B),
        Key("L", 0x4C), Key(";", 0xBA), Key("'", 0xDE),
        Key("LShift", 0xA0, extended: true),
        Key("Z", 0x5A), Key("X", 0x58), Key("C", 0x43), Key("V", 0x56),
        Key("B", 0x42), Key("N", 0x4E), Key("M", 0x4D), Key(",", 0xBC),
        Key(".", 0xBE), Key("/", 0xBF), Key("RShift", 0xA1, extended: true),
        Key("Ctrl", 0xA2, extended: true), Key("LWin", 0x5B, extended: true),
        Key("RWin", 0x5C, extended: true), Key("Alt", 0xA3, extended: true),
        Key("Space", 0x20), Key("Menu", 0x5D, extended: true),
        Key("Left", 0x25, extended: true), Key("Up", 0x26, extended: true),
        Key("Right", 0x27, extended: true), Key("Down", 0x28, extended: true),
        Key("Insert", 0x2D), Key("Scroll", 0x2D, extended: true),
        Key("Delete", 0x2E, extended: true), Key("Home", 0x24, extended: true),
        Key("End", 0x23, extended: true), Key("PgUp", 0x21, extended: true),
        Key("PgDn", 0x22, extended: true),
        Key("NumLock", 0x90, extended: true), Key("Num0", 0x60),
        Key("Num1", 0x61), Key("Num2", 0x62), Key("Num3", 0x63),
        Key("Num4", 0x64), Key("Num5", 0x65), Key("Num6", 0x66),
        Key("Num7", 0x67), Key("Num8", 0x68), Key("Num9", 0x69),
        Key("Num+", 0x6B, extended: true), Key("Num-", 0x6D, extended: true),
        Key("Num*", 0x6A, extended: true), Key("Num/", 0x6F, extended: true),
        Key("NumEnter", 0x0D, extended: true), Key("Num.", 0x6E, extended: true),
    ];

    public static IReadOnlyList<MacroKeyChoice> MouseButtons { get; } =
    [
        new("Left", new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = 1 }),
        new("Right", new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = 2 }),
        new("Middle", new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = 3 }),
        new("Side 1", new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = 4 }),
        new("Side 2", new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = 5 }),
    ];

    public static IReadOnlyList<MacroKeyChoice> Wheels { get; } =
    [
        new("Wheel up", new MacroInput { Kind = MacroInputKind.MouseWheel, WheelDelta = 120 }),
        new("Wheel down", new MacroInput { Kind = MacroInputKind.MouseWheel, WheelDelta = -120 }),
    ];

    private static MacroKeyChoice Key(string label, int virtualKey, bool extended = false) =>
        new(label, new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = virtualKey,
            Extended = extended,
        });
}
