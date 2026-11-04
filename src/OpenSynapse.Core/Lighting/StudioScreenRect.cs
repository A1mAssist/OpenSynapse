namespace OpenSynapse.Core.Lighting;

/// <summary>Normalized 0..1 screen region used by ambient capture and effects.</summary>
public readonly record struct StudioScreenRect(double X, double Y, double Width, double Height)
{
    public const double MinimumSize = 0.01;

    public static StudioScreenRect FullScreen => new(0, 0, 1, 1);

    /// <summary>
    /// Keeps a region inside the screen. Non-finite or empty regions fall back to
    /// the full screen; tiny regions grow to <see cref="MinimumSize"/>.
    /// </summary>
    public StudioScreenRect Clamp(double minimumSize = MinimumSize)
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) ||
            !double.IsFinite(Width) || !double.IsFinite(Height) ||
            Width <= 0 || Height <= 0)
        {
            return FullScreen;
        }

        var minimum = double.IsFinite(minimumSize)
            ? Math.Clamp(minimumSize, 0.0001, 1)
            : MinimumSize;
        var width = Math.Clamp(Width, minimum, 1);
        var height = Math.Clamp(Height, minimum, 1);
        var x = Math.Clamp(X, 0, 1 - width);
        var y = Math.Clamp(Y, 0, 1 - height);
        return new StudioScreenRect(
            Math.Round(x, 6),
            Math.Round(y, 6),
            Math.Round(width, 6),
            Math.Round(height, 6));
    }

    /// <summary>
    /// Converts normalized coordinates to physical pixels. The capture frame is
    /// already in physical pixels, so DPI scaling must not be applied here.
    /// </summary>
    public (int X, int Y, int Width, int Height) ToPixelBounds(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        var region = Clamp();
        var left = Math.Clamp((int)Math.Floor(region.X * pixelWidth), 0, pixelWidth - 1);
        var top = Math.Clamp((int)Math.Floor(region.Y * pixelHeight), 0, pixelHeight - 1);
        var right = Math.Clamp(
            CeilingPixel((region.X + region.Width) * pixelWidth),
            left + 1,
            pixelWidth);
        var bottom = Math.Clamp(
            CeilingPixel((region.Y + region.Height) * pixelHeight),
            top + 1,
            pixelHeight);
        return (left, top, right - left, bottom - top);
    }

    private static int CeilingPixel(double value) =>
        (int)Math.Ceiling(value - 1e-9);

    /// <summary>Resolves the effect preset into a normalized screen rectangle.</summary>
    public static StudioScreenRect Resolve(string? screen, StudioScreenRect? custom) =>
        screen?.Trim().ToLowerInvariant() switch
        {
            "left" => new StudioScreenRect(0, 0, 0.1, 1),
            "top" => new StudioScreenRect(0, 0, 1, 0.1),
            "right" => new StudioScreenRect(0.9, 0, 0.1, 1),
            "bottom" => new StudioScreenRect(0, 0.9, 1, 0.1),
            "custom" when custom is { } rect => rect.Clamp(),
            _ => FullScreen,
        };

    /// <summary>
    /// Maps an effect region into the captured region. The result is relative to
    /// the crop, so a full-screen region samples the whole crop. Empty
    /// intersections fall back to the whole crop instead of producing a black frame.
    /// </summary>
    public static StudioScreenRect MapIntoCapture(
        StudioScreenRect region,
        StudioScreenRect capture)
    {
        var bounds = capture.Clamp();
        var target = region.Clamp();
        var left = Math.Max(target.X, bounds.X);
        var top = Math.Max(target.Y, bounds.Y);
        var right = Math.Min(target.X + target.Width, bounds.X + bounds.Width);
        var bottom = Math.Min(target.Y + target.Height, bounds.Y + bounds.Height);
        if (right <= left || bottom <= top) return FullScreen;

        return new StudioScreenRect(
            (left - bounds.X) / bounds.Width,
            (top - bounds.Y) / bounds.Height,
            (right - left) / bounds.Width,
            (bottom - top) / bounds.Height);
    }
}
