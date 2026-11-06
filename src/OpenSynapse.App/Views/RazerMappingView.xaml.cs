using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace OpenSynapse.App.Views;

public sealed partial class RazerMappingView : UserControl
{
    private RazerHidMappingViewModel? _mapping;
    private bool _connectorsPending;
    private readonly Dictionary<string, Path> _keyboardKeyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<byte, Path> _keyboardPathsByButton = new();

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
            _mapping.PropertyChanged += MappingPropertyChanged;
            DrawKeyboard();
            UpdateKeyboardSelection();
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
        _mapping.PropertyChanged -= MappingPropertyChanged;
        _mapping = null;
        KeyboardCanvas.Children.Clear();
        _keyboardKeyPaths.Clear();
        _keyboardPathsByButton.Clear();
    }

    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestConnectors();

    private void MappingPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RazerHidMappingViewModel.KeyboardLayout))
        {
            DrawKeyboard();
            UpdateKeyboardSelection();
        }
        else if (args.PropertyName == nameof(RazerHidMappingViewModel.SelectedRow))
        {
            UpdateKeyboardSelection();
        }
        else if (args.PropertyName is nameof(RazerHidMappingViewModel.LayoutImage) or
            nameof(RazerHidMappingViewModel.Layout))
        {
            RequestConnectors();
        }
    }

    private void DrawKeyboard()
    {
        KeyboardCanvas.Children.Clear();
        _keyboardKeyPaths.Clear();
        _keyboardPathsByButton.Clear();
        if (Mapping?.KeyboardLayout is not { } layout)
        {
            return;
        }

        KeyboardCanvas.Width = layout.Visual.Width;
        KeyboardCanvas.Height = layout.Visual.Height;
        var ledKeys = layout.Visual.Paths
            .Where(path => path.Role == StudioSvgRole.Led && !string.IsNullOrWhiteSpace(path.KeyName))
            .Select(path => path.KeyName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var source in layout.Visual.Paths.OrderBy(path => StudioCanvasGeometry.RoleOrder(path.Role)))
        {
            if (source.Role == StudioSvgRole.Selection &&
                source.KeyName is { Length: > 0 } selectedKey &&
                ledKeys.Contains(selectedKey))
            {
                continue;
            }

            if (StudioCanvas.ConvertGeometry(source.Data) is not { } geometry)
            {
                continue;
            }

            var element = new Path
            {
                Data = geometry,
                IsHitTestVisible = false,
                Opacity = double.IsFinite(source.Opacity) ? Math.Clamp(source.Opacity, 0, 1) : 1,
            };
            if (StudioCanvasGeometry.ParseColor(source.Fill ?? string.Empty) is { } fill)
            {
                element.Fill = new SolidColorBrush(fill);
            }

            if (StudioCanvasGeometry.ParseColor(source.Stroke ?? string.Empty) is { } stroke)
            {
                element.Stroke = new SolidColorBrush(stroke);
                element.StrokeThickness = 1;
            }

            if (source.Role is StudioSvgRole.Led or StudioSvgRole.Selection &&
                source.KeyName is { Length: > 0 } keyName)
            {
                element.IsHitTestVisible = true;
                element.Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                element.Stroke = Application.Current.Resources["LineStrongBrush"] as Brush;
                element.StrokeThickness = 0.6;
                element.Tag = keyName;
                element.PointerPressed += KeyboardKeyPressed;
                AutomationProperties.SetName(element, keyName);
                ToolTipService.SetToolTip(element, keyName);
                _keyboardKeyPaths[keyName] = element;
                if (layout.ButtonIdsByKeyName.TryGetValue(keyName, out var buttonId))
                {
                    _keyboardPathsByButton[buttonId] = element;
                }
            }

            KeyboardCanvas.Children.Add(element);
        }
    }

    private void KeyboardKeyPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not Path { Tag: string keyName } ||
            Mapping is not { KeyboardLayout: { } layout } mapping ||
            !layout.ButtonIdsByKeyName.TryGetValue(keyName, out var buttonId))
        {
            return;
        }

        var row = mapping.Rows.FirstOrDefault(item => item.ButtonId == buttonId);
        if (row is not null)
        {
            mapping.SelectedRow = row;
        }
    }

    private void UpdateKeyboardSelection()
    {
        var mapping = Mapping;
        if (mapping?.KeyboardLayout is not { } layout)
        {
            return;
        }

        var accent = Application.Current.Resources["AccentBrush"] as Brush ??
            new SolidColorBrush(Color.FromArgb(255, 0x44, 0xD6, 0x2C));
        var accentColor = accent is SolidColorBrush solid
            ? solid.Color
            : Color.FromArgb(255, 0x44, 0xD6, 0x2C);
        var selected = mapping.SelectedRow?.ButtonId;
        foreach (var (keyName, element) in _keyboardKeyPaths)
        {
            var isSelected = selected is { } id &&
                layout.ButtonIdsByKeyName.TryGetValue(keyName, out var mapped) &&
                mapped == id;
            element.Fill = isSelected
                ? new SolidColorBrush(Color.FromArgb(72, accentColor.R, accentColor.G, accentColor.B))
                : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            element.Stroke = isSelected
                ? accent
                : Application.Current.Resources["LineStrongBrush"] as Brush;
            element.StrokeThickness = isSelected ? 1.8 : 0.6;
        }
    }

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

            var target = new Point(
                drawLeft + hotspot.X * scale,
                drawTop + hotspot.Y * scale);
            var labelEdge = container.TransformToVisual(MappingStage)
                .TransformPoint(new Point(
                    left ? container.ActualWidth : 0,
                    container.ActualHeight / 2));
            var start = new Point(labelEdge.X + (left ? 8 : -8), labelEdge.Y);
            var corner = new Point(start.X + (left ? 53 : -53), start.Y);
            AddConnector(start, corner, target, accent);
            AddDot(start, 5, accent, 0.65);
            AddDot(target, 7, accent, 0.95);
            AddRing(target, 13, accent, 0.55);
        }
    }

    private void AddConnector(Point start, Point corner, Point target, Brush accent)
    {
        foreach (var (thickness, opacity) in new[] { (4.0, 0.12), (1.1, 0.72) })
        {
            ConnectorCanvas.Children.Add(new Polyline
            {
                Points = new PointCollection { start, corner, target },
                Stroke = accent,
                StrokeThickness = thickness,
                Opacity = opacity,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            });
        }
    }

    private void AddRing(Point point, double size, Brush brush, double opacity)
    {
        var ring = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = brush,
            StrokeThickness = 1.5,
            Opacity = opacity,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ring, point.X - size / 2);
        Canvas.SetTop(ring, point.Y - size / 2);
        ConnectorCanvas.Children.Add(ring);
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