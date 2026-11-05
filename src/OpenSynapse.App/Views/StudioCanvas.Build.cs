using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using static OpenSynapse.App.Views.StudioCanvasGeometry;

namespace OpenSynapse.App.Views;

/// <summary>Device host construction: SVG paths, LED brushes and hit targets.</summary>
public sealed partial class StudioCanvas
{
    private void BuildDevice(
        StudioDeviceVisual device, double visualX, double visualY, int rotation, int zIndex)
    {
        var width = Extent(device.Width);
        var height = Extent(device.Height);
        var (visualWidth, visualHeight) = StudioCanvasPlacement.VisualExtent(width, height, rotation);
        var host = new Canvas
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        var hostState = new HostState(
            host, device.InstanceId ?? string.Empty, device, width, height, visualWidth, visualHeight,
            rotation, visualX, visualY);
        host.DataContext = hostState;
        host.PointerPressed += HostPointerPressed;
        var ledsByPath = new Dictionary<string, StudioVisualLed>(StringComparer.Ordinal);
        foreach (var led in device.Leds)
        {
            if (!string.IsNullOrEmpty(led.PathId)) ledsByPath.TryAdd(led.PathId, led);
        }
        var elements = new List<(StudioVisualPath Source, Path Element, Geometry Geometry)>();
        var selectionPaths = new Dictionary<string, Path>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in device.Paths.OrderBy(path => RoleOrder(path.Role)))
        {
            if (ConvertGeometry(source.Data) is not { } geometry) continue;
            var element = new Path { Data = geometry, IsHitTestVisible = false };
            if (double.IsFinite(source.Opacity)) element.Opacity = Math.Clamp(source.Opacity, 0, 1);
            if (ToBrush(source.Fill) is { } fill) element.Fill = fill;
            if (ToBrush(source.Stroke) is { } stroke)
            {
                element.Stroke = stroke;
                element.StrokeThickness = 1;
            }
            if (source.Role == StudioSvgRole.Selection)
            {
                element.Fill = null;
                element.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(source.Id)) selectionPaths[source.Id] = element;
            }
            elements.Add((source, element, geometry));
        }
        foreach (var (source, element, geometry) in elements)
        {
            host.Children.Add(element);
            if (source.Role != StudioSvgRole.Led) continue;
            var brush = element.Fill as SolidColorBrush;
            if (brush is null)
            {
                brush = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                element.Fill = brush;
            }
            if (string.IsNullOrEmpty(source.Id) || !ledsByPath.TryGetValue(source.Id, out var led)) continue;
            element.IsHitTestVisible = true;
            element.Tag = led;
            element.PointerPressed += LedPointerPressed;
            _ledEntries.Add(new LedEntry(
                led,
                element,
                brush,
                FindSelectionPath(led, selectionPaths),
                element.Stroke,
                element.StrokeThickness,
                ComputeBounds(geometry, rotation, width, height),
                device.InstanceId ?? string.Empty,
                led.SelectionGroup,
                hostState));
        }
        Surface.Children.Add(host);
        Canvas.SetZIndex(host, zIndex);
        _hosts.Add(hostState);
    }
}
