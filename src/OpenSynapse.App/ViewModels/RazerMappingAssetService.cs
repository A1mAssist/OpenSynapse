using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.ViewModels;

public enum RazerMappingDeviceKind
{
    Mouse,
    Keyboard,
    Unknown,
}

/// <summary>
/// Resolves optional mapping art from Razer's own product bundles and the lighting
/// studio manifests. Everything is fetched on demand and cached on disk; a missing
/// asset never blocks the mapping read and never invents a hotspot.
/// </summary>
public sealed class RazerMappingAssetService
{
    private const string UiRoot = "https://apps.razer.com/synapse/products/";
    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxScriptBytes = 16 * 1024 * 1024;
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const string UserAgent = "OpenSynapse-Mapping/1.0";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _cacheRoot;
    private readonly StudioManifestService _studio;
    private readonly ConcurrentDictionary<(ushort ProductId, RazerMappingDeviceKind Kind), Lazy<Task<RazerMappingVisual>>> _cache = new();

    public RazerMappingAssetService()
    {
        _cacheRoot = AppDataPaths.Combine("mapping", "devices");
        _studio = new StudioManifestService(
            Http,
            AppDataPaths.Combine("mapping", "lighting-manifests"),
            TimeSpan.FromMinutes(10),
            offlineCatalogPath: Path.Combine(
                AppContext.BaseDirectory, "Assets", "LightingLayouts", "studio-manifests.zip"));
    }

    public static RazerMappingAssetService Shared { get; } = new();

    public Task<RazerMappingVisual> GetAsync(
        ushort productId,
        RazerMappingDeviceKind kind,
        CancellationToken cancellationToken = default)
    {
        if (productId == 0) return Task.FromResult(RazerMappingVisual.Empty);
        var lazy = _cache.GetOrAdd(
            (productId, kind),
            key => new Lazy<Task<RazerMappingVisual>>(
                () => LoadAsync(key.ProductId, key.Kind),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value.WaitAsync(cancellationToken);
    }

    private async Task<RazerMappingVisual> LoadAsync(ushort productId, RazerMappingDeviceKind kind)
    {
        try
        {
            if (kind == RazerMappingDeviceKind.Mouse)
            {
                return await LoadMouseOrKeyboardAsync(productId).ConfigureAwait(false);
            }

            if (kind == RazerMappingDeviceKind.Keyboard)
            {
                if (await LoadKeyboardAsync(productId, CancellationToken.None).ConfigureAwait(false) is { } keyboard)
                {
                    return new RazerMappingVisual(null, keyboard, null);
                }

                // Keyboards outside the studio catalog still get bundle art with drawLines hotspots.
                var image = await LoadBundleAsync(productId, CancellationToken.None).ConfigureAwait(false);
                return image is null
                    ? new RazerMappingVisual(null, null, "keyboard-svg-unavailable")
                    : new RazerMappingVisual(image, null, null);
            }

            return await LoadMouseOrKeyboardAsync(productId).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new RazerMappingVisual(null, null, "timeout");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            TimeoutException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new RazerMappingVisual(null, null, exception.Message);
        }
    }

    private async Task<RazerMappingVisual> LoadMouseOrKeyboardAsync(ushort productId)
    {
        var mouse = await LoadBundleAsync(productId, CancellationToken.None).ConfigureAwait(false);
        if (mouse is not null)
        {
            return new RazerMappingVisual(mouse, null, null);
        }

        var keyboard = await LoadKeyboardAsync(productId, CancellationToken.None).ConfigureAwait(false);
        return keyboard is null
            ? RazerMappingVisual.Empty
            : new RazerMappingVisual(null, keyboard, null);
    }

    private async Task<RazerMappingImageLayout?> LoadBundleAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        var manifest = await ReadAssetManifestAsync(productId, cancellationToken).ConfigureAwait(false);
        if (manifest?.MainJs is not { Length: > 0 } mainJs)
        {
            return null;
        }

        var scriptPath = await CacheRemoteFileAsync(
            productId, mainJs, MaxScriptBytes, "script", cancellationToken).ConfigureAwait(false);
        if (scriptPath is null)
        {
            return null;
        }

        var script = await File.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        var bundle = RazerMappingBundleParser.Parse(script, manifest.Image3x);
        if (bundle.MouseAnchors.Count == 0 || bundle.Image3x is not { Length: > 0 } image)
        {
            return null;
        }

        var imagePath = await CacheRemoteFileAsync(
            productId, image, MaxImageBytes, "image", cancellationToken).ConfigureAwait(false);
        if (imagePath is null)
        {
            return null;
        }

        var hotspots = new Dictionary<byte, RazerMappingPoint>(bundle.MouseAnchors.Count);
        foreach (var anchor in bundle.MouseAnchors)
        {
            hotspots.TryAdd(anchor.ButtonId, new RazerMappingPoint(anchor.X, anchor.Y, anchor.IsLeft));
        }

        return hotspots.Count == 0
            ? null
            : new RazerMappingImageLayout(
                new Uri(imagePath), bundle.ImageWidth, bundle.ImageHeight, hotspots);
    }

    private async Task<RazerMappingKeyboardLayout?> LoadKeyboardAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        // shortcut: the shipped catalog only has edition 0 or 129; probing 128..135 would add two HTTP timeouts per edition.
        foreach (var edition in new[] { 0, 129 })
        {
            var result = await _studio.GetAsync(productId, edition, cancellationToken).ConfigureAwait(false);
            if (result.Manifest is null)
            {
                continue;
            }

            var visual = StudioKeyboardVisualBuilder.Build(result.Manifest);
            if (visual is not { Keys.Count: > 0 })
            {
                continue;
            }

            return new RazerMappingKeyboardLayout(
                visual, RazerKeyboardKeyCatalog.Resolve(visual.Keys));
        }

        return null;
    }

