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
    private const double AxisMinimum = 30;
    private const double AxisMaximum = 120;
    private const double PlotPadding = 24;
    private const double HandleSize = 12;
    private const double HandleGrabRadius = 22;

    private readonly List<Line> _gridLines = [];
    private readonly List<Polyline> _seriesLines = [new(), new()];
    private readonly List<Ellipse> _handles = [new(), new()];
    private readonly Dictionary<int, int> _seriesHandles = [];

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
        foreach (var index in _seriesHandles.Keys.ToArray())
        {
            _seriesHandles.Remove(index);
        }

        var gridBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        for (var line = 0; line < 3; line++)
        {
            var gridLine = new Line { Stroke = gridBrush, StrokeThickness = 1 };
            PlotCanvas.Children.Add(gridLine);
            _gridLines.Add(gridLine);
        }

        foreach (var index in _seriesHandles.Keys.ToArray())
        {
            _seriesHandles.Remove(index);
        }

        for (var series = 0; series < _seriesLines.Count; series++)
        {
            var polyline = _seriesLines[series];
            polyline.Stroke = new SolidColorBrush(series == 0
                ? global::Windows.UI.Color.FromArgb(0xFF, 0x2D, 0x9C, 0xDB)
                : global::Windows.UI.Color.FromArgb(0xFF, 0x99, 0xDD, 0x72));
            polyline.StrokeThickness = 2;
            polyline.IsHitTestVisible = false;
            PlotCanvas.Children.Add(polyline);

            var handle = _handles[series];
            handle.Width = HandleSize;
            handle.Height = HandleSize;
            handle.Fill = (Brush)polyline.Stroke;
            handle.IsHitTestVisible = false;
            PlotCanvas.Children.Add(handle);
        }
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

        for (var index = 0; index < _gridLines.Count; index++)
        {
            var y = PlotPadding + (height - 2 * PlotPadding) * (index + 1) / (_gridLines.Count + 1);
            _gridLines[index].X1 = PlotPadding;
            _gridLines[index].Y1 = y;
            _gridLines[index].X2 = width - PlotPadding;
            _gridLines[index].Y2 = y;
        }

        _seriesHandles.Clear();
        for (var series = 0; series < _seriesLines.Count; series++)
        {
            _seriesLines[series].Points.Clear();
            _handles[series].Visibility = Visibility.Collapsed;
        }

        if (!hasPoints)
        {
            return;
        }

        // Filled circles sit on top; only one handle per series is drawn at the hovered tier.
        for (var index = 0; index < _editor!.Points.Count; index++)
        {
            var tier = _editor.Points[index];
            var x = XFromTemperature(tier.TemperatureCelsius, width);
            var cpuY = YFromRpm(tier.CpuRpm, height);
            var gpuY = YFromRpm(tier.GpuRpm, height);
            _seriesLines[0].Points.Add(new Point(x, cpuY));
            _seriesLines[1].Points.Add(new Point(x, gpuY));

            if (index == _editor.SelectedIndex || _editor.Points.Count == 1)
            {
                ShowHandle(_seriesLines[0], _handles[0], x, cpuY);
                _seriesHandles[index] = 0;
                ShowHandle(_seriesLines[1], _handles[1], x, gpuY);
                _seriesHandles[index] = 1;
            }
        }

        static void ShowHandle(Polyline owner, Ellipse handle, double x, double y)
        {
            Canvas.SetLeft(handle, x - HandleSize / 2);
            Canvas.SetTop(handle, y - HandleSize / 2);
            handle.Visibility = Visibility.Visible;
        }
    }

    private static double XFromTemperature(double temperature, double width) =>
        PlotPadding + (width - 2 * PlotPadding) *
        Math.Clamp((temperature - AxisMinimum) / (AxisMaximum - AxisMinimum), 0, 1);

    private static double YFromRpm(double rpm, double height) =>
        PlotPadding + (height - 2 * PlotPadding) *
        (1 - Math.Clamp(
            (rpm - BladeFanCurve.MinimumPointFanSpeedRpm) /
            (BladeFanCurve.MaximumPointFanSpeedRpm - BladeFanCurve.MinimumPointFanSpeedRpm), 0, 1));

    private static int RpmFromY(double y, double height)
    {
        var fraction = 1 - Math.Clamp(
            (y - PlotPadding) / Math.Max(1, height - 2 * PlotPadding), 0, 1);
        var raw = BladeFanCurve.MinimumPointFanSpeedRpm +
            fraction * (BladeFanCurve.MaximumPointFanSpeedRpm - BladeFanCurve.MinimumPointFanSpeedRpm);
        return (int)(Math.Round(raw / 100.0) * 100);
    }
}