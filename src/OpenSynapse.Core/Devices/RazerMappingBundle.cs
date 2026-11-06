using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenSynapse.Core.Devices;

public sealed record RazerMappingBundleAnchor(byte ButtonId, double X, double Y, bool IsLeft);

public sealed record RazerMappingBundle(
    string? MainJs,
    string? Image3x,
    double ImageWidth,
    double ImageHeight,
    IReadOnlyList<RazerMappingBundleAnchor> MouseAnchors,
    IReadOnlyDictionary<string, byte> KeyIdsByName)
{
    public IReadOnlyDictionary<byte, string> ButtonNamesById { get; init; } =
        new Dictionary<byte, string>();
}

public static class RazerMappingBundleParser
{
    private const double DefaultCanvasWidth = 310;
    private const double DefaultImageWidth = 300;
    private const double DefaultImageHeight = 340;
    private const double DefaultDotRadius = 2.5;

    private static readonly IReadOnlyDictionary<string, byte> EmptyKeyIds =
        new Dictionary<string, byte>(StringComparer.Ordinal);

    private static readonly Regex CanvasConfigRegex = new(
        """\{[^{}]*cvsW:[^{}]*\}""",
        RegexOptions.CultureInvariant);

    private static readonly Regex CanvasWidthRegex = new(
        """cvsW:\s*(?<value>-?\d+(?:\.\d+)?)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex CanvasHeightRegex = new(
        """cvsH:\s*(?<value>-?\d+(?:\.\d+)?)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex DotRadiusRegex = new(
        """dotRadius:\s*(?<value>-?\d+(?:\.\d+)?)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex DrawLineRegex = new(
        """drawLines?\(\s*(?<index>\d+)\s*,\s*(?:this\.props\.buttonList\[(?<button>\d+)\]|(?<buttonArray>[A-Za-z_$][\w$]*)\[(?<arrayButton>\d+)\])(?:\s*\|\|\s*[A-Za-z_$][\w$]*)?\s*,\s*(?<x>-?\d+(?:\.\d+)?)\s*,\s*(?<y>-?\d+(?:\.\d+)?)(?:\s*,\s*!(?<left>[01]))?\s*\)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex ButtonListAliasRegex = new(
        """(?:const|let|var)\s+(?<name>[A-Za-z_$][\w$]*)\s*=\s*this\.props\.buttonList\s*(?:\|\|\s*\[\s*\])?""",
        RegexOptions.CultureInvariant);

    private static readonly Regex ButtonObjectRegex = new(
        """\{buttonKey:"(?<key>[^"]+)"(?:[^{}]|\{[^{}]*\})*\}""",
        RegexOptions.CultureInvariant);

    private static readonly Regex CounterRegex = new(
        """counter:\s*(?:"(?<text>[^"]*)"|(?<number>-?\d+))""",
        RegexOptions.CultureInvariant);

    private static readonly Regex HidRegex = new(
        """HID:\s*(?:"(?<text>[^"]*)"|(?<number>-?\d+))""",
        RegexOptions.CultureInvariant);

    private static readonly Regex InputIdRegex = new(
        "inputID:\\s*\"(?<text>[^\"]*)\"",
        RegexOptions.CultureInvariant);

    private static readonly Regex ButtonPosRegex = new(
        "buttonPos:\\s*\"(?<text>[^\"]*)\"",
        RegexOptions.CultureInvariant);

    private static readonly Regex KeyCatalogRegex = new(
        """\{(?=[^{}]*name:"(?<name>[^"]+)")(?=[^{}]*HID:"(?<hid>[0-9]{1,3})")[^{}]*\}""",
        RegexOptions.CultureInvariant);

    private static readonly Regex Image3xKeyRegex = new(
        """^(?:\./)?static/media/prd-3x[^/]*\.(?:avif|png|webp)$""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static RazerMappingBundle Parse(string mainJs, string? image3xUrl = null)
    {
        if (string.IsNullOrEmpty(mainJs))
        {
            return new RazerMappingBundle(
                mainJs,
                image3xUrl,
                DefaultImageWidth,
                DefaultImageHeight,
                Array.Empty<RazerMappingBundleAnchor>(),
                EmptyKeyIds);
        }

        var canvasMatch = CanvasConfigRegex.Match(mainJs);
        var canvasText = canvasMatch.Success ? canvasMatch.Value : mainJs;
        var canvasWidth = ReadPositive(CanvasWidthRegex, canvasText) ??
            ReadPositive(CanvasWidthRegex, mainJs) ?? DefaultCanvasWidth;
        var canvasHeight = ReadPositive(CanvasHeightRegex, canvasText) ??
            ReadPositive(CanvasHeightRegex, mainJs) ?? DefaultImageHeight;
        var dotRadius = ReadPositive(DotRadiusRegex, canvasText) ??
            ReadPositive(DotRadiusRegex, mainJs) ?? DefaultDotRadius;
        var buttons = ParseButtons(mainJs);
        var keyIds = ParseKeyIds(mainJs, buttons);
        var anchors = new List<RazerMappingBundleAnchor>();
        var seenButtonIds = new HashSet<byte>();
        var imageOffsetX = (canvasWidth - DefaultImageWidth) / 2;
        var imageOffsetY = (canvasHeight - DefaultImageHeight) / 2;

        var buttonListAliases = new HashSet<string>(
            ButtonListAliasRegex.Matches(mainJs).Select(match => match.Groups["name"].Value),
            StringComparer.Ordinal);
        var drawLines = new List<(int Index, int MatchOffset, int ButtonIndex, double X, double Y, bool IsLeft)>();
        foreach (Match match in DrawLineRegex.Matches(mainJs))
        {
            var buttonGroup = match.Groups["button"];
            if (!buttonGroup.Success)
            {
                if (!buttonListAliases.Contains(match.Groups["buttonArray"].Value))
                {
                    continue;
                }

                buttonGroup = match.Groups["arrayButton"];
            }

            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var index) ||
                !int.TryParse(buttonGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var buttonIndex) ||
                !double.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var x) ||
                !double.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var y))
            {
                continue;
            }

            var isLeft = !match.Groups["left"].Success || match.Groups["left"].Value == "0";
            drawLines.Add((index, match.Index, buttonIndex, x, y, isLeft));
        }

        foreach (var drawLine in drawLines.OrderBy(line => line.Index).ThenBy(line => line.MatchOffset))
        {
            if (drawLine.ButtonIndex < 0 || drawLine.ButtonIndex >= buttons.Count)
            {
                continue;
            }

            var button = buttons[drawLine.ButtonIndex];
            if (button.ResolveButtonId() is not { } buttonId || !seenButtonIds.Add(buttonId))
            {
                continue;
            }

            var isLeft = button.ButtonPos switch
            {
                not null when button.ButtonPos.Equals("left", StringComparison.OrdinalIgnoreCase) => true,
                not null when button.ButtonPos.Equals("right", StringComparison.OrdinalIgnoreCase) => false,
                _ => drawLine.IsLeft,
            };

            // drawLine draws the dot at (x + 3r, y + r), not at the raw pair.
            anchors.Add(new RazerMappingBundleAnchor(
                buttonId,
                drawLine.X + (3 * dotRadius) - imageOffsetX,
                drawLine.Y + dotRadius - imageOffsetY,
                isLeft));
        }

        return new RazerMappingBundle(
            mainJs,
            image3xUrl,
            DefaultImageWidth,
            DefaultImageHeight,
            anchors,
            keyIds)
        {
            ButtonNamesById = BuildButtonNames(buttons),
        };
    }

    public static string? FindMainJs(string assetManifestJson)
    {
        using var document = ParseManifestDocument(assetManifestJson);
        if (document is null || !TryGetFiles(document, out var files))
        {
            return null;
        }

        return TryGetAssetPath(files, "main.js", out var path) ||
               TryGetAssetPath(files, "./main.js", out path)
            ? path
            : null;
    }

    public static string? FindImage3x(string assetManifestJson)
    {
        using var document = ParseManifestDocument(assetManifestJson);
        if (document is null || !TryGetFiles(document, out var files))
        {
            return null;
        }

        foreach (var property in files.EnumerateObject())
        {
            if (!Image3xKeyRegex.IsMatch(property.Name))
            {
                continue;
            }

            var path = ReadFirstString(property.Value);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return null;
    }

    private static JsonDocument? ParseManifestDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("files", out var files) &&
                files.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetFiles(JsonDocument document, out JsonElement files) =>
        document.RootElement.TryGetProperty("files", out files) &&
        files.ValueKind == JsonValueKind.Object;

    private static bool TryGetAssetPath(JsonElement files, string name, out string? path)
    {
        path = null;
        if (!files.TryGetProperty(name, out var value))
        {
            return false;
        }

        path = ReadFirstString(value);
        return !string.IsNullOrWhiteSpace(path);
    }

    private static string? ReadFirstString(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static List<ButtonObject> ParseButtons(string text)
    {
        var buttons = new List<ButtonObject>();
        var seenOffsets = new HashSet<int>();
        foreach (Match match in ButtonObjectRegex.Matches(text))
        {
            if (!seenOffsets.Add(match.Index))
            {
                continue;
            }

            buttons.Add(new ButtonObject(
                match.Groups["key"].Value,
                MatchText(match.Value, CounterRegex),
                MatchByte(match.Value, HidRegex),
                MatchText(match.Value, InputIdRegex),
                MatchText(match.Value, ButtonPosRegex)));
        }

        return buttons;
    }

    private static Dictionary<string, byte> ParseKeyIds(string text, List<ButtonObject> buttons)
    {
        var keyIds = new Dictionary<string, byte>(StringComparer.Ordinal);

        foreach (var button in buttons)
        {
            if (button.Hid is not { } hid || hid == 0)
            {
                continue;
            }

            var counterIsNumeric = byte.TryParse(button.Counter, NumberStyles.None, CultureInfo.InvariantCulture,
                out _);
            var displayName = string.IsNullOrEmpty(button.Counter) || counterIsNumeric
                ? button.Key
                : button.Counter;
            AddKey(keyIds, displayName, hid);
            AddKey(keyIds, button.InputId, hid);
        }

        foreach (Match match in KeyCatalogRegex.Matches(text))
        {
            if (!byte.TryParse(match.Groups["hid"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var hid) ||
                hid == 0)
            {
                continue;
            }

            AddKey(keyIds, match.Groups["name"].Value, hid);
            AddKey(keyIds, MatchText(match.Value, InputIdRegex), hid);
        }

        return keyIds;
    }

    private static Dictionary<byte, string> BuildButtonNames(List<ButtonObject> buttons)
    {
        var names = new Dictionary<byte, string>();
        foreach (var button in buttons)
        {
            if (button.ResolveButtonId() is not { } buttonId)
            {
                continue;
            }

            names.TryAdd(buttonId, DisplayName(button.Key));
        }

        return names;
    }

    // The bundle counter is a UI sequence number, not the firmware button id.
    // Standard mouse semantics map the bundle keys onto the ids the device reports.
    private static byte? StandardMouseButtonId(string key) => key switch
    {
        "LeftClick" => 0x01,
        "RightClick" => 0x02,
        "ScrollClick" or "MiddleClick" => 0x03,
        "MouseButton4" or "Button4" => 0x04,
        "MouseButton5" or "Button5" => 0x05,
        "MouseButton6" or "Button6" => 0x06,
        "MouseButton7" or "Button7" => 0x07,
        "MouseButton8" or "Button8" => 0x08,
        "ScrollUp" => 0x09,
        "ScrollDown" => 0x0A,
        "CycleUpSensitivityStages" => 0x60,
        "CycleDownSensitivityStages" => 0x61,
        _ => null,
    };

    private static string DisplayName(string key) => key switch
    {
        "LeftClick" => "Left",
        "RightClick" => "Right",
        "ScrollClick" or "MiddleClick" => "Middle",
        "ScrollUp" => "Scroll up",
        "ScrollDown" => "Scroll down",
        "MouseButton4" or "Button4" => "Button 4",
        "MouseButton5" or "Button5" => "Button 5",
        "MouseButton6" or "Button6" => "Button 6",
        "MouseButton7" or "Button7" => "Button 7",
        "MouseButton8" or "Button8" => "Button 8",
        "CycleUpSensitivityStages" => "DPI cycle up",
        "CycleDownSensitivityStages" => "DPI cycle down",
        _ => key,
    };

    private static double? ReadPositive(Regex regex, string text)
    {
        var value = ReadNumber(regex.Match(text));
        return value is > 0 ? value : null;
    }

    private static void AddKey(Dictionary<string, byte> keyIds, string? name, byte hid)
    {
        if (!string.IsNullOrEmpty(name))
        {
            keyIds.TryAdd(name, hid);
        }
    }

    private static string? MatchText(string text, Regex regex)
    {
        var match = regex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["text"].Success
            ? match.Groups["text"].Value
            : match.Groups["number"].Value;
    }

    private static byte? MatchByte(string text, Regex regex)
    {
        var value = MatchText(text, regex);
        return byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadNumber(Match match)
    {
        if (!match.Success)
        {
            return null;
        }

        return double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private sealed record ButtonObject(
        string Key,
        string? Counter,
        byte? Hid,
        string? InputId,
        string? ButtonPos)
    {
        public byte? ResolveButtonId()
        {
            if (StandardMouseButtonId(Key) is { } standard)
            {
                return standard;
            }

            if (byte.TryParse(Counter, NumberStyles.None, CultureInfo.InvariantCulture, out var counter) &&
                counter > 0)
            {
                return counter;
            }

            return Hid is > 0 ? Hid : null;
        }
    }
}