namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Synapse 2 stores keyboard actions in a P2 record as an internal key code,
/// not as a USB HID usage. The table is FUN_100e9c00 in rzdevicedll; the reverse
/// lookup is its inverse and covers every key the mapping editor offers.
/// </summary>
internal static class RazerKeypadKeyCodes
{
    private static readonly IReadOnlyDictionary<byte, ushort> HidToInternal =
        new Dictionary<byte, ushort>
        {
            [4] = 30, [5] = 48, [6] = 46, [7] = 32, [8] = 18, [9] = 33, [10] = 34,
            [11] = 35, [12] = 23, [13] = 36, [14] = 37, [15] = 38, [16] = 50,
            [17] = 49, [18] = 24, [19] = 25, [20] = 16, [21] = 19, [22] = 31,
            [23] = 20, [24] = 22, [25] = 47, [26] = 17, [27] = 45, [28] = 21,
            [29] = 44, [30] = 2, [31] = 3, [32] = 4, [33] = 5, [34] = 6, [35] = 7,
            [36] = 8, [37] = 9, [38] = 10, [39] = 11, [40] = 28, [41] = 1,
            [42] = 14, [43] = 15, [44] = 57, [45] = 12, [46] = 13, [47] = 26,
            [48] = 27, [49] = 43, [51] = 39, [52] = 40, [53] = 41, [54] = 51,
            [55] = 52, [56] = 53, [57] = 58, [58] = 59, [59] = 60, [60] = 61,
            [61] = 62, [62] = 63, [63] = 64, [64] = 65, [65] = 66, [66] = 67,
            [67] = 68, [68] = 87, [69] = 88, [70] = 57399, [71] = 70,
            [72] = 57414, [73] = 57426, [74] = 57415, [75] = 57417, [76] = 57427,
            [77] = 57423, [78] = 57425, [79] = 57421, [80] = 57419, [81] = 57424,
            [82] = 57416, [83] = 69, [84] = 57397, [85] = 55, [86] = 74, [87] = 78,
            [88] = 57372, [89] = 79, [90] = 80, [91] = 81, [92] = 75, [93] = 76,
            [94] = 77, [95] = 71, [96] = 72, [97] = 73, [98] = 82, [99] = 83,
            [101] = 57437, [102] = 57573, [103] = 89, [104] = 100, [105] = 101,
            [106] = 102, [107] = 103, [108] = 104, [109] = 105, [110] = 106,
            [111] = 107, [112] = 108, [113] = 109, [114] = 110, [115] = 118,
            [135] = 115, [136] = 112, [137] = 125, [138] = 121, [224] = 29,
            [225] = 42, [226] = 56, [227] = 57435, [228] = 57373, [229] = 54,
            [230] = 57400, [231] = 57436,
        };

    private static readonly IReadOnlyDictionary<ushort, byte> InternalToHid =
        BuildReverse();

    internal static ushort ToInternal(byte hidUsage) =>
        HidToInternal.TryGetValue(hidUsage, out var code) ? code : (ushort)0;

    internal static byte ToHid(ushort internalCode) =>
        InternalToHid.TryGetValue(internalCode, out var hid) ? hid : (byte)0;

    private static IReadOnlyDictionary<ushort, byte> BuildReverse()
    {
        var reverse = new Dictionary<ushort, byte>(HidToInternal.Count);
        foreach (var (hid, code) in HidToInternal)
        {
            reverse.TryAdd(code, hid);
        }

        return reverse;
    }
}
