using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Diagnostics;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;

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
public sealed partial class RazerMappingAssetService
{
    private const string UiRoot = "https://apps.razer.com/synapse/products/";

    // Viper V4 Pro (00E5/00E6) publishes its UI under page 167, not the decimal PID.
    private static ushort UiPageId(ushort productId) =>
        productId is 0x00E5 or 0x00E6 ? (ushort)167 : productId;

    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxScriptBytes = 16 * 1024 * 1024;
    private const int MaxImageBytes = 4 * 1024 * 1024;
    private const string UserAgent = "OpenSynapse-Mapping/1.0";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly LocalDiagnosticLog Log = new();

    private readonly string _cacheRoot;
    private readonly StudioManifestService _studio;
    private readonly ConcurrentDictionary<(ushort ProductId, RazerMappingDeviceKind Kind), Lazy<Task<RazerMappingVisual>>> _cache = new();
    private readonly ConcurrentDictionary<ushort, byte> _sourceChecked = new();

    public RazerMappingAssetService()
    {
        _cacheRoot = AppDataPaths.Combine("mapping", "devices");
        _studio = new StudioManifestService(
            Http,
            AppDataPaths.Combine("mapping", "lighting-manifests"),
            TimeSpan.FromMinutes(10));
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
        // The wireless receiver shares the wired model's product page. 0086 owns
        // the mapping art and button keys; 0088 only adds a battery endpoint.
        var assetProductId = RazerProductAliases.Canonical(productId);
        var mouse = await LoadBundleAsync(assetProductId, CancellationToken.None).ConfigureAwait(false);
        if (mouse is not null)
        {
            return new RazerMappingVisual(mouse, null, null);
        }

        var keyboard = await LoadKeyboardAsync(assetProductId, CancellationToken.None).ConfigureAwait(false);
        return keyboard is null
            ? RazerMappingVisual.Empty
            : new RazerMappingVisual(null, keyboard, null);
    }

    private async Task<RazerMappingImageLayout?> LoadBundleAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        var manifest = await ReadAssetManifestAsync(productId, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            return null;
        }

        ReportStaleSource(productId, manifest.MainJs);

