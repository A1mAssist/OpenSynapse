using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerMappingAssetService
{
    private static async Task<RazerMappingImageLayout?> CreateImageOnlyAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (uri.LocalPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var svg = await File.ReadAllTextAsync(uri.LocalPath, cancellationToken).ConfigureAwait(false);
                var layout = StudioSvgLayoutParser.Parse(svg);
                return layout.Width > 0 && layout.Height > 0
                    ? new RazerMappingImageLayout(
                        uri, layout.Width, layout.Height, new Dictionary<byte, RazerMappingPoint>())
                    : null;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or
                InvalidOperationException or System.Xml.XmlException)
            {
                return null;
            }
        }

        if (uri.LocalPath.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(uri.LocalPath, cancellationToken).ConfigureAwait(false);
                return RazerAvifDecoder.TryDecode(bytes, out _, out var width, out var height)
                    ? new RazerMappingImageLayout(
                        uri, width, height, new Dictionary<byte, RazerMappingPoint>())
                    : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        var size = await ReadImageSizeAsync(uri.LocalPath).ConfigureAwait(false);
        return size is { } value && value.Width > 0 && value.Height > 0
            ? new RazerMappingImageLayout(
                uri, value.Width, value.Height, new Dictionary<byte, RazerMappingPoint>())
            : null;
    }
}
