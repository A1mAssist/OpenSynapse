using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using Windows.Foundation;

namespace OpenSynapse.App;

/// <summary>
/// Draggable fan-curve plot. X is temperature, Y is fan speed. Each series has its own
/// curve and its own handles, so CPU and GPU can be tuned independently; the tier table
/// stays the place to type exact values.
/// </summary>
public sealed partial class BladeFanCurvePlot : UserControl
{
    private const double AxisMinimumTemperature = 30;
    private const double AxisMaximumTemperature = 120;
    private const double PlotLeftMargin = 64;
    private const double PlotRightMargin = 30;
    private const double PlotTopMargin = 24;
    private const double PlotBottomMargin = 48;
    private const double HandleSize = 12;
    private const double PointMarkerSize = 6;
    private const double HandleGrabRadius = 22;
    private static readonly double[] TemperatureTicks = [30, 45, 60, 75, 90, 105, 120];
    private static readonly double[] RpmTicks = [2000, 2500, 3000, 3500, 4000, 4500, 5000];

    private readonly List<Line> _horizontalGridLines = [];
    private readonly List<Line> _verticalGridLines = [];
    private readonly List<TextBlock> _temperatureTickLabels = [];
    private readonly List<TextBlock> _rpmTickLabels = [];
    private readonly List<Ellipse>[] _pointMarkers = [[], []];
    private readonly List<Polyline> _seriesLines = [new(), new()];
    private readonly List<Ellipse> _handles = [new(), new()];
    private readonly Dictionary<int, int> _seriesHandles = [];
    private readonly Rectangle _plotBackground = new();
    private readonly Line _temperatureAxis = new();
    private readonly Line _rpmAxis = new();
    private readonly TextBlock _rpmUnit = new();
    private readonly TextBlock _temperatureUnit = new();
    private readonly SolidColorBrush[] _seriesBrushes =
    [
        new(global::Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x9C, 0xDB)),
        new(global::Windows.UI.Color.FromArgb(0xFF, 0x99, 0xDD, 0x72)),
    ];

    private BladeFanCurveEditorViewModel? _editor;
    private int _draggedHandle = -1;

    public BladeFanCurvePlot()
    {
        InitializeComponent();
        Loaded += (_, _) => Localized.RefreshTree(this);
        PlotCanvas.PointerPressed += PlotPointerPressed;
        PlotCanvas.PointerMoved += PlotPointerMoved;
        PlotCanvas.PointerReleased += PlotPointerReleased;
        PlotCanvas.PointerCaptureLost += (_, _) => _draggedHandle = -1;
        SizeChanged += (_, _) => Render();
        DataContextChanged += (_, _) => Attach(DataContext as BladeFanCurveEditorViewModel);
        Attach(DataContext as BladeFanCurveEditorViewModel);
        BuildStaticShapes();
    }

    private void BuildStaticShapes()
    {
        _seriesHandles.Clear();

        var gridBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        var axisBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
        var labelBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xA8, 0xFF, 0xFF, 0xFF));
        var surfaceStroke = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x1C, 0x1C));

        _plotBackground.Fill = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF));
        _plotBackground.Stroke = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        _plotBackground.StrokeThickness = 1;
        _plotBackground.RadiusX = 6;
        _plotBackground.RadiusY = 6;
        _plotBackground.IsHitTestVisible = false;
        Canvas.SetZIndex(_plotBackground, 0);
        PlotCanvas.Children.Add(_plotBackground);

        foreach (var _ in TemperatureTicks)
        {
            var gridLine = new Line { Stroke = gridBrush, StrokeThickness = 1, IsHitTestVisible = false };
            Canvas.SetZIndex(gridLine, 1);
            _verticalGridLines.Add(gridLine);
            PlotCanvas.Children.Add(gridLine);
        }

        foreach (var _ in RpmTicks)
        {
            var gridLine = new Line { Stroke = gridBrush, StrokeThickness = 1, IsHitTestVisible = false };
            Canvas.SetZIndex(gridLine, 1);
            _horizontalGridLines.Add(gridLine);
            PlotCanvas.Children.Add(gridLine);
        }

        foreach (var axis in new[] { _temperatureAxis, _rpmAxis })
        {
            axis.Stroke = axisBrush;
            axis.StrokeThickness = 1.5;
            axis.IsHitTestVisible = false;
            Canvas.SetZIndex(axis, 2);
            PlotCanvas.Children.Add(axis);
        }

        ConfigureUnitLabel(_rpmUnit, "RPM", TextAlignment.Right);
        ConfigureUnitLabel(_temperatureUnit, "°C", TextAlignment.Right);

        foreach (var temperature in TemperatureTicks)
        {
            _temperatureTickLabels.Add(CreateTickLabel(temperature.ToString("0"), TextAlignment.Center, labelBrush));
        }

        foreach (var rpm in RpmTicks)
        {
            _rpmTickLabels.Add(CreateTickLabel(rpm.ToString("0"), TextAlignment.Right, labelBrush));
        }

        for (var series = 0; series < _seriesLines.Count; series++)
        {
            var polyline = _seriesLines[series];
            polyline.Stroke = _seriesBrushes[series];
            polyline.StrokeThickness = 2;
            polyline.IsHitTestVisible = false;
            Canvas.SetZIndex(polyline, 4);
            PlotCanvas.Children.Add(polyline);

            var handle = _handles[series];
            handle.Width = HandleSize;
            handle.Height = HandleSize;
            handle.Fill = _seriesBrushes[series];
            handle.Stroke = surfaceStroke;
            handle.StrokeThickness = 2;
            handle.IsHitTestVisible = false;
            Canvas.SetZIndex(handle, 6);
            PlotCanvas.Children.Add(handle);
        }
    }

    private TextBlock CreateTickLabel(string text, TextAlignment alignment, Brush brush)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = 52,
            Height = 16,
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            Foreground = brush,
            TextAlignment = alignment,
            IsHitTestVisible = false,
        };
        Canvas.SetZIndex(label, 3);
        PlotCanvas.Children.Add(label);
        return label;
    }

    private void ConfigureUnitLabel(TextBlock label, string text, TextAlignment alignment)
    {
        label.Text = text;
        label.Height = 16;
        label.FontSize = 9;
        label.FontFamily = new FontFamily("Cascadia Mono,Consolas");
        label.Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        label.TextAlignment = alignment;
        label.IsHitTestVisible = false;
        Canvas.SetZIndex(label, 3);
        PlotCanvas.Children.Add(label);
    }

    private void Attach(BladeFanCurveEditorViewModel? editor)
    {
        if (!ReferenceEquals(_editor, editor))
        {
            if (_editor is not null)
            {
                _editor.PropertyChanged -= EditorChanged;
            }

            _editor = editor;
            if (_editor is not null)
            {
                _editor.PropertyChanged += EditorChanged;
            }
        }

        Render();
    }

    private void EditorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Render();

    private void PlotPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!TryHitHandle(e.GetCurrentPoint(PlotCanvas).Position, out var pointIndex, out var series))
        {
            return;
        }

        _draggedHandle = _seriesHandles[pointIndex];
        _editor!.SelectedIndex = pointIndex;
        PlotCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PlotPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_editor is null || _draggedHandle < 0)
        {
            return;
        }

        var point = _editor.Points[_draggedHandle];
        var position = e.GetCurrentPoint(PlotCanvas).Position;
        var rpm = RpmFromY(position.Y, PlotCanvas.ActualHeight);
        var series = _draggedHandle >> 8;
        if (series == 0)
        {
            point.CpuRpm = rpm;
        }
        else
        {
            point.GpuRpm = rpm;
        }

        Render();
        e.Handled = true;
    }

    private void PlotPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_draggedHandle < 0)
        {
            return;
        }

        _draggedHandle = -1;
        PlotCanvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private bool TryHitHandle(Point position, out int pointIndex, out int series)
    {
        pointIndex = -1;
        series = -1;
        var width = PlotCanvas.ActualWidth;
        var height = PlotCanvas.ActualHeight;
        if (_editor is null || width <= 0 || height <= 0)
        {
            return false;
        }

        for (var index = 0; index < _editor.Points.Count; index++)
        {
            var tier = _editor.Points[index];
            var x = XFromTemperature(tier.TemperatureCelsius, width);
            foreach (var candidate in new[] { 0, 1 })
            {
                var y = YFromRpm(candidate == 0 ? tier.CpuRpm : tier.GpuRpm, height);
                var dx = position.X - x;
                var dy = position.Y - y;
                if (dx * dx + dy * dy <= HandleGrabRadius * HandleGrabRadius)
                {
                    pointIndex = index;
                    series = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private void Render()
    {
        if (PlotCanvas is null)
        {
            return;
        }

        var width = PlotCanvas.ActualWidth;
        var height = PlotCanvas.ActualHeight;
        var hasPoints = _editor is { Points.Count: > 0 };
        EmptyHint.Opacity = hasPoints && width > 0 && height > 0 ? 0 : 1;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var left = PlotLeftMargin;
        var right = width - PlotRightMargin;
        var top = PlotTopMargin;
        var bottom = height - PlotBottomMargin;
        if (right <= left || bottom <= top)
        {
            return;
        }

        Canvas.SetLeft(_plotBackground, left);
        Canvas.SetTop(_plotBackground, top);
        _plotBackground.Width = right - left;
        _plotBackground.Height = bottom - top;

        _rpmAxis.X1 = left;
        _rpmAxis.Y1 = top;
        _rpmAxis.X2 = left;
        _rpmAxis.Y2 = bottom;

        _temperatureAxis.X1 = left;
        _temperatureAxis.Y1 = bottom;
        _temperatureAxis.X2 = right;
        _temperatureAxis.Y2 = bottom;

        for (var index = 0; index < TemperatureTicks.Length; index++)
        {
            var x = XFromTemperature(TemperatureTicks[index], width);
            var gridLine = _verticalGridLines[index];
            gridLine.X1 = x;
            gridLine.Y1 = top;
            gridLine.X2 = x;
            gridLine.Y2 = bottom;

            var label = _temperatureTickLabels[index];
            Canvas.SetLeft(label, x - 26);
            Canvas.SetTop(label, bottom + 6);
        }

        for (var index = 0; index < RpmTicks.Length; index++)
        {
            var y = YFromRpm(RpmTicks[index], height);
            var gridLine = _horizontalGridLines[index];
            gridLine.X1 = left;
            gridLine.Y1 = y;
            gridLine.X2 = right;
            gridLine.Y2 = y;

            var label = _rpmTickLabels[index];
            label.Width = PlotLeftMargin - 12;
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
        }

        _rpmUnit.Width = PlotLeftMargin - 12;
        _rpmUnit.Height = 14;
        Canvas.SetLeft(_rpmUnit, 0);
        Canvas.SetTop(_rpmUnit, 0);

        _temperatureUnit.Width = 28;
        Canvas.SetLeft(_temperatureUnit, right - 28);
        Canvas.SetTop(_temperatureUnit, bottom + 26);

        _seriesHandles.Clear();
        for (var series = 0; series < _seriesLines.Count; series++)
        {
            _seriesLines[series].Points.Clear();
            _handles[series].Visibility = Visibility.Collapsed;
            foreach (var marker in _pointMarkers[series])
            {
                marker.Visibility = Visibility.Collapsed;
            }
        }

        if (!hasPoints)
        {
            return;
        }

        // Each tier gets a small marker; the selected tier also gets a larger draggable handle.
        for (var index = 0; index < _editor!.Points.Count; index++)
        {
            var tier = _editor.Points[index];
            var x = XFromTemperature(tier.TemperatureCelsius, width);
            var cpuY = YFromRpm(tier.CpuRpm, height);
            var gpuY = YFromRpm(tier.GpuRpm, height);
            _seriesLines[0].Points.Add(new Point(x, cpuY));
            _seriesLines[1].Points.Add(new Point(x, gpuY));

            ShowMarker(GetPointMarker(0, index), x, cpuY);
            ShowMarker(GetPointMarker(1, index), x, gpuY);

            if (index == _editor.SelectedIndex || _editor.Points.Count == 1)
            {
                ShowHandle(_handles[0], x, cpuY);
                _seriesHandles[index] = 0;
                ShowHandle(_handles[1], x, gpuY);
                _seriesHandles[index] = 1;
            }
        }

        static void ShowMarker(Ellipse marker, double x, double y)
        {
            Canvas.SetLeft(marker, x - PointMarkerSize / 2);
            Canvas.SetTop(marker, y - PointMarkerSize / 2);
            marker.Visibility = Visibility.Visible;
        }

        static void ShowHandle(Ellipse handle, double x, double y)
        {
            Canvas.SetLeft(handle, x - HandleSize / 2);
            Canvas.SetTop(handle, y - HandleSize / 2);
            handle.Visibility = Visibility.Visible;
        }
    }

    private Ellipse GetPointMarker(int series, int index)
    {
        var markers = _pointMarkers[series];
        while (markers.Count <= index)
        {
            var marker = new Ellipse
            {
                Width = PointMarkerSize,
                Height = PointMarkerSize,
                Fill = _seriesBrushes[series],
                Stroke = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x1C, 0x1C)),
                StrokeThickness = 1,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            Canvas.SetZIndex(marker, 5);
            markers.Add(marker);
            PlotCanvas.Children.Add(marker);
        }

        return markers[index];
    }

    private static double XFromTemperature(double temperature, double width) =>
        PlotLeftMargin + (width - PlotLeftMargin - PlotRightMargin) *
        Math.Clamp(
            (temperature - AxisMinimumTemperature) / (AxisMaximumTemperature - AxisMinimumTemperature),
            0,
            1);

    private static double YFromRpm(double rpm, double height) =>
        PlotTopMargin + (height - PlotTopMargin - PlotBottomMargin) *
        (1 - Math.Clamp(
            (rpm - BladeFanCurve.MinimumPointFanSpeedRpm) /
            (BladeFanCurve.MaximumPointFanSpeedRpm - BladeFanCurve.MinimumPointFanSpeedRpm), 0, 1));

    private static int RpmFromY(double y, double height)
    {
        var fraction = 1 - Math.Clamp(
            (y - PlotTopMargin) / Math.Max(1, height - PlotTopMargin - PlotBottomMargin), 0, 1);
        var raw = BladeFanCurve.MinimumPointFanSpeedRpm +
            fraction * (BladeFanCurve.MaximumPointFanSpeedRpm - BladeFanCurve.MinimumPointFanSpeedRpm);
        return (int)(Math.Round(raw / 100.0) * 100);
    }
}