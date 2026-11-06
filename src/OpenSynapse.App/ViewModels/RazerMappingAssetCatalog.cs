namespace OpenSynapse.App.ViewModels;

using OpenSynapse.Core.Lighting;

/// <summary>One button hotspot in source-image pixels.</summary>
public readonly record struct RazerMappingPoint(double X, double Y, bool IsLeft);

/// <summary>
/// A device image plus the hotspot map from that product's own Razer UI bundle.
/// Models without an evidenced image and hotspot map keep the table-only fallback.
/// </summary>
public sealed record RazerMappingImageLayout(
    Uri ImageUri,
    double SourceWidth,
    double SourceHeight,
    IReadOnlyDictionary<byte, RazerMappingPoint> Hotspots);

/// <summary>A keyboard SVG plus the firmware key id resolved for each key name.</summary>
public sealed record RazerMappingKeyboardLayout(
    StudioKeyboardVisual Visual,
    IReadOnlyDictionary<string, byte> ButtonIdsByKeyName);

/// <summary>Optional device art. Null members mean the table-only fallback is used.</summary>
public sealed record RazerMappingVisual(
    RazerMappingImageLayout? Mouse,
    RazerMappingKeyboardLayout? Keyboard,
    string? Error)
{
    public static RazerMappingVisual Empty { get; } = new(null, null, null);
    public bool HasVisual => Mouse is not null || Keyboard is not null;
}

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
    /// Offline fallback for 00B8 only. The live path uses the product's own UI bundle;
    /// this captured pair keeps the Viper page usable when the CDN is unreachable.
    /// </summary>
    public static RazerMappingImageLayout? FindLayout(ushort productId)
    {
        if (FindDeviceImage(productId) is not { } image || productId != 0x00B8)
        {
            return null;
        }

        return new RazerMappingImageLayout(
            image,
            408,
            778,
            new Dictionary<byte, RazerMappingPoint>
            {
                [0x01] = new(96, 52, true),
                [0x02] = new(282, 52, false),
                [0x03] = new(180, 142, true),
                [0x04] = new(3, 313, true),
                [0x05] = new(3, 403, true),
                [0x09] = new(183, 103, false),
                [0x0A] = new(183, 193, false),
                [0x60] = new(183, 292, false),
            });
    }
}