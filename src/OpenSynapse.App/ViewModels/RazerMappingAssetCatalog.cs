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