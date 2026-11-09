using OpenSynapse.Core.Lighting;
using Windows.Foundation;

namespace OpenSynapse.App.Views;

public sealed partial class StudioCanvas
{
    private double _zoomViewX;
    private double _zoomViewY;
    private bool _zoomViewPending;
    private int _zoomViewGeneration;

    public void SetZoom(double percent) => SetZoom(percent, null);

    private void SetZoom(double percent, Point? anchor, bool animate = false)
    {
        if (!double.IsFinite(percent)) return;
        var clamped = Math.Clamp(percent, MinZoom, MaxZoom);
        if (Math.Abs(clamped - _zoom) < 0.001) return;
        var viewportWidth = Scroller.ViewportWidth;
        var viewportHeight = Scroller.ViewportHeight;
        var offsetX = _zoomViewPending ? _zoomViewX : Scroller.HorizontalOffset;
        var offsetY = _zoomViewPending ? _zoomViewY : Scroller.VerticalOffset;
        var keepAnchor = StudioCanvasPlacement.TryGetAnchoredOffset(
            offsetX,
            offsetY,
            anchor?.X ?? viewportWidth / 2,
            anchor?.Y ?? viewportHeight / 2,
            _zoom,
            clamped,
            viewportWidth,
            viewportHeight,
            out var targetX,
            out var targetY);
        _zoom = clamped;
        ApplyZoom();
        if (keepAnchor)
        {
            // The scrollable extent only grows after layout; ChangeView would otherwise clamp to the old size.
            Scroller.UpdateLayout();
            Scroller.ChangeView(targetX, targetY, null, !animate);
            _zoomViewX = targetX;
            _zoomViewY = targetY;
            _zoomViewPending = true;
            var generation = ++_zoomViewGeneration;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _zoomViewGeneration) return;
                if (Math.Abs(Scroller.HorizontalOffset - targetX) < 0.5 &&
                    Math.Abs(Scroller.VerticalOffset - targetY) < 0.5)
                {
                    _zoomViewPending = false;
                    return;
                }

                Scroller.UpdateLayout();
                Scroller.ChangeView(targetX, targetY, null, true);
                _zoomViewPending =
                    Math.Abs(Scroller.HorizontalOffset - targetX) >= 0.5 ||
                    Math.Abs(Scroller.VerticalOffset - targetY) >= 0.5;
            });
        }
        else
        {
            _zoomViewPending = false;
            _zoomViewGeneration++;
        }
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void CancelPendingZoomView()
    {
        _zoomViewGeneration++;
        _zoomViewPending = false;
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
