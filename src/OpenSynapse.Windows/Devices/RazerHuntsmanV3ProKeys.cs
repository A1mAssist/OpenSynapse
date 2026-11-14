namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Razer 6737 bundle: the mapping page addresses keys by firmware id, not by USB HID
/// usage. The table is identical for 02A6 / 02A7 / 02B0 / 02CF.
/// </summary>
internal static class RazerHuntsmanV3ProKeys
{
    public const string ProfileKeyIdMarker = "key-id";
    public const string ProfileFirmwareKeyIds = "firmware";

    private static readonly IReadOnlyDictionary<byte, byte> FirmwareKeyIdsByHid =
        new Dictionary<byte, byte>
        {
            [4] = 31,      // KEY_A
            [5] = 50,      // KEY_B
            [6] = 48,      // KEY_C
            [7] = 33,      // KEY_D
            [8] = 19,      // KEY_E
            [9] = 34,      // KEY_F
            [10] = 35,     // KEY_G
            [11] = 36,     // KEY_H
            [12] = 24,     // KEY_I
            [13] = 37,     // KEY_J
            [14] = 38,     // KEY_K
            [15] = 39,     // KEY_L
            [16] = 52,     // KEY_M
            [17] = 51,     // KEY_N
            [18] = 25,     // KEY_O
            [19] = 26,     // KEY_P
            [20] = 17,     // KEY_Q
            [21] = 20,     // KEY_R
            [22] = 32,     // KEY_S
            [23] = 21,     // KEY_T
            [24] = 23,     // KEY_U
            [25] = 49,     // KEY_V
            [26] = 18,     // KEY_W
            [27] = 47,     // KEY_X
            [28] = 22,     // KEY_Y
            [29] = 46,     // KEY_Z
            [30] = 2,      // KEY_1
            [31] = 3,      // KEY_2
            [32] = 4,      // KEY_3
            [33] = 5,      // KEY_4
            [34] = 6,      // KEY_5
            [35] = 7,      // KEY_6
            [36] = 8,      // KEY_7
            [37] = 9,      // KEY_8
            [38] = 10,     // KEY_9
            [39] = 11,     // KEY_0
            [40] = 43,     // KEY_ENTER
            [41] = 110,    // KEY_ESC
            [42] = 15,     // KEY_BACKSPACE
            [43] = 16,     // KEY_TAB
            [44] = 61,     // KEY_SPACEBAR
            [45] = 12,     // KEY_HYPEN
            [46] = 13,     // KEY_EQUAL
            [47] = 27,     // KEY_OPEN_SQUARE_BRACKET
            [48] = 28,     // KEY_CLOSE_SQUARE_BRACKET
            [49] = 29,     // KEY_BACKSLASH
            [50] = 42,     // KEY_NON_US_POUND
            [51] = 40,     // KEY_SEMICOLON
            [52] = 41,     // KEY_APOSTROPHE
            [53] = 1,      // KEY_TILDE
            [54] = 53,     // KEY_COMMA
            [55] = 54,     // KEY_PERIOD
            [56] = 55,     // KEY_SLASH
            [57] = 30,     // KEY_CAPS_LOCK
            [58] = 112,    // KEY_F1
            [59] = 113,    // KEY_F2
            [60] = 114,    // KEY_F3
            [61] = 115,    // KEY_F4
            [62] = 116,    // KEY_F5
            [63] = 117,    // KEY_F6
            [64] = 118,    // KEY_F7
            [65] = 119,    // KEY_F8
            [66] = 120,    // KEY_F9
            [67] = 121,    // KEY_F10
            [68] = 122,    // KEY_F11
            [69] = 123,    // KEY_F12
            [70] = 124,    // KEY_PRINT_SCREEN
            [71] = 125,    // KEY_SCROLL_LOCK
            [72] = 126,    // KEY_PAUSE
            [73] = 75,     // KEY_INSERT
            [74] = 80,     // KEY_HOME
            [75] = 85,     // KEY_PAGE_UP
            [76] = 76,     // KEY_DELETE
            [77] = 81,     // KEY_END
            [78] = 86,     // KEY_PAGE_DOWN
            [79] = 89,     // KEY_RIGHT_ARROW
            [80] = 79,     // KEY_LEFT_ARROW
            [81] = 84,     // KEY_DOWN_ARROW
            [82] = 83,     // KEY_UP_ARROW
            [83] = 90,     // KEY_NUMPAD_NUM_LOCK
            [84] = 95,     // KEY_NUMPAD_SLASH
            [85] = 100,    // KEY_NUMPAD_ASTERISK
            [86] = 105,    // KEY_NUMPAD_DASH
            [87] = 106,    // KEY_NUMPAD_PLUS
            [88] = 108,    // KEY_NUMPAD_ENTER
            [89] = 93,     // KEY_NUMPAD_1
            [90] = 98,     // KEY_NUMPAD_2
            [91] = 103,    // KEY_NUMPAD_3
            [92] = 92,     // KEY_NUMPAD_4
            [93] = 97,     // KEY_NUMPAD_5
            [94] = 102,    // KEY_NUMPAD_6
            [95] = 91,     // KEY_NUMPAD_7
            [96] = 96,     // KEY_NUMPAD_8
            [97] = 101,    // KEY_NUMPAD_9
            [98] = 99,     // KEY_NUMPAD_0
            [99] = 104,    // KEY_NUMPAD_PERIOD
            [100] = 45,    // KEY_NON_US_BACKSLASH
            [101] = 129,   // KEY_APPLICATION
            [103] = 107,   // KEY_NUMPAD_EQUAL
            [135] = 56,    // KEY_RO
            [136] = 151,   // KEY_KATAKANA_HIRAGANA
            [137] = 14,    // KEY_YEN
            [138] = 154,   // KEY_HENKAN
            [139] = 150,   // KEY_MUHENKAN
            [144] = 153,   // KEY_HANGEUL
            [145] = 152,   // KEY_HANJA
            [224] = 58,    // KEY_LEFT_CTRL
            [225] = 44,    // KEY_LEFT_SHIFT
            [226] = 60,    // KEY_LEFT_ALT
            [227] = 127,   // KEY_LEFT_GUI
            [228] = 64,    // KEY_RIGHT_CTRL
            [229] = 57,    // KEY_RIGHT_SHIFT
            [230] = 62,    // KEY_RIGHT_ALT
            [231] = 59,    // KEY_RIGHT_GUI
        };

