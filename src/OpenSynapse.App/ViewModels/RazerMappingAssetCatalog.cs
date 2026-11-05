namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Device images are optional. A model appears only when its own captured asset exists;
/// no synthetic outline and no coordinates reused from another model.
/// </summary>
public static class RazerMappingAssetCatalog
{
    public static Uri? FindDeviceImage(ushort productId)
    {
        foreach (var extension in new[] { ".svg", ".png" })
        {
            var name = $"{productId:X4}{extension}";
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "MappingLayouts", name);
            if (File.Exists(path))
            {
                return new Uri(Path.GetFullPath(path));
            }
        }

        return null;
    }
}
