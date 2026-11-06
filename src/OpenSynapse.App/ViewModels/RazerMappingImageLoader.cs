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
/// Decodes the cached product image. Windows AVIF art has an opaque black backdrop;
/// the mapping card is near-black, so the edge-connected backdrop is flattened to the
/// canvas colour instead of being made transparent. Removing those pixels would eat
/// the black device body; flattening them keeps the silhouette intact.
/// </summary>
internal static class RazerMappingImageLoader
{
    private const byte CanvasB = 0x14;
    private const byte CanvasG = 0x14;
    private const byte CanvasR = 0x14;

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
            if (pixels is null || !LiftDarkBackdrop(pixels, width, height))
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
    /// Lift the black backdrop to the canvas colour. Adding the canvas value keeps
    /// every edge and every enclosed dark area; alpha removal would punch holes in
    /// the black device body.
    /// </summary>
    private static bool LiftDarkBackdrop(byte[] pixels, int width, int height)
    {
        if (width < 2 || height < 2) return false;
        if (BackgroundMode(pixels, width, height) != 1) return false;

        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = (byte)Math.Min(255, pixels[offset] + CanvasB);
            pixels[offset + 1] = (byte)Math.Min(255, pixels[offset + 1] + CanvasG);
            pixels[offset + 2] = (byte)Math.Min(255, pixels[offset + 2] + CanvasR);
        }

        return true;
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