    private static readonly IReadOnlyDictionary<byte, byte> HidByFirmwareKeyId =
        FirmwareKeyIdsByHid.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static bool UsesFirmwareKeyIds(ushort productId) =>
        productId is 0x02A6 or 0x02A7 or 0x02B0 or 0x02CF;

    public static bool TryGetFirmwareKeyId(byte hid, out byte firmwareKeyId) =>
        FirmwareKeyIdsByHid.TryGetValue(hid, out firmwareKeyId);

    public static bool TryGetHidUsage(byte firmwareKeyId, out byte hid) =>
        HidByFirmwareKeyId.TryGetValue(firmwareKeyId, out hid);

    /// <summary>
    /// Saved profiles written before the firmware-id switch stored USB HID usages.
    /// </summary>
    public static bool TryResolveProfileButtonId(
        ushort productId,
        byte buttonId,
        IReadOnlyDictionary<string, string>? extra,
        out byte resolved)
    {
        resolved = buttonId;
        if (!UsesFirmwareKeyIds(productId) ||
            (extra is not null &&
             extra.TryGetValue(ProfileKeyIdMarker, out var marker) &&
             string.Equals(marker, ProfileFirmwareKeyIds, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return TryGetFirmwareKeyId(buttonId, out resolved);
    }

    internal static IReadOnlyDictionary<byte, byte> All => FirmwareKeyIdsByHid;
}
