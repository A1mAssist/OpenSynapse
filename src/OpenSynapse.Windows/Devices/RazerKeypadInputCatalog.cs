using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>Normal keyboard/mouse inputs from the official keypad default layouts.</summary>
public static class RazerKeypadInputCatalog
{
    public const ushort ProductId = 0x0244;

    public static bool IsLegacy(int productId) => productId is 0x0111 or 0x0113 or 0x0201 or 0x0207 or 0x0208;
    public static bool UsesHostMapping(int productId) => IsLegacy(productId) || productId is 0x022B or 0x0244;

    // UI ids, HID usages and Set 1 scancodes from the product's default layout.
    private static readonly (byte Button, byte Usage, ushort ScanCode, ushort Flag)[] Keys =
    [
        (0x40, 30, 0x02, 0), (0x41, 31, 0x03, 0), (0x42, 32, 0x04, 0),
        (0x43, 33, 0x05, 0), (0x50, 34, 0x06, 0), (0x44, 43, 0x0F, 0),
        (0x45, 20, 0x10, 0), (0x46, 26, 0x11, 0), (0x47, 8, 0x12, 0),
        (0x51, 21, 0x13, 0), (0x48, 57, 0x3A, 0), (0x49, 4, 0x1E, 0),
        (0x4A, 22, 0x1F, 0), (0x4B, 7, 0x20, 0), (0x52, 9, 0x21, 0),
        (0x53, 225, 0x2A, 0), (0x54, 29, 0x2C, 0), (0x55, 27, 0x2D, 0),
        (0x56, 6, 0x2E, 0), (0x60, 44, 0x39, 0), (0x3C, 226, 0x38, 0),
        (0x2C, 82, 0x48, 2), (0x2D, 81, 0x50, 2),
        (0x2E, 80, 0x4B, 2), (0x2F, 79, 0x4D, 2),
    ];

    private static readonly IReadOnlyDictionary<int, (byte Button, byte Usage, ushort ScanCode, ushort Flag)[]> LegacyKeys =
        new[] { 0x0111, 0x0113, 0x0201, 0x0207, 0x0208 }.ToDictionary(id => id, BuildLegacyKeys);

    private static (byte Button, byte Usage, ushort ScanCode, ushort Flag)[] BuildLegacyKeys(int productId)
    {
        byte[] usages = [53, 30, 31, 32, 33, 43, 20, 26, 8, 21, 57, 4, 22, 7, 9, 225, 29, 27, 6, 25];
        var first = productId is 0x0113 or 0x0207 ? 0 : 5;
        var count = productId == 0x0111 ? 14 : usages.Length - first;
        var inputs = usages.Skip(first).Take(count).Select((usage, index) =>
            (Button: (byte)(productId == 0x0111 ? index + 1 : first + index + 1), Usage: usage))
            .Concat(new (byte Button, byte Usage)[]
            {
                (0xE0, 226), (0xE1, 44), (0xF0, 82), (0xF1, 81), (0xF2, 80), (0xF3, 79),
            });
        return inputs.Select(input =>
        {
            if (!RazerHidUsageMapping.TryGetScancode(input.Usage, out var scan))
                throw new InvalidOperationException($"No scancode for keypad HID usage {input.Usage}.");
            return (input.Button, input.Usage, (ushort)(scan & 0xFF), (ushort)(scan > 0xFF ? 2 : 0));
        }).ToArray();
    }

    private static IReadOnlyList<(byte Button, byte Usage, ushort ScanCode, ushort Flag)> KeysFor(int productId) =>
        LegacyKeys.TryGetValue(productId, out var keys) ? keys : productId is 0x022B or 0x0244
            ? Keys : throw new ArgumentOutOfRangeException(nameof(productId));

    public static IReadOnlyList<byte> GetButtonIds(int productId) => IsLegacy(productId)
        ? KeysFor(productId).Select(key => key.Button)
            .Concat(productId == 0x0111 ? new byte[] { 0xE3, 0xE4, 0xE5 } : []).Order().ToArray()
        : UsesHostMapping(productId) ? AllButtonIds : [];

    public static IReadOnlyList<(ushort ScanCode, ushort Flag)> GetKeyboardHooks(int productId) =>
        KeysFor(productId).Select(key => (key.ScanCode, key.Flag)).ToArray();

    public static bool HasWheel(int productId) => productId is 0x0111 or 0x022B or 0x0244;

    public static bool IsWheelInput(byte buttonId, int productId) =>
        productId == 0x0111 ? buttonId is 0xE4 or 0xE5 : !IsLegacy(productId) && buttonId is 0x09 or 0x0A;

    public static IReadOnlyList<byte> AllButtonIds { get; } = Array.AsReadOnly<byte>(
        [0x03, 0x09, 0x0A, .. Keys.Select(key => key.Button).Order()]);

