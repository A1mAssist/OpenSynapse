namespace OpenSynapse.App.ViewModels;

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
    /// Hotspots are only supplied for a product whose own Razer UI bundle contains
    /// anchors. The 00B8 values come from product 184's drawLines() table, translated
    /// to the trimmed 3x product image (12 px transparent safety border).
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
                [0x01] = new(96, 52, true),   // LeftClick, Razer (114,58)
                [0x02] = new(282, 52, false), // RightClick, Razer (181,58)
                [0x03] = new(180, 142, true), // ScrollClick, Razer (147,88)
                [0x04] = new(3, 313, true),   // MouseButton4, Razer (88,145)
                [0x05] = new(3, 403, true),   // MouseButton5, Razer (88,175)
                [0x09] = new(183, 103, false),// ScrollUp, Razer (148,75)
                [0x0A] = new(183, 193, false),// ScrollDown, Razer (148,105)
                [0x60] = new(183, 292, false),// CycleUpSensitivityStages, Razer (148,138)
            });
    }
}