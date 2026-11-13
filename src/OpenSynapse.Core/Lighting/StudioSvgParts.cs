using System.Text;
using System.Xml;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Resolves the SVG part Synapse draws for a device. A manifest declares one part
/// per layout; payloads are inline (base64 or XML) or file names the host must read.
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

    /// <summary>
    /// Reads the part the manifest selected. The other parts are alternative
    /// layouts of the same device, not layers to stack.
    /// </summary>
    public static StudioSvgLayout? Select(
        StudioManifestDevice manifest,
        Func<string, string?>? fileResolver)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var part = manifest.SvgParts.FirstOrDefault(candidate => candidate.Key == manifest.SvgKey)
            ?? new StudioManifestSvg(
                manifest.SvgKey, manifest.SvgBase64, IsFileReference(manifest.SvgBase64));
        var payload = part.IsFile ? fileResolver?.Invoke(part.Value) : part.Value;
        return TryParse(payload);
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
