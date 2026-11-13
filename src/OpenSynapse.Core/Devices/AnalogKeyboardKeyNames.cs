using System.Globalization;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Maps the Chroma Studio keyboard SVG key names to the middleware analog-key ids
/// used by the per-key actuation commands (bundle 2291, product 0x02A7).
/// </summary>
public static class AnalogKeyboardKeyNames
{
    private static readonly IReadOnlyDictionary<string, byte> KeyIds = Build();

    public static bool TryGet(string keyName, out byte keyId)
    {
        if (KeyIds.TryGetValue(keyName, out keyId))
        {
            return true;
        }

        return KeyIds.TryGetValue(Normalize(keyName), out keyId);
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IReadOnlyDictionary<string, byte> Build()
    {
        var map = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = 110,
            ["Tilde"] = 1,
            ["Dash"] = 12,
            ["Equal"] = 13,
            ["Backspace"] = 15,
            ["Tab"] = 16,
            ["StartSquareBracket"] = 27,
            ["EndSquareBracket"] = 28,
            ["Backslash"] = 29,
            ["Caps"] = 30,
            ["SemiColon"] = 40,
            ["Apostrophe"] = 41,
            ["Enter"] = 43,
            ["Comma"] = 53,
            ["Dot"] = 54,
            ["ForwardSlash"] = 55,
            ["Space"] = 61,
            ["PrintScreen"] = 124,
            ["ScrollLock"] = 125,
            ["PauseBreak"] = 126,
            ["Insert"] = 75,
            ["Home"] = 80,
            ["PageUp"] = 85,
            ["Delete"] = 76,
            ["End"] = 81,
            ["PageDown"] = 86,
            ["UpArrow"] = 83,
            ["LeftArrow"] = 79,
            ["DownArrow"] = 84,
            ["RightArrow"] = 89,
            ["LeftCtrl"] = 58,
            ["LeftShift"] = 44,
            ["LeftAlt"] = 60,
            ["Window"] = 127,
            ["RightCtrl"] = 64,
            ["RightShift"] = 57,
            ["RightAlt"] = 62,
            ["Menu"] = 129,
            ["NumPad"] = 90,
            ["NumPadForwardSlash"] = 95,
            ["NumPadAsterisk"] = 100,
            ["NumPadMinus"] = 105,
            ["NumPadPlus"] = 106,
            ["NumPadEnter"] = 108,
            ["NumPadDot"] = 104,
        };

        for (var index = 0; index < 10; index++)
        {
            map[index.ToString(CultureInfo.InvariantCulture)] = (byte)(2 + index);
        }

        for (var index = 0; index < 26; index++)
        {
            map[((char)('A' + index)).ToString()] = index switch
            {
                0 => 31,
                1 => 50,
                2 => 48,
                3 => 33,
                4 => 19,
                5 => 34,
                6 => 35,
                7 => 36,
                8 => 24,
                9 => 37,
                10 => 38,
                11 => 39,
                12 => 52,
                13 => 51,
                14 => 25,
                15 => 26,
                16 => 17,
                17 => 20,
                18 => 32,
                19 => 21,
                20 => 23,
                21 => 49,
                22 => 18,
                23 => 47,
                24 => 22,
                _ => 46,
            };
        }

        for (var index = 0; index < 12; index++)
        {
            map[$"F{index + 1}"] = (byte)(112 + index);
        }

        for (var index = 0; index <= 9; index++)
        {
            map[$"NumPad{index}"] = index switch
            {
                0 => 99,
                1 => 93,
                2 => 98,
                3 => 103,
                4 => 92,
                5 => 97,
                6 => 102,
                7 => 91,
                8 => 96,
                _ => 101,
            };
        }

        foreach (var pair in map.ToArray())
        {
            map[Normalize(pair.Key)] = pair.Value;
        }

        return map;
    }
}