    private async Task<AssetManifest?> ReadAssetManifestAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        var directory = DeviceDirectory(productId);
        var cachePath = Path.Combine(directory, "asset-manifest.json");
        var etagPath = cachePath + ".etag";
        var cached = TryReadCachedManifest(cachePath);
        var etag = TryReadText(etagPath);

        var uri = FormattableString.Invariant($"{UiRoot}{productId}/ui/asset-manifest.json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (etag is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                return cached;
            }

            if (!response.IsSuccessStatusCode)
            {
                return cached;
            }

            var bytes = await ReadBoundedAsync(response.Content, MaxManifestBytes, timeout.Token)
                .ConfigureAwait(false);
            if (bytes is null)
            {
                return cached;
            }

            var json = Encoding.UTF8.GetString(bytes);
            var manifest = AssetManifest.Parse(json);
            if (manifest is null)
            {
                return cached;
            }

            await WriteCacheAsync(cachePath, bytes, timeout.Token).ConfigureAwait(false);
            var newEtag = response.Headers.ETag?.ToString();
            if (string.IsNullOrWhiteSpace(newEtag))
            {
                TryDelete(etagPath);
            }
            else
            {
                await WriteCacheAsync(etagPath, Encoding.UTF8.GetBytes(newEtag), timeout.Token)
                    .ConfigureAwait(false);
            }

