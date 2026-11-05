using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// A Razer Synapse product manifest. Synapse addresses products by the decimal USB PID,
/// so the wired and dongle PIDs each have their own manifest.
/// </summary>
public sealed record SynapseProductManifest(
    ushort ProductId,
    string Name,
    DeviceCategory Category,
    IReadOnlyList<int> EditionIds,
    string SourceUrl,
    bool FromCache);

/// <summary>
/// Fetches the per-product manifest that Synapse uses to name a product. The manifest is
/// only metadata: mapping rows still come from the device's own button-list probe.
/// </summary>
public sealed class SynapseProductManifestService
{
    private const int MaxResponseBytes = 1024 * 1024;
    private const string UserAgent = "OpenSynapse/1.0";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromMinutes(30);

    private const string CatalogUrl = "https://apps.razer.com/synapse/dashboard/AvailableDevices.json";

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly TimeSpan _memoryCacheLifetime;
    private readonly ConcurrentDictionary<ushort, MemoryEntry> _memory = new();
    private readonly ConcurrentDictionary<ushort, SemaphoreSlim> _locks = new();
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private IReadOnlyDictionary<ushort, ushort>? _catalog;

    public SynapseProductManifestService(
        HttpClient httpClient,
        string cacheDirectory,
        TimeSpan? memoryCacheLifetime = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _memoryCacheLifetime = memoryCacheLifetime ?? TimeSpan.FromMinutes(30);
    }

    public async Task<SynapseProductManifest?> GetAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        var direct = await GetDirectAsync(productId, cancellationToken).ConfigureAwait(false);
        if (direct is not null) return direct;

        // A dongle or BLE link enumerates its own PID, which usually has no middleware
        // bundle. AvailableDevices.json maps that PID to the product's wired PID.
        var catalog = await GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        return catalog.TryGetValue(productId, out var product) && product != productId
            ? await GetDirectAsync(product, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private async Task<SynapseProductManifest?> GetDirectAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        if (TryMemory(productId, out var remembered)) return remembered;

        var gate = _locks.GetOrAdd(productId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryMemory(productId, out remembered)) return remembered;

            var cachePath = CachePath(productId);
            var etagPath = cachePath + ".etag";
            var etag = await ReadEtagAsync(etagPath, cancellationToken).ConfigureAwait(false);
            var outcome = await FetchAsync(Url(productId), etag, cancellationToken).ConfigureAwait(false);

            SynapseProductManifest? manifest;
            switch (outcome.Status)
            {
                case FetchStatus.Success:
                {
                    manifest = Parse(productId, outcome.Body!, Url(productId), fromCache: false);
                    if (manifest is not null)
                    {
                        await WriteCacheAsync(cachePath, etagPath, outcome.Body!, outcome.ETag, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    break;
                }
                default:
                    manifest = await ReadCacheAsync(cachePath, productId, fromCache: true, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }

            Remember(productId, manifest);
            return manifest;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<ushort, ushort>> GetCatalogAsync(
        CancellationToken cancellationToken)
    {
        if (_catalog is not null) return _catalog;

        await _catalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalog is not null) return _catalog;

            var cachePath = Path.Combine(_cacheDirectory, "catalog.json");
            var etagPath = cachePath + ".etag";
            var etag = await ReadEtagAsync(etagPath, cancellationToken).ConfigureAwait(false);
            var outcome = await FetchAsync(CatalogUrl, etag, cancellationToken).ConfigureAwait(false);
            var body = outcome.Status == FetchStatus.Success
                ? outcome.Body
                : await ReadBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
            _catalog = body is null
                ? new Dictionary<ushort, ushort>()
                : ParseCatalog(body);
            if (outcome.Status == FetchStatus.Success && outcome.Body is not null)
            {
                await WriteCacheAsync(cachePath, etagPath, outcome.Body, outcome.ETag, cancellationToken)
                    .ConfigureAwait(false);
            }

            return _catalog;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    private static IReadOnlyDictionary<ushort, ushort> ParseCatalog(byte[] body)
    {
        var aliases = new Dictionary<ushort, ushort>();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return aliases;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("productId", out var productElement) ||
                    !productElement.TryGetInt32(out var product) ||
                    product is < 1 or > ushort.MaxValue)
                {
                    continue;
                }

                foreach (var key in new[] { "dongleId", "bleId", "wiredId", "xBoxId" })
                {
                    if (entry.TryGetProperty(key, out var aliasElement) &&
                        aliasElement.TryGetInt32(out var alias) &&
                        alias is > 0 and <= ushort.MaxValue &&
                        alias != product)
                    {
                        aliases.TryAdd((ushort)alias, (ushort)product);
                    }
                }
            }
        }
        catch (JsonException)
        {
            aliases.Clear();
        }

        return aliases;
    }
    private static string Url(ushort productId) =>
        FormattableString.Invariant(
            $"https://apps.razer.com/synapse/products/{productId}/mw/manifest.json");

    private static SynapseProductManifest? Parse(
        ushort productId,
        byte[] body,
        string url,
        bool fromCache)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var name = root.TryGetProperty("deviceName", out var deviceName)
                ? deviceName.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var editions = new List<int>();
            if (root.TryGetProperty("editionId", out var editionIds) &&
                editionIds.ValueKind == JsonValueKind.Array)
            {
                foreach (var edition in editionIds.EnumerateArray())
                {
                    if (edition.TryGetInt32(out var value)) editions.Add(value);
                }
            }

            var icon = root.TryGetProperty("icons", out var icons) &&
                       icons.ValueKind == JsonValueKind.Array &&
                       icons.GetArrayLength() > 0 &&
                       icons[0].TryGetProperty("src", out var source)
                ? source.GetString()
                : null;
            return new SynapseProductManifest(
                productId,
                name.Trim(),
                ParseCategory(icon),
                editions,
                url,
                fromCache);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The manifest icon is the product's own category sign: MOUSE.svg, KEYBOARD.svg,
    /// MOUSEMAT.svg. No name or usage-page heuristic is involved.
    /// </summary>
    internal static DeviceCategory ParseCategory(string? icon)
    {
        var file = Path.GetFileNameWithoutExtension(icon ?? string.Empty).ToUpperInvariant();
        return file switch
        {
            "MOUSE" => DeviceCategory.Mouse,
            "KEYBOARD" or "KEYPAD" => DeviceCategory.Keyboard,
            "MOUSEMAT" or "MOUSEPLUSMAT" => DeviceCategory.MouseMat,
            "GAMEPAD" or "GAMEPAD_ARCADE_CONTROLLER" => DeviceCategory.Gamepad,
            _ => DeviceCategory.Unknown,
        };
    }

    private bool TryMemory(ushort productId, out SynapseProductManifest? manifest)
    {
        manifest = null;
        if (!_memory.TryGetValue(productId, out var entry)) return false;
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _memory.TryRemove(productId, out _);
            return false;
        }

        manifest = entry.Manifest;
        return true;
    }

