using System.Collections.Concurrent;

namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Downloads per-product cover art from the Synapse asset host and keeps it on
/// disk so the device picker still has art when the network is unavailable.
/// </summary>
public sealed class StudioProductImageService
{
    // Synapse 4's dashboard falls back to app-assets.razer.com/files/synapse/devices/{productId}/cover.png.
    private const string AssetRoot = "https://app-assets.razer.com/files/synapse/devices/";
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const int MemoryCacheLimit = 64;
    private const string UserAgent = "OpenSynapse-LightingStudio/1.0";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SuccessCacheLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureCacheLifetime = TimeSpan.FromMinutes(1);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<ushort, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<ushort, MemoryEntry> _memory = new();

    public StudioProductImageService(HttpClient httpClient, string cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _httpClient = httpClient;
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
    }

    /// <summary>Resolves the verified Synapse cover URL; zero has no product art.</summary>
    public static Uri? ResolveUri(ushort productId) =>
        productId == 0
            ? null
            : new Uri(FormattableString.Invariant($"{AssetRoot}{AssetProductId(productId)}/cover.png"));

    /// <summary>
    /// The wireless BlackWidow V3 Mini (0271) shares the wired 0258 bundle, whose
    /// Synapse asset folder is 600. The PID-based URL returns 403 for 0271.
    /// </summary>
    private static ushort AssetProductId(ushort productId) =>
        productId == 0x0271 ? (ushort)0x0258 : productId;

    public async Task<StudioProductImage> GetAsync(
        ushort productId,
        string? name,
        CancellationToken cancellationToken = default)
    {
        if (productId == 0) return StudioProductImage.Unavailable(name, "no-product");
        if (TryMemory(productId, name, out var remembered)) return remembered;

        var gate = _locks.GetOrAdd(productId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryMemory(productId, name, out remembered)) return remembered;

            var cachePath = CachePath(productId);
            if (await IsCachedImageAsync(cachePath, cancellationToken).ConfigureAwait(false))
            {
                var cached = StudioProductImage.Loaded(name, cachePath);
                Remember(productId, cached);
                return cached;
            }

            var result = await DownloadAsync(productId, cachePath, name, cancellationToken).ConfigureAwait(false);
            Remember(productId, result);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<StudioProductImage> DownloadAsync(
        ushort productId,
        string cachePath,
        string? name,
        CancellationToken cancellationToken)
    {
        var uri = ResolveUri(productId);
        if (uri is null) return StudioProductImage.Unavailable(name, "no-product");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return StudioProductImage.Unavailable(
                    name,
                    FormattableString.Invariant($"http-{(int)response.StatusCode}"));
            }

            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength > MaxImageBytes)
            {
                return StudioProductImage.Unavailable(name, "too-large");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null &&
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return StudioProductImage.Unavailable(name, "not-image");
            }

            var bytes = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            if (bytes is null || !IsSupportedImage(bytes))
            {
                return StudioProductImage.Unavailable(name, bytes is null ? "too-large" : "not-image");
            }

            try
            {
                await WriteAtomicallyAsync(cachePath, bytes, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return StudioProductImage.Unavailable(name, "cache");
            }

            return StudioProductImage.Loaded(name, cachePath);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return StudioProductImage.Unavailable(name, "timeout");
        }
        catch (HttpRequestException)
        {
            return StudioProductImage.Unavailable(name, "network");
        }
        catch (IOException)
        {
            return StudioProductImage.Unavailable(name, "network");
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private async Task<bool> IsCachedImageAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length <= 0 || file.Length > MaxImageBytes) return false;
            var header = new byte[12];
            await using var stream = file.OpenRead();
            var read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
            return IsSupportedImage(header.AsSpan(0, read));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSupportedImage(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||
        bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ||
        (bytes.Length >= 12 &&
            bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes[8..12].SequenceEqual("WEBP"u8));

    private static async Task WriteAtomicallyAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string CachePath(ushort productId) =>
        Path.Combine(_cacheDirectory, FormattableString.Invariant($"{productId}.png"));

    private bool TryMemory(ushort productId, string? name, out StudioProductImage image)
    {
        if (_memory.TryGetValue(productId, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                image = entry.Path is { } path
                    ? StudioProductImage.Loaded(name, path)
                    : StudioProductImage.Unavailable(name, entry.Error);
                return true;
            }

            _memory.TryRemove(productId, out _);
        }

        image = StudioProductImage.ForName(name);
        return false;
    }

    private void Remember(ushort productId, StudioProductImage image)
    {
        var lifetime = image.State == StudioProductImageState.Loaded
            ? SuccessCacheLifetime
            : FailureCacheLifetime;
        _memory[productId] = new MemoryEntry(image.Path, image.Error, DateTimeOffset.UtcNow + lifetime);
        if (_memory.Count <= MemoryCacheLimit) return;
        foreach (var pair in _memory.OrderBy(item => item.Value.ExpiresAt)
                     .Take(_memory.Count - MemoryCacheLimit))
        {
            _memory.TryRemove(pair.Key, out _);
        }
    }

    private readonly record struct MemoryEntry(string? Path, string? Error, DateTimeOffset ExpiresAt);
}