            return manifest;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return cached;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            UnauthorizedAccessException or JsonException)
        {
            return cached;
        }
    }

    private async Task<string?> CacheRemoteFileAsync(
        ushort productId,
        string relativeUrl,
        int maxBytes,
        string kind,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(relativeUrl.Split('?')[0]);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var directory = DeviceDirectory(productId);
        var cachePath = Path.Combine(directory, fileName);
        if (IsUsableCache(cachePath, maxBytes))
        {
            return cachePath;
        }

        var uri = ResolveProductUrl(productId, relativeUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            using var response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (kind == "image" &&
                response.Content.Headers.ContentType?.MediaType is { } mediaType &&
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var bytes = await ReadBoundedAsync(response.Content, maxBytes, timeout.Token)
                .ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            await WriteCacheAsync(cachePath, bytes, timeout.Token).ConfigureAwait(false);
            return cachePath;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return IsUsableCache(cachePath, maxBytes) ? cachePath : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            UnauthorizedAccessException)
        {
            return IsUsableCache(cachePath, maxBytes) ? cachePath : null;
        }
    }

    private static AssetManifest? TryReadCachedManifest(string path)
    {
        try
        {
            return File.Exists(path)
                ? AssetManifest.Parse(File.ReadAllText(path))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsUsableCache(string path, int maxBytes)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length > 0 && file.Length <= maxBytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static async Task WriteCacheAsync(
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
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string DeviceDirectory(ushort productId) =>
        Path.Combine(_cacheRoot, productId.ToString(CultureInfo.InvariantCulture));

    private static string ResolveProductUrl(ushort productId, string relativeUrl) =>
        FormattableString.Invariant(
            $"{UiRoot}{productId}/ui/{relativeUrl.TrimStart('.', '/')}");

    private sealed record AssetManifest(string? MainJs, string? Image3x)
    {
        public static AssetManifest? Parse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json.TrimStart('\uFEFF'));
                if (!document.RootElement.TryGetProperty("files", out var files) ||
                    files.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                string? mainJs = null;
                string? image = null;
                foreach (var property in files.EnumerateObject())
                {
                    if (property.NameEquals("main.js"))
                    {
                        mainJs = Value(property.Value);
                    }
                    else if (image is null &&
                        IsPrd3x(property.Name))
                    {
                        image = Value(property.Value);
                    }
                }

                return new AssetManifest(mainJs, image);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsPrd3x(string name) =>
            name.StartsWith("static/media/prd-3x", StringComparison.OrdinalIgnoreCase) &&
            (name.EndsWith(".avif", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

        private static string? Value(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            _ => null,
        };
    }
}

/// <summary>Standard USB HID usage ids used by the mapping bundle for keyboard key ids.</summary>
internal static class RazerKeyboardKeyCatalog
{
    private static readonly IReadOnlyDictionary<string, byte> Hid = Build();

    public static IReadOnlyDictionary<string, byte> Resolve(IEnumerable<StudioKeyboardKey> keys)
    {
        var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (TryGet(key.KeyName, out var id))
            {
                result[key.KeyName] = id;
            }
        }

        return result;
    }

    public static bool TryGet(string name, out byte id)
    {
        if (Hid.TryGetValue(name, out id)) return true;
        return Hid.TryGetValue(Normalize(name), out id);
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IReadOnlyDictionary<string, byte> Build()
    {
        var map = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            ["Esc"] = 41,
            ["Tilde"] = 53,
            ["Dash"] = 45,
            ["Equal"] = 46,
            ["Backspace"] = 42,
            ["Tab"] = 43,
            ["StartSquareBracket"] = 47,
            ["EndSquareBracket"] = 48,
            ["Backslash"] = 49,
            ["Caps"] = 57,
            ["SemiColon"] = 51,
            ["Apostrophe"] = 52,
            ["Enter"] = 40,
            ["Comma"] = 54,
            ["Dot"] = 55,
            ["ForwardSlash"] = 56,
            ["Space"] = 44,
            ["PrintScreen"] = 70,
            ["ScrollLock"] = 71,
            ["PauseBreak"] = 72,
            ["Insert"] = 73,
            ["Home"] = 74,
            ["PageUp"] = 75,
            ["Delete"] = 76,
            ["End"] = 77,
            ["PageDown"] = 78,
            ["RightArrow"] = 79,
            ["LeftArrow"] = 80,
            ["DownArrow"] = 81,
            ["UpArrow"] = 82,
            ["Menu"] = 101,
            ["LeftCtrl"] = 224,
            ["LeftShift"] = 225,
            ["LeftAlt"] = 226,
            ["Window"] = 227,
            ["RightCtrl"] = 228,
            ["RightShift"] = 229,
            ["RightAlt"] = 230,
            ["NumPad"] = 83,
            ["NumPadForwardSlash"] = 84,
            ["NumPadAsterisk"] = 85,
            ["NumPadMinus"] = 86,
            ["NumPadPlus"] = 87,
            ["NumPadEnter"] = 88,
            ["NumPad0"] = 98,
            ["NumPadDot"] = 99,
        };

        for (var index = 0; index < 10; index++)
        {
            map[index.ToString(CultureInfo.InvariantCulture)] = (byte)(30 + index);
        }

        for (var index = 0; index < 26; index++)
        {
            map[((char)('A' + index)).ToString()] = (byte)(4 + index);
        }

        for (var index = 0; index < 12; index++)
        {
            map[$"F{index + 1}"] = (byte)(58 + index);
        }

        for (var index = 1; index <= 9; index++)
        {
            map[$"NumPad{index}"] = (byte)(88 + index);
        }

        foreach (var pair in map.ToArray())
        {
            map[Normalize(pair.Key)] = pair.Value;
        }

        return map;
    }
}