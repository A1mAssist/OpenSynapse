using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// How a DeviceManifest SVG path takes part in the studio canvas.
/// </summary>
public enum StudioSvgRole
{
    Product,
    Led,
    Selection,
    Lettering,
    Other,
}

/// <summary>One renderable shape, in document order.</summary>
public sealed record StudioSvgPath(
    string Id,
    string Data,
    string? Fill,
    string? Stroke,
    double Opacity,
    StudioSvgRole Role,
    int? Row,
    int? Col,
    string? KeyName,
    string? SelectionGroup);

/// <summary>Flat render data for a single device SVG.</summary>
public sealed record StudioSvgLayout(
    double Width,
    double Height,
    IReadOnlyList<StudioSvgPath> Paths);

/// <summary>
/// Flattens a Chroma DeviceManifest SVG into render data. Basic shapes become
/// paths, and group or element transforms are baked into the path coordinates.
/// use/image/filter/mask/text are intentionally not rendered.
/// </summary>
public static class StudioSvgLayoutParser
{
    private const double DefaultSize = 1000;

    private static readonly Regex CssRulePattern = new(
        @"(?<selectors>[^{}]+)\{(?<body>[^}]*)\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex DeclarationPattern = new(
        @"(?<property>[A-Za-z-]+)\s*:\s*(?<value>[^;]+)",
        RegexOptions.CultureInvariant);

    // Some catalog payloads start with a newline or a byte order mark before the XML declaration.
    private static readonly char[] LeadingNoise = ['\uFEFF', ' ', '\t', '\r', '\n'];

