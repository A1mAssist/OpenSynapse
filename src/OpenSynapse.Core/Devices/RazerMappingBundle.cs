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

public static partial class RazerMappingBundleParser
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

    private static readonly Regex InputIdObjectRegex = new(
        """\{inputID:\s*"(?<input>[^"]+)"(?:[^{}]|\{[^{}]*\})*\}""",
        RegexOptions.CultureInvariant);

    private static readonly Regex CounterRegex = new(
        """counter:\s*(?:"(?<text>[^"]*)"|(?<number>-?\d+))""",
        RegexOptions.CultureInvariant);

    private static readonly Regex FwIdRegex = new(
        """fwID:\s*(?<number>\d+)""",
        RegexOptions.CultureInvariant);

    private static readonly Regex KeyNumberRegex = new(
        """(?:^|[,{])key:\s*(?<number>\d+)""",
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
        var (buttonScope, includeInputIdOnly) = FindButtonListScope(mainJs);
        var buttons = ParseButtonObjects(buttonScope, includeInputIdOnly);
        var explicitButtonIds = ParseExplicitButtonIds(mainJs);
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
            if (button.ResolveButtonId(explicitButtonIds) is not { } buttonId || !seenButtonIds.Add(buttonId))
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
            ButtonNamesById = BuildButtonNames(buttons, explicitButtonIds),
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

    private static List<ButtonObject> ParseButtonObjects(string text, bool includeInputIdOnly)
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
                MatchText(match.Value, ButtonPosRegex),
                MatchInt(match.Value, KeyNumberRegex),
                MatchInt(match.Value, FwIdRegex)));
        }

        if (includeInputIdOnly)
        {
            foreach (Match match in InputIdObjectRegex.Matches(text))
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
                    MatchText(match.Value, ButtonPosRegex),
                    MatchInt(match.Value, KeyNumberRegex),
                    MatchInt(match.Value, FwIdRegex)));
            }
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

    private static Dictionary<byte, string> BuildButtonNames(
        List<ButtonObject> buttons,
        IReadOnlyDictionary<string, byte> explicitButtonIds)
    {
        var names = new Dictionary<byte, string>();
        foreach (var button in buttons)
        {
            if (button.ResolveButtonId(explicitButtonIds) is not { } buttonId)
            {
                continue;
            }

            names.TryAdd(buttonId, DisplayName(
                string.IsNullOrEmpty(button.Key) ? button.InputId ?? string.Empty : button.Key));
        }

        return names;
    }

    // The bundle counter is a UI sequence number, not the firmware button id.
    // Standard mouse semantics map the bundle keys onto the ids the device reports.
    private static byte? StandardMouseButtonId(string key) => key switch
    {
        "LeftClick" => 0x01,
        "RightClick" => 0x02,
        "ScrollClick" or "MiddleClick" or "ScrollButton" => 0x03,
        "MouseButton4" or "Button4" => 0x04,
        "MouseButton5" or "Button5" => 0x05,
        "MouseButton6" or "Button6" => 0x06,
        "MouseButton7" or "Button7" => 0x07,
        "MouseButton8" or "Button8" => 0x08,
        "ScrollUp" => 0x09,
        "ScrollDown" => 0x0A,
        "ScrollLeft" or "RepeatScrollLeft" => 0x34,
        "ScrollRight" or "RepeatScrollRight" => 0x35,
        "CycleUpSensitivityStages" => 0x60,
        "CycleDownSensitivityStages" => 0x61,
        _ => null,
    };

    // DKM_M_01..08 and DKM_SB_01..04 are the firmware ids the device reports,
    // independent of the bundle's UI-only counter.
    private static byte? DkmButtonId(string? inputId)
    {
        if (string.IsNullOrEmpty(inputId))
        {
            return null;
        }

        if (inputId.StartsWith("DKM_M_", StringComparison.Ordinal) &&
            int.TryParse(inputId.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var mouse))
        {
            if (mouse is >= 1 and <= 8)
            {
                return (byte)(31 + mouse);
            }

            if (mouse is > 8 and <= byte.MaxValue)
            {
                return (byte)mouse;
            }
        }

        if (inputId.StartsWith("DKM_SB_", StringComparison.Ordinal) &&
            int.TryParse(inputId.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var side))
        {
            if (side is >= 1 and <= 4)
            {
                return (byte)(79 + side);
            }

            if (side is > 4 and <= byte.MaxValue)
            {
                return (byte)side;
            }
        }

        return null;
    }

    private static string DisplayName(string key) => key switch
    {
        "LeftClick" => "Left",
        "RightClick" => "Right",
        "ScrollClick" or "MiddleClick" or "ScrollButton" => "Middle",
        "ScrollUp" => "Scroll up",
        "ScrollDown" => "Scroll down",
        "ScrollLeft" => "Scroll left",
        "ScrollRight" => "Scroll right",
        "MouseButton4" or "Button4" => "Button 4",
        "MouseButton5" or "Button5" => "Button 5",
        "MouseButton6" or "Button6" => "Button 6",
        "MouseButton7" or "Button7" => "Button 7",
        "MouseButton8" or "Button8" => "Button 8",
        "CycleUpSensitivityStages" => "DPI cycle up",
        "CycleDownSensitivityStages" => "DPI cycle down",
        "SensitivityStageUp" => "DPI +",
        "SensitivityStageDown" => "DPI -",
        "SensitivityClutch" => "Sniper",
        "CycleUpProfile" => "Profile switch",
        "RepeatScrollLeft" => "Scroll left",
        "RepeatScrollRight" => "Scroll right",
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

    private static int? MatchInt(string text, Regex regex)
    {
        var value = MatchText(text, regex);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
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
        string? ButtonPos,
        int? KeyNumber,
        int? FwId)
    {
        public byte? ResolveButtonId(IReadOnlyDictionary<string, byte> explicitButtonIds)
        {
            if (InputId is { Length: > 0 } inputId &&
                explicitButtonIds.TryGetValue(inputId, out var explicitId) &&
                explicitId > 0)
            {
                return explicitId;
            }

            if (explicitButtonIds.TryGetValue(Key, out explicitId) && explicitId > 0)
            {
                return explicitId;
            }

            if (FwId is > 0 and <= byte.MaxValue)
            {
                return (byte)FwId.Value;
            }

            if ((StandardMouseButtonId(Key) ?? StandardMouseButtonId(InputId ?? string.Empty)) is { } standard)
            {
                return standard;
            }

            if (DkmButtonId(InputId) is { } dkm)
            {
                return dkm;
            }

            if (KeyNumber is > 0 and <= byte.MaxValue)
            {
                return (byte)KeyNumber.Value;
            }

            return Hid is > 0 ? Hid : null;
        }
    }
}
