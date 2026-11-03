using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Fetches Chroma device manifests from Razer and keeps a per-product cache so
/// the lighting studio still opens when the network is unavailable.
/// </summary>
public sealed class StudioManifestService
{
    private const int MaxResponseBytes = 32 * 1024 * 1024;
    private const string UserAgent = "OpenSynapse-LightingStudio/1.0";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<(ushort ProductId, int EditionId), SemaphoreSlim> _locks = new();

    public StudioManifestService(HttpClient httpClient, string cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _httpClient = httpClient;
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
    }

    public async Task<StudioManifestResult> GetAsync(
        ushort productId,
        int editionId,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd((productId, editionId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cachePath = CachePath(productId, editionId, ".json");
            var etagPath = CachePath(productId, editionId, ".etag");
            var cached = await ReadCacheAsync(cachePath, etagPath, productId, editionId, cancellationToken)
                .ConfigureAwait(false);

            return cached is null
                ? await FetchFirstAvailableAsync(productId, editionId, cachePath, etagPath, cancellationToken)
                    .ConfigureAwait(false)
                : await RefreshCachedAsync(cached, productId, editionId, cachePath, etagPath, cancellationToken)
                    .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<StudioManifestResult> FetchFirstAvailableAsync(
        ushort productId,
        int editionId,
        string cachePath,
        string etagPath,
        CancellationToken cancellationToken)
    {
        var lastUrl = PrimaryUrl(productId, editionId);
        string? lastError = null;
        foreach (var url in new[] { lastUrl, FallbackUrl(productId, editionId) })
        {
            lastUrl = url;
            var outcome = await FetchAsync(url, null, cancellationToken).ConfigureAwait(false);
            if (outcome.Status == FetchStatus.Success)
            {
                var manifest = StudioManifestParser.Parse(
                    Encoding.UTF8.GetString(outcome.Body!),
                    productId,
                    editionId,
                    outcome.ETag,
                    outcome.LastModified);
                if (manifest is null)
                {
                    return new StudioManifestResult(null, url, false, "parse-error");
                }

                await WriteCacheAsync(cachePath, etagPath, outcome.Body!, outcome.ETag, cancellationToken)
                    .ConfigureAwait(false);
                return new StudioManifestResult(manifest, url, false, null);
            }

            lastError = outcome.Error;
            if (outcome.Status is FetchStatus.HttpError or FetchStatus.TooLarge)
            {
                return new StudioManifestResult(null, url, false, lastError);
            }
        }

        return new StudioManifestResult(null, lastUrl, false, lastError ?? "not-found");
    }

    private async Task<StudioManifestResult> RefreshCachedAsync(
        StudioManifestDevice cached,
        ushort productId,
        int editionId,
        string cachePath,
        string etagPath,
        CancellationToken cancellationToken)
    {
        var url = PrimaryUrl(productId, editionId);
        var outcome = await FetchAsync(url, cached.ETag, cancellationToken).ConfigureAwait(false);
        switch (outcome.Status)
        {
            case FetchStatus.Success:
            {
                var manifest = StudioManifestParser.Parse(
                    Encoding.UTF8.GetString(outcome.Body!),
                    productId,
                    editionId,
                    outcome.ETag,
                    outcome.LastModified);
                if (manifest is null)
                {
                    return new StudioManifestResult(cached, url, true, "parse-error");
                }

                await WriteCacheAsync(cachePath, etagPath, outcome.Body!, outcome.ETag, cancellationToken)
                    .ConfigureAwait(false);
                return new StudioManifestResult(manifest, url, false, null);
            }
            case FetchStatus.NotModified:
                return new StudioManifestResult(cached, url, true, null);
            case FetchStatus.Timeout:
            case FetchStatus.Unavailable:
                return new StudioManifestResult(cached, url, true, "offline-cache");
            default:
                return new StudioManifestResult(cached, url, true, outcome.Error);
        }
    }

    private async Task<FetchOutcome> FetchAsync(string url, string? etag, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (etag is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return etag is null
                    ? new FetchOutcome(FetchStatus.HttpError, null, null, null, "http-304")
                    : new FetchOutcome(FetchStatus.NotModified, null, null, null, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                    ? new FetchOutcome(FetchStatus.NotFound, null, null, null, "not-found")
                    : new FetchOutcome(
                        FetchStatus.HttpError,
                        null,
                        null,
                        null,
                        FormattableString.Invariant($"http-{(int)response.StatusCode}"));
            }

            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength > MaxResponseBytes)
            {
                return new FetchOutcome(FetchStatus.TooLarge, null, null, null, "too-large");
            }

            var body = await ReadBodyAsync(response.Content, timeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                return new FetchOutcome(FetchStatus.TooLarge, null, null, null, "too-large");
            }

            return new FetchOutcome(
                FetchStatus.Success,
                body,
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified,
                null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FetchOutcome(FetchStatus.Timeout, null, null, null, "timeout");
        }
        catch (HttpRequestException)
        {
            return new FetchOutcome(FetchStatus.Unavailable, null, null, null, "network");
        }
        catch (IOException)
        {
            return new FetchOutcome(FetchStatus.Unavailable, null, null, null, "network");
        }
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private async Task<StudioManifestDevice?> ReadCacheAsync(
        string cachePath,
        string etagPath,
        ushort productId,
        int editionId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
            var etag = File.Exists(etagPath)
                ? (await File.ReadAllTextAsync(etagPath, cancellationToken).ConfigureAwait(false)).Trim()
                : null;
            if (string.IsNullOrEmpty(etag))
            {
                etag = null;
            }

            return StudioManifestParser.Parse(json, productId, editionId, etag, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteCacheAsync(
        string cachePath,
        string etagPath,
        byte[] body,
        string? etag,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            await WriteAtomicallyAsync(cachePath, body, cancellationToken).ConfigureAwait(false);
            if (etag is null)
            {
                if (File.Exists(etagPath))
                {
                    File.Delete(etagPath);
                }
            }
            else
            {
                await WriteAtomicallyAsync(etagPath, Encoding.UTF8.GetBytes(etag), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string CachePath(ushort productId, int editionId, string extension) =>
        Path.Combine(_cacheDirectory, FormattableString.Invariant($"{productId}_{editionId}{extension}"));

    private static string PrimaryUrl(ushort productId, int editionId) =>
        FormattableString.Invariant(
            $"https://apps.razer.com/synapse/products/{productId}/mw/lighting-manifests/DeviceManifest_{productId}_{editionId}.json");

    private static string FallbackUrl(ushort productId, int editionId) =>
        FormattableString.Invariant(
            $"https://apps.razer.com/synapse/lighting-engine/manifest/DeviceManifest_{productId}_{editionId}.json");

    private enum FetchStatus
    {
        Success,
        NotModified,
        NotFound,
        Timeout,
        Unavailable,
        HttpError,
        TooLarge,
    }

    private readonly record struct FetchOutcome(
        FetchStatus Status,
        byte[]? Body,
        string? ETag,
        DateTimeOffset? LastModified,
        string? Error);
}
