using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using OpenSynapse.Core.Updates;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace OpenSynapse.App.Updates;

/// <summary>
/// Velopack source backed by the public Gitee mirror. The release feed stays in
/// <c>releases.{channel}.json</c>; packages are served by the versioned Gitee
/// release download path, which is anonymous for public repositories.
/// </summary>
internal sealed class GiteeSource : IUpdateSource
{
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly string _apiUrl;
    private readonly string _repositoryUrl;
    private readonly bool _includePrerelease;

    public GiteeSource(string owner, string repository, bool includePrerelease)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        _apiUrl = $"https://gitee.com/api/v5/repos/{owner}/{repository}/releases?per_page=100&direction=desc";
        _repositoryUrl = $"https://gitee.com/{owner}/{repository}";
        _includePrerelease = includePrerelease;
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId,
        VelopackAsset? latestLocalRelease)
    {
        var feedName = $"releases.{NormalizeChannel(channel)}.json";
        var releasesJson = await GetStringAsync(_apiUrl, TimeSpan.FromSeconds(15));
        var release = GiteeReleaseCatalog.Parse(releasesJson)
            .Where(candidate => _includePrerelease || !candidate.Prerelease)
            .Select(candidate => (Release: candidate, Version: ParseVersion(candidate.TagName)))
            .Where(candidate =>
                candidate.Version is not null &&
                candidate.Release.FindAsset(feedName) is not null)
            .OrderByDescending(candidate => candidate.Version)
            .FirstOrDefault();

        if (release.Release is null)
        {
            throw new InvalidOperationException(
                $"Gitee has no {feedName} asset in a published release.");
        }

        var feedAsset = release.Release.FindAsset(feedName)
            ?? throw new InvalidOperationException($"Gitee release is missing {feedName}.");
        var feedJson = await GetStringAsync(
            feedAsset.BrowserDownloadUrl,
            TimeSpan.FromSeconds(30));
        logger?.Log(
            VelopackLogLevel.Information,
            $"Gitee update feed {release.Release.TagName} selected.",
            exception: null);
        return ParseFeed(feedJson, release.Release.Body);
    }

    public async Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken)
    {
        var tag = $"v{releaseEntry.Version}";
        var url =
            $"{_repositoryUrl}/releases/download/{Uri.EscapeDataString(tag)}/" +
            Uri.EscapeDataString(releaseEntry.FileName);
        try
        {
            await DownloadFileAsync(
                url,
                releaseEntry,
                localFile,
                progress,
                cancelToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // A hand-created mirror release may not use the v{version} tag.
            var fallback = await FindAssetUrlAsync(releaseEntry.FileName, cancelToken);
            if (fallback is null)
            {
                throw new InvalidOperationException(
                    $"Gitee has no release asset named {releaseEntry.FileName}.",
                    exception);
            }

            await DownloadFileAsync(
                fallback,
                releaseEntry,
                localFile,
                progress,
                cancelToken);
        }
    }

    private async Task<string?> FindAssetUrlAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        var releasesJson = await GetStringAsync(
            _apiUrl,
            TimeSpan.FromSeconds(15),
            cancellationToken);
        return GiteeReleaseCatalog.Parse(releasesJson)
            .SelectMany(release => release.Assets)
            .FirstOrDefault(asset =>
                StringComparer.Ordinal.Equals(asset.Name, fileName))
            ?.BrowserDownloadUrl;
    }

    private static VelopackAssetFeed ParseFeed(string json, string notesMarkdown)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Gitee update feed has no Assets array.");
        }

        var parsed = new List<VelopackAsset>();
        foreach (var asset in assets.EnumerateArray())
        {
            var versionText = GetString(asset, "Version");
            var fileName = GetString(asset, "FileName");
            if (string.IsNullOrWhiteSpace(versionText) ||
                string.IsNullOrWhiteSpace(fileName) ||
                !SemanticVersion.TryParse(versionText, out var version))
            {
                continue;
            }

            var typeText = GetString(asset, "Type");
            var type = Enum.TryParse<VelopackAssetType>(
                typeText,
                ignoreCase: true,
                out var parsedType)
                ? parsedType
                : VelopackAssetType.Full;
            parsed.Add(new VelopackAsset
            {
                PackageId = GetString(asset, "PackageId") ?? "OpenSynapse",
                Version = version,
                Type = type,
                FileName = fileName,
                SHA1 = GetString(asset, "SHA1") ?? string.Empty,
                SHA256 = GetString(asset, "SHA256") ?? string.Empty,
                Size = GetInt64(asset, "Size"),
                NotesMarkdown = notesMarkdown,
            });
        }

        return new VelopackAssetFeed { Assets = parsed.ToArray() };
    }

    private static async Task DownloadFileAsync(
        string url,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        var partial = $"{localFile}.gitee-{Guid.NewGuid():N}.partial";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        cancellationToken = deadline.Token;
        try
        {
            using var response = await Http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? releaseEntry.Size;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                partial,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await OpenSynapse.Core.Storage.DownloadStream.ReadAsync(input, buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (total > 0)
                    {
                        progress?.Invoke((int)Math.Min(100, received * 100 / total));
                    }
                }
            }

            var length = new FileInfo(partial).Length;
            if (releaseEntry.Size > 0 && length != releaseEntry.Size)
            {
                throw new InvalidDataException(
                    $"Gitee asset {releaseEntry.FileName} is {length} bytes; expected {releaseEntry.Size}.");
            }

            if (!string.IsNullOrWhiteSpace(releaseEntry.SHA256))
            {
                await using var stream = File.OpenRead(partial);
                var actual = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken));
                if (!StringComparer.OrdinalIgnoreCase.Equals(actual, releaseEntry.SHA256))
                {
                    throw new InvalidDataException(
                        $"Gitee asset {releaseEntry.FileName} failed SHA-256 verification.");
                }
            }

            File.Move(partial, localFile, overwrite: true);
            progress?.Invoke(100);
        }
        finally
        {
            try
            {
                File.Delete(partial);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<string> GetStringAsync(
        string url,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var response = await Http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeoutSource.Token);
    }

    private static SemanticVersion? ParseVersion(string tagName)
    {
        var value = tagName.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        return SemanticVersion.TryParse(value, out var version) ? version : null;
    }

    private static string NormalizeChannel(string channel) =>
        string.IsNullOrWhiteSpace(channel) ? "win" : channel;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed)
            ? parsed
            : 0;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSynapse");
        return client;
    }
}
