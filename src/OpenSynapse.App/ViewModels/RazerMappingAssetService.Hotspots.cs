using System.Linq;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerMappingAssetService
{
    // 0098-style pages ship prd.svg instead of prd-3x; the anchors still come from main.js.
    private static RazerMappingImageLayout ApplyBundleAnchors(
        ushort productId,
        RazerMappingImageLayout layout,
        RazerMappingBundle bundle)
    {
        if (bundle.MouseAnchors.Count == 0)
        {
            return layout;
        }

        var scaleX = layout.SourceWidth / bundle.ImageWidth;
        var scaleY = layout.SourceHeight / bundle.ImageHeight;
        var hotspots = new Dictionary<byte, RazerMappingPoint>(bundle.MouseAnchors.Count);
        foreach (var anchor in bundle.MouseAnchors)
        {
            var art = RazerMappingBundleParser.ToArtSpace(productId, anchor, svgArt: true);
            hotspots.TryAdd(
                ResolveMappingKeyId(
                    productId,
                    RazerSpecialKeyMap.TryResolveButtonId(
                        productId, art.ButtonId, out var mappedButtonId)
                        ? mappedButtonId
                        : art.ButtonId),
                new RazerMappingPoint(art.X * scaleX, art.Y * scaleY, art.IsLeft));
        }

        return hotspots.Count == 0
            ? layout
            : layout with
            {
                Hotspots = hotspots,
                ButtonNames = MapButtonNames(productId, bundle.ButtonNamesById),
            };
    }

    private static RazerMappingImageLayout ApplyKnownHotspots(
        ushort productId,
        RazerMappingImageLayout layout)
    {
        if (productId is not (0x0078 or 0x007A or 0x0091))
        {
            return layout;
        }

        var scaleX = layout.SourceWidth / 900.0;
        var scaleY = layout.SourceHeight / 1024.0;
        var hotspots = RazerMappingKnownHotspots.Viper.ToDictionary(
            pair => pair.Key,
            pair => new RazerMappingPoint(
                pair.Value.X * scaleX,
                pair.Value.Y * scaleY,
                pair.Value.IsLeft));
        return layout with { Hotspots = hotspots };
    }
}
