namespace OpenSynapse.Core.Lighting;

/// <summary>Pure design-space placement math shared by the canvas and its tests.</summary>
public static class StudioCanvasPlacement
{
    public const double DesignSize = 10000;
    public const double GridStep = 40;

    public static double MaxStart(double extent)
    {
        var size = double.IsFinite(extent) ? Math.Max(0, extent) : 0;
        var available = DesignSize - size;
        return available <= 0 ? 0 : Math.Floor(available / GridStep) * GridStep;
    }

    /// <summary>Maps a viewport anchor through a zoom change, clamped to the new scrollable extent.</summary>
    public static bool TryGetAnchoredOffset(
        double offsetX,
        double offsetY,
        double anchorX,
        double anchorY,
        double oldZoomPercent,
        double newZoomPercent,
        double viewportWidth,
        double viewportHeight,
        out double targetX,
        out double targetY)
    {
        targetX = 0;
        targetY = 0;
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY) ||
            !double.IsFinite(anchorX) || !double.IsFinite(anchorY) ||
            !double.IsFinite(oldZoomPercent) || !double.IsFinite(newZoomPercent) ||
            !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            oldZoomPercent <= 0 || newZoomPercent <= 0 ||
            viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        var oldFactor = oldZoomPercent / 100;
        var newFactor = newZoomPercent / 100;
        var clampedAnchorX = Math.Clamp(anchorX, 0, viewportWidth);
        var clampedAnchorY = Math.Clamp(anchorY, 0, viewportHeight);
        var designX = (offsetX + clampedAnchorX) / oldFactor;
        var designY = (offsetY + clampedAnchorY) / oldFactor;
        var maxX = Math.Max(0, DesignSize * newFactor - viewportWidth);
        var maxY = Math.Max(0, DesignSize * newFactor - viewportHeight);
        targetX = Math.Clamp(designX * newFactor - clampedAnchorX, 0, maxX);
        targetY = Math.Clamp(designY * newFactor - clampedAnchorY, 0, maxY);
        return true;
    }

    public static double Snap(double value, double extent)
    {
        var target = double.IsFinite(value) ? value : 0;
        var snapped = Math.Round(target / GridStep, MidpointRounding.AwayFromZero) * GridStep;
        return Math.Clamp(snapped, 0, MaxStart(extent));
    }

    public static (double Width, double Height) VisualExtent(double width, double height, int rotation) =>
        rotation is 90 or 270 ? (height, width) : (width, height);

    public static (double X, double Y)[] CenterRow(
        IReadOnlyList<(double Width, double Height)> sizes, double gap)
    {
        var result = new (double X, double Y)[sizes.Count];
        if (sizes.Count == 0) return result;
        var total = gap * (sizes.Count - 1);
        foreach (var size in sizes) total += Math.Max(0, size.Width);
        var x = (DesignSize - total) / 2;
        for (var index = 0; index < sizes.Count; index++)
        {
            var size = sizes[index];
            result[index] = (
                Math.Clamp(x, 0, MaxStart(size.Width)),
                Math.Clamp((DesignSize - size.Height) / 2, 0, MaxStart(size.Height)));
            x += size.Width + gap;
        }
        return result;
    }

    /// <summary>Assigns z-order: large footprints at the back, small devices in front.</summary>
    public static int[] LayerOrder(IReadOnlyList<(double Width, double Height)> sizes)
    {
        var order = Enumerable.Range(0, sizes.Count)
            .OrderByDescending(index => Math.Max(0, sizes[index].Width) * Math.Max(0, sizes[index].Height))
            .ThenBy(index => index)
            .ToArray();
        var z = new int[sizes.Count];
        for (var index = 0; index < order.Length; index++) z[order[index]] = index;
        return z;
    }
}
