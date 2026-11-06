using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerDriverPackage(
    string ProductId,
    string Name,
    string Version,
    long Size,
    string Url);

/// <summary>
/// Maps a connected Razer product to the official per-device driver package
/// published in the Synapse device bundles. A wireless dongle PID resolves to
/// the base product package, whose INF covers both hardware IDs.
/// </summary>
internal static class RazerDriverPackageCatalog
{
    private const string ResourceName =
        "OpenSynapse.Windows.Devices.RazerDriverPackages.json";

    private static readonly Lazy<CatalogData> Data = new(Load);

    internal static bool TryResolve(ushort productId, out RazerDriverPackage package)
    {
        package = null!;
        var key = productId.ToString("X4");
        var data = Data.Value;
        if (!data.Packages.TryGetValue(key, out var entry))
        {
            if (!data.Aliases.TryGetValue(key, out var baseProductId) ||
                !data.Packages.TryGetValue(baseProductId, out entry))
            {
                return false;
            }

            key = baseProductId;
        }

        package = new RazerDriverPackage(
            key,
            entry.Name,
            entry.Version,
            entry.Size,
            entry.Url);
        return true;
    }

    private static CatalogData Load()
    {
        using var stream = typeof(RazerDriverPackageCatalog).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Razer driver catalog '{ResourceName}' is missing.");
        var source = JsonSerializer.Deserialize<CatalogJson>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Razer driver catalog is empty.");

        var packages = new Dictionary<string, PackageJson>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source.Packages)
        {
            if (!Uri.TryCreate(pair.Value.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "app-assets.razer.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Razer driver package {pair.Key} has an unexpected URL.");
            }

            packages[pair.Key.ToUpperInvariant()] = pair.Value;
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source.Aliases)
        {
            if (packages.ContainsKey(pair.Value))
            {
                aliases[pair.Key.ToUpperInvariant()] = pair.Value.ToUpperInvariant();
            }
        }

        return new CatalogData(packages, aliases);
    }

    private sealed record CatalogData(
        IReadOnlyDictionary<string, PackageJson> Packages,
        IReadOnlyDictionary<string, string> Aliases);

    private sealed class CatalogJson
    {
        public Dictionary<string, PackageJson> Packages { get; init; } = [];
        public Dictionary<string, string> Aliases { get; init; } = [];
    }

    private sealed class PackageJson
    {
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public long Size { get; init; }
        public string Url { get; init; } = string.Empty;
    }
}
