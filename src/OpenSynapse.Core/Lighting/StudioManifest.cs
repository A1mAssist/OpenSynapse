using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

/// <summary>One SVG entry: inline payload text or a file name the host must resolve.</summary>
public sealed record StudioManifestSvg(string Key, string Value, bool IsFile);

/// <summary>A Chroma lighting manifest for one product edition.</summary>
public sealed record StudioManifestDevice(
    ushort ProductId,
    int EditionId,
    string? ProductName,
    bool IsStudioSupported,
    string SvgKey,
    string SvgBase64,
    string LedConfigJson,
    string? ETag,
    DateTimeOffset? LastModified,
    ushort WiredProductId = 0,
    ushort DongleProductId = 0)
{
    /// <summary>Razer device category, for example KEYBOARD, KEYPAD or MOUSE.</summary>
    public string? Category { get; init; }

    /// <summary>
    /// Every declared SVG part. Older manifests inline base64; newer ones name
    /// external files, and some mix both within one device.
    /// </summary>
    public IReadOnlyList<StudioManifestSvg> SvgParts { get; init; } = [];

    /// <summary>Product folder that owns the Synapse cover art.</summary>
    public ushort CoverProductId => WiredProductId != 0 ? WiredProductId : ProductId;
}

/// <summary>The manifest plus its source and any reason it came from cache.</summary>
public sealed record StudioManifestResult(
    StudioManifestDevice? Manifest,
    string? SourceUrl,
    bool FromCache,
    string? Error);

internal static class StudioManifestParser
{
    public static StudioManifestDevice? Parse(
        string json,
        ushort requestedProductId,
        int editionId,
        string? etag,
        DateTimeOffset? lastModified)
    {
        try
        {
            using var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var productId = requestedProductId;
            if (root.TryGetProperty("productId", out var productIdElement) &&
                productIdElement.TryGetUInt16(out var declaredProductId))
            {
                productId = declaredProductId;
            }

            var wiredProductId = ReadUInt16(root, "wiredProductId");
            var dongleProductId = ReadUInt16(root, "dongleProductId");

            var productName = root.TryGetProperty("productName", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;

            var category = root.TryGetProperty("category", out var categoryElement) &&
                categoryElement.ValueKind == JsonValueKind.String
                    ? categoryElement.GetString()
                    : null;

            var isStudioSupported = root.TryGetProperty("isChromaStudioSupported", out var supportedElement) &&
                supportedElement.ValueKind == JsonValueKind.True;

            var svgParts = ReadSvgParts(root);
            var (svgKey, svgBase64) = SelectPrimarySvg(svgParts, editionId, category);

            var ledConfigJson = root.TryGetProperty("ledConfig", out var ledConfigElement)
                ? ledConfigElement.GetRawText()
                : string.Empty;

            return new StudioManifestDevice(
                productId,
                editionId,
                productName,
                isStudioSupported,
                svgKey,
                svgBase64,
                ledConfigJson,
                etag,
                lastModified,
                wiredProductId,
                dongleProductId)
            {
                Category = category,
                SvgParts = svgParts,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ushort ReadUInt16(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.TryGetUInt16(out var value)
            ? value
            : (ushort)0;

    private static IReadOnlyList<StudioManifestSvg> ReadSvgParts(JsonElement root)
    {
        if (!root.TryGetProperty("svg", out var svgElement) ||
            svgElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var parts = new List<StudioManifestSvg>();
        foreach (var property in svgElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(value)) continue;
            parts.Add(new StudioManifestSvg(property.Name, value, StudioSvgParts.IsFileReference(value)));
        }

        return parts;
    }

    private static (string Key, string Value) SelectPrimarySvg(
        IReadOnlyList<StudioManifestSvg> parts,
        int editionId,
        string? category)
    {
        if (parts.Count == 0) return (string.Empty, string.Empty);

        // Synapse falls back to layout 1 on keyboards and layout 0 elsewhere when the
        // device does not report a layout; a manifest declares one part per layout.
        var isKeyboard = string.Equals(category, "KEYBOARD", StringComparison.OrdinalIgnoreCase);
        foreach (var layout in new[] { isKeyboard ? 1 : 0, isKeyboard ? 0 : 1 })
        {
            var key = FormattableString.Invariant($"svg_{editionId}_{layout}");
            var part = parts.FirstOrDefault(candidate => candidate.Key == key);
            if (part is not null) return (part.Key, part.Value);
        }

        return (parts[0].Key, parts[0].Value);
    }
}
