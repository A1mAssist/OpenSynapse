namespace OpenSynapse.Core.Macros;

/// <summary>
/// A key or mouse button the user can pick from the on-screen picker.
/// </summary>
public sealed record MacroKeyChoice(string Label, MacroInput Input)
{
    public override string ToString() => Label;
}

/// <summary>
/// One key position on the picker board. Columns and rows are quarter-key
/// units, so the odd widths of Tab, Caps Lock, the shifts and the space bar
/// come out exact.
/// </summary>
public sealed record MacroKeySlot(MacroKeyChoice Key, int Row, int Column, int ColumnSpan = 1, int RowSpan = 1);

/// <summary>
/// The full-size board the picker draws. The function row, the typing area, the
/// navigation cluster and the number pad share one coordinate space, so the
/// whole keyboard can be drawn as a single grid instead of four stacked ones.
/// </summary>
public sealed class MacroKeyBoard(int columns, int rows, params MacroKeySlot[] slots)
{
    public int Columns { get; } = columns;

    public int Rows { get; } = rows;

    public IReadOnlyList<MacroKeySlot> Slots { get; } = slots;
}

/// <summary>
/// The built-in key list offered by the macro key picker, so users can add a
/// step without going through the global input hook. The keys are laid out
/// like a full-size board, because a grid of blocks in virtual-key order is
/// unreadable. Labels live here because the operating system only reports raw
/// names such as "VK 0xA1" for the modifier and navigation keys.
/// </summary>
public static class MacroKeyCatalog
{
    // Quarter-key units: one standard key is four columns.
    private const int U = 4;

    // The board is 99 quarter-units wide: a 15 key typing area, a navigation
    // cluster and a number pad, with the usual gaps in between.
    private const int MainColumns = 15 * U;
    private const int NavigationColumn = 62;
    private const int NumberPadColumn = 83;
    private const int BoardColumns = 99;
    private const int BoardRows = 6;

    private static readonly MacroKeyChoice Escape = Key("Esc", 0x1B);
    private static readonly MacroKeyChoice[] FunctionKeys =
    [
        Key("F1", 0x70), Key("F2", 0x71), Key("F3", 0x72), Key("F4", 0x73),
        Key("F5", 0x74), Key("F6", 0x75), Key("F7", 0x76), Key("F8", 0x77),
        Key("F9", 0x78), Key("F10", 0x79), Key("F11", 0x7A), Key("F12", 0x7B),
    ];

    private static readonly MacroKeyChoice Backquote = Key("`", 0xC0);
    private static readonly MacroKeyChoice[] DigitKeys =
    [
        Key("1", 0x31), Key("2", 0x32), Key("3", 0x33), Key("4", 0x34), Key("5", 0x35),
        Key("6", 0x36), Key("7", 0x37), Key("8", 0x38), Key("9", 0x39), Key("0", 0x30),
    ];
    private static readonly MacroKeyChoice Minus = Key("-", 0xBD);
    private static readonly MacroKeyChoice EqualsKey = Key("=", 0xBB);
    private static readonly MacroKeyChoice Backspace = Key("Bksp", 0x08);
    private static readonly MacroKeyChoice Tab = Key("Tab", 0x09);

    private static readonly MacroKeyChoice[] TopLetters =
    [
        Key("Q", 0x51), Key("W", 0x57), Key("E", 0x45), Key("R", 0x52), Key("T", 0x54),
        Key("Y", 0x59), Key("U", 0x55), Key("I", 0x49), Key("O", 0x4F), Key("P", 0x50),
    ];

    private static readonly MacroKeyChoice BracketLeft = Key("[", 0xDB);
    private static readonly MacroKeyChoice BracketRight = Key("]", 0xDD);
    private static readonly MacroKeyChoice Backslash = Key("\\", 0xDC);
    private static readonly MacroKeyChoice CapsLock = Key("Caps", 0x14);

    private static readonly MacroKeyChoice[] HomeLetters =
    [
        Key("A", 0x41), Key("S", 0x53), Key("D", 0x44), Key("F", 0x46), Key("G", 0x47),
        Key("H", 0x48), Key("J", 0x4A), Key("K", 0x4B), Key("L", 0x4C),
    ];

    private static readonly MacroKeyChoice Semicolon = Key(";", 0xBA);
    private static readonly MacroKeyChoice Quote = Key("'", 0xDE);
    private static readonly MacroKeyChoice Enter = Key("Enter", 0x0D);

    private static readonly MacroKeyChoice[] BottomLetters =
    [
        Key("Z", 0x5A), Key("X", 0x58), Key("C", 0x43), Key("V", 0x56), Key("B", 0x42),
        Key("N", 0x4E), Key("M", 0x4D),
    ];

    private static readonly MacroKeyChoice Comma = Key(",", 0xBC);
    private static readonly MacroKeyChoice Period = Key(".", 0xBE);
    private static readonly MacroKeyChoice Slash = Key("/", 0xBF);