        RazerMappingBundle? bundle = null;
        if (manifest.MainJs is { Length: > 0 } mainJs)
        {
            var scriptPath = await CacheRemoteFileAsync(
                productId, mainJs, MaxScriptBytes, "script", cancellationToken).ConfigureAwait(false);
            if (scriptPath is not null)
            {
                var script = await File.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);
                bundle = RazerMappingBundleParser.Parse(script, manifest.Image3x);
                if (bundle.Image3x is { Length: > 0 } image)
                {
                    var imagePath = await CacheRemoteFileAsync(
                        productId, image, MaxImageBytes, "image", cancellationToken).ConfigureAwait(false);
                    if (imagePath is not null)
                    {
                        if (bundle.MouseAnchors.Count == 0)
                        {
                            var imageOnly = await CreateImageOnlyAsync(
                                new Uri(imagePath), cancellationToken).ConfigureAwait(false);
                            var layout = imageOnly is null
                                ? null
                                : ApplyKnownHotspots(productId, imageOnly) with
                                {
                                    ButtonNames = MapButtonNames(productId, bundle.ButtonNamesById),
                                };
                            // A picture without anchors would put every callout on one side.
                            return layout is { Hotspots.Count: > 0 } ? layout : null;
                        }

                        if (await HasExpectedAspectAsync(imagePath, bundle.ImageWidth, bundle.ImageHeight)
                                .ConfigureAwait(false))
                        {
                            var hotspots = new Dictionary<byte, RazerMappingPoint>(bundle.MouseAnchors.Count);
                            foreach (var anchor in bundle.MouseAnchors)
                            {
                                hotspots.TryAdd(
                                    RazerSpecialKeyMap.TryResolveButtonId(
                                        productId, anchor.ButtonId, out var mappedButtonId)
                                        ? mappedButtonId
                                        : anchor.ButtonId,
                                    new RazerMappingPoint(anchor.X, anchor.Y, anchor.IsLeft));
                            }

                            if (hotspots.Count > 0)
                            {
                                return ApplyKnownHotspots(
                                    productId,
                                    new RazerMappingImageLayout(
                                        new Uri(imagePath), bundle.ImageWidth, bundle.ImageHeight, hotspots)) with
                                {
                                    ButtonNames = MapButtonNames(productId, bundle.ButtonNamesById),
                                };
                            }
                        }
                    }
                }
            }
        }

        if (manifest.Svg is { Length: > 0 } svg)
        {
            var svgPath = await CacheRemoteFileAsync(
                productId, svg, MaxImageBytes, "svg", cancellationToken).ConfigureAwait(false);
            if (svgPath is not null)
            {
                var imageOnly = await CreateImageOnlyAsync(new Uri(svgPath), cancellationToken)
                    .ConfigureAwait(false);
                var layout = imageOnly is null || bundle is null
                    ? imageOnly
                    : ApplyBundleAnchors(productId, imageOnly, bundle);
                return layout is { Hotspots.Count: > 0 } ? layout : null;
            }
        }

        return null;
    }

    // DPI anchors come from the bundle's DKM code (32/33), not the profile button id.
    private static IReadOnlyDictionary<byte, string> MapButtonNames(
        ushort productId,
        IReadOnlyDictionary<byte, string> names) =>
        names
            .Select(pair => (
                Id: RazerSpecialKeyMap.TryResolveButtonId(productId, pair.Key, out var mapped) ? mapped : pair.Key,
                pair.Value))
            .DistinctBy(item => item.Id)
            .ToDictionary(item => item.Id, item => item.Value);

    private async Task<RazerMappingKeyboardLayout?> LoadKeyboardAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        // 0244's own page draws these 20 paths over its 3x product photo; use that
        // art instead of the Chroma studio SVG when the photo is available.
        if (RazerKeypadMappingLayoutCatalog.TryGet(productId, out var keypadLayout) &&
            await LoadKeypadBackgroundAsync(productId, cancellationToken).ConfigureAwait(false) is { } background)
        {
            return CreateOfficialKeypadLayout(keypadLayout, background);
        }

        // 0244 declares editions [0, 128]; other catalog keyboards expose 129. Probe 0 first
        // so the existing art keeps winning, then keep both known alternate editions.
        foreach (var edition in new[] { 0, 129, 128 })
        {
            var result = await _studio.GetAsync(productId, edition, cancellationToken).ConfigureAwait(false);
            if (result.Manifest is null)
            {
                continue;
            }

            var visual = StudioKeyboardVisualBuilder.Build(result.Manifest, _studio.TryReadSvgAsset);
            if (visual is { Keys.Count: > 0 })
            {
                return new RazerMappingKeyboardLayout(
                    visual,
                    visual.IsKeypad
                        ? RazerKeyboardKeyCatalog.ResolveKeypad(productId, visual.Keys)
                        : RazerKeyboardKeyCatalog.Resolve(productId, visual.Keys));
            }

            if (result.Manifest.Category?.Equals("KEYPAD", StringComparison.OrdinalIgnoreCase) == true &&
                StudioKeyboardVisualBuilder.BuildKeypad(result.Manifest, _studio.TryReadSvgAsset) is { } keypad)
            {
                return new RazerMappingKeyboardLayout(
                    keypad, RazerKeyboardKeyCatalog.ResolveKeypad(productId, keypad.Keys));
            }
        }

        return null;
    }

    private async Task<Uri?> LoadKeypadBackgroundAsync(
        ushort productId,
        CancellationToken cancellationToken)
    {
        var manifest = await ReadAssetManifestAsync(productId, cancellationToken).ConfigureAwait(false);
        if (manifest?.Image3x is not { Length: > 0 } image)
        {
            return null;
        }

        var path = await CacheRemoteFileAsync(productId, image, MaxImageBytes, "image", cancellationToken)
            .ConfigureAwait(false);
        if (path is null)
        {
            return null;
        }

        // A broken cache entry would leave the stage blank instead of falling back to the SVG.
        var uri = new Uri(path);
        return await CreateImageOnlyAsync(uri, cancellationToken).ConfigureAwait(false) is null
            ? null
            : uri;
    }

    private static RazerMappingKeyboardLayout CreateOfficialKeypadLayout(
        RazerKeypadMappingLayout source,
        Uri background)
    {
        var paths = new StudioSvgPath[source.Keys.Count];
        var visualKeys = new StudioKeyboardKey[source.Keys.Count];
        var buttonIds = new Dictionary<string, byte>(source.Keys.Count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < source.Keys.Count; index++)
        {
            var key = source.Keys[index];
            var pathId = FormattableString.Invariant($"keypad-{key.Id:X2}");
            paths[index] = new StudioSvgPath(
                pathId, key.Path, null, null, 1, StudioSvgRole.Selection, null, null, key.Label, null);
            visualKeys[index] = new StudioKeyboardKey(key.Label, pathId, key.Path, null);
            buttonIds[key.Label] = key.Id;
        }

        var visual = new StudioKeyboardVisual(source.Width, source.Height, paths, visualKeys)
        {
            IsKeypad = true,
        };
        return new RazerMappingKeyboardLayout(visual, buttonIds)
        {
            BackgroundImage = background,
            ScrollImageData = source.ScrollImage,
            DpadImageData = source.DpadImage,
        };
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

        var uri = FormattableString.Invariant($"{UiRoot}{UiPageId(productId)}/ui/asset-manifest.json");
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

    private void ReportStaleSource(ushort productId, string? liveMainJs)
    {
        if (liveMainJs is not { Length: > 0 } ||
            !RazerSpecialKeyMap.TryGetEmbeddedSource(productId, out var embedded) ||
            !_sourceChecked.TryAdd(productId, 0))
        {
            return;
        }

        var live = Path.GetFileName(liveMainJs.Split('?')[0]);
        if (!string.Equals(Path.GetFileName(embedded), live, StringComparison.OrdinalIgnoreCase))
        {
            Log.TryWrite(
                "mapping-catalog",
                $"pid={productId:X4} stale embedded={embedded} live={live}");
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

}

/// <summary>Standard USB HID usage ids used by the mapping bundle for keyboard key ids.</summary>
internal static class RazerKeyboardKeyCatalog
{
    private static readonly IReadOnlyDictionary<string, byte> Hid = Build();

    public static IReadOnlyDictionary<string, byte> Resolve(
        ushort productId,
        IEnumerable<StudioKeyboardKey> keys)
    {
        var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var firmwareIds = RazerHuntsmanV3ProKeys.UsesFirmwareKeyIds(productId);
        foreach (var key in keys)
        {
            if (TryGet(key.KeyName, out var id))
            {
                result[key.KeyName] = firmwareIds &&
                    RazerHuntsmanV3ProKeys.TryGetFirmwareKeyId(id, out var firmwareKeyId)
                        ? firmwareKeyId
                        : id;
            }
        }

        return result;
    }

    public static IReadOnlyDictionary<string, byte> ResolveKeypad(
        ushort productId,
        IEnumerable<StudioKeyboardKey> keys) =>
        RazerKeypadKeyCatalog.Resolve(productId, keys);

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
