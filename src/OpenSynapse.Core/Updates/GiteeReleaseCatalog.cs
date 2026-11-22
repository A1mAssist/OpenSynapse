using System.Globalization;
using System.Text.Json;

namespace OpenSynapse.Core.Updates;

public sealed record GiteeReleaseAsset(string Name, string BrowserDownloadUrl);

public sealed record GiteeReleaseInfo(
    string TagName,
    string Name,
    string Body,
    bool Prerelease,
    DateTimeOffset CreatedAt,
    IReadOnlyList<GiteeReleaseAsset> Assets)
{
    public GiteeReleaseAsset? FindAsset(string fileName) =>
        Assets.FirstOrDefault(asset =>
            StringComparer.Ordinal.Equals(asset.Name, fileName));
}

/// <summary>
/// Reads the subset of the Gitee v5 release response the updater needs.
/// Gitee returns the same asset names and browser URLs as GitHub, but omits
/// size/digest fields; the signed <c>releases.win.json</c> asset remains the
/// source of truth for both.
/// </summary>
public static class GiteeReleaseCatalog
{
    public static IReadOnlyList<GiteeReleaseInfo> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Gitee releases response must be an array.");
        }

        var releases = new List<GiteeReleaseInfo>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var tagName = GetString(item, "tag_name");
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var assets = new List<GiteeReleaseAsset>();
            if (item.TryGetProperty("assets", out var assetsElement) &&
                assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElement.EnumerateArray())
                {
                    var name = GetString(asset, "name");
                    var url = GetString(asset, "browser_download_url");
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                    {
                        assets.Add(new GiteeReleaseAsset(name, url));
                    }
                }
            }

            var createdAt = DateTimeOffset.TryParse(
                GetString(item, "created_at"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsedCreatedAt)
                ? parsedCreatedAt
                : DateTimeOffset.MinValue;

            releases.Add(new GiteeReleaseInfo(
                tagName,
                GetString(item, "name") ?? tagName,
                GetString(item, "body") ?? string.Empty,
                item.TryGetProperty("prerelease", out var prerelease) &&
                    prerelease.ValueKind == JsonValueKind.True,
                createdAt,
                assets));
        }

        return releases;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
