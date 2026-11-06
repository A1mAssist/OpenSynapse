using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using global::Windows.Storage;
using Buffer = global::Windows.Storage.Streams.Buffer;
using DataReader = global::Windows.Storage.Streams.DataReader;
using DataWriter = global::Windows.Storage.Streams.DataWriter;
using InMemoryRandomAccessStream = global::Windows.Storage.Streams.InMemoryRandomAccessStream;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Decodes the cached product image. The Windows AVIF codec returns the transparent
/// background as opaque black, so AVIF art gets an edge-connected black/white
/// background removed before it reaches WinUI. Other formats go straight to
/// BitmapImage. Art is optional; a decode failure only removes the device picture.
/// </summary>
internal static class RazerMappingImageLoader
{
    public static async Task<ImageSource?> LoadAsync(Uri uri)
    {
        if (uri.LocalPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return new SvgImageSource { UriSource = uri };
        }

        if (uri.LocalPath.EndsWith(".avif", StringComparison.OrdinalIgnoreCase) &&
            await TryLoadAvifAsync(uri).ConfigureAwait(true) is { } avif)
        {
            return avif;
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
            var file = await StorageFile.GetFileFromPathAsync(uri.LocalPath);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var pixels = ReadPixels(bitmap);
            if (pixels is null || !ClearConnectedBackground(pixels, width, height))
            {
                return null;
            }

            using var processed = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Straight);
            WritePixels(processed, pixels);
            using var png = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
            encoder.SetSoftwareBitmap(processed);
            await encoder.FlushAsync();
            png.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(png);
            return image;
        }
        catch (Exception exception) when (exception is COMException or IOException or
            ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[]? ReadPixels(SoftwareBitmap bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var length = stride * bitmap.PixelHeight;
        var buffer = new Buffer((uint)length);
        bitmap.CopyToBuffer(buffer);
        if (buffer.Length == 0)
        {
            buffer.Length = (uint)length;
        }

        var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static void WritePixels(SoftwareBitmap bitmap, byte[] pixels)
    {
        var writer = new DataWriter();
        writer.WriteBytes(pixels);
        bitmap.CopyFromBuffer(writer.DetachBuffer());
    }

    /// <summary>
    /// The codec keeps the alpha channel but reports the transparent pixels as opaque
    /// black. Remove only the black or white region connected to the image edge so an
    /// enclosed dark device body survives.
    /// </summary>
    private static bool ClearConnectedBackground(byte[] pixels, int width, int height)
    {
        if (width < 2 || height < 2) return false;
        var mode = BackgroundMode(pixels, width, height);
        if (mode == 0) return false;

        var queue = new Queue<int>();
        for (var x = 0; x < width; x++)
        {
            Seed(x);
            Seed((height - 1) * width + x);
        }

        for (var y = 1; y < height - 1; y++)
        {
            Seed(y * width);
            Seed(y * width + width - 1);
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var x = index % width;
            var y = index / width;
            if (x > 0) Visit(index - 1);
            if (x + 1 < width) Visit(index + 1);
            if (y > 0) Visit(index - width);
            if (y + 1 < height) Visit(index + width);
        }

        return true;

        void Seed(int index)
        {
            if (pixels[(index * 4) + 3] != 0 && IsBackground(pixels, index, mode))
            {
                pixels[(index * 4) + 3] = 0;
                queue.Enqueue(index);
            }
        }

        void Visit(int index)
        {
            if (pixels[(index * 4) + 3] != 0 && IsBackground(pixels, index, mode))
            {
                pixels[(index * 4) + 3] = 0;
                queue.Enqueue(index);
            }
        }
    }

    private static int BackgroundMode(byte[] pixels, int width, int height)
    {
        var corners = new[]
        {
            0,
            width - 1,
            (height - 1) * width,
            (height * width) - 1,
        };
        var black = true;
        var white = true;
        foreach (var index in corners)
        {
            var max = MaxChannel(pixels, index);
            var min = MinChannel(pixels, index);
            black &= max <= 12;
            white &= min >= 243;
        }

        return black ? 1 : white ? 2 : 0;
    }

    private static bool IsBackground(byte[] pixels, int index, int mode) =>
        mode == 1 ? MaxChannel(pixels, index) <= 12 : MinChannel(pixels, index) >= 243;

    private static byte MaxChannel(byte[] pixels, int index)
    {
        var offset = index * 4;
        return Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2]));
    }

    private static byte MinChannel(byte[] pixels, int index)
    {
        var offset = index * 4;
        return Math.Min(pixels[offset], Math.Min(pixels[offset + 1], pixels[offset + 2]));
    }
}