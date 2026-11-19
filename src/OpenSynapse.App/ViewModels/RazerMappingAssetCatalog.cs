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
    IReadOnlyDictionary<byte, RazerMappingPoint> Hotspots)
{
    public IReadOnlyDictionary<byte, string>? ButtonNames { get; init; }
}

/// <summary>A keyboard SVG plus the firmware key id resolved for each key name.</summary>
public sealed record RazerMappingKeyboardLayout(
    StudioKeyboardVisual Visual,
    IReadOnlyDictionary<string, byte> ButtonIdsByKeyName)
{
    /// <summary>Official product photo drawn under the key paths; null for a Chroma SVG.</summary>
    public Uri? BackgroundImage { get; init; }

    /// <summary>Embedded Synapse 2 photo used when Razer's product CDN has no page.</summary>
    public string? BackgroundImageData { get; init; }

    /// <summary>Official wheel widget art, base64 PNG; null when the product has no wheel widget.</summary>
    public string? ScrollImageData { get; init; }

    /// <summary>Official D-pad centre art, base64 PNG; null when the product has no D-pad widget.</summary>
    public string? DpadImageData { get; init; }
}

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
/// Hotspots that the product bundle itself does not expose. Coordinates are
/// source-image pixels, taken from the official mapping page's drawLines().
/// </summary>
internal static class RazerMappingKnownHotspots
{
    // Razer Viper (0078): products/120/ui/static/js/main.90154a92.js.
    // drawLines() authors a 310x340 canvas; the 3x product image is 900x1024.
    // 0x04/0x05 are the left side buttons; 0x06/0x07 are the right side buttons.
    // 0x0E is the DPI button under the wheel.
    internal static IReadOnlyDictionary<byte, RazerMappingPoint> Viper { get; } =
        new Dictionary<byte, RazerMappingPoint>
        {
            [0x01] = new(349.5, 166.5, true),
            [0x02] = new(550.5, 166.5, false),
            [0x03] = new(451.5, 259.5, true),
            [0x04] = new(271.5, 541.5, true),
            [0x05] = new(265.5, 427.5, true),
            [0x06] = new(631.5, 541.5, false),
            [0x07] = new(637.5, 427.5, false),
            [0x09] = new(451.5, 208.5, false),
            [0x0A] = new(451.5, 313.5, false),
            [0x0E] = new(451.5, 751.5, true),
        };
}
