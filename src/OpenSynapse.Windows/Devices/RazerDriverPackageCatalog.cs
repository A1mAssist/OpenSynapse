using System.Collections.Concurrent;
using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerDriverPackage(
    string ProductId,
    string InstallProductId,
    string Name,
    string Version,
    long Size,
    string Url);

internal sealed record RazerDriverEntry(
    string ProductId,
    string InstallProductId,
    string Name,
    int Page,
    string Version,
    long Size,
    string Url);

/// <summary>
/// Maps a connected Razer product to the official per-device driver package
/// published in the Synapse device bundles. The package url, version and size
/// come from <c>products/&lt;page&gt;/mw/manifest.json</c>; the local table keeps
/// only the product page and the PID in the installer file name. A wireless
/// dongle PID resolves to the base product package, whose INF covers both.
/// </summary>
internal static class RazerDriverPackageCatalog
{
    private const string ResourceName =
        "OpenSynapse.Windows.Devices.RazerDriverPackages.json";

    private static readonly Lazy<CatalogData> Data = new(Load);

    private static readonly ConcurrentDictionary<int, Lazy<Task<IReadOnlyList<RazerManifestResource>?>>>
        Manifests = new();

    internal static bool TryGetEntry(ushort productId, out RazerDriverEntry entry)
    {
        entry = null!;
        var key = productId.ToString("X4");
        var data = Data.Value;
        if (!data.Packages.TryGetValue(key, out var found))
        {
            if (data.Aliases.TryGetValue(key, out var baseProductId) ||
                data.InstallPackages.TryGetValue(key, out baseProductId))
            {
                key = baseProductId;
                found = data.Packages[key];
            }
            else
            {
                return false;
            }
        }

        entry = new RazerDriverEntry(
            key,
            found.InstallPid,
            found.Name,
            found.Page,
            found.Version,
            found.Size,
            found.Url);
        return true;
    }

    /// <summary>
    /// The manifest is authoritative; entries whose product page carries no
    /// driver resource keep their pinned url as a fallback, so a Razer outage
    /// does not remove a card that used to work.
    /// </summary>
    internal static async Task<RazerDriverPackage?> ResolveAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetEntry(productId, out var entry))
        {
            return null;
        }

        if (entry.Page > 0 &&
            await ResolveFromManifestAsync(entry, cancellationToken).ConfigureAwait(false)
                is { } resolved)
        {
            return resolved;
        }

        return entry.Url.Length > 0
            ? new RazerDriverPackage(
                entry.ProductId,
                entry.InstallProductId,
                entry.Name,
                entry.Version,
                entry.Size,
                entry.Url)
            : null;
    }

    /// <summary>
    /// A connected wireless dongle, the catalog key and the PID in the official
    /// installer file name are three different identities. Every install-state,
    /// INF and service lookup must try all of them.
    /// </summary>
    internal static IReadOnlyList<ushort> GetCandidateProductIds(
        ushort connectedProductId,
        RazerDriverEntry entry)
    {
        var candidates = new List<ushort>(3);
        Add(connectedProductId);
        Add(Parse(entry.ProductId));
        Add(Parse(entry.InstallProductId));
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

    private static async Task<RazerDriverPackage?> ResolveFromManifestAsync(
        RazerDriverEntry entry,
        CancellationToken cancellationToken)
    {
        var lazy = Manifests.GetOrAdd(
            entry.Page,
            static page => new Lazy<Task<IReadOnlyList<RazerManifestResource>?>>(
                () => LoadManifestAsync(page)));
        var resources = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (resources is null)
        {
            // Do not cache a transient network failure for the whole process.
            Manifests.TryRemove(entry.Page, out _);
            return null;
        }

        var resource = resources.FirstOrDefault(item =>
            item.Name.StartsWith("NormalDriver_", StringComparison.OrdinalIgnoreCase) &&
            item.Name.Contains(entry.InstallProductId, StringComparison.OrdinalIgnoreCase));
        if (resource?.Package is not { } package)
        {
            return null;
        }

        return new RazerDriverPackage(
            entry.ProductId,
            entry.InstallProductId,
            entry.Name,
            package.Version?.ToString() ?? string.Empty,
            package.Length,
            package.Uri.ToString());
    }

    private static async Task<IReadOnlyList<RazerManifestResource>?> LoadManifestAsync(int page)
    {
        using var document = await RazerManifestResources
            .FetchAsync(page, CancellationToken.None)
            .ConfigureAwait(false);
        return document is null ? null : RazerManifestResources.Read(document.RootElement);
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
                pair.Value.Page < 0 ||
                pair.Value.InstallPid.Length != 4 ||
                !ushort.TryParse(
                    pair.Value.InstallPid,
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out _))
            {
                throw new InvalidOperationException(
                    $"Razer driver package {pair.Key} has invalid metadata.");
            }

            if (pair.Value.Page == 0 ||
                pair.Value.Url.Length > 0)
            {
                if (!Uri.TryCreate(pair.Value.Url, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps ||
                    !string.Equals(uri.Host, "app-assets.razer.com", StringComparison.OrdinalIgnoreCase) ||
                    pair.Value.Size <= 0 ||
                    string.IsNullOrWhiteSpace(pair.Value.Version))
                {
                    throw new InvalidOperationException(
                        $"Razer driver package {pair.Key} has no usable fallback.");
                }
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
            installPackages.TryAdd(pair.Value.InstallPid, pair.Key);
        }

        return new CatalogData(packages, aliases, installPackages);
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
        public int Page { get; init; }
        public string InstallPid { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public long Size { get; init; }
        public string Url { get; init; } = string.Empty;
    }
}
