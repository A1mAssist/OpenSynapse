using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using Windows.Foundation;

namespace OpenSynapse.App.Controls;

public sealed partial class EqualizerCurveEditor : UserControl
{
    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands), typeof(object), typeof(EqualizerCurveEditor),
        new PropertyMetadata(null, OnBandsChanged));

    private readonly List<(IEqualizerBand Band, Slider Slider, TextBlock Value)> _items = [];
    private INotifyCollectionChanged? _collection;
    private bool _refreshing;

    public EqualizerCurveEditor()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
        Unloaded += (_, _) => Detach();
        PlotCanvas.SizeChanged += (_, _) => DrawCurve();
        IsEnabledChanged += (_, _) => EditorGrid.Opacity = IsEnabled ? 1 : 0.45;
    }

    public object? Bands
    {
        get => GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    private static void OnBandsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var editor = (EqualizerCurveEditor)sender;
        if (editor.IsLoaded) editor.Rebuild();
    }

    private void Detach()
    {
        if (_collection is not null) _collection.CollectionChanged -= CollectionChanged;
        _collection = null;
        foreach (var item in _items)
        {
            item.Band.PropertyChanged -= BandChanged;
            item.Slider.ValueChanged -= SliderChanged;
        }
        _items.Clear();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => Rebuild();

    private void Rebuild()
    {
        Detach();
        foreach (var grid in new[] { SlidersGrid, LabelsGrid, ValuesGrid })
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
        }
        _collection = Bands as INotifyCollectionChanged;
        if (_collection is not null) _collection.CollectionChanged += CollectionChanged;
        if (Bands is IEnumerable<IEqualizerBand> bands)
        {
            foreach (var band in bands)
            {
                var index = _items.Count;
                foreach (var grid in new[] { SlidersGrid, LabelsGrid, ValuesGrid })
                    grid.ColumnDefinitions.Add(new ColumnDefinition());

                var slider = new Slider
                {
                    Orientation = Orientation.Vertical,
                    Minimum = band.Minimum,
                    Maximum = band.Maximum,
                    StepFrequency = band.Step,
                    SmallChange = band.Step,
                    LargeChange = band.Step * 2,
                    Value = band.Level,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Width = 32,
                    Tag = band,
                    IsThumbToolTipEnabled = false,
                };
                AutomationProperties.SetName(slider, band.Label);
                AutomationProperties.SetAutomationId(slider, $"EqBand{index}");
                AutomationProperties.SetHelpText(slider, band.LevelText);
                ToolTipService.SetToolTip(slider, $"{band.Label} · {band.LevelText}");
                Grid.SetColumn(slider, index);
                SlidersGrid.Children.Add(slider);

                var label = CreateLabel(band.Label);
                label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                Grid.SetColumn(label, index);
                LabelsGrid.Children.Add(label);
                var value = CreateLabel(band.LevelText);
                value.FontSize = 12;
                Grid.SetColumn(value, index);
                ValuesGrid.Children.Add(value);
                _items.Add((band, slider, value));
                slider.ValueChanged += SliderChanged;
                band.PropertyChanged += BandChanged;
            }
        }
        EditorGrid.MinWidth = 56 + _items.Count * 50;
        DrawCurve();
    }

    private static TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        TextAlignment = TextAlignment.Center,
        FontSize = 13,
        Foreground = (Brush)Application.Current.Resources["MutedBrush"],
    };

    private void SliderChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_refreshing || sender is not Slider { Tag: IEqualizerBand band } slider) return;
        band.Level = slider.Value;
        RefreshValues();
    }

    private void BandChanged(object? sender, PropertyChangedEventArgs args) => RefreshValues();

    private void RefreshValues()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            foreach (var item in _items)
            {
                item.Slider.Value = item.Band.Level;
                item.Value.Text = item.Band.LevelText;
                AutomationProperties.SetHelpText(item.Slider, item.Band.LevelText);
                ToolTipService.SetToolTip(item.Slider, $"{item.Band.Label} · {item.Band.LevelText}");
            }
            DrawCurve();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void DrawCurve()
    {
        PlotCanvas.Children.Clear();
        AxisCanvas.Children.Clear();
        var width = PlotCanvas.ActualWidth;
        var height = PlotCanvas.ActualHeight;
        if (_items.Count == 0 || width <= 0 || height <= 12) return;

        // Native Slider endpoints are inset by half of its 12px thumb.
        const double top = 6;
        var bottom = height - top;
        var band = _items[0].Band;
        double Y(double value) => top + (band.Maximum - value) /
            (band.Maximum - band.Minimum) * (bottom - top);
        var lineBrush = (Brush)Application.Current.Resources["LineBrush"];
        var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var cellWidth = width / _items.Count;
        var points = new List<Point>();
        for (var index = 0; index < _items.Count; index++)
        {
            var x = (index + 0.5) * cellWidth;
            PlotCanvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = top, Y2 = bottom,
                Stroke = lineBrush, StrokeThickness = 1, Opacity = 0.65,
            });
            points.Add(new Point(x, Y(_items[index].Band.Level)));
        }

        var baseline = band.Minimum < 0 && band.Maximum > 0 ? 0 : (band.Minimum + band.Maximum) / 2;
        PlotCanvas.Children.Add(new Line
        {
            X1 = 0, X2 = width, Y1 = Y(baseline), Y2 = Y(baseline),
            Stroke = lineBrush, StrokeThickness = 1,
        });
        var fill = new Polygon
        {
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = accent.Color, Offset = 0 },
                    new GradientStop { Color = global::Windows.UI.Color.FromArgb(0, accent.Color.R, accent.Color.G, accent.Color.B), Offset = 1 },
                },
            },
            Opacity = 0.3,
        };
        var curve = new Polyline { Stroke = accent, StrokeThickness = 2 };
        foreach (var point in points)
        {
            fill.Points.Add(point);
            curve.Points.Add(point);
        }
        fill.Points.Add(new Point(points[^1].X, bottom));
        fill.Points.Add(new Point(points[0].X, bottom));
        PlotCanvas.Children.Add(fill);
        PlotCanvas.Children.Add(curve);
        foreach (var value in new[] { band.Maximum, baseline, band.Minimum })
        {
            var label = CreateLabel(band.Decibels ? $"{value:+0.#;-0.#;0} dB" : $"{value:0.#}");
            label.Width = 48;
            label.TextAlignment = TextAlignment.Left;
            Canvas.SetTop(label, Y(value) - 8);
            AxisCanvas.Children.Add(label);
        }
    }
}
