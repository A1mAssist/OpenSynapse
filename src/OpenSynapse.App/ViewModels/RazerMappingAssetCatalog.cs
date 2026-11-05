namespace OpenSynapse.App.ViewModels;

/// <summary>One button hotspot in source-image pixels.</summary>
public readonly record struct RazerMappingPoint(double X, double Y);

/// <summary>
/// A device image plus the hotspot map measured from that same image. Models without
/// an evidenced image and hotspot map keep the table-only fallback.
/// </summary>
public sealed record RazerMappingImageLayout(
    Uri ImageUri,
    double SourceWidth,
    double SourceHeight,
    IReadOnlyDictionary<byte, RazerMappingPoint> Hotspots);

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

    /// <summary>
    /// Hotspots are only supplied for a product image that was measured directly. The
    /// Viper coordinates are source pixels in the cropped 00B8 PNG.
    /// </summary>
    public static RazerMappingImageLayout? FindLayout(ushort productId)
    {
        if (FindDeviceImage(productId) is not { } image || productId != 0x00B8)
        {
            return null;
        }

        return new RazerMappingImageLayout(
            image,
            193,
            348,
            new Dictionary<byte, RazerMappingPoint>
            {
                [0x01] = new(55, 80),   // left click
                [0x02] = new(140, 80),  // right click
                [0x03] = new(101, 64),  // wheel click
                [0x04] = new(22, 140),  // back
                [0x05] = new(22, 172),  // forward
                [0x09] = new(101, 42),  // wheel up
                [0x0A] = new(101, 86),  // wheel down
                [0x60] = new(101, 112), // DPI cycle up
            });
    }
}