using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using StorageFile = global::Windows.Storage.StorageFile;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerMappingAssetService
{
    /// <summary>
    /// The drawLine coordinates are authored for the 300 x 340 product frame. A bundle
    /// whose 3x image has another aspect would place every hotspot off the body, so the
    /// card falls back to the table instead of drawing a wrong callout.
    /// </summary>
    private static async Task<(double Width, double Height)?> ReadImageSizeAsync(string imagePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return decoder.PixelWidth > 0 && decoder.PixelHeight > 0
                ? (decoder.PixelWidth, decoder.PixelHeight)
                : null;
        }
        catch (Exception exception) when (exception is COMException or IOException or
            InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<bool> HasExpectedAspectAsync(
        string imagePath, double expectedWidth, double expectedHeight)
    {
        if (expectedWidth <= 0 || expectedHeight <= 0) return true;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (decoder.PixelHeight == 0) return true;
            var actual = (double)decoder.PixelWidth / decoder.PixelHeight;
            var expected = expectedWidth / expectedHeight;
            return Math.Abs(actual - expected) <= expected * 0.02;
        }
        catch (Exception exception) when (exception is COMException or IOException or
            InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

}
