using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using Windows.Foundation;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class RazerMappingView : UserControl
{
    private RazerHidMappingViewModel? _mapping;
    private bool _connectorsPending;

    public RazerMappingView()
    {
        InitializeComponent();
        DataContextChanged += MappingDataContextChanged;
        Loaded += MappingLoaded;
        Unloaded += MappingUnloaded;
        SizeChanged += MappingSizeChanged;
        LayoutUpdated += MappingLayoutUpdated;
    }

    private RazerHidMappingViewModel? Mapping => DataContext as RazerHidMappingViewModel;

    private async void ReadClick(object sender, RoutedEventArgs e)
    {
        await (Mapping?.ReadAsync() ?? Task.CompletedTask);
        RequestConnectors();
    }

    private async void ApplyClick(object sender, RoutedEventArgs e) =>
        await (Mapping?.ApplyAsync() ?? Task.CompletedTask);

    private void CalloutClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RazerHidMappingRowViewModel row } &&
            Mapping is { } mapping)
        {
            mapping.SelectedRow = row;
        }
    }

    private void CloseEditorClick(object sender, RoutedEventArgs e)
    {
        if (Mapping is { } mapping)
        {
            mapping.SelectedRow = null;
        }
    }

    private void MappingDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        BindRows();
        RequestConnectors();
    }

    private void MappingLoaded(object sender, RoutedEventArgs e)
    {
        BindRows();
        RequestConnectors();
    }

    private void MappingUnloaded(object sender, RoutedEventArgs e)
    {
        UnbindRows();
        ConnectorCanvas.Children.Clear();
    }

    private void MappingSizeChanged(object sender, SizeChangedEventArgs e) => DrawConnectors();

    private void MappingLayoutUpdated(object? sender, object e)
    {
        if (_connectorsPending)
        {
            DrawConnectors();
        }
    }

    private void BindRows()
    {
        var mapping = Mapping;
        if (ReferenceEquals(_mapping, mapping))
        {
            return;
        }

        UnbindRows();
        _mapping = mapping;
        if (_mapping is not null)
        {
            _mapping.Rows.CollectionChanged += RowsChanged;
            _mapping.LeftRows.CollectionChanged += RowsChanged;
            _mapping.RightRows.CollectionChanged += RowsChanged;
        }
    }

    private void UnbindRows()
    {
        if (_mapping is null)
        {
            return;
        }

        _mapping.Rows.CollectionChanged -= RowsChanged;
        _mapping.LeftRows.CollectionChanged -= RowsChanged;
        _mapping.RightRows.CollectionChanged -= RowsChanged;
        _mapping = null;
    }

    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestConnectors();

    private void RequestConnectors()
    {
        _connectorsPending = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, DrawConnectors);
    }

    private void DrawConnectors()
    {
        _connectorsPending = false;
        ConnectorCanvas.Children.Clear();
        if (Mapping is not { } mapping ||
            mapping.Layout is not { } layout ||
            DeviceImage.ActualWidth <= 0 ||
            DeviceImage.ActualHeight <= 0)
        {
            return;
        }

        var imageBounds = DeviceImage.TransformToVisual(MappingStage).TransformBounds(
            new Rect(0, 0, DeviceImage.ActualWidth, DeviceImage.ActualHeight));
        var scale = Math.Min(
            DeviceImage.ActualWidth / layout.SourceWidth,
            DeviceImage.ActualHeight / layout.SourceHeight);
        if (!(scale > 0))
        {
            return;
        }

        var drawWidth = layout.SourceWidth * scale;
        var drawHeight = layout.SourceHeight * scale;
        var drawLeft = imageBounds.X + (imageBounds.Width - drawWidth) / 2;
        var drawTop = imageBounds.Y + (imageBounds.Height - drawHeight) / 2;
        var accent = Application.Current.Resources["AccentBrush"] as Brush ??
            new SolidColorBrush(Color.FromArgb(255, 0x44, 0xD6, 0x2C));

        DrawSide(mapping.LeftRows, LeftCalloutControl, true, layout, drawLeft, drawTop, scale, accent);
        DrawSide(mapping.RightRows, RightCalloutControl, false, layout, drawLeft, drawTop, scale, accent);
    }

    private void DrawSide(
        IReadOnlyList<RazerHidMappingRowViewModel> rows,
        ItemsControl control,
        bool left,
        RazerMappingImageLayout layout,
        double drawLeft,
        double drawTop,
        double scale,
        Brush accent)
    {
        foreach (var row in rows)
        {
            if (!layout.Hotspots.TryGetValue(row.ButtonId, out var hotspot))
            {
                continue;
            }

            if (control.ContainerFromItem(row) is not FrameworkElement container ||
                container.ActualHeight <= 0)
            {
                _connectorsPending = true;
                continue;
            }

            var start = new Point(
                drawLeft + hotspot.X * scale,
                drawTop + hotspot.Y * scale);
            var labelEdge = container.TransformToVisual(MappingStage)
                .TransformPoint(new Point(
                    left ? container.ActualWidth : 0,
                    container.ActualHeight / 2));
            var end = new Point(labelEdge.X + (left ? 8 : -8), labelEdge.Y);
            ConnectorCanvas.Children.Add(new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = accent,
                StrokeThickness = 4,
                Opacity = 0.12,
                IsHitTestVisible = false,
            });
            ConnectorCanvas.Children.Add(new Line
            {
                X1 = start.X,
                Y1 = start.Y,
                X2 = end.X,
                Y2 = end.Y,
                Stroke = accent,
                StrokeThickness = 1.1,
                Opacity = 0.72,
                IsHitTestVisible = false,
            });
            AddDot(start, 7, accent, 0.9);
            AddDot(end, 5, accent, 0.75);
        }
    }

    private void AddDot(Point point, double size, Brush brush, double opacity)
    {
        var dot = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = brush,
            Opacity = opacity,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(dot, point.X - size / 2);
        Canvas.SetTop(dot, point.Y - size / 2);
        ConnectorCanvas.Children.Add(dot);
    }
}