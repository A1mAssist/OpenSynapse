using System.Globalization;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Razer keypads expose numbered SVG selection paths instead of named keys. The 022B/0244
/// firmware table puts the 12 main keys at 64..75, seven thumb keys at 80..86, the scroll
/// click at 3 and the bottom thumb key at 96. The 02EB Tartarus V2 Pro is a separate
/// 27-key table: three six-key rows, one five-key row and a four-key bottom row, using
/// the generic Razer keyboard firmware ids.
/// </summary>
public static class RazerKeypadKeyCatalog
{
    // 02EB groupList / Studio sel1..sel27 order. FwIDs come from the bundle's
    // InputIDToMapping_Keyboard_Generic NormalMode table, not its LED matrix.
    private static readonly byte[] TartarusV2ProFirmwareIds =
    [
        1, 2, 3, 4, 5, 6,
        16, 17, 18, 19, 20, 21,
        30, 31, 32, 33, 34, 35,
        44, 46, 47, 48, 49,
        58, 50, 24, 61,
    ];

    /// <summary>
    /// 022B/0244 expose the wheel and D-pad outside the numbered SVG selection paths.
    /// The keypad bundle maps ScrollClick to firmware id 3, ScrollUp/ScrollDown to
    /// 9/10, the four D-pad directions to 44..47 (0x2C..0x2F) and Alt to 60 (0x3C).
    /// 02EB adds Fn 59 (0x3B), Profile Cycle 217 (0xD9), Keymap Cycle 218 (0xDA)
    /// and the right-stick click 219 (0xDB).
    /// </summary>
    public static IReadOnlyList<byte> ExtraButtonIds(ushort productId) =>
        productId switch
        {
            0x022B or 0x0244 => [0x03, 0x09, 0x0A, 0x2C, 0x2D, 0x2E, 0x2F, 0x3C],
            0x02EB => [0x3B, 0xD9, 0xDA, 0xDB],
            _ => [],
        };

    public static IReadOnlyDictionary<string, byte> Resolve(
        ushort productId,
        IEnumerable<StudioKeyboardKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        // shortcut: 022B/0244 share Razer's keypad firmware table; 02EB has its own.
        var firmwareIds = productId is 0x022B or 0x0244 or 0x02EB;
        foreach (var key in keys)
        {
            if (key.KeyName.Equals("scroll", StringComparison.OrdinalIgnoreCase))
            {
                result[key.KeyName] = 3;
                continue;
            }

            if (key.KeyName.StartsWith("key", StringComparison.OrdinalIgnoreCase) &&
                byte.TryParse(key.KeyName.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
                id > 0)
            {
                result[key.KeyName] = firmwareIds ? KeypadFirmwareId(productId, key.PathId, id) : id;
            }
        }

        return result;
    }

    // The 3x4 main pad is SPAD_NUM_1..12 (64..75), the seven thumb keys are HPAD_NUM_1..7 (80..86),
    // the scroll click is 3, and the bottom thumb key is 96.
    private static byte KeypadFirmwareId(ushort productId, string pathId, byte fallback)
    {
        if (!pathId.StartsWith("selection-", StringComparison.Ordinal) ||
            !int.TryParse(pathId.AsSpan("selection-".Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var selection))
        {
            return fallback;
        }

        if (productId == 0x02EB)
        {
            return selection > 0 && selection <= TartarusV2ProFirmwareIds.Length
                ? TartarusV2ProFirmwareIds[selection - 1]
                : fallback;
        }

        return selection switch
        {
            >= 1 and <= 4 => (byte)(63 + selection),
            5 => 80,
            >= 6 and <= 9 => (byte)(62 + selection),
            10 => 81,
            >= 11 and <= 14 => (byte)(61 + selection),
            15 => 82,
            >= 16 and <= 19 => (byte)(67 + selection),
            20 => 3,
            21 => 96,
            _ => fallback,
        };
    }
}
