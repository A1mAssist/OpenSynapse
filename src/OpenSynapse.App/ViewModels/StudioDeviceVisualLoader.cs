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

    public StudioDeviceVisualLoader(string? cacheDirectory = null)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _service = new StudioManifestService(
            _http,
            cacheDirectory ?? AppDataPaths.Combine("lighting-studio", "manifests"));
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

    public void Dispose() => _http.Dispose();
}
