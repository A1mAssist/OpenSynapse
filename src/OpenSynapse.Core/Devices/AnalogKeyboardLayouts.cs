namespace OpenSynapse.Core.Devices;

public sealed record AnalogKeyboardKey(byte KeyId, string Label, int Row, double WidthUnits = 1.0);

public sealed record AnalogKeyboardLayout(
    ushort ProductId,
    string Name,
    IReadOnlyList<AnalogKeyboardKey> Keys);

/// <summary>
/// Per-key analog layouts taken from the middleware key table in bundle 7795.
/// only product ids with an observed protocol-2 analog capability are listed.
/// </summary>
public static class AnalogKeyboardLayouts
{
    private static readonly IReadOnlyDictionary<ushort, AnalogKeyboardLayout> Layouts = Build();

    public static bool TryGet(ushort productId, out AnalogKeyboardLayout layout) =>
        Layouts.TryGetValue(productId, out layout!);

    private static IReadOnlyDictionary<ushort, AnalogKeyboardLayout> Build()
    {
        var layouts = new Dictionary<ushort, AnalogKeyboardLayout>
        {
            [0x02A6] = Create(0x02A6, "Huntsman V3 Pro", includeFunctionRow: true,
                includeNavigation: true, includeNumpad: true),
            [0x02D0] = Create(0x02D0, "Huntsman V3 Pro TKL", includeFunctionRow: true,
                includeNavigation: true, includeNumpad: false),
            [0x02D1] = Create(0x02D1, "Huntsman V3 Pro Mini", includeFunctionRow: false,
                includeNavigation: false, includeNumpad: false),
            [0x02E4] = Create(0x02E4, "Huntsman V3 HE Mini 65%", includeFunctionRow: false,
                includeNavigation: true, includeNumpad: false),
            [0x02EA] = Create(0x02EA, "Huntsman V3 HE TKL", includeFunctionRow: true,
                includeNavigation: true, includeNumpad: false),
        };
        return layouts;
    }

    private static AnalogKeyboardLayout Create(
        ushort productId,
        string name,
        bool includeFunctionRow,
        bool includeNavigation,
        bool includeNumpad)
    {
        var keys = new List<AnalogKeyboardKey>(includeNumpad ? 104 : 88);
        var row = 0;
        if (includeFunctionRow)
        {
            Row(keys, row++, (110, "Esc"), (112, "F1"), (113, "F2"), (114, "F3"), (115, "F4"),
                (116, "F5"), (117, "F6"), (118, "F7"), (119, "F8"), (120, "F9"),
                (121, "F10"), (122, "F11"), (123, "F12"));
            if (includeNavigation)
            {
                Append(keys, row - 1, (124, "PrtSc"), (125, "ScrLk"), (126, "Pause"));
            }
            if (includeNumpad)
            {
                Append(keys, row - 1, (90, "Num Lock"), (95, "Num /"), (100, "Num *"), (105, "Num -"));
            }
        }

        Row(keys, row++, (1, "`"), (2, "1"), (3, "2"), (4, "3"), (5, "4"), (6, "5"),
            (7, "6"), (8, "7"), (9, "8"), (10, "9"), (11, "0"), (12, "-"), (13, "="),
            (15, "Backspace", 2.0));
        if (includeNavigation)
        {
            Append(keys, row - 1, (75, "Insert"), (80, "Home"), (85, "Page Up"));
        }
        if (includeNumpad)
        {
            Append(keys, row - 1, (91, "Num 7"), (96, "Num 8"), (101, "Num 9"), (106, "Num +"));
        }

        Row(keys, row++, (16, "Tab", 1.5), (17, "Q"), (18, "W"), (19, "E"), (20, "R"),
            (21, "T"), (22, "Y"), (23, "U"), (24, "I"), (25, "O"), (26, "P"),
            (27, "["), (28, "]"), (29, "\\", 1.5));
        if (includeNumpad)
        {
            Append(keys, row - 1, (92, "Num 4"), (97, "Num 5"), (102, "Num 6"));
        }

        Row(keys, row++, (30, "Caps", 1.75), (31, "A"), (32, "S"), (33, "D"), (34, "F"),
            (35, "G"), (36, "H"), (37, "J"), (38, "K"), (39, "L"), (40, ";"), (41, "'"),
            (43, "Enter", 2.25));
        if (includeNavigation)
        {
            Append(keys, row - 1, (76, "Delete"), (81, "End"), (86, "Page Down"));
        }
        if (includeNumpad)
        {
            Append(keys, row - 1, (93, "Num 1"), (98, "Num 2"), (103, "Num 3"), (108, "Num Enter"));
        }

        Row(keys, row++, (44, "Shift", 2.25), (46, "Z"), (47, "X"), (48, "C"), (49, "V"),
            (50, "B"), (51, "N"), (52, "M"), (53, ","), (54, "."), (55, "/"),
            (57, "Shift", 2.75));
        if (includeNavigation)
        {
            Append(keys, row - 1, (83, "Up"));
        }
        if (includeNumpad)
        {
            Append(keys, row - 1, (99, "Num 0"), (104, "Num ."));
        }

        Row(keys, row, (58, "Ctrl", 1.25), (127, "Win", 1.25), (60, "Alt", 1.25),
            (61, "Space", 6.25), (62, "Alt", 1.25), (129, "Menu", 1.25), (64, "Ctrl", 1.25));
        if (includeNavigation)
        {
            Append(keys, row, (79, "Left"), (84, "Down"), (89, "Right"));
        }

        return new AnalogKeyboardLayout(productId, name, keys);
    }

    private static void Row(List<AnalogKeyboardKey> keys, int row, params KeySpec[] entries)
    {
        foreach (var entry in entries)
        {
            keys.Add(new AnalogKeyboardKey(entry.KeyId, entry.Label, row, entry.WidthUnits));
        }
    }

    private static void Append(List<AnalogKeyboardKey> keys, int row, params KeySpec[] entries) =>
        Row(keys, row, entries);

    private readonly record struct KeySpec(byte KeyId, string Label, double WidthUnits)
    {
        public static implicit operator KeySpec((int KeyId, string Label) entry) =>
            new((byte)entry.KeyId, entry.Label, 1.0);

        public static implicit operator KeySpec((int KeyId, string Label, double WidthUnits) entry) =>
            new((byte)entry.KeyId, entry.Label, entry.WidthUnits);
    }
}
