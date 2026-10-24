using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;

namespace OpenSynapse.App;

/// <summary>
/// Visual path editor for a recorded mouse trajectory. The point list beside
/// this control is the exact-value editor; the plot only changes the selected
/// point so a long recording does not create thousands of handles.
/// </summary>
public sealed partial class MousePathPlot : UserControl
{
    private const double PlotLeftMargin = 58;
    private const double PlotRightMargin = 20;
    private const double PlotTopMargin = 24;
    private const double PlotBottomMargin = 42;
    private const double HandleSize = 14;
    private const double PointMarkerSize = 6;
    private const double HitRadius = 12;
    private const int MaxPointMarkers = 240;

    private readonly List<Line> _verticalGridLines = [];
    private readonly List<Line> _horizontalGridLines = [];
    private readonly List<Ellipse> _pointMarkers = [];
    private readonly Rectangle _plotBackground = new();
    private readonly Line _xAxis = new();
    private readonly Line _yAxis = new();
    private readonly XamlPath _path = new();
    private readonly Ellipse _selectedHandle = new();
    private readonly TextBlock _minXLabel = CreateLabel(TextAlignment.Left);
    private readonly TextBlock _maxXLabel = CreateLabel(TextAlignment.Right);
    private readonly TextBlock _minYLabel = CreateLabel(TextAlignment.Left);
    private readonly TextBlock _maxYLabel = CreateLabel(TextAlignment.Left);
    private readonly TextBlock _xAxisLabel = CreateLabel(TextAlignment.Right);
    private readonly TextBlock _yAxisLabel = CreateLabel(TextAlignment.Left);
    private MousePathEditorViewModel? _editor;
    private int _draggedIndex = -1;

