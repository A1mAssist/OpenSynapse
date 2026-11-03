using Microsoft.UI.Xaml.Controls;
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
        SetZoom(Math.Min(viewportWidth / contentWidth, viewportHeight / contentHeight) * 100);
        var factor = _zoom / 100;
        Scroller.ChangeView(
            Math.Max(0, (minX + maxX) / 2 * factor - viewportWidth / 2),
            Math.Max(0, (minY + maxY) / 2 * factor - viewportHeight / 2),
            null,
            true);
    }

    private static void PositionHost(HostState host, double factor)
    {
        Canvas.SetLeft(host.Element, (host.BaseX + (host.VisualWidth - host.Width) / 2) * factor);
        Canvas.SetTop(host.Element, (host.BaseY + (host.VisualHeight - host.Height) / 2) * factor);
        host.Element.RenderTransform = CreateTransform(host.Width, host.Height, host.Rotation, factor);
    }
}
