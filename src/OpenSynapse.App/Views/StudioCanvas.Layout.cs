using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;

namespace OpenSynapse.App.Views;

/// <summary>Viewport fitting and host positioning in design coordinates.</summary>
public sealed partial class StudioCanvas
{
    private const double FitPadding = 160;

    /// <summary>Fits the devices' bounding box, not the empty 10000x10000 canvas.</summary>
    public void FitToView()
    {
        if (_hosts.Count == 0) return;
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var host in _hosts)
        {
            minX = Math.Min(minX, host.BaseX);
            minY = Math.Min(minY, host.BaseY);
            maxX = Math.Max(maxX, host.BaseX + host.VisualWidth);
            maxY = Math.Max(maxY, host.BaseY + host.VisualHeight);
        }

        var width = ActualWidth > 0 ? ActualWidth : 480;
        var height = ActualHeight > 0 ? ActualHeight : 640;
        var viewportWidth = Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : width;
        var viewportHeight = Scroller.ViewportHeight > 0 ? Scroller.ViewportHeight : height;
        var contentWidth = Math.Max(1, maxX - minX + FitPadding * 2);
        var contentHeight = Math.Max(1, maxY - minY + FitPadding * 2);
        SetZoom(Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight) * 100, null, animate: false);
        var factor = _zoom / 100;
        // The scrollable extent only grows after layout; ChangeView would otherwise clamp to the old size.
        Scroller.UpdateLayout();
        var targetX = Math.Max(0, (minX + maxX) / 2 * factor - viewportWidth / 2);
        var targetY = Math.Max(0, (minY + maxY) / 2 * factor - viewportHeight / 2);
        Scroller.ChangeView(targetX, targetY, null, true);
#if DEBUG
        var dump = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            File.AppendAllText(dump,
                $"{Environment.NewLine}fit zoom={_zoom:0.##} box=({minX:0.#},{minY:0.#})-({maxX:0.#},{maxY:0.#}) viewport={viewportWidth:0}x{viewportHeight:0} target={targetX:0.#},{targetY:0.#} offset={Scroller.HorizontalOffset:0.#},{Scroller.VerticalOffset:0.#}");
        }
#endif
    }

    /// <summary>Current design-space position, so a placement created later does not move the device.</summary>
    public bool TryGetPlacement(string instanceId, out double x, out double y)
    {
        foreach (var host in _hosts)
        {
            if (!string.Equals(host.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase)) continue;
            x = host.BaseX;
            y = host.BaseY;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    // One composite keeps rotation and zoom on the same centre; a TransformGroup
    // rotated about the unscaled centre and then scaled about the origin throws the
    // device off-screen at any zoom below 100%.
    private static Transform CreateTransform(double width, double height, int rotation, double factor) =>
        new CompositeTransform
        {
            CenterX = width / 2,
            CenterY = height / 2,
            Rotation = rotation,
            ScaleX = factor,
            ScaleY = factor,
        };

    private static void PositionHost(HostState host, double factor)
    {
        var (left, top) = StudioCanvasGeometry.ScaledHostOrigin(
            host.BaseX, host.BaseY, host.Width, host.Height, host.Rotation, factor);
        Canvas.SetLeft(host.Element, left);
        Canvas.SetTop(host.Element, top);
        host.Element.RenderTransform = CreateTransform(host.Width, host.Height, host.Rotation, factor);
    }
}
