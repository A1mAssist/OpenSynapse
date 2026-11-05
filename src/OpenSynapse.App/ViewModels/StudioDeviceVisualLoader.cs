using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Resolves the Chroma device manifest for one connected device and turns it
/// into canvas-ready paths. Results are cached per product id by the caller.
/// </summary>
public sealed class StudioDeviceVisualLoader : IDisposable
{
    private readonly HttpClient _http;
    private readonly StudioManifestService _service;
    private readonly StudioProductImageService _images;

    public StudioDeviceVisualLoader(string? cacheDirectory = null)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _service = new StudioManifestService(
            _http,
            cacheDirectory ?? AppDataPaths.Combine("lighting-studio", "manifests"),
            TimeSpan.FromMinutes(10));
        _images = new StudioProductImageService(
            _http,
            AppDataPaths.Combine("lighting-studio", "product-images"));
    }

    public Task<StudioDeviceVisual?> LoadAsync(DeviceDescriptor device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        return LoadAsync(device.ProductId, device.Id, device.Name, cancellationToken);
    }

    public async Task<StudioDeviceVisual?> LoadAsync(
        ushort productId, string instanceId, string name, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(productId, 0, cancellationToken).ConfigureAwait(false);
        if (result.Manifest is null) return null;
        return StudioDeviceVisualBuilder.Build(result.Manifest, instanceId, name);
    }

    /// <summary>Loads only the LED table; the device card does not need SVG paths.</summary>
    public async Task<StudioLedLayout?> LoadLayoutAsync(ushort productId, CancellationToken cancellationToken)
    {
        if (StudioLegacyLayoutCatalog.TryGet(productId, out var legacy)) return legacy;

        var result = await _service.GetAsync(productId, 0, cancellationToken).ConfigureAwait(false);
        if (result.Manifest?.LedConfigJson is not { Length: > 0 } json)
        {
            return null;
        }

        try
        {
            return StudioLedLayout.Parse(json);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or
            InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    public async Task<StudioProductImage> LoadProductImageAsync(
        ushort productId,
        string? name,
        CancellationToken cancellationToken)
    {
        if (productId == 0) return StudioProductImage.Unavailable(name, "no-product");

        var result = await _service.GetAsync(productId, 0, cancellationToken).ConfigureAwait(false);
        var coverProductId = result.Manifest?.CoverProductId ?? productId;
        return await _images.GetAsync(coverProductId, name, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}
