using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

internal sealed partial class WindowsDisplayCaptureAdapter
{
    private readonly object _regionGate = new();
    private StudioScreenRect _captureRegion = StudioScreenRect.FullScreen;

    /// <summary>Normalized screen region sampled by the next capture frame.</summary>
    internal StudioScreenRect CaptureRegion
    {
        get
        {
            lock (_regionGate) return _captureRegion;
        }
    }

    internal void SetCaptureRegion(StudioScreenRect region)
    {
        var clamped = region.Clamp();
        lock (_regionGate) _captureRegion = clamped;
    }

    internal static AmbientCaptureFrame ReduceEdgeBand(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int stride,
        int edgeBandPixels,
        int outputWidth = BladeLightingLayout.LogicalColumns,
        int outputHeight = BladeLightingLayout.LogicalRows) =>
        ReduceEdgeBand(
            bgra,
            width,
            height,
            stride,
            0,
            0,
            width,
            height,
            edgeBandPixels,
            outputWidth,
            outputHeight);

    internal static AmbientCaptureFrame ReduceEdgeBand(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int stride,
        int cropX,
        int cropY,
        int cropWidth,
        int cropHeight,
        int edgeBandPixels,
        int outputWidth = BladeLightingLayout.LogicalColumns,
        int outputHeight = BladeLightingLayout.LogicalRows)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
        if (stride < checked(width * 4) || bgra.Length < checked(stride * height))
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }
        if (cropX < 0 || cropY < 0 || cropWidth <= 0 || cropHeight <= 0 ||
            (long)cropX + cropWidth > width || (long)cropY + cropHeight > height)
        {
            throw new ArgumentOutOfRangeException(nameof(cropWidth));
        }
        if (edgeBandPixels <= 0 || edgeBandPixels > Math.Min(cropWidth, cropHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(edgeBandPixels));
        }
        if (outputWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputWidth));
        }
        if (outputHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputHeight));
        }

        var pixels = new RazerRgb[checked(outputWidth * outputHeight)];
        for (var row = 0; row < outputHeight; row++)
        {
            for (var column = 0; column < outputWidth; column++)
            {
                var topDistance = (row + 0.5) / outputHeight;
                var bottomDistance = 1 - topDistance;
                var leftDistance = (column + 0.5) / outputWidth;
                var rightDistance = 1 - leftDistance;
                var nearest = Math.Min(Math.Min(topDistance, bottomDistance), Math.Min(leftDistance, rightDistance));

                int x0;
                int x1;
                int y0;
                int y1;
                if (nearest == topDistance)
                {
                    (x0, x1) = Segment(column, outputWidth, cropWidth);
                    x0 += cropX;
                    x1 += cropX;
                    y0 = cropY;
                    y1 = cropY + edgeBandPixels;
                }
                else if (nearest == bottomDistance)
                {
                    (x0, x1) = Segment(column, outputWidth, cropWidth);
                    x0 += cropX;
                    x1 += cropX;
                    y0 = cropY + cropHeight - edgeBandPixels;
                    y1 = cropY + cropHeight;
                }
                else if (nearest == leftDistance)
                {
                    x0 = cropX;
                    x1 = cropX + edgeBandPixels;
                    (y0, y1) = Segment(row, outputHeight, cropHeight);
                    y0 += cropY;
                    y1 += cropY;
                }
                else
                {
                    x0 = cropX + cropWidth - edgeBandPixels;
                    x1 = cropX + cropWidth;
                    (y0, y1) = Segment(row, outputHeight, cropHeight);
                    y0 += cropY;
                    y1 += cropY;
                }

                pixels[row * outputWidth + column] = Average(bgra, stride, x0, x1, y0, y1);
            }
        }
        return new AmbientCaptureFrame(Array.AsReadOnly(pixels), outputWidth, outputHeight);
    }

    private static (int Start, int End) Segment(int index, int segments, int length)
    {
        var start = (int)((long)index * length / segments);
        var end = Math.Max(start + 1, (int)((long)(index + 1) * length / segments));
        return (start, Math.Min(end, length));
    }

    private static RazerRgb Average(
        ReadOnlySpan<byte> bgra,
        int stride,
        int x0,
        int x1,
        int y0,
        int y1)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        var count = checked((x1 - x0) * (y1 - y0));
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var offset = y * stride + x * 4;
                blue += bgra[offset];
                green += bgra[offset + 1];
                red += bgra[offset + 2];
            }
        }
        return new RazerRgb(
            RoundedAverage(red, count),
            RoundedAverage(green, count),
            RoundedAverage(blue, count));
    }

    private static byte RoundedAverage(long value, int count) =>
        checked((byte)((value + count / 2) / count));
}
