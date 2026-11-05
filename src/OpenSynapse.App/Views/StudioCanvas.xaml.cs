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
using static OpenSynapse.Core.Lighting.StudioLedSelection;

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
    private readonly HashSet<(string InstanceId, int Row, int Col)> _penVisited = new();
    private const double SelectionStrokePixels = 2.5;
    private readonly SolidColorBrush _selectionBrush = new(Color.FromArgb(255, 0xB6, 0xFF, 0x6B));
    private readonly SolidColorBrush _marqueeFill = new(Color.FromArgb(56, 0xB6, 0xFF, 0x6B));
    private int _topZIndex;
    private int _moveOriginalZIndex;
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
    private bool _spaceHeld;
    private bool _panActive;
    private Point _panStart;
    private double _panOffsetX;
    private double _panOffsetY;
    public StudioCanvas()
    {
        InitializeComponent();
        IsTabStop = true;
        KeyDown += CanvasKeyDown;
        KeyUp += CanvasKeyUp;
        // The ScrollViewer can mark pointer moves handled while panning; keep receiving them.
        Surface.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(SurfacePointerMoved), true);
        Surface.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(SurfacePointerReleased), true);
        Surface.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(SurfacePointerCaptureLost), true);
        LostFocus += (_, _) =>
        {
            _spaceHeld = false;
            _panActive = false;
            UpdateCursor();
        };
        Loaded += (_, _) => UpdateCursor();
        Refresh();
    }
    public event EventHandler? SelectionChanged;

    /// <summary>Shift-click on one LED: (instanceId, matrix row, matrix column).</summary>
    public event Action<string, int, int>? LedShiftClicked;
    /// <summary>Bucket click: the page resolves the region/device cells and calls SelectLeds.</summary>
    public event Action<string, int, int>? BucketClicked;
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
        _topZIndex = devices.Count;
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
        var zByIndex = StudioCanvasPlacement.LayerOrder(
            sizes.Select(size => (size.Width, size.Height)).ToArray());
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
            BuildDevice(devices[index], x, y, rotation, zByIndex[index]);
        }
        ApplyZoom();
        ReapplyFrames();
        UpdateSelectionVisuals();
#if DEBUG
        var dump = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            var hosts = string.Join(" | ", _hosts.Select(host =>
                $"{host.InstanceId} x={host.BaseX:0.#} y={host.BaseY:0.#} w={host.Width:0.#} h={host.Height:0.#} rot={host.Rotation} kids={host.Element.Children.Count} leds={_ledEntries.Count}"));
            File.AppendAllText(dump,
                $"{Environment.NewLine}canvas zoom={_zoom} surface={Surface.Width:0}x{Surface.Height:0} hosts={_hosts.Count} offset={Scroller.HorizontalOffset:0.#},{Scroller.VerticalOffset:0.#} viewport={Scroller.ViewportWidth:0}x{Scroller.ViewportHeight:0} :: {hosts}");
        }
#endif
    }

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
        if (!Contains(leds, led)) leds.Add(led);
    }

    /// <summary>Selects every LED of every placed device, tagged with its instance.</summary>
    public void SelectAll()
    {
        var next = new List<StudioLedRef>();
        foreach (var entry in _ledEntries)
        {
            AddCell(next, Cell(entry.Host.Device, entry.Led));
        }

        ApplySelection(next);
    }

    private void ApplyZoom()
    {
        var factor = _zoom / 100;
        foreach (var host in _hosts)
        {
            foreach (var path in host.Element.Children.OfType<Path>())
            {
                if (path.Data is { } geometry)
                {
                    geometry.Transform = new ScaleTransform { ScaleX = factor, ScaleY = factor };
                }
            }

            PositionHost(host, factor);
        }

        Surface.Width = StudioCanvasPlacement.DesignSize * factor;
        Surface.Height = StudioCanvasPlacement.DesignSize * factor;
        ScaleGrid(factor);
        if (_selection.Count > 0) UpdateSelectionVisuals();
    }
    private void LedPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Path { Tag: StudioVisualLed led } element) return;
        Focus(FocusState.Programmatic);
#if DEBUG
        PointerQaLog($"led-press space={_spaceHeld} middle={e.GetCurrentPoint(element).Properties.IsMiddleButtonPressed} tool={_tool}");
