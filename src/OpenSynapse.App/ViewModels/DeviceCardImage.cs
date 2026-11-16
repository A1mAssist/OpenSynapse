using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Razer's dashboard art is a 250 x 140 card canvas; a mouse only fills the middle
/// ~56 px, so the device card would show it at about half the tile height. Crop the
/// transparent padding once and cache the result beside the downloaded image.
/// </summary>
internal static class DeviceCardImage
{
    public static async Task<string> EnsureCroppedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var croppedPath = Path.ChangeExtension(path, ".card-256.png");
        if (File.Exists(croppedPath))
        {
            return croppedPath;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var input = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(input);
            var width = (int)decoder.PixelWidth;
            var height = (int)decoder.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return path;
            }

            var scale = Math.Min(1.0, 256.0 / width);
            var scaledWidth = Math.Max(1, (int)Math.Round(width * scale));
            var scaledHeight = Math.Max(1, (int)Math.Round(height * scale));
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                new BitmapTransform
                {
                    ScaledWidth = (uint)scaledWidth,
                    ScaledHeight = (uint)scaledHeight,
                },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            var pixels = data.DetachPixelData();
            width = scaledWidth;
            height = scaledHeight;
            if (!TryGetContentBounds(pixels, width, height, out var bounds))
            {
                return path;
            }

            if (bounds.Width >= width * 0.9 && bounds.Height >= height * 0.9)
            {
                return path;
            }

            using var bitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8,
                bounds.Width,
                bounds.Height,
                BitmapAlphaMode.Straight);
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(Crop(pixels, width, bounds));
                bitmap.CopyFromBuffer(writer.DetachBuffer());
            }

            using var png = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();
            png.Seek(0);

            var bytes = new byte[png.Size];
            using (var reader = new DataReader(png.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)png.Size);
                reader.ReadBytes(bytes);
            }

            await File.WriteAllBytesAsync(croppedPath, bytes, cancellationToken);
            return croppedPath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or COMException or NotSupportedException)
        {
            return path;
        }
    }

    private static bool TryGetContentBounds(
        byte[] pixels,
        int width,
        int height,
        out (int Width, int Height, int Left, int Top) bounds)
    {
        const byte threshold = 8;
        const int padding = 2;
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + x * 4 + 3] <= threshold)
                {
                    continue;
                }

                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
        {
            bounds = default;
            return false;
        }

        var left = Math.Max(0, minX - padding);
        var top = Math.Max(0, minY - padding);
        var right = Math.Min(width - 1, maxX + padding);
        var bottom = Math.Min(height - 1, maxY + padding);
        bounds = (right - left + 1, bottom - top + 1, left, top);
        return true;
    }

    private static byte[] Crop(
        byte[] pixels,
        int sourceWidth,
        (int Width, int Height, int Left, int Top) bounds)
    {
        var stride = sourceWidth * 4;
        var targetStride = bounds.Width * 4;
        var cropped = new byte[targetStride * bounds.Height];
        for (var y = 0; y < bounds.Height; y++)
        {
            System.Buffer.BlockCopy(
                pixels,
                (bounds.Top + y) * stride + bounds.Left * 4,
                cropped,
                y * targetStride,
                targetStride);
        }

        return cropped;
    }
}
