using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSynapse.Core.Devices;
using Windows.Graphics.Imaging;
using DataWriter = Windows.Storage.Streams.DataWriter;
using InMemoryRandomAccessStream = Windows.Storage.Streams.InMemoryRandomAccessStream;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Decodes the cached product image. AVIF goes through the bundled libavif + dav1d
/// pair because Windows' AVIF codec drops the alpha plane; PNG and SVG keep the
/// platform decoders.
/// </summary>
internal static class RazerMappingImageLoader
{
    public static async Task<ImageSource?> LoadAsync(Uri uri)
    {
        if (uri.LocalPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return new SvgImageSource { UriSource = uri };
        }

        if (uri.LocalPath.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
        {
            return await TryLoadAvifAsync(uri).ConfigureAwait(true);
        }

        try
        {
            return new BitmapImage(uri);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            return null;
        }
    }

    private static async Task<ImageSource?> TryLoadAvifAsync(Uri uri)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(uri.LocalPath).ConfigureAwait(true);
            if (!RazerAvifDecoder.TryDecode(bytes, out var pixels, out var width, out var height))
            {
                return null;
            }

            using var bitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Straight);
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(pixels);
                bitmap.CopyFromBuffer(writer.DetachBuffer());
            }

            using var png = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            png.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(png);
            return image;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