#endif
        if (TryBeginPan(e)) return;
        if (!e.GetCurrentPoint(element).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        if (element.DataContext is not HostState host) return;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
        {
            LedShiftClicked?.Invoke(host.InstanceId, led.MatrixRow, led.MatrixCol);
            return;
        }

        var erase = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control);
        switch (_tool)
        {
            case StudioCanvasTool.Pen:
                _penActive = true;
                _penVisited.Clear();
                PenToggle(host.Device, led, erase);
                Surface.CapturePointer(e.Pointer);
                break;
            case StudioCanvasTool.Bucket:
                BucketClicked?.Invoke(host.InstanceId, led.MatrixRow, led.MatrixCol);
                break;
            case StudioCanvasTool.Move:
                BeginMove(host, e);
                break;
            default:
                SelectLed(host, led, erase);
                break;
        }
    }
    private static StudioLedRef Cell(StudioDeviceVisual device, StudioVisualLed led) =>
        StudioLedSelection.CellFor(device, led.MatrixRow, led.MatrixCol);
    private void SelectLed(HostState host, StudioVisualLed led, bool additive) =>
        ApplySelection(SelectCell(_selection, Cell(host.Device, led), additive));
    private void PenToggle(StudioDeviceVisual device, StudioVisualLed led, bool erase)
    {
        if (_penVisited.Add((device.InstanceId, led.MatrixRow, led.MatrixCol)))
            ApplySelection(PenCell(_selection, Cell(device, led), erase));
    }
    private void PenAt(Point point, bool erase)
    {
        if (LedAt(point) is { } entry) PenToggle(entry.Host.Device, entry.Led, erase);
    }
    private LedEntry? LedAt(Point point)
    {
        var factor = _zoom / 100;
        return _ledEntries.Find(entry => Contains(entry.AbsoluteBounds(), point.X / factor, point.Y / factor));
    }

    private void BeginMove(HostState host, PointerRoutedEventArgs e)
    {
        _moveHost = host;
        _moveOriginalZIndex = Canvas.GetZIndex(host.Element);
        Canvas.SetZIndex(host.Element, ++_topZIndex);
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
        Canvas.SetZIndex(host.Element, _moveOriginalZIndex);
        host.BaseX = StudioCanvasPlacement.Snap(host.BaseX, host.VisualWidth);
        host.BaseY = StudioCanvasPlacement.Snap(host.BaseY, host.VisualHeight);
        DeviceMoved?.Invoke(host.InstanceId, host.BaseX, host.BaseY);
    }

    private void HostPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Canvas canvas || canvas.DataContext is not HostState host) return;
        Focus(FocusState.Programmatic);
        if (TryBeginPan(e)) return;
        if (!e.GetCurrentPoint(canvas).Properties.IsLeftButtonPressed) return;
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
        Focus(FocusState.Programmatic);
        if (TryBeginPan(e)) return;
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
        var panPoint = e.GetCurrentPoint(Scroller).Position;
#if DEBUG
        if (_panActive) PointerQaLog($"pan-move {panPoint.X:0},{panPoint.Y:0} offset={Scroller.HorizontalOffset:0}");
#endif
        if (_panActive) { PanTo(panPoint); e.Handled = true; return; }
        if (_penActive) { PenAt(point, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)); e.Handled = true; }
        else if (_moveHost is not null) { MoveTo(point); e.Handled = true; }
        else if (_marqueeActive) ShowMarquee(_marqueeStart, ClampToSurface(point));
    }
    private void SurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface).Position;
        var panPoint = e.GetCurrentPoint(Scroller).Position;
#if DEBUG
        if (_panActive) PointerQaLog($"pan-release {panPoint.X:0},{panPoint.Y:0} offset={Scroller.HorizontalOffset:0}");
#endif
        if (_panActive) { PanTo(panPoint); EndPan(e); e.Handled = true; return; }
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
            if (Intersects(entry.AbsoluteBounds(), marquee))
            {
                AddCell(next, Cell(entry.Host.Device, entry.Led));
            }
        }
        ApplySelection(next);
    }
    private void SurfacePointerCaptureLost(object sender, PointerRoutedEventArgs e) => ResetGesture();
    private void SurfacePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) return;
        e.Handled = true;
        var point = e.GetCurrentPoint(Scroller);
        var delta = point.Properties.MouseWheelDelta;
        if (delta != 0) SetZoomStep(NextStep(delta > 0 ? 1 : -1), point.Position);
    }
    private void ResetGesture()
    {
        _marqueeActive = false;
        _penActive = false;
        if (_moveHost is { } host) Canvas.SetZIndex(host.Element, _moveOriginalZIndex);
        _moveHost = null;
        _panActive = false;
        _penVisited.Clear();
        RemoveMarquee();
        UpdateCursor();
    }

    private void ShowMarquee(Point start, Point end)
    {
        _marquee ??= new Rectangle
        {
            IsHitTestVisible = false,
            Stroke = _selectionBrush,
            Fill = _marqueeFill,
        };
        if (_marquee.Parent is null)
        {
            Surface.Children.Add(_marquee);
            Canvas.SetZIndex(_marquee, int.MaxValue);
        }
        _marquee.StrokeThickness = SelectionStrokePixels / Math.Max(_zoom / 100, 0.1);
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
        var factor = _zoom / 100;
        var thickness = SelectionStrokePixels / Math.Max(factor, 0.1);
        foreach (var entry in _ledEntries)
        {
            var selected = IndexOfCell(
                _selection, new StudioLedRef(
                    entry.Led.MatrixRow, entry.Led.MatrixCol, InstanceId: entry.InstanceId)) >= 0;
            if (entry.Selection is { } selection)
            {
                selection.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                if (selected)
                {
                    selection.Stroke = _selectionBrush;
                    selection.StrokeThickness = thickness;
                }
            }
            var usePath = selected && entry.Selection is null;
            entry.Element.Stroke = usePath ? _selectionBrush : entry.Stroke;
            entry.Element.StrokeThickness = usePath ? thickness : entry.Thickness;
        }
    }

    internal static Geometry? ConvertGeometry(string? data)
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
        string? SelectionGroup,
        HostState Host)
    {
        public Rect AbsoluteBounds()
        {
            var x = Bounds.X + Host.BaseX + (Host.VisualWidth - Host.Width) / 2;
            var y = Bounds.Y + Host.BaseY + (Host.VisualHeight - Host.Height) / 2;
            return new Rect(x, y, Bounds.Width, Bounds.Height);
        }
    }
}