    public static StudioSvgLayout Parse(string svg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svg);
        var text = svg.TrimStart(LeadingNoise);
        try
        {
            var head = ReadHead(text);
            return new StudioSvgLayout(head.Width, head.Height, ReadPaths(text, head.Rules));
        }
        catch (XmlException ex)
        {
            throw new ArgumentException("The SVG payload is not well-formed XML.", nameof(svg), ex);
        }
    }

    private static Head ReadHead(string text)
    {
        var rules = new List<CssRule>();
        var foundRoot = false;
        var width = 0d;
        var height = 0d;

        using var reader = CreateReader(text);
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (!foundRoot)
                {
                    if (reader.Depth != 0 || reader.LocalName != "svg")
                    {
                        throw new ArgumentException("The SVG root element is missing.", "svg");
                    }

                    foundRoot = true;
                    width = ReadLength(reader.GetAttribute("width"));
                    height = ReadLength(reader.GetAttribute("height"));
                    if (width <= 0 || height <= 0)
                    {
                        var (viewWidth, viewHeight) = ReadViewBox(reader.GetAttribute("viewBox"));
                        if (width <= 0) width = viewWidth;
                        if (height <= 0) height = viewHeight;
                    }
                }
                else if (reader.LocalName == "style")
                {
                    rules.AddRange(ParseCss(reader.ReadElementContentAsString()));
                    continue;
                }
            }

            reader.Read();
        }

        if (!foundRoot)
        {
            throw new ArgumentException("The SVG root element is missing.", "svg");
        }

        return new Head(
            width > 0 ? width : DefaultSize,
            height > 0 ? height : DefaultSize,
            rules);
    }

    private static List<StudioSvgPath> ReadPaths(string text, List<CssRule> rules)
    {
        var paths = new List<StudioSvgPath>();
        var groups = new Stack<Group>();
        var skipDepth = int.MaxValue;

        using var reader = CreateReader(text);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.Depth == skipDepth)
                {
                    skipDepth = int.MaxValue;
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element) continue;

            // Groups end at the next element at the same or a lower depth; no end-tag bookkeeping needed.
            while (groups.Count > 0 && groups.Peek().Depth >= reader.Depth)
            {
                groups.Pop();
            }

            if (reader.Depth > skipDepth) continue;

            if (IsHiddenContainer(reader.LocalName))
            {
                // Referenced paint servers and text content are not drawn where they are defined.
                if (!reader.IsEmptyElement) skipDepth = reader.Depth;
                continue;
            }

            var inherited = groups.Count > 0
                ? groups.Peek()
                : new Group(0, StudioSvgRole.Other, SvgMatrix.Identity, null, null, null, null);
            if (reader.LocalName == "g")
            {
                var own = RoleOf(reader.GetAttribute("id"));
                if (own == StudioSvgRole.Other) own = RoleOf(reader.GetAttribute("class"));
                groups.Push(new Group(
                    reader.Depth,
                    own == StudioSvgRole.Other ? inherited.Role : own,
                    inherited.Transform.Multiply(SvgMatrix.Parse(reader.GetAttribute("transform"))),
                    ReadInt(reader.GetAttribute("data-row")) ?? inherited.Row,
                    ReadInt(reader.GetAttribute("data-col")) ?? inherited.Col,
                    reader.GetAttribute("data-assumed-key-name") ?? inherited.KeyName,
                    reader.GetAttribute("data-selection-group") ?? inherited.SelectionGroup));
            }
            else if (IsDrawable(reader.LocalName))
            {
                if (CreatePath(reader, inherited, rules) is { } path) paths.Add(path);
            }
        }

        return paths;
    }

    private static bool IsHiddenContainer(string name) =>
        name is "defs" or "mask" or "clipPath" or "filter" or "symbol" or "pattern" or "marker" or "text" or "foreignObject";

    private static bool IsDrawable(string name) =>
        name is "path" or "rect" or "circle" or "ellipse" or "line" or "polygon" or "polyline";

    private static StudioSvgPath? CreatePath(XmlReader reader, Group inherited, List<CssRule> rules)
    {
        var classes = reader.GetAttribute("class");
        string? fill = null;
        string? stroke = null;
        double? opacity = null;

        if (!string.IsNullOrWhiteSpace(classes))
        {
            var tokens = classes.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var rule in rules)
            {
                if (!tokens.Contains(rule.Selector, StringComparer.Ordinal)) continue;
                fill = rule.Fill ?? fill;
                stroke = rule.Stroke ?? stroke;
                opacity = rule.Opacity ?? opacity;
            }
        }

        // CSS cascade order: stylesheet rules, then presentation attributes, then the style attribute.
        fill = NormalizePaint(reader.GetAttribute("fill")) ?? fill;
        stroke = NormalizePaint(reader.GetAttribute("stroke")) ?? stroke;
        opacity = ReadDouble(reader.GetAttribute("opacity")) ?? opacity;
        ApplyDeclarations(reader.GetAttribute("style"), ref fill, ref stroke, ref opacity);

        var data = reader.LocalName == "path"
            ? reader.GetAttribute("d")
            : SvgShapePath.Create(reader);
        if (string.IsNullOrWhiteSpace(data)) return null;

        var transform = inherited.Transform.Multiply(SvgMatrix.Parse(reader.GetAttribute("transform")));
        // A child can carry a more specific role (characters/key) than its product group.
        var ownRole = RoleOf(classes);
        if (ownRole == StudioSvgRole.Other)
        {
            ownRole = RoleOf(reader.GetAttribute("id"));
        }

        var role = ownRole != StudioSvgRole.Other ? ownRole : inherited.Role;
        return new StudioSvgPath(
            reader.GetAttribute("id") ?? string.Empty,
            SvgPathData.Transform(data, transform),
            Painted(fill),
            Painted(stroke),
            opacity ?? 1d,
            role,
            ReadInt(reader.GetAttribute("data-row")) ?? inherited.Row,
            ReadInt(reader.GetAttribute("data-col")) ?? inherited.Col,
            reader.GetAttribute("data-assumed-key-name") ?? inherited.KeyName,
            reader.GetAttribute("data-selection-group") ?? inherited.SelectionGroup);
    }

    private static List<CssRule> ParseCss(string css)
    {
        var rules = new List<CssRule>();
        foreach (Match rule in CssRulePattern.Matches(css))
        {
            string? fill = null;
            string? stroke = null;
            double? opacity = null;
            ApplyDeclarations(rule.Groups["body"].Value, ref fill, ref stroke, ref opacity);
            foreach (var selector in SelectorNames(rule.Groups["selectors"].Value))
            {
                rules.Add(new CssRule(selector, fill, stroke, opacity));
            }
        }

        return rules;
    }

    // Handles comma grouped selectors and ignores type or attribute selectors.
    private static IEnumerable<string> SelectorNames(string selectors)
    {
        foreach (var raw in selectors.Split(','))
        {
            var token = raw.Trim();
            var dot = token.LastIndexOf('.');
            if (dot < 0 || dot == token.Length - 1) continue;

            var name = token[(dot + 1)..].Trim();
            var end = name.IndexOfAny([':', ' ', '>', '+', '~', '[', ']']);
            if (end >= 0) name = name[..end];
            if (name.Length > 0) yield return name;
        }
    }

    private static void ApplyDeclarations(string? declarations, ref string? fill, ref string? stroke, ref double? opacity)
    {
        if (string.IsNullOrWhiteSpace(declarations)) return;

        foreach (Match declaration in DeclarationPattern.Matches(declarations))
        {
            var value = declaration.Groups["value"].Value.Trim();
            switch (declaration.Groups["property"].Value.ToLowerInvariant())
            {
                case "fill":
                    fill = NormalizePaint(value);
                    break;
                case "stroke":
                    stroke = NormalizePaint(value);
                    break;
                case "opacity":
                    opacity = ReadDouble(value) ?? opacity;
                    break;
            }
        }
    }

    private static StudioSvgRole RoleOf(string? token)
    {
        if (string.IsNullOrEmpty(token)) return StudioSvgRole.Other;
        if (token.Contains("selection", StringComparison.OrdinalIgnoreCase)) return StudioSvgRole.Selection;
        if (token.Contains("led", StringComparison.OrdinalIgnoreCase)) return StudioSvgRole.Led;
        if (token.Contains("lettering", StringComparison.OrdinalIgnoreCase) ||
            token.Contains("characters", StringComparison.OrdinalIgnoreCase)) return StudioSvgRole.Lettering;
        if (token.Contains("product", StringComparison.OrdinalIgnoreCase)) return StudioSvgRole.Product;
        return StudioSvgRole.Other;
    }

    private static string? NormalizePaint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 4 && trimmed[0] == '#'
            ? $"#{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}"
            : trimmed;
    }

    private static string? Painted(string? value) =>
        value is null ||
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static double ReadLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var trimmed = value.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^2];
        return ReadDouble(trimmed) is { } parsed && parsed > 0 ? parsed : 0;
    }

    private static (double Width, double Height) ReadViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (0, 0);
        var parts = value.Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return (0, 0);
        var width = ReadDouble(parts[2]) ?? 0;
        var height = ReadDouble(parts[3]) ?? 0;
        return (width > 0 ? width : 0, height > 0 ? height : 0);
    }

    private static double? ReadDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static int? ReadInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static XmlReader CreateReader(string text)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = true,
        };

        return XmlReader.Create(new StringReader(text), settings);
    }

    private sealed record Head(double Width, double Height, List<CssRule> Rules);

    private readonly record struct Group(
        int Depth,
        StudioSvgRole Role,
        SvgMatrix Transform,
        int? Row,
        int? Col,
        string? KeyName,
        string? SelectionGroup);

    private sealed record CssRule(string Selector, string? Fill, string? Stroke, double? Opacity);
}