    private void Remember(ushort productId, SynapseProductManifest? manifest)
    {
        var lifetime = manifest is null ? FailureLifetime : _memoryCacheLifetime;
        if (lifetime <= TimeSpan.Zero) return;
        _memory[productId] = new MemoryEntry(manifest, DateTimeOffset.UtcNow + lifetime);
    }

    private async Task<FetchOutcome> FetchAsync(
        string url,
        string? etag,
        CancellationToken cancellationToken)
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
                    ? new FetchOutcome(FetchStatus.NotFound, null, null)
                    : new FetchOutcome(FetchStatus.NotModified, null, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new FetchOutcome(FetchStatus.NotFound, null, null);
            }

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            {
                return new FetchOutcome(FetchStatus.NotFound, null, null);
            }

            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            return body.Length > MaxResponseBytes
                ? new FetchOutcome(FetchStatus.NotFound, null, null)
                : new FetchOutcome(FetchStatus.Success, body, response.Headers.ETag?.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FetchOutcome(FetchStatus.Unavailable, null, null);
        }
        catch (HttpRequestException)
        {
            return new FetchOutcome(FetchStatus.Unavailable, null, null);
        }
        catch (IOException)
        {
            return new FetchOutcome(FetchStatus.Unavailable, null, null);
        }
    }

    private static async Task<byte[]?> ReadBytesAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return File.Exists(path)
                ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
                : null;
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
    private async Task<SynapseProductManifest?> ReadCacheAsync(
        string cachePath,
        ushort productId,
        bool fromCache,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            var body = await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
            return Parse(productId, body, Url(productId), fromCache);
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

    private static async Task<string?> ReadEtagAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var value = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
            return value.Length == 0 ? null : value;
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
            await File.WriteAllBytesAsync(cachePath, body, cancellationToken).ConfigureAwait(false);
            if (etag is null)
            {
                if (File.Exists(etagPath)) File.Delete(etagPath);
            }
            else
            {
                await File.WriteAllTextAsync(etagPath, etag, Encoding.UTF8, cancellationToken)
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

    private string CachePath(ushort productId) =>
        Path.Combine(_cacheDirectory, productId.ToString(CultureInfo.InvariantCulture) + ".json");

    private readonly record struct MemoryEntry(SynapseProductManifest? Manifest, DateTimeOffset ExpiresAt);

    private enum FetchStatus
    {
        Success,
        NotModified,
        NotFound,
        Unavailable,
    }

    private readonly record struct FetchOutcome(FetchStatus Status, byte[]? Body, string? ETag);
}