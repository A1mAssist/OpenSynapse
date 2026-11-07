using System.Text.Json;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

internal sealed partial class ChromaRestHost
{
    private static JsonElement RequireObject(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException();
        }
        return element;
    }

    private static (int Code, ParsedFrame? Frame) ParseFrame(
        ChromaDeviceKind kind,
        JsonElement root,
        string? effect)
    {
        switch (effect?.ToUpperInvariant())
        {
            case "CHROMA_NONE":
                return (Success, new ParsedFrame([default], 1, 1, kind));
            case "CHROMA_STATIC":
            case "CHROMA_STATIC2":
            {
                var staticParam = RequireObject(root, "param");
                var staticColor = ChromaKeyboardFrameMapper.ToRgb(ParseColor(staticParam, "color"));
                return (Success, new ParsedFrame([staticColor], 1, 1, kind));
            }
            case "CHROMA_CUSTOM" when kind == ChromaDeviceKind.Keyboard:
            {
                var custom = RequireObject(root, "param");
                var customMatrix = ParseMatrix(custom, 6, 22);
                return (Success, new ParsedFrame(
                    ToSourceFrame(customMatrix),
                    6,
                    22,
                    kind,
                    ChromaKeyboardFrameMapper.Custom(customMatrix)));
            }
            case "CHROMA_CUSTOM2" when kind == ChromaDeviceKind.Keyboard:
            {
                var custom2 = RequireObject(root, "param");
                var custom2Colors = ParseMatrix(custom2, "color", 8, 24);
                var custom2Keys = ParseMatrix(custom2, "key", 6, 22);
                var custom2Source = ToSourceFrame(custom2Colors);
                ApplySourceKeyOverrides(custom2Source, custom2Keys);
                return (Success, new ParsedFrame(
                    custom2Source,
                    6,
                    22,
                    kind,
                    ChromaKeyboardFrameMapper.Custom2Key(custom2Colors, custom2Keys)));
            }
            case "CHROMA_CUSTOM_KEY" when kind == ChromaDeviceKind.Keyboard:
            {
                var parameter = RequireObject(root, "param");
                var customKeyColors = ParseMatrix(parameter, "color", 6, 22);
                var customKeyKeys = ParseMatrix(parameter, "key", 6, 22);
                var customKeySource = ToSourceFrame(customKeyColors);
                ApplySourceKeyOverrides(customKeySource, customKeyKeys);
                return (Success, new ParsedFrame(
                    customKeySource,
                    6,
                    22,
                    kind,
                    ChromaKeyboardFrameMapper.CustomKey(customKeyColors, customKeyKeys)));
            }
            case "CHROMA_CUSTOM":
            {
                var custom = RequireObject(root, "param");
                return kind switch
                {
                    ChromaDeviceKind.Mouse => CustomColors(custom, 30, kind),
                    ChromaDeviceKind.Headset => CustomColors(custom, 5, kind),
                    ChromaDeviceKind.Mousepad => CustomColors(custom, 15, kind),
                    ChromaDeviceKind.ChromaLink => CustomColors(custom, 5, kind),
                    ChromaDeviceKind.Keypad => CustomMatrix(custom, 4, 5, kind),
                    _ => (NotSupported, null),
                };
            }
            case "CHROMA_CUSTOM2":
            {
                var custom2 = RequireObject(root, "param");
                return kind switch
                {
                    ChromaDeviceKind.Mouse => CustomMatrix(custom2, 9, 7, kind),
                    ChromaDeviceKind.Mousepad => CustomColors(custom2, 20, kind),
                    _ => (NotSupported, null),
                };
            }
            default:
                return (NotSupported, null);
        }
    }

    private static (int Code, ParsedFrame? Frame) CustomColors(
        JsonElement parameter,
        int count,
        ChromaDeviceKind kind)
    {
        var colors = ParseColors(parameter, count);
        return (Success, new ParsedFrame(
            colors.Select(ChromaKeyboardFrameMapper.ToRgb).ToArray(),
            1,
            count,
            kind));
    }

    private static (int Code, ParsedFrame? Frame) CustomMatrix(
        JsonElement parameter,
        int rows,
        int columns,
        ChromaDeviceKind kind)
    {
        var matrix = ParseMatrix(parameter, rows, columns);
        return (Success, new ParsedFrame(
            matrix.SelectMany(row => row).Select(ChromaKeyboardFrameMapper.ToRgb).ToArray(),
            rows,
            columns,
            kind));
    }

    private static RazerRgb[] ToSourceFrame(IReadOnlyList<IReadOnlyList<uint>> matrix)
    {
        var frame = new RazerRgb[6 * 22];
        var rowOffset = matrix.Count == 8 ? 1 : 0;
        var columnOffset = matrix[0].Count == 24 ? 1 : 0;
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 22; column++)
            {
                frame[row * 22 + column] = ChromaKeyboardFrameMapper.ToRgb(
                    matrix[row + rowOffset][column + columnOffset]);
            }
        }
        return frame;
    }

    private static void ApplySourceKeyOverrides(
        RazerRgb[] frame,
        IReadOnlyList<IReadOnlyList<uint>> keys)
    {
        const uint keyActiveMask = 0x01000000;
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 22; column++)
            {
                var encoded = keys[row][column];
                if ((encoded & keyActiveMask) != 0)
                {
                    frame[row * 22 + column] = ChromaKeyboardFrameMapper.ToRgb(
                        (~encoded) & 0x00FFFFFFu);
                }
            }
        }
    }

    private static uint ParseColor(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetUInt32(out var value) ||
            value > 0x00FF_FFFFu)
        {
            throw new InvalidOperationException();
        }
        return value;
    }

    private static List<uint> ParseColors(JsonElement parent, int expected)
    {
        if (!parent.TryGetProperty("color", out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException();
        }

        var colors = new List<uint>(expected);
        if (element.GetArrayLength() > 0 && element[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var row in element.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) throw new InvalidOperationException();
                foreach (var item in row.EnumerateArray())
                {
                    colors.Add(ParseColor(item));
                }
            }
        }
        else
        {
            foreach (var item in element.EnumerateArray())
            {
                colors.Add(ParseColor(item));
            }
        }
        if (colors.Count != expected) throw new InvalidOperationException();
        return colors;
    }

    private static uint ParseColor(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetUInt32(out var value) ||
            value > 0x00FF_FFFFu)
        {
            throw new InvalidOperationException();
        }
        return value;
    }

    private static List<List<uint>> ParseMatrix(JsonElement parent, string propertyName, int rows, int columns)
    {
        if (!parent.TryGetProperty(propertyName, out var element))
        {
            throw new InvalidOperationException();
        }
        return ParseMatrix(element, rows, columns);
    }

    private static List<List<uint>> ParseMatrix(JsonElement element, int rows, int columns)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != rows)
        {
            throw new InvalidOperationException();
        }

        var matrix = new List<List<uint>>();
        foreach (var row in element.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != columns)
            {
                throw new InvalidOperationException();
            }

            var values = new List<uint>();
            foreach (var value in row.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out var color))
                {
                    throw new InvalidOperationException();
                }
                values.Add(color);
            }
            matrix.Add(values);
        }
        return matrix;
    }
}
