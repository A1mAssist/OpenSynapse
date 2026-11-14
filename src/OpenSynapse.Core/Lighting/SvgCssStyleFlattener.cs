using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Moves simple class-based CSS rules from an SVG style element onto the matching
/// elements as presentation attributes. WinUI's SvgImageSource does not reliably
/// apply style-sheet classes, while it does honor fill/stroke attributes.
/// </summary>
public static class SvgCssStyleFlattener
{
    private const string Important = "!important";
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private static readonly Regex CssRulePattern = new(
        @"(?<selectors>[^{}]+)\{(?<body>[^}]*)\}",
        RegexOptions.CultureInvariant);

    private static readonly Regex DeclarationPattern = new(
        @"(?<property>[A-Za-z-]+)\s*:\s*(?<value>[^;]+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex ClassSelectorPattern = new(
        @"^\.[A-Za-z_][A-Za-z0-9_-]*$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SupportedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "fill",
        "fill-opacity",
        "fill-rule",
        "stroke",
        "stroke-opacity",
        "stroke-width",
        "stroke-miterlimit",
        "stroke-linecap",
        "stroke-linejoin",
        "stroke-dasharray",
        "stroke-dashoffset",
        "opacity",
    };

    /// <returns>The flattened SVG, or null when it cannot be flattened safely.</returns>
    public static string? Flatten(string svg)
    {
        if (string.IsNullOrWhiteSpace(svg)) return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }

        var styles = document.Root is null
            ? []
            : document.Root.DescendantsAndSelf()
                .Where(element => element.Name.LocalName == "style" && IsSvgElement(element))
                .ToList();
        if (styles.Count == 0) return null;

        var rules = new List<CssRule>();
        foreach (var style in styles)
        {
            var parsed = ParseCss(style.Value);
            if (parsed is null)
            {
                if (!string.IsNullOrWhiteSpace(style.Value)) return null;
                continue;
            }
            rules.AddRange(parsed);
        }
        if (rules.Count == 0) return null;

        var applied = false;
        foreach (var element in document.Root!.DescendantsAndSelf().Where(IsSvgElement))
        {
            var classes = (string?)element.Attribute("class");
            if (string.IsNullOrWhiteSpace(classes)) continue;

            var tokens = classes.Split(
                [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var matching = rules
                .Where(rule => tokens.Contains(rule.Selector, StringComparer.Ordinal))
                .ToList();
            if (matching.Count == 0) continue;

            applied = true;
            var inline = InlineDeclarations((string?)element.Attribute("style"));
            foreach (var (property, value) in inline)
            {
                element.SetAttributeValue(property, value);
            }

            foreach (var rule in matching)
            {
                foreach (var (property, value) in rule.Declarations)
                {
                    if (inline.ContainsKey(property)) continue;
                    element.SetAttributeValue(property, value);
                }
            }
        }

        if (!applied) return null;
        foreach (var style in styles) style.Remove();
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static List<CssRule>? ParseCss(string css)
    {
        if (string.IsNullOrWhiteSpace(css)) return null;

        // At-rules, comments and imports cannot be moved onto elements safely.
        if (CssRulePattern.Replace(css, string.Empty).Trim().Length > 0) return null;

        var rules = new List<CssRule>();
        foreach (Match match in CssRulePattern.Matches(css))
        {
            var declarations = ParseDeclarations(match.Groups["body"].Value);
            if (declarations is null) return null;

            foreach (var raw in match.Groups["selectors"].Value.Split(','))
            {
                var selector = raw.Trim();
                if (!ClassSelectorPattern.IsMatch(selector)) return null;
                rules.Add(new CssRule(selector[1..], declarations));
            }
        }

        return rules.Count == 0 ? null : rules;
    }

    private static IReadOnlyList<KeyValuePair<string, string>>? ParseDeclarations(string body)
    {
        var declarations = new List<KeyValuePair<string, string>>();
        foreach (Match match in DeclarationPattern.Matches(body))
        {
            var property = match.Groups["property"].Value.ToLowerInvariant();
            if (!SupportedProperties.Contains(property)) return null;

            var value = CleanValue(match.Groups["value"].Value);
            if (value is null) return null;

            declarations.Add(new KeyValuePair<string, string>(property, value));
        }

        return declarations.Count == 0 ? null : declarations;
    }

    private static Dictionary<string, string> InlineDeclarations(string? style)
    {
        var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style)) return declarations;
        foreach (Match match in DeclarationPattern.Matches(style))
        {
            var property = match.Groups["property"].Value.ToLowerInvariant();
            if (!SupportedProperties.Contains(property)) continue;
            var value = CleanValue(match.Groups["value"].Value);
            if (value is not null) declarations[property] = value;
        }
        return declarations;
    }

    private static string? CleanValue(string raw)
    {
        var value = raw.Trim();
        if (value.EndsWith(Important, StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^Important.Length].TrimEnd();
        }
        if (value.Length == 0 ||
            value.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("var(", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("expression(", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return value;
    }

    private static bool IsSvgElement(XElement element) =>
        element.Name.NamespaceName.Length == 0 || element.Name.NamespaceName == SvgNamespace;

    private sealed record CssRule(
        string Selector,
        IReadOnlyList<KeyValuePair<string, string>> Declarations);
}