    public static IReadOnlyList<(ushort ScanCode, ushort Flag)> KeyboardHooks { get; } =
        Array.AsReadOnly(Keys.Select(key => (key.ScanCode, key.Flag)).ToArray());

    public static IReadOnlyList<byte> SupportedFunctions { get; } = Array.AsReadOnly<byte>(
    [
        RazerHidMappingFunctions.Off,
        RazerHidMappingFunctions.KeyCode,
        RazerHidMappingFunctions.ButtonCode,
        RazerHidMappingFunctions.MediaKeys,
        RazerHidMappingFunctions.DoubleClick,
        RazerHidMappingFunctions.ModeButtonkey,
        RazerHidMappingFunctions.Win8ShortcutsKey,
    ]);

    public static bool TryResolveInput(BladeMappingInputEvent input, out byte buttonId, int productId = ProductId)
    {
        buttonId = 0;
        if (input.Kind == BladeMappingInputKind.Keyboard)
        {
            buttonId = KeysFor(productId).FirstOrDefault(key =>
                key.ScanCode == input.Code && (key.Flag == 2) == input.Extended).Button;
        }
        else if (HasWheel(productId) && !input.Extended && input.Kind == BladeMappingInputKind.MouseButton && input.Code == 3)
        {
            buttonId = productId == 0x0111 ? (byte)0xE3 : (byte)0x03;
        }
        else if (HasWheel(productId) && !input.Extended && input.Kind == BladeMappingInputKind.MouseWheel && input.Code != 0)
        {
            buttonId = productId == 0x0111
                ? input.Code > 0 ? (byte)0xE4 : (byte)0xE5
                : input.Code > 0 ? (byte)0x09 : (byte)0x0A;
        }

        return buttonId != 0;
    }

    public static RazerHidButtonAssignment GetDefaultAssignment(byte buttonId, byte mode = 0, int productId = ProductId)
    {
        ValidateInput(buttonId, mode, productId);
        if (HasWheel(productId) && (productId == 0x0111 ? buttonId is 0xE3 or 0xE4 or 0xE5 : buttonId is 0x03 or 0x09 or 0x0A))
        {
            var output = productId == 0x0111 ? buttonId switch { 0xE3 => (byte)3, 0xE4 => (byte)9, _ => (byte)10 } : buttonId;
            return new(buttonId, mode, RazerHidMappingFunctions.ButtonCode, [output]);
        }

        return new(buttonId, mode, RazerHidMappingFunctions.KeyCode,
            [0, KeysFor(productId).Single(key => key.Button == buttonId).Usage]);
    }

    public static bool IsSupportedAction(byte functionId, IReadOnlyList<byte>? data) =>
        data is not null && functionId switch
        {
            RazerHidMappingFunctions.Off => data.Count == 0,
            RazerHidMappingFunctions.KeyCode => data.Count == 2 &&
                (data[1] == 0 ? data[0] != 0 : RazerHidUsageMapping.TryGetScancode(data[1], out _)),
            RazerHidMappingFunctions.ButtonCode => data.Count == 1 &&
                data[0] is >= 1 and <= 5 or 0x09 or 0x0A or 0x34 or 0x35 or 0x68 or 0x69,
            RazerHidMappingFunctions.MediaKeys => data.Count == 2 &&
                MediaVirtualKey((ushort)((data[0] << 8) | data[1])) != 0,
            RazerHidMappingFunctions.DoubleClick => data.Count == 1 && data[0] == 1,
            RazerHidMappingFunctions.ModeButtonkey => data.Count == 1 && data[0] == 1,
            RazerHidMappingFunctions.Win8ShortcutsKey => data.Count == 1 && data[0] is 99 or 100 or 101,
            _ => false,
        };

    public static void ValidateAssignment(RazerHidButtonAssignment assignment, int productId = ProductId)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ValidateInput(assignment.ButtonId, assignment.Mode, productId);
        if (!IsSupportedAction(assignment.FunctionId, assignment.Data))
        {
            throw new NotSupportedException(
                $"Keypad host mappings do not support function 0x{assignment.FunctionId:X2} " +
                "with this action data.");
        }
    }

    internal static void ValidateInput(byte buttonId, byte mode, int productId = ProductId)
    {
        if (!GetButtonIds(productId).Contains(buttonId))
        {
            throw new ArgumentOutOfRangeException(nameof(buttonId), buttonId, "Unknown keypad input.");
        }
        if (mode > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Only normal and HyperShift layers are supported.");
        }
    }

    internal static int MediaVirtualKey(ushort usage) => usage switch
    {
        226 => 0xAD,
        233 => 0xAF,
        234 => 0xAE,
        205 => 0xB3,
        181 => 0xB0,
        182 => 0xB1,
        183 => 0xB2,
        _ => 0,
    };
}
