namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Device images are optional. A model appears only when its SVG asset was captured
/// with the layout; no synthetic outline or reused coordinates.
/// </summary>
public static class RazerMappingAssetCatalog
{
    public static Uri? FindDeviceImage(ushort productId)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "MappingLayouts",
            $"{productId:X4}.svg");
        return File.Exists(path)
            ? new Uri($"ms-appx:///Assets/MappingLayouts/{productId:X4}.svg")
            : null;
    }
}