    // The left and right modifiers use different virtual keys, so a macro can
    // tell them apart. Only the keys Windows reports as extended carry the
    // extended flag, otherwise the injected key comes out as the wrong one.
    private static readonly MacroKeyChoice ControlLeft = Key("Ctrl", 0xA2);
    private static readonly MacroKeyChoice WindowsLeft = Key("Win", 0x5B, extended: true);
    private static readonly MacroKeyChoice AltLeft = Key("Alt", 0xA4);
    private static readonly MacroKeyChoice Space = Key("Space", 0x20);
    private static readonly MacroKeyChoice AltRight = Key("Alt", 0xA5, extended: true);
    private static readonly MacroKeyChoice WindowsRight = Key("Win", 0x5C, extended: true);
    private static readonly MacroKeyChoice Menu = Key("Menu", 0x5D, extended: true);
    private static readonly MacroKeyChoice ControlRight = Key("Ctrl", 0xA3, extended: true);
    private static readonly MacroKeyChoice ShiftLeft = Key("Shift", 0xA0);
    private static readonly MacroKeyChoice ShiftRight = Key("Shift", 0xA1);

    private static readonly MacroKeyChoice PrintScreen = Key("PrtSc", 0x2C, extended: true);
    private static readonly MacroKeyChoice ScrollLock = Key("ScrLk", 0x91, extended: true);
    private static readonly MacroKeyChoice Pause = Key("Pause", 0x13, extended: true);
    private static readonly MacroKeyChoice Insert = Key("Ins", 0x2D, extended: true);
    private static readonly MacroKeyChoice Delete = Key("Del", 0x2E, extended: true);
    private static readonly MacroKeyChoice Home = Key("Home", 0x24, extended: true);
    private static readonly MacroKeyChoice End = Key("End", 0x23, extended: true);
    private static readonly MacroKeyChoice PageUp = Key("PgUp", 0x21, extended: true);
    private static readonly MacroKeyChoice PageDown = Key("PgDn", 0x22, extended: true);
    private static readonly MacroKeyChoice ArrowLeft = Key("Left", 0x25, extended: true);
    private static readonly MacroKeyChoice ArrowUp = Key("Up", 0x26, extended: true);
    private static readonly MacroKeyChoice ArrowRight = Key("Right", 0x27, extended: true);
    private static readonly MacroKeyChoice ArrowDown = Key("Down", 0x28, extended: true);

    private static readonly MacroKeyChoice NumLock = Key("Num", 0x90, extended: true);
    private static readonly MacroKeyChoice NumpadDivide = Key("/", 0x6F, extended: true);
    private static readonly MacroKeyChoice NumpadMultiply = Key("*", 0x6A);
    private static readonly MacroKeyChoice NumpadMinus = Key("-", 0x6D);
    private static readonly MacroKeyChoice NumpadPlus = Key("+", 0x6B);
    private static readonly MacroKeyChoice NumpadZero = Key("0", 0x60);
    private static readonly MacroKeyChoice NumpadDecimal = Key(".", 0x6E);
    private static readonly MacroKeyChoice NumpadEnter = Key("Enter", 0x0D, extended: true);
    private static readonly MacroKeyChoice[] NumpadDigits =
    [
        Key("7", 0x67), Key("8", 0x68), Key("9", 0x69),
        Key("4", 0x64), Key("5", 0x65), Key("6", 0x66),
        Key("1", 0x61), Key("2", 0x62), Key("3", 0x63),
    ];

    /// <summary>
    /// The board drawn the way it sits on the desk: the function row on top,
    /// then the typing area with the navigation cluster and the number pad
    /// beside it.
    /// </summary>
    public static MacroKeyBoard Board { get; } = BuildBoard();

    public static IReadOnlyList<MacroKeyChoice> ExtendedFunctionKeys { get; } =
    [
        Key("F13", 0x7C), Key("F14", 0x7D), Key("F15", 0x7E), Key("F16", 0x7F),
        Key("F17", 0x80), Key("F18", 0x81), Key("F19", 0x82), Key("F20", 0x83),
        Key("F21", 0x84), Key("F22", 0x85), Key("F23", 0x86), Key("F24", 0x87),
    ];

