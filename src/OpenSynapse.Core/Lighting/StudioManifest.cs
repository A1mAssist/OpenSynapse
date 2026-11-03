using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

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
    DateTimeOffset? LastModified);

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

            var productName = root.TryGetProperty("productName", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString()
                    : null;

            var isStudioSupported = root.TryGetProperty("isChromaStudioSupported", out var supportedElement) &&
                supportedElement.ValueKind == JsonValueKind.True;

            var (svgKey, svgBase64) = ReadSvg(root, editionId);

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
                lastModified);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string Key, string Base64) ReadSvg(JsonElement root, int editionId)
    {
        if (!root.TryGetProperty("svg", out var svgElement) ||
            svgElement.ValueKind != JsonValueKind.Object)
        {
            return (string.Empty, string.Empty);
        }

        var preferredKey = FormattableString.Invariant($"svg_{editionId}_1");
        if (svgElement.TryGetProperty(preferredKey, out var preferred) &&
            preferred.ValueKind == JsonValueKind.String)
        {
            return (preferredKey, preferred.GetString() ?? string.Empty);
        }

        foreach (var property in svgElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                return (property.Name, property.Value.GetString() ?? string.Empty);
            }
        }

        return (string.Empty, string.Empty);
    }
}
