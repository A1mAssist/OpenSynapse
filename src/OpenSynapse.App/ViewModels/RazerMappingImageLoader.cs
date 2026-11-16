using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
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
    private const int MappingDecodeWidth = 330;

    public static async Task<ImageSource?> LoadAsync(Uri uri)
    {
        if (uri.LocalPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return new SvgImageSource
            {
                UriSource = await PrepareSvgAsync(uri).ConfigureAwait(true),
            };
        }

        if (uri.LocalPath.EndsWith(".avif", StringComparison.OrdinalIgnoreCase))
        {
            return await TryLoadAvifAsync(uri).ConfigureAwait(true);
        }

        try
        {
            var image = new BitmapImage { DecodePixelWidth = MappingDecodeWidth };
            image.UriSource = uri;
            return image;
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

            pixels = DownscaleBgra(pixels, width, height, MappingDecodeWidth, out width, out height);
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

    private static byte[] DownscaleBgra(
        byte[] source,
        int width,
        int height,
        int targetWidth,
        out int scaledWidth,
        out int scaledHeight)
    {
        if (targetWidth >= width)
        {
            scaledWidth = width;
            scaledHeight = height;
            return source;
        }

        scaledWidth = targetWidth;
        scaledHeight = Math.Max(1, (int)Math.Round(height * (double)targetWidth / width));
        var target = new byte[scaledWidth * scaledHeight * 4];
        for (var y = 0; y < scaledHeight; y++)
        {
            var sourceTop = y * height / scaledHeight;
            var sourceBottom = Math.Max(sourceTop + 1, (y + 1) * height / scaledHeight);
            for (var x = 0; x < scaledWidth; x++)
            {
                var sourceLeft = x * width / scaledWidth;
                var sourceRight = Math.Max(sourceLeft + 1, (x + 1) * width / scaledWidth);
                var blue = 0;
                var green = 0;
                var red = 0;
                var alpha = 0;
                var count = 0;
                for (var sourceY = sourceTop; sourceY < sourceBottom; sourceY++)
                {
                    var row = sourceY * width * 4;
                    for (var sourceX = sourceLeft; sourceX < sourceRight; sourceX++)
                    {
                        var index = row + sourceX * 4;
                        blue += source[index];
                        green += source[index + 1];
                        red += source[index + 2];
                        alpha += source[index + 3];
                        count++;
                    }
                }

                var offset = (y * scaledWidth + x) * 4;
                target[offset] = (byte)(blue / count);
                target[offset + 1] = (byte)(green / count);
                target[offset + 2] = (byte)(red / count);
                target[offset + 3] = (byte)(alpha / count);
            }
        }

        return target;
    }

    private static async Task<Uri> PrepareSvgAsync(Uri uri)
    {
        try
        {
            var text = await File.ReadAllTextAsync(uri.LocalPath).ConfigureAwait(true);
            var flattened = SvgCssStyleFlattener.Flatten(text);
            if (flattened is null) return uri;

            var directory = Path.GetDirectoryName(uri.LocalPath);
            if (string.IsNullOrEmpty(directory)) return uri;

            var path = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(uri.LocalPath) + ".flat.svg");
            await File.WriteAllTextAsync(path, flattened).ConfigureAwait(true);
            return new Uri(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return uri;
        }
    }
}
