using System.Text;
using System.Xml;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Resolves every SVG part a manifest declares. Razer stores some payloads inline
/// (base64 or XML) and others as file names the host must read; both are accepted.
/// </summary>
public static class StudioSvgParts
{
    private const string Base64DataPrefix = "data:image/svg+xml;base64,";
    private const string TextDataPrefix = "data:image/svg+xml,";

    public static bool IsFileReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains('<') &&
        !value.Contains('\n') &&
        !value.Contains('\r');

    public static IReadOnlyList<StudioSvgLayout> Parse(
        StudioManifestDevice manifest,
        Func<string, string?>? fileResolver)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var parts = manifest.SvgParts.Count > 0
            ? manifest.SvgParts
            : [new StudioManifestSvg(manifest.SvgKey, manifest.SvgBase64,
                IsFileReference(manifest.SvgBase64))];

        var layouts = new List<StudioSvgLayout>(parts.Count);
        foreach (var part in parts)
        {
            var payload = part.IsFile ? fileResolver?.Invoke(part.Value) : part.Value;
            if (TryParse(payload) is { } layout) layouts.Add(layout);
        }

        return layouts;
    }

    /// <summary>Parts are authored as overlays on one canvas, so the largest extent wins.</summary>
    public static StudioSvgLayout? Merge(IReadOnlyList<StudioSvgLayout> layouts)
    {
        if (layouts.Count == 0) return null;
        if (layouts.Count == 1) return layouts[0];

        var paths = new List<StudioSvgPath>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in layouts.SelectMany(layout => layout.Paths))
        {
            if (!string.IsNullOrEmpty(path.Id) && !ids.Add(path.Id)) continue;
            paths.Add(path);
        }

        return new StudioSvgLayout(
            layouts.Max(layout => layout.Width),
            layouts.Max(layout => layout.Height),
            paths);
    }

    public static StudioSvgLayout? TryParse(string? payload)
    {
        try
        {
            var svg = Decode(payload);
            if (svg is null) return null;
            return StudioSvgLayoutParser.Parse(svg);
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or XmlException or UriFormatException)
        {
            return null;
        }
    }

    private static string? Decode(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        var value = payload.Trim();
        if (value.StartsWith(Base64DataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value[Base64DataPrefix.Length..]));
        }

        if (value.StartsWith(TextDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(value[TextDataPrefix.Length..]);
        }

        if (value.StartsWith('<')) return value;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
