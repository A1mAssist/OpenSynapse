using System.Security;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using static OpenSynapse.App.Views.StudioCanvasGeometry;

namespace OpenSynapse.App.Views;

public enum StudioCanvasTool { Select, Pen, Bucket, Move }

public delegate void DeviceMovedEventHandler(string instanceId, double x, double y);

/// <summary>
/// Vector device canvas for the lighting studio. The owning page supplies profiles,
/// device visuals and LED frames; this control only renders and reports selection.
/// </summary>
public sealed partial class StudioCanvas : UserControl
{
    private const double RowGap = 120;
    private const double MinZoom = 10;
    private const double MaxZoom = 400;
    private const double MarqueeThreshold = 3;
    private static readonly double[] _zoomSteps = [10, 25, 50, 100, 200, 300, 400];
    private readonly List<HostState> _hosts = new();
    private readonly List<LedEntry> _ledEntries = new();
    private readonly List<StudioLedRef> _selection = new();
    private readonly HashSet<(int Row, int Col)> _penVisited = new();
    private readonly SolidColorBrush _selectionBrush = new(Color.FromArgb(255, 0x44, 0xD6, 0x2C));
    private readonly SolidColorBrush _marqueeFill = new(Color.FromArgb(40, 0x44, 0xD6, 0x2C));
    private StudioCanvasTool _tool;
    private double _zoom = 100;
    private bool _marqueeActive;
    private bool _penActive;
    private Point _marqueeStart;
    private Rectangle? _marquee;
    private HostState? _moveHost;
    private Point _moveStart;
    private double _moveX;
    private double _moveY;
    public StudioCanvas()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateCursor();
        Refresh();
    }
    public event EventHandler? SelectionChanged;
    public event DeviceMovedEventHandler? DeviceMoved;
    public event EventHandler? ZoomChanged;
    public StudioCanvasTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            if (_marqueeActive || _penActive || _moveHost is not null) Surface.ReleasePointerCaptures();
            ResetGesture();
            UpdateCursor();
        }
    }

    public StudioProfile? Profile { get; set; }
    public IReadOnlyList<StudioDeviceVisual> Devices { get; set; } = Array.Empty<StudioDeviceVisual>();
    public IReadOnlyList<StudioLedRef> Selection => _selection;
    public double ZoomPercent => _zoom;
    public int ZoomStepIndex => Array.IndexOf(_zoomSteps, _zoom);

    public void Refresh()
    {
        if (_marqueeActive || _penActive || _moveHost is not null) Surface.ReleasePointerCaptures();
        ResetGesture();
        Surface.Children.Clear();
        _hosts.Clear();
        _ledEntries.Clear();
        var devices = Devices;
        var placements = new Dictionary<string, StudioDevicePlacement>(StringComparer.OrdinalIgnoreCase);
        if (Profile is { } profile)
        {
            foreach (var placement in profile.Devices)
            {
                if (!string.IsNullOrEmpty(placement.InstanceId)) placements[placement.InstanceId] = placement;
            }
        }

        var sizes = new (double Width, double Height, int Rotation)[devices.Count];
        var unplaced = new List<(double Width, double Height)>();
        for (var index = 0; index < devices.Count; index++)
        {
            var id = devices[index].InstanceId ?? string.Empty;
            var rotation = placements.TryGetValue(id, out var placement)
                ? NormalizeRotation(placement.Rotation)
                : 0;
            var (visualWidth, visualHeight) = StudioCanvasPlacement.VisualExtent(
                Extent(devices[index].Width), Extent(devices[index].Height), rotation);
            sizes[index] = (visualWidth, visualHeight, rotation);
            if (!placements.ContainsKey(id)) unplaced.Add((visualWidth, visualHeight));
        }

        RebuildGrid();
        var defaults = StudioCanvasPlacement.CenterRow(unplaced, RowGap);
        var defaultIndex = 0;
        for (var index = 0; index < devices.Count; index++)
        {
            var id = devices[index].InstanceId ?? string.Empty;
            var (visualWidth, visualHeight, rotation) = sizes[index];
            double x;
            double y;
            if (placements.TryGetValue(id, out var placement))
            {
                // Placement X/Y is the visual bounding box top-left, not a centered ratio.
                // Imported values keep their exact coordinate; only the out-of-bounds edge is clamped.
                x = double.IsFinite(placement.X)
                    ? Math.Clamp(placement.X, 0, StudioCanvasPlacement.MaxStart(visualWidth))
                    : 5000;
                y = double.IsFinite(placement.Y)
                    ? Math.Clamp(placement.Y, 0, StudioCanvasPlacement.MaxStart(visualHeight))
                    : 5000;
            }
            else
            {
                (x, y) = defaults[defaultIndex++];
            }
            BuildDevice(devices[index], x, y, rotation);
        }
        ApplyZoom();
        UpdateSelectionVisuals();
    }

    public void SetZoom(double percent)
    {
        if (!double.IsFinite(percent)) return;
        var clamped = Math.Clamp(percent, MinZoom, MaxZoom);
        if (Math.Abs(clamped - _zoom) < 0.001) return;
        var viewportWidth = Scroller.ViewportWidth;
        var viewportHeight = Scroller.ViewportHeight;
        var keepCenter = viewportWidth > 0 && viewportHeight > 0;
        var centerX = keepCenter ? (Scroller.HorizontalOffset + viewportWidth / 2) / (_zoom / 100) : 0;
        var centerY = keepCenter ? (Scroller.VerticalOffset + viewportHeight / 2) / (_zoom / 100) : 0;
        _zoom = clamped;
        ApplyZoom();
        if (keepCenter)
        {
            // The scrollable extent only grows after layout; ChangeView would otherwise clamp to the old size.
            Scroller.UpdateLayout();
            var factor = _zoom / 100;
            Scroller.ChangeView(
                Math.Max(0, centerX * factor - viewportWidth / 2),
                Math.Max(0, centerY * factor - viewportHeight / 2),
                null,
                false);
        }
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects a preset stop: 10, 25, 50, 100, 200, 300 or 400 percent.</summary>
    public void SetZoomStep(int step) => SetZoom(_zoomSteps[Math.Clamp(step, 0, _zoomSteps.Length - 1)]);
    public void ClearSelection()
    {
        if (_selection.Count == 0) return;
        _selection.Clear();
        UpdateSelectionVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectLeds(IEnumerable<StudioLedRef> leds)
    {
        var next = new List<StudioLedRef>();
        foreach (var led in leds) AddCell(next, led);
        ApplySelection(next);
    }
    private static void AddCell(List<StudioLedRef> leds, StudioLedRef led)
    {
        if (IndexOfCell(leds, led.Row, led.Col) < 0) leds.Add(led);
    }

    private void BuildDevice(
        StudioDeviceVisual device, double visualX, double visualY, int rotation)
    {
        var width = Extent(device.Width);
        var height = Extent(device.Height);
        var (visualWidth, visualHeight) = StudioCanvasPlacement.VisualExtent(width, height, rotation);
        var hostX = visualX + (visualWidth - width) / 2;
        var hostY = visualY + (visualHeight - height) / 2;
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
                ComputeBounds(geometry, rotation, width, height, hostX, hostY),
                device.InstanceId ?? string.Empty,
                led.SelectionGroup));
        }
        Surface.Children.Add(host);
        _hosts.Add(hostState);
    }
    private void ApplyZoom()
    {
        var factor = _zoom / 100;
        foreach (var host in _hosts) PositionHost(host, factor);
        Surface.Width = StudioCanvasPlacement.DesignSize * factor;
        Surface.Height = StudioCanvasPlacement.DesignSize * factor;
        ScaleGrid(factor);
    }
    private static Transform CreateTransform(double width, double height, int rotation, double factor)
    {
        if (rotation == 0) return new ScaleTransform { ScaleX = factor, ScaleY = factor };
        var group = new TransformGroup();
        group.Children.Add(new RotateTransform { Angle = rotation, CenterX = width / 2, CenterY = height / 2 });
        group.Children.Add(new ScaleTransform { ScaleX = factor, ScaleY = factor });
        return group;
    }

    private void LedPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Path { Tag: StudioVisualLed led } element ||
            !e.GetCurrentPoint(element).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (element.DataContext is not HostState host) return;
        var erase = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        switch (_tool)
        {
            case StudioCanvasTool.Pen:
                _penActive = true;
                _penVisited.Clear();
                PenToggle(led, erase);
                Surface.CapturePointer(e.Pointer);
                break;
            case StudioCanvasTool.Bucket:
                SelectBucket(host.Device, led);
                break;
            case StudioCanvasTool.Move:
                BeginMove(host, e);
                break;
            default:
                SelectLed(led, erase);
                break;
        }
    }
    private void SelectLed(StudioVisualLed led, bool additive) =>
        ApplySelection(SelectCell(_selection, led.MatrixRow, led.MatrixCol, additive));
    private void PenToggle(StudioVisualLed led, bool erase)
    {
        if (_penVisited.Add((led.MatrixRow, led.MatrixCol)))
            ApplySelection(PenCell(_selection, led.MatrixRow, led.MatrixCol, erase));
    }
    private void PenAt(Point point, bool erase)
    {
        if (LedAt(point) is { } entry) PenToggle(entry.Led, erase);
    }
    private LedEntry? LedAt(Point point)
    {
        var factor = _zoom / 100;
        return _ledEntries.Find(entry => Contains(entry.Bounds, point.X / factor, point.Y / factor));
    }

    private void BeginMove(HostState host, PointerRoutedEventArgs e)
    {
        _moveHost = host;
        var factor = _zoom / 100;
        var point = e.GetCurrentPoint(Surface).Position;
        _moveStart = new Point(point.X / factor, point.Y / factor);
        _moveX = host.BaseX;
        _moveY = host.BaseY;
        Surface.CapturePointer(e.Pointer);
    }
    private void MoveTo(Point point)
    {
        if (_moveHost is not { } host) return;
        var factor = _zoom / 100;
        host.BaseX = StudioCanvasPlacement.Snap(
            _moveX + point.X / factor - _moveStart.X, host.VisualWidth);
        host.BaseY = StudioCanvasPlacement.Snap(
            _moveY + point.Y / factor - _moveStart.Y, host.VisualHeight);
        PositionHost(host, factor);
    }
    private void EndMove()
    {
        if (_moveHost is not { } host) return;
        _moveHost = null;
        host.BaseX = StudioCanvasPlacement.Snap(host.BaseX, host.VisualWidth);
        host.BaseY = StudioCanvasPlacement.Snap(host.BaseY, host.VisualHeight);
        DeviceMoved?.Invoke(host.InstanceId, host.BaseX, host.BaseY);
    }

    private void HostPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Canvas canvas || canvas.DataContext is not HostState host ||
            !e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
        if (_tool == StudioCanvasTool.Move)
        {
            BeginMove(host, e);
            e.Handled = true;
        }
        else if (_tool == StudioCanvasTool.Select)
        {
            BeginMarquee(e.GetCurrentPoint(Surface).Position, e);
            e.Handled = true;
        }
    }

    private void SurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_tool != StudioCanvasTool.Select || !ReferenceEquals(e.OriginalSource, Surface)) return;
        var point = e.GetCurrentPoint(Surface);
        if (!point.Properties.IsLeftButtonPressed) return;
        BeginMarquee(point.Position, e);
        e.Handled = true;
    }
    private void BeginMarquee(Point start, PointerRoutedEventArgs e)
    {
        _marqueeStart = start;
        _marqueeActive = true;
        Surface.CapturePointer(e.Pointer);
        ShowMarquee(start, start);
    }

    private void SurfacePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface).Position;
        if (_penActive) { PenAt(point, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)); e.Handled = true; }
        else if (_moveHost is not null) { MoveTo(point); e.Handled = true; }
        else if (_marqueeActive) ShowMarquee(_marqueeStart, ClampToSurface(point));
    }
    private void SurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface).Position;
        if (_penActive)
        {
            PenAt(point, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control));
            _penActive = false;
            Surface.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            return;
        }
        if (_moveHost is not null)
        {
            MoveTo(point);
            EndMove();
            Surface.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            return;
        }
        if (!_marqueeActive) return;
        _marqueeActive = false;
        var rect = NormalizeRect(_marqueeStart, ClampToSurface(point));
        RemoveMarquee();
        Surface.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
        if (rect.Width < MarqueeThreshold || rect.Height < MarqueeThreshold) return;
        var factor = _zoom / 100;
        var marquee = new Rect(rect.X / factor, rect.Y / factor, rect.Width / factor, rect.Height / factor);
        var next = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)
            ? new List<StudioLedRef>(_selection)
            : new List<StudioLedRef>();
        foreach (var entry in _ledEntries)
        {
            if (Intersects(entry.Bounds, marquee))
            {
                AddCell(next, new StudioLedRef(entry.Led.MatrixRow, entry.Led.MatrixCol));
            }
        }
        ApplySelection(next);
    }
    private void SurfacePointerCaptureLost(object sender, PointerRoutedEventArgs e) => ResetGesture();
    private void SurfacePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) return;
        e.Handled = true;
        var delta = e.GetCurrentPoint(Surface).Properties.MouseWheelDelta;
        if (delta != 0) SetZoom(_zoom + (delta > 0 ? 10 : -10));
    }
    private void ResetGesture()
    {
        _marqueeActive = false;
        _penActive = false;
        _moveHost = null;
        _penVisited.Clear();
        RemoveMarquee();
    }

    private void UpdateCursor()
    {
        if (!IsLoaded) return;
        ProtectedCursor = InputSystemCursor.Create(_tool switch
        {
            StudioCanvasTool.Pen => InputSystemCursorShape.Cross,
            StudioCanvasTool.Bucket => InputSystemCursorShape.Hand,
            StudioCanvasTool.Move => InputSystemCursorShape.SizeAll,
            _ => InputSystemCursorShape.Arrow,
        });
    }
    private void ShowMarquee(Point start, Point end)
    {
        _marquee ??= new Rectangle
        {
            IsHitTestVisible = false,
            Stroke = _selectionBrush,
            StrokeThickness = 1,
            Fill = _marqueeFill,
        };
        if (_marquee.Parent is null) Surface.Children.Add(_marquee);
        var rect = NormalizeRect(start, end);
        Canvas.SetLeft(_marquee, rect.X);
        Canvas.SetTop(_marquee, rect.Y);
        _marquee.Width = rect.Width;
        _marquee.Height = rect.Height;
    }
    private void RemoveMarquee()
    {
        if (_marquee is null) return;
        Surface.Children.Remove(_marquee);
        _marquee = null;
    }
    private Point ClampToSurface(Point point) =>
        ClampPoint(point, Surface.ActualWidth, Surface.ActualHeight);
    private void ApplySelection(List<StudioLedRef> next)
    {
        if (SameSelection(_selection, next)) return;
        _selection.Clear();
        _selection.AddRange(next);
        UpdateSelectionVisuals();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void UpdateSelectionVisuals()
    {
        foreach (var entry in _ledEntries)
        {
            var selected = IndexOfCell(_selection, entry.Led.MatrixRow, entry.Led.MatrixCol) >= 0;
            if (entry.Selection is { } selection)
            {
                selection.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                if (selected)
                {
                    selection.Stroke = _selectionBrush;
                    selection.StrokeThickness = 2;
                }
            }
            var usePath = selected && entry.Selection is null;
            entry.Element.Stroke = usePath ? _selectionBrush : entry.Stroke;
            entry.Element.StrokeThickness = usePath ? 2 : entry.Thickness;
        }
    }

    private static Geometry? ConvertGeometry(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        Geometry? geometry = null;
        try
        {
            geometry = XamlBindingHelper.ConvertValue(typeof(Geometry), data) as Geometry;
        }
        catch (Exception)
        {
            // Vendor path data is untrusted; fall through to the XAML fragment parser.
        }
        if (geometry is null)
        {
            try
            {
                var fragment = "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"" +
                               SecurityElement.Escape(data) + "\" />";
                geometry = (XamlReader.Load(fragment) as Path)?.Data;
            }
            catch (Exception)
            {
                // One malformed path must not abort the whole refresh.
            }
        }
        return geometry;
    }
    private static Brush? ToBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (ParseColor(value) is { } color) return new SolidColorBrush(color);
        try
        {
            return XamlBindingHelper.ConvertValue(typeof(Brush), value) as Brush;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class HostState(
        Canvas element,
        string instanceId,
        StudioDeviceVisual device,
        double width,
        double height,
        double visualWidth,
        double visualHeight,
        int rotation,
        double baseX,
        double baseY)
    {
        public readonly Canvas Element = element;
        public readonly string InstanceId = instanceId;
        public readonly StudioDeviceVisual Device = device;
        public readonly double Width = width;
        public readonly double Height = height;
        public readonly double VisualWidth = visualWidth;
        public readonly double VisualHeight = visualHeight;
        public readonly int Rotation = rotation;
        public double BaseX = baseX;
        public double BaseY = baseY;
    }

    private sealed record LedEntry(
        StudioVisualLed Led,
        Path Element,
        SolidColorBrush Brush,
        Path? Selection,
        Brush? Stroke,
        double Thickness,
        Rect Bounds,
        string InstanceId,
        string? SelectionGroup);
}
