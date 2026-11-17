using System.Net;
using System.Text.Json;

namespace OpenSynapse.Windows.Devices;

internal sealed record RazerManifestPackage(
    Uri Uri,
    long Length,
    string Sha256,
    Version? Version);

internal sealed record RazerManifestResource(
    string Name,
    string Url,
    RazerManifestPackage? Package);

/// <summary>
/// Reads the per-product resource list Synapse itself installs from. The product
/// page number is the only stable key; urls, versions, sizes and hashes all come
/// from the manifest.
/// </summary>
internal static class RazerManifestResources
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    internal static Uri ManifestUri(int page) => new(
        $"https://apps.razer.com/synapse/products/{page}/mw/manifest.json");

    internal static async Task<JsonDocument?> FetchAsync(
        int page,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(
                ManifestUri(page),
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<RazerManifestResource> Read(JsonElement manifest)
    {
        var resources = new List<RazerManifestResource>();
        if (!manifest.TryGetProperty("resourceManifest", out var resourceManifest) ||
            !resourceManifest.TryGetProperty("resources", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return resources;
        }

        foreach (var resource in array.EnumerateArray())
        {
            var version = resource.TryGetProperty("resourceVersion", out var versionValue)
                ? versionValue.GetString()
                : null;
            if (ReadPackage(resource, version) is not { } package)
            {
                continue;
            }

            resources.Add(new RazerManifestResource(
                resource.TryGetProperty("resourceName", out var nameValue)
                    ? nameValue.GetString() ?? string.Empty
                    : string.Empty,
                package.Uri.ToString(),
                package));
        }

        return resources;
    }

    /// <summary>Extracts the <c>/devices/&lt;page&gt;/</c> segment from a Razer asset url.</summary>
    internal static int? PageFromUrl(string? url)
    {
        if (url is null)
        {
            return null;
        }

        var parts = url.Split('/');
        for (var index = 0; index + 1 < parts.Length; index++)
        {
            if (parts[index].Equals("devices", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[index + 1], out var page))
            {
                return page;
            }
        }

        return null;
    }

    internal static Version? ParsePackageVersion(string? packageUri)
    {
        if (string.IsNullOrWhiteSpace(packageUri))
        {
            return null;
        }

        var name = Path.GetFileName(packageUri);
        var marker = name.LastIndexOf("-v", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var start = marker + 2;
        var end = name.IndexOf(".exe", start, StringComparison.OrdinalIgnoreCase);
        return end > start && Version.TryParse(name[start..end], out var version)
            ? version
            : null;
    }

    private static RazerManifestPackage? ReadPackage(JsonElement resource, string? versionText)
    {
        if (!resource.TryGetProperty("url", out var urlValue) ||
            urlValue.GetString() is not { } url ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !resource.TryGetProperty("size", out var sizeValue) ||
            !sizeValue.TryGetInt64(out var size) || size <= 0 ||
            !resource.TryGetProperty("sha256", out var shaValue) ||
            shaValue.GetString() is not { Length: 64 } sha256 ||
            !sha256.All(Uri.IsHexDigit))
        {
            return null;
        }

        var version = Version.TryParse(versionText, out var parsed)
            ? parsed
            : ParsePackageVersion(url);
        return new RazerManifestPackage(
            uri,
            size,
            sha256.ToUpperInvariant(),
            version);
    }
}