    public static IReadOnlyList<MacroKeyChoice> MediaKeys { get; } =
    [
        Key("Mute", 0xAD, extended: true),
        Key("Vol -", 0xAE, extended: true),
        Key("Vol +", 0xAF, extended: true),
        Key("Prev", 0xB1, extended: true),
        Key("Play", 0xB3, extended: true),
        Key("Next", 0xB0, extended: true),
        Key("Stop", 0xB2, extended: true),
        Key("Mail", 0xB4, extended: true),
        Key("Home", 0xAC, extended: true),
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

    private static MacroKeyBoard BuildBoard()
    {
        var slots = new List<MacroKeySlot>();

        // Function row: escape, then the twelve function keys in groups of four.
        slots.Add(new(Escape, 0, 0, U));
        var column = U + U / 2;
        for (var index = 0; index < FunctionKeys.Length; index++)
        {
            if (index > 0 && index % 4 == 0)
            {
                column += U / 2;
            }

            slots.Add(new(FunctionKeys[index], 0, column, U));
            column += U;
        }

        // The typing area, one row lower than the function row.
        var row = 1;
        var main = 0;
        slots.Add(new(Backquote, row, main, U));
        main += U;
        foreach (var key in DigitKeys)
        {
            slots.Add(new(key, row, main, U));
            main += U;
        }

        slots.Add(new(Minus, row, main, U));
        main += U;
        slots.Add(new(EqualsKey, row, main, U));
        main += U;
        slots.Add(new(Backspace, row, main, 2 * U));

        row = 2;
        main = 0;
        slots.Add(new(Tab, row, main, 3 * U / 2));
        main += 3 * U / 2;
        foreach (var key in TopLetters)
        {
            slots.Add(new(key, row, main, U));
            main += U;
        }

        slots.Add(new(BracketLeft, row, main, U));
        main += U;
        slots.Add(new(BracketRight, row, main, U));
        main += U;
        slots.Add(new(Backslash, row, main, 3 * U / 2));

        row = 3;
        main = 0;
        slots.Add(new(CapsLock, row, main, 7 * U / 4));
        main += 7 * U / 4;
        foreach (var key in HomeLetters)
        {
            slots.Add(new(key, row, main, U));
            main += U;
        }

        slots.Add(new(Semicolon, row, main, U));
        main += U;
        slots.Add(new(Quote, row, main, U));
        main += U;
        slots.Add(new(Enter, row, main, 9 * U / 4));

        row = 4;
        main = 0;
        slots.Add(new(ShiftLeft, row, main, 9 * U / 4));
        main += 9 * U / 4;
        foreach (var key in BottomLetters)
        {
            slots.Add(new(key, row, main, U));
            main += U;
        }

        slots.Add(new(Comma, row, main, U));
        main += U;
        slots.Add(new(Period, row, main, U));
        main += U;
        slots.Add(new(Slash, row, main, U));
        main += U;
        slots.Add(new(ShiftRight, row, main, 11 * U / 4));

        row = 5;
        main = 0;
        slots.Add(new(ControlLeft, row, main, 5 * U / 4));
        main += 5 * U / 4;
        slots.Add(new(WindowsLeft, row, main, 5 * U / 4));
        main += 5 * U / 4;
        slots.Add(new(AltLeft, row, main, 5 * U / 4));
        main += 5 * U / 4;
        slots.Add(new(Space, row, main, 25 * U / 4));
        main += 25 * U / 4;
        slots.Add(new(AltRight, row, main, 5 * U / 4));
        main += 5 * U / 4;
        slots.Add(new(WindowsRight, row, main, 5 * U / 4));
        main += 5 * U / 4;
        slots.Add(new(Menu, row, main, 5 * U / 4));
        main += 5 * U / 4;
        slots.Add(new(ControlRight, row, main, 5 * U / 4));

        // The navigation cluster: the system keys stacked on the left, the
        // editing block and the arrow keys to the right of them.
        var navigation = NavigationColumn;
        slots.Add(new(PrintScreen, 1, navigation, U));
        slots.Add(new(ScrollLock, 2, navigation, U));
        slots.Add(new(Pause, 3, navigation, U));
        slots.Add(new(Insert, 1, navigation + 2 * U, U));
        slots.Add(new(Home, 1, navigation + 3 * U, U));
        slots.Add(new(PageUp, 1, navigation + 4 * U, U));
        slots.Add(new(Delete, 2, navigation + 2 * U, U));
        slots.Add(new(End, 2, navigation + 3 * U, U));
        slots.Add(new(PageDown, 2, navigation + 4 * U, U));
        slots.Add(new(ArrowUp, 4, navigation + 3 * U, U));
        slots.Add(new(ArrowLeft, 5, navigation + 2 * U, U));
        slots.Add(new(ArrowDown, 5, navigation + 3 * U, U));
        slots.Add(new(ArrowRight, 5, navigation + 4 * U, U));

        // The number pad.
        var pad = NumberPadColumn;
        slots.Add(new(NumLock, 1, pad, U));
        slots.Add(new(NumpadDivide, 1, pad + U, U));
        slots.Add(new(NumpadMultiply, 1, pad + 2 * U, U));
        slots.Add(new(NumpadMinus, 1, pad + 3 * U, U));
        slots.Add(new(NumpadPlus, 2, pad + 3 * U, U, 2));
        slots.Add(new(NumpadZero, 5, pad, 2 * U));
        slots.Add(new(NumpadDecimal, 5, pad + 2 * U, U));
        slots.Add(new(NumpadEnter, 4, pad + 3 * U, U, 2));
        for (var index = 0; index < NumpadDigits.Length; index++)
        {
            slots.Add(new(NumpadDigits[index], 2 + index / 3, pad + (index % 3) * U, U));
        }

        return new MacroKeyBoard(BoardColumns, BoardRows, [.. slots]);
    }

    private static MacroKeyChoice Key(string label, int virtualKey, bool extended = false) =>
        new(label, new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = virtualKey,
            Extended = extended,
        });
}
