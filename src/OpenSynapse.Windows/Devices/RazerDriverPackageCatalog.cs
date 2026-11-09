using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerDriverPackage(
    string ProductId,
    string InstallProductId,
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
            if (data.Aliases.TryGetValue(key, out var baseProductId) ||
                data.InstallPackages.TryGetValue(key, out baseProductId))
            {
                entry = data.Packages[baseProductId];
                key = baseProductId;
            }
            else
            {
                return false;
            }
        }

        package = new RazerDriverPackage(
            key,
            entry.InstallProductId,
            entry.Name,
            entry.Version,
            entry.Size,
            entry.Url);
        return true;
    }

    /// <summary>
    /// A connected wireless dongle, the catalog key and the PID in the official
    /// installer file name are three different identities. Every install-state,
    /// INF and service lookup must try all of them.
    /// </summary>
    internal static IReadOnlyList<ushort> GetCandidateProductIds(
        ushort connectedProductId,
        RazerDriverPackage package)
    {
        var candidates = new List<ushort>(3);
        Add(connectedProductId);
        Add(Parse(package.ProductId));
        Add(Parse(package.InstallProductId));
        return candidates;

        void Add(ushort? productId)
        {
            if (productId is { } value && !candidates.Contains(value))
            {
                candidates.Add(value);
            }
        }

        static ushort? Parse(string value) =>
            ushort.TryParse(
                value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var productId)
                ? productId
                : null;
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
            if (!ushort.TryParse(
                    pair.Key,
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out _) ||
                pair.Key.Length != 4 ||
                string.IsNullOrWhiteSpace(pair.Value.Name) ||
                string.IsNullOrWhiteSpace(pair.Value.Version) ||
                pair.Value.Size <= 0 ||
                !Uri.TryCreate(pair.Value.Url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, "app-assets.razer.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Razer driver package {pair.Key} has invalid metadata.");
            }

            pair.Value.InstallProductId = ResolveInstallProductId(pair.Value.Url);
            if (pair.Value.InstallProductId.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Razer driver package {pair.Key} has no PID in its installer file name.");
            }

            packages[pair.Key.ToUpperInvariant()] = pair.Value;
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source.Aliases)
        {
            if (pair.Key.Length != 4 ||
                pair.Value.Length != 4 ||
                !ushort.TryParse(
                    pair.Key,
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out _) ||
                !ushort.TryParse(
                    pair.Value,
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out _) ||
                !packages.ContainsKey(pair.Value))
            {
                throw new InvalidOperationException(
                    $"Razer driver alias {pair.Key} has invalid metadata.");
            }

            aliases[pair.Key.ToUpperInvariant()] = pair.Value.ToUpperInvariant();
        }

        foreach (var pair in packages)
        {
            installPackages.TryAdd(pair.Value.InstallProductId, pair.Key);
        }

        return new CatalogData(packages, aliases, installPackages);
    }

    private static string ResolveInstallProductId(string url)
    {
        var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
        var marker = fileName.IndexOf("PID", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || marker + 7 > fileName.Length)
        {
            return string.Empty;
        }

        var candidate = fileName.AsSpan(marker + 3, 4);
        return ushort.TryParse(
            candidate,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out _)
            ? candidate.ToString().ToUpperInvariant()
            : string.Empty;
    }

    private sealed record CatalogData(
        IReadOnlyDictionary<string, PackageJson> Packages,
        IReadOnlyDictionary<string, string> Aliases,
        IReadOnlyDictionary<string, string> InstallPackages);

    private sealed class CatalogJson
    {
        public Dictionary<string, PackageJson> Packages { get; init; } = [];
        public Dictionary<string, string> Aliases { get; init; } = [];
    }

    private sealed class PackageJson
    {
        public string Name { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string InstallProductId { get; set; } = string.Empty;
        public long Size { get; init; }
        public string Url { get; init; } = string.Empty;
    }
}
