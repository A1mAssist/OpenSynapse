using System.Globalization;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Razer keypads expose numbered SVG selection paths instead of named keys. The 022B/0244
/// firmware table puts the 12 main keys at 64..75, seven thumb keys at 80..86, the scroll
/// click at 3 and the bottom thumb key at 96.
/// </summary>
public static class RazerKeypadKeyCatalog
{
    public static IReadOnlyDictionary<string, byte> Resolve(
        ushort productId,
        IEnumerable<StudioKeyboardKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        // shortcut: 022B/0244 share Razer's keypad firmware table; add a per-product table when another keypad gets art.
        var firmwareIds = productId is 0x022B or 0x0244;
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
                result[key.KeyName] = firmwareIds ? KeypadFirmwareId(key.PathId, id) : id;
            }
        }

        return result;
    }

    // The 3x4 main pad is SPAD_NUM_1..12 (64..75), the seven thumb keys are HPAD_NUM_1..7 (80..86),
    // the scroll click is 3, and the bottom thumb key is 96.
    private static byte KeypadFirmwareId(string pathId, byte fallback)
    {
        if (!pathId.StartsWith("selection-", StringComparison.Ordinal) ||
            !int.TryParse(pathId.AsSpan("selection-".Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var selection))
        {
            return fallback;
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
