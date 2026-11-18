using OpenSynapse.Windows.Devices;

namespace OpenSynapse.Windows.Protocols;

public static partial class RazerHidMappingActions
{
    private static readonly (string Name, byte FunctionId, byte[] Data)[] WindowsShortcuts =
    [
        ("Screenshot", RazerHidMappingFunctions.Win8ShortcutsKey, [99]),
        ("Xbox Game Bar", RazerHidMappingFunctions.Win8ShortcutsKey, [100]),
        ("Cycle apps", RazerHidMappingFunctions.Win8ShortcutsKey, [101]),
        // The firmware only has native ids for the three above; the rest are ordinary
        // chords it already knows how to emit.
        ("Task Manager", RazerHidMappingFunctions.KeyCode, [0x03, 0x29]),
        ("Close app", RazerHidMappingFunctions.KeyCode, [0x04, 0x3D]),
        ("Switch apps", RazerHidMappingFunctions.KeyCode, [0x04, 0x2B]),
        ("Cut", RazerHidMappingFunctions.KeyCode, [0x01, 0x1B]),
        ("Copy", RazerHidMappingFunctions.KeyCode, [0x01, 0x06]),
        ("Paste", RazerHidMappingFunctions.KeyCode, [0x01, 0x19]),
    ];

    private static IReadOnlyList<string> WindowsShortcutNames() =>
        WindowsShortcuts.Select(shortcut => shortcut.Name).ToArray();

    private static RazerHidMappingAction? CreateWindowsShortcut(int optionIndex) =>
        optionIndex >= 0 && optionIndex < WindowsShortcuts.Length
            ? CreateWindowsShortcut(WindowsShortcuts[optionIndex])
            : null;

    private static RazerHidMappingAction CreateWindowsShortcut(
        (string Name, byte FunctionId, byte[] Data) shortcut) =>
        new(shortcut.Name, shortcut.FunctionId, shortcut.Data.ToArray());

    /// <summary>
    /// The assignment that puts a keyboard key back to its own key code. A key
    /// left on a firmware shortcut never reaches the low-level hook, so a
    /// software binding for it would never fire.
    /// </summary>
    public static (byte FunctionId, byte[] Data)? NativeKeyboardAssignment(
        byte buttonId,
        ushort productId)
    {
        if (RazerHuntsmanV3ProKeys.UsesFirmwareKeyIds(productId))
        {
            if (!RazerHuntsmanV3ProKeys.TryGetHidUsage(buttonId, out var hid))
            {
                return null;
            }

            return (RazerHidMappingFunctions.KeyCode, [0, hid]);
        }

        // Keypad rows use firmware ids, not HID usages; writing one as a key code
        // would land on the wrong key.
        if (buttonId is 0 or > 0xA4)
        {
            return null;
        }

        return (RazerHidMappingFunctions.KeyCode, [0, buttonId]);
    }

    /// <summary>
    /// The firmware keeps Win down when these are sent as raw chords. Synapse uses its
    /// native shortcut ids instead, which release the modifier themselves.
    /// </summary>
    private static RazerHidMappingAction? CreateNativeWindowsShortcut(byte modifierMask, byte hid)
    {
        var code = modifierMask == 0x08
            ? hid switch { 0x0A => (byte)100, 0x2B => (byte)101, _ => (byte)0 }
            : (byte)0;
        if (code == 0)
        {
            return null;
        }

        var native = WindowsShortcuts.First(item =>
            item.FunctionId == RazerHidMappingFunctions.Win8ShortcutsKey && item.Data[0] == code);
        return CreateWindowsShortcut(native);
    }

    private static string? WindowsShortcutName(byte functionId, IReadOnlyList<byte> data) =>
        WindowsShortcutIndex(functionId, data) is { } index
            ? WindowsShortcuts[index].Name
            : null;

    private static int? WindowsShortcutIndex(byte functionId, IReadOnlyList<byte> data)
    {
        for (var index = 0; index < WindowsShortcuts.Length; index++)
        {
            var shortcut = WindowsShortcuts[index];
            if (shortcut.FunctionId == functionId && data.SequenceEqual(shortcut.Data))
            {
                return index;
            }
        }

        return null;
    }

    private static readonly (byte Bit, string Name)[] ModifierBits =
    [
        (0x01, "Ctrl"), (0x02, "Shift"), (0x04, "Alt"), (0x08, "Win"),
        (0x10, "Right Ctrl"), (0x20, "Right Shift"), (0x40, "Right Alt"), (0x80, "Right Win"),
    ];

    private static string ModifierLabel(byte mask) => string.Join(
        '+', ModifierBits.Where(bit => (mask & bit.Bit) != 0).Select(bit => bit.Name));
}
