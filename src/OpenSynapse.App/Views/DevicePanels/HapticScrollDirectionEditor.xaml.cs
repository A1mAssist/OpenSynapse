using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.App.ViewModels;
using OpenSynapse.Core.Devices;
using Windows.Foundation;

namespace OpenSynapse.App.Views.DevicePanels;

public sealed partial class HapticScrollDirectionEditor : UserControl
{
    private HapticScrollDirectionViewModel? _model;
    private int? _dragging;
    public event EventHandler? EditCommitted;
    public HapticScrollDirectionEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (_model is not null) _model.PropertyChanged -= Changed; _model = null; };
        Plot.SizeChanged += (_, _) => Draw();
    }
    private void Attach()
    {
        if (_model is not null) _model.PropertyChanged -= Changed;
        _model = DataContext as HapticScrollDirectionViewModel;
        if (_model is not null) _model.PropertyChanged += Changed;
        Draw();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) => Draw();
    private Point Position(double x, double y) => new(22 + x / 100 * Math.Max(1, Plot.ActualWidth - 44), 198 - y / 100 * 176);
    private void Draw()
    {
        Plot.Children.Clear();
        if (_model is null || Plot.ActualWidth <= 44) return;
        var muted = (Brush)Application.Current.Resources["MutedBrush"];
        var grid = (Brush)Application.Current.Resources["LineBrush"];
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        foreach (var tick in new[] { 0, 50, 100 })
        {
            var a = Position(0, tick); var b = Position(100, tick);
            Plot.Children.Add(new Line { X1 = a.X, X2 = b.X, Y1 = a.Y, Y2 = b.Y, Stroke = grid, StrokeThickness = 1 });
            var label = new TextBlock { Text = tick.ToString(), FontSize = 12, Foreground = muted };
            Canvas.SetLeft(label, 0); Canvas.SetTop(label, a.Y - 8); Plot.Children.Add(label);
        }
        try
        {
            var curve = new Polyline { Stroke = accent, StrokeThickness = 2.5, IsHitTestVisible = false };
            foreach (var point in HapticScrollCurve.Sample(_model.Capture())) curve.Points.Add(Position(point.X, point.Y));
            Plot.Children.Add(curve);
        }
        catch (InvalidDataException) { }
        foreach (var point in _model.Points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) continue;
            var at = Position(point.X, point.Y);
            var dot = new Ellipse { Width = 12, Height = 12, Fill = point.CanEdit ? accent : muted, IsHitTestVisible = false };
            Canvas.SetLeft(dot, at.X - 6); Canvas.SetTop(dot, at.Y - 6); Plot.Children.Add(dot);
        }
    }
    private void PlotPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_model is null || !IsEnabled) return;
        var cursor = e.GetCurrentPoint(Plot).Position;
        var closest = _model.Points.Where(p => p.CanEdit).OrderBy(p => Distance(Position(p.X, p.Y), cursor)).First();
        if (Distance(Position(closest.X, closest.Y), cursor) > 24 * 24) return;
        _dragging = closest.Index;
        Plot.CapturePointer(e.Pointer);
        e.Handled = true;
    }
    private static double Distance(Point a, Point b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
    private void PlotMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is not { } i || _model is null) return;
        var cursor = e.GetCurrentPoint(Plot).Position;
        var min = Math.Max(i == 2 ? 1 : 0, _model.Points[i - 1].X);
        var max = Math.Min(i == 2 ? 99 : 100, _model.Points[i + 1].X);
        if (min > max || !double.IsFinite(min) || !double.IsFinite(max)) return;
        _model.Points[i].X = Math.Clamp(Math.Round((cursor.X - 22) / Math.Max(1, Plot.ActualWidth - 44) * 100), min, max);
        _model.Points[i].Y = Math.Clamp(Math.Round((198 - cursor.Y) / 176 * 100), 0, 100);
        e.Handled = true;
    }
    private void PlotReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null) return;
        PlotMoved(sender, e);
        Commit();
        Plot.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
    private void PlotCaptureLost(object sender, PointerRoutedEventArgs e) => Commit();
    private void Commit()
    {
        if (_dragging is null) return;
        _dragging = null;
        EditCommitted?.Invoke(this, EventArgs.Empty);
    }
}
