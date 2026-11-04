using Windows.Foundation;

namespace OpenSynapse.App.Views;

public sealed partial class StudioCanvas
{
    public void SetZoom(double percent) => SetZoom(percent, null);

    private void SetZoom(double percent, Point? anchor, bool animate = true)
    {
        if (!double.IsFinite(percent)) return;
        var clamped = Math.Clamp(percent, MinZoom, MaxZoom);
        if (Math.Abs(clamped - _zoom) < 0.001) return;
        var viewportWidth = Scroller.ViewportWidth;
        var viewportHeight = Scroller.ViewportHeight;
        var keepAnchor = viewportWidth > 0 && viewportHeight > 0;
        var anchorX = keepAnchor ? Math.Clamp(anchor?.X ?? viewportWidth / 2, 0, viewportWidth) : 0;
        var anchorY = keepAnchor ? Math.Clamp(anchor?.Y ?? viewportHeight / 2, 0, viewportHeight) : 0;
        var factor = _zoom / 100;
        var designX = keepAnchor ? (Scroller.HorizontalOffset + anchorX) / factor : 0;
        var designY = keepAnchor ? (Scroller.VerticalOffset + anchorY) / factor : 0;
        _zoom = clamped;
        ApplyZoom();
        if (keepAnchor)
        {
            // The scrollable extent only grows after layout; ChangeView would otherwise clamp to the old size.
            Scroller.UpdateLayout();
            var newFactor = _zoom / 100;
            Scroller.ChangeView(
                Math.Max(0, designX * newFactor - anchorX),
                Math.Max(0, designY * newFactor - anchorY),
                null,
                !animate);
        }
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects a preset stop: 10, 25, 50, 100, 200, 300 or 400 percent.</summary>
    public void SetZoomStep(int step) => SetZoomStep(step, null);

    private void SetZoomStep(int step, Point? anchor) =>
        SetZoom(_zoomSteps[Math.Clamp(step, 0, _zoomSteps.Length - 1)], anchor);

    public void ZoomStepUp() => SetZoomStep(NextStep(1), null);

    public void ZoomStepDown() => SetZoomStep(NextStep(-1), null);

    private int NextStep(int direction)
    {
        if (direction > 0)
        {
            for (var index = 0; index < _zoomSteps.Length; index++)
                if (_zoomSteps[index] > _zoom + 0.001) return index;
            return _zoomSteps.Length - 1;
        }

        for (var index = _zoomSteps.Length - 1; index >= 0; index--)
            if (_zoomSteps[index] < _zoom - 0.001) return index;
        return 0;
    }
}
