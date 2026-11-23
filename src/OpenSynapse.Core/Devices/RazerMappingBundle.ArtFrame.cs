namespace OpenSynapse.Core.Devices;

public static partial class RazerMappingBundleParser
{
    /// <summary>
    /// Maps a canvas anchor into the 300 x 340 product art. Razer's 310-wide canvas puts
    /// the photo 5px right of its origin, but prd.svg uses left:calc(50% - 160px), i.e.
    /// 5px left; page 214 (Basilisk V3 Pro 35K) also gives its SVG a 5px top margin.
    /// </summary>
    public static RazerMappingBundleAnchor ToArtSpace(
        ushort productId,
        RazerMappingBundleAnchor anchor,
        bool svgArt)
    {
        var x = anchor.X + (svgArt ? 5d : -5d);
        var y = anchor.Y - (svgArt && productId is 0x00D6 ? 5d : 0d);
        return anchor with { X = x, Y = y };
    }
}