    public MousePathPlot()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Localized.RefreshTree(this);
            Render();
        };
        SizeChanged += (_, _) => Render();
        DataContextChanged += (_, _) => Attach(DataContext as MousePathEditorViewModel);
        PlotCanvas.PointerPressed += PlotPointerPressed;
        PlotCanvas.PointerMoved += PlotPointerMoved;
        PlotCanvas.PointerReleased += PlotPointerReleased;
        PlotCanvas.PointerCaptureLost += (_, _) => _draggedIndex = -1;
        Attach(DataContext as MousePathEditorViewModel);
        BuildStaticShapes();
    }

    private void BuildStaticShapes()
    {
        var gridBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        var axisBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
        var accent = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x99, 0xDD, 0x72));
        var handleSurface = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20));

        _plotBackground.Fill = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF));
        _plotBackground.Stroke = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        _plotBackground.StrokeThickness = 1;
        _plotBackground.RadiusX = 6;
        _plotBackground.RadiusY = 6;
        _plotBackground.IsHitTestVisible = false;
        Canvas.SetZIndex(_plotBackground, 0);
        PlotCanvas.Children.Add(_plotBackground);

        for (var index = 0; index < 5; index++)
        {
            var vertical = new Line { Stroke = gridBrush, StrokeThickness = 1, IsHitTestVisible = false };
            var horizontal = new Line { Stroke = gridBrush, StrokeThickness = 1, IsHitTestVisible = false };
            Canvas.SetZIndex(vertical, 1);
            Canvas.SetZIndex(horizontal, 1);
            _verticalGridLines.Add(vertical);
            _horizontalGridLines.Add(horizontal);
            PlotCanvas.Children.Add(vertical);
            PlotCanvas.Children.Add(horizontal);
        }

        foreach (var axis in new[] { _xAxis, _yAxis })
        {
            axis.Stroke = axisBrush;
            axis.StrokeThickness = 1.5;
            axis.IsHitTestVisible = false;
            Canvas.SetZIndex(axis, 2);
            PlotCanvas.Children.Add(axis);
        }

        foreach (var label in new[] { _minXLabel, _maxXLabel, _minYLabel, _maxYLabel, _xAxisLabel, _yAxisLabel })
        {
            Canvas.SetZIndex(label, 3);
            PlotCanvas.Children.Add(label);
        }

        _path.Stroke = accent;
        _path.StrokeThickness = 2;
        _path.StrokeLineJoin = PenLineJoin.Round;
        _path.IsHitTestVisible = false;
        Canvas.SetZIndex(_path, 4);
        PlotCanvas.Children.Add(_path);

        _selectedHandle.Width = HandleSize;
        _selectedHandle.Height = HandleSize;
        _selectedHandle.Fill = handleSurface;
        _selectedHandle.Stroke = accent;
        _selectedHandle.StrokeThickness = 2;
        _selectedHandle.IsHitTestVisible = false;
        Canvas.SetZIndex(_selectedHandle, 6);
        PlotCanvas.Children.Add(_selectedHandle);
    }

    private void Attach(MousePathEditorViewModel? editor)
    {
        if (ReferenceEquals(_editor, editor))
        {
            return;
        }

        if (_editor is not null)
        {
            _editor.PlotChanged -= EditorChanged;
        }

        _editor = editor;
        if (_editor is not null)
        {
            _editor.PlotChanged += EditorChanged;
        }

        Render();
    }

    private void EditorChanged(object? sender, EventArgs e) => Render();

    private void Render()
    {
        var width = PlotCanvas.ActualWidth;
        var height = PlotCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var plotLeft = PlotLeftMargin;
        var plotTop = PlotTopMargin;
        var plotWidth = width - PlotLeftMargin - PlotRightMargin;
        var plotHeight = height - PlotTopMargin - PlotBottomMargin;
        if (plotWidth < 1 || plotHeight < 1)
        {
            return;
        }

        _plotBackground.Width = plotWidth;
        _plotBackground.Height = plotHeight;
        Canvas.SetLeft(_plotBackground, plotLeft);
        Canvas.SetTop(_plotBackground, plotTop);

        for (var index = 0; index < _verticalGridLines.Count; index++)
        {
            var x = plotLeft + (plotWidth * index / (_verticalGridLines.Count - 1));
            var line = _verticalGridLines[index];
            line.X1 = x;
            line.X2 = x;
            line.Y1 = plotTop;
            line.Y2 = plotTop + plotHeight;
        }

        for (var index = 0; index < _horizontalGridLines.Count; index++)
        {
            var y = plotTop + (plotHeight * index / (_horizontalGridLines.Count - 1));
            var line = _horizontalGridLines[index];
            line.X1 = plotLeft;
            line.X2 = plotLeft + plotWidth;
            line.Y1 = y;
            line.Y2 = y;
        }

        _xAxis.X1 = plotLeft;
        _xAxis.X2 = plotLeft + plotWidth;
        _xAxis.Y1 = plotTop + plotHeight;
        _xAxis.Y2 = plotTop + plotHeight;
        _yAxis.X1 = plotLeft;
        _yAxis.X2 = plotLeft;
        _yAxis.Y1 = plotTop;
        _yAxis.Y2 = plotTop + plotHeight;

        if (_editor is null)
        {
            EmptyHint.Visibility = Visibility.Visible;
            _selectedHandle.Visibility = Visibility.Collapsed;
            _path.Data = null;
            UpdateMarkers([], []);
            _minXLabel.Text = string.Empty;
            _maxXLabel.Text = string.Empty;
            _minYLabel.Text = string.Empty;
            _maxYLabel.Text = string.Empty;
            _xAxisLabel.Text = string.Empty;
            _yAxisLabel.Text = string.Empty;
            return;
        }

        var points = _editor.Points;
        var hasPoints = points.Count > 0;
        EmptyHint.Visibility = hasPoints ? Visibility.Collapsed : Visibility.Visible;
        _selectedHandle.Visibility = hasPoints && _editor.SelectedPoint is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!hasPoints)
        {
            _path.Data = null;
            UpdateMarkers([], []);
            _minXLabel.Text = string.Empty;
            _maxXLabel.Text = string.Empty;
            _minYLabel.Text = string.Empty;
            _maxYLabel.Text = string.Empty;
            _xAxisLabel.Text = string.Empty;
            _yAxisLabel.Text = string.Empty;
            return;
        }

        var minX = _editor.PlotMinX;
        var maxX = _editor.PlotMaxX;
        var minY = _editor.PlotMinY;
        var maxY = _editor.PlotMaxY;
        var spanX = Math.Max(0.001, maxX - minX);
        var spanY = Math.Max(0.001, maxY - minY);
        var mapped = points
            .Select(point => new Point(
                plotLeft + ((point.X - minX) / spanX) * plotWidth,
                plotTop + ((maxY - point.Y) / spanY) * plotHeight))
            .ToArray();

        var figure = new PathFigure { StartPoint = mapped[0] };
        if (mapped.Length > 1)
        {
            var segment = new PolyLineSegment();
            foreach (var point in mapped.Skip(1))
            {
                segment.Points.Add(point);
            }

            figure.Segments.Add(segment);
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _path.Data = geometry;

        UpdateMarkers(points, mapped);
        if (_editor.SelectedPoint is { } selected)
        {
            var selectedIndex = points.IndexOf(selected);
            if (selectedIndex >= 0)
            {
                PlaceHandle(_selectedHandle, mapped[selectedIndex]);
            }
        }

        _minXLabel.Text = FormatValue(minX);
        _maxXLabel.Text = FormatValue(maxX);
        _minYLabel.Text = FormatValue(minY);
        _maxYLabel.Text = FormatValue(maxY);
        _xAxisLabel.Text = "X";
        _yAxisLabel.Text = "Y";
        PlaceLabels(plotLeft, plotTop, plotWidth, plotHeight);
    }

    private void UpdateMarkers(
        IReadOnlyList<MousePathPointViewModel> points,
        IReadOnlyList<Point> positions)
    {
        var showMarkers = positions.Count <= MaxPointMarkers;
        var targetCount = showMarkers ? positions.Count : 0;
        while (_pointMarkers.Count > targetCount)
        {
            var marker = _pointMarkers[^1];
            _pointMarkers.RemoveAt(_pointMarkers.Count - 1);
            PlotCanvas.Children.Remove(marker);
        }

        while (_pointMarkers.Count < targetCount)
        {
            var marker = new Ellipse
            {
                Width = PointMarkerSize,
                Height = PointMarkerSize,
                Fill = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x99, 0xDD, 0x72)),
                Stroke = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            };
            Canvas.SetZIndex(marker, 5);
            _pointMarkers.Add(marker);
            PlotCanvas.Children.Add(marker);
        }

        for (var index = 0; index < targetCount; index++)
        {
            PlaceHandle(_pointMarkers[index], positions[index]);
        }
    }

    private static void PlaceHandle(FrameworkElement element, Point position)
    {
        Canvas.SetLeft(element, position.X - (element.Width / 2));
        Canvas.SetTop(element, position.Y - (element.Height / 2));
    }

    private void PlaceLabels(double left, double top, double width, double height)
    {
        Canvas.SetLeft(_minXLabel, left);
        Canvas.SetTop(_minXLabel, top + height + 18);
        _minXLabel.Width = 96;

        Canvas.SetLeft(_maxXLabel, left + width - 96);
        Canvas.SetTop(_maxXLabel, top + height + 18);
        _maxXLabel.Width = 96;

        Canvas.SetLeft(_minYLabel, 2);
        Canvas.SetTop(_minYLabel, top + height - 12);
        _minYLabel.Width = 52;

        Canvas.SetLeft(_maxYLabel, 2);
        Canvas.SetTop(_maxYLabel, top - 12);
        _maxYLabel.Width = 52;

        Canvas.SetLeft(_xAxisLabel, left + width - 14);
        Canvas.SetTop(_xAxisLabel, top + height + 18);
        _xAxisLabel.Width = 14;

        Canvas.SetLeft(_yAxisLabel, left - 14);
        Canvas.SetTop(_yAxisLabel, top - 20);
        _yAxisLabel.Width = 14;
    }

    private void PlotPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_editor is null || !TryHitPoint(e.GetCurrentPoint(PlotCanvas).Position, out var index))
        {
            return;
        }

        _editor.SelectedPoint = _editor.Points[index];
        _draggedIndex = index;
        PlotCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void PlotPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_editor is null || _draggedIndex < 0)
        {
            return;
        }

        var position = e.GetCurrentPoint(PlotCanvas).Position;
        var width = PlotCanvas.ActualWidth - PlotLeftMargin - PlotRightMargin;
        var height = PlotCanvas.ActualHeight - PlotTopMargin - PlotBottomMargin;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var dataX = _editor.PlotMinX +
            (Math.Clamp(position.X - PlotLeftMargin, 0, width) / width) *
            (_editor.PlotMaxX - _editor.PlotMinX);
        var dataY = _editor.PlotMaxY -
            (Math.Clamp(position.Y - PlotTopMargin, 0, height) / height) *
            (_editor.PlotMaxY - _editor.PlotMinY);
        _editor.MovePoint(_draggedIndex, (int)Math.Round(dataX), (int)Math.Round(dataY));
        e.Handled = true;
    }

    private void PlotPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_draggedIndex < 0)
        {
            return;
        }

        _draggedIndex = -1;
        PlotCanvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private bool TryHitPoint(Point position, out int index)
    {
        index = -1;
        if (_editor is null || _editor.Points.Count == 0)
        {
            return false;
        }

        var width = PlotCanvas.ActualWidth - PlotLeftMargin - PlotRightMargin;
        var height = PlotCanvas.ActualHeight - PlotTopMargin - PlotBottomMargin;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var minX = _editor.PlotMinX;
        var maxX = _editor.PlotMaxX;
        var minY = _editor.PlotMinY;
        var maxY = _editor.PlotMaxY;
        var spanX = Math.Max(0.001, maxX - minX);
        var spanY = Math.Max(0.001, maxY - minY);
        var bestDistance = HitRadius;
        for (var pointIndex = 0; pointIndex < _editor.Points.Count; pointIndex++)
        {
            var point = _editor.Points[pointIndex];
            var x = PlotLeftMargin + ((point.X - minX) / spanX) * width;
            var y = PlotTopMargin + ((maxY - point.Y) / spanY) * height;
            var distance = Math.Sqrt(Math.Pow(position.X - x, 2) + Math.Pow(position.Y - y, 2));
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                index = pointIndex;
            }
        }

        return index >= 0;
    }

    private static string FormatValue(double value) =>
        Math.Abs(value) >= 100
            ? Math.Round(value).ToString("0")
            : Math.Round(value, 1).ToString("0.#");

    private static TextBlock CreateLabel(TextAlignment alignment) => new()
    {
        FontSize = 9,
        FontFamily = new FontFamily("Cascadia Mono,Consolas"),
        Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
        TextAlignment = alignment,
        IsHitTestVisible = false,
    };
}
