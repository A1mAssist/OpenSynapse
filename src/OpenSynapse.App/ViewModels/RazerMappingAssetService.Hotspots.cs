using System.Linq;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerMappingAssetService
{
    private static RazerMappingImageLayout ApplyKnownHotspots(
        ushort productId,
        RazerMappingImageLayout layout)
    {
        if (productId != 0x0078)
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
