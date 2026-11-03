using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.UI;

namespace OpenSynapse.App.Views;

public sealed partial class StudioEffectInspector
{
    private static double UnitValue(NumberBox box) =>
        double.IsNaN(box.Value) ? 0 : Math.Clamp(box.Value, 0, 1);

    /// <summary>16:9 canvas that mirrors ScreenRect; drag the body to move, the handle to resize.</summary>
    private sealed class ScreenRectPreview
    {
        private const double CanvasWidth = 320;
        private const double CanvasHeight = 180;
        private const double HandleSize = 16;
        private const double MinimumSize = 0.02;

        private readonly Canvas _canvas;
        private readonly Rectangle _selection;
        private readonly Rectangle _handle;
        private readonly Action<StudioScreenRect> _changed;
        private bool _dragging;
        private bool _resizing;
        private StudioScreenRect _startRect;
        private Point _startPoint;

        public ScreenRectPreview(StudioScreenRect value, Action<StudioScreenRect> changed)
        {
            _changed = changed;
            var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            var canvasBrush = (Brush)Application.Current.Resources["CanvasBrush"];

            _canvas = new Canvas
            {
                Width = CanvasWidth,
                Height = CanvasHeight,
                Background = canvasBrush,
            };
            AutomationProperties.SetName(_canvas, AppStrings.Text("LightingStudioParamScreenRect"));
            AutomationProperties.SetHelpText(_canvas, AppStrings.Text("LightingStudioParamScreenRectHint"));

            _canvas.Children.Add(new Rectangle
            {
                Width = CanvasWidth,
                Height = CanvasHeight,
                Stroke = (Brush)Application.Current.Resources["LineBrush"],
                StrokeThickness = 1,
                IsHitTestVisible = false,
            });

            _selection = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(64, accent.Color.R, accent.Color.G, accent.Color.B)),
                Stroke = accent,
                StrokeThickness = 2,
            };
            _selection.PointerPressed += (_, e) => BeginDrag(e, resizing: false);
            _canvas.Children.Add(_selection);

            _handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = accent,
                Stroke = canvasBrush,
                StrokeThickness = 2,
                RadiusX = 3,
                RadiusY = 3,
            };
            _handle.PointerPressed += (_, e) => BeginDrag(e, resizing: true);
            _canvas.Children.Add(_handle);

            _canvas.PointerMoved += OnPointerMoved;
            _canvas.PointerReleased += EndDrag;
            _canvas.PointerCaptureLost += (_, _) => _dragging = false;

            View = new Viewbox
            {
                Child = _canvas,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            Render(value);
        }

        public FrameworkElement View { get; }

        public void Render(StudioScreenRect value)
        {
            var x = Math.Clamp(value.X, 0, 1);
            var y = Math.Clamp(value.Y, 0, 1);
            // Values typed outside the screen still hold; the preview shows only the visible part.
            var width = Math.Clamp(value.Width, 0, 1 - x);
            var height = Math.Clamp(value.Height, 0, 1 - y);

            Canvas.SetLeft(_selection, x * CanvasWidth);
            Canvas.SetTop(_selection, y * CanvasHeight);
            _selection.Width = width * CanvasWidth;
            _selection.Height = height * CanvasHeight;
            Canvas.SetLeft(_handle, Math.Clamp(
                x * CanvasWidth + width * CanvasWidth - HandleSize, 0, CanvasWidth - HandleSize));
            Canvas.SetTop(_handle, Math.Clamp(
                y * CanvasHeight + height * CanvasHeight - HandleSize, 0, CanvasHeight - HandleSize));
        }

        private void BeginDrag(PointerRoutedEventArgs e, bool resizing)
        {
            if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
            _startPoint = e.GetCurrentPoint(_canvas).Position;
            _startRect = new StudioScreenRect(
                Canvas.GetLeft(_selection) / CanvasWidth,
                Canvas.GetTop(_selection) / CanvasHeight,
                _selection.Width / CanvasWidth,
                _selection.Height / CanvasHeight);
            _resizing = resizing;
            _dragging = _canvas.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging) return;
            var point = e.GetCurrentPoint(_canvas).Position;
            var dx = (point.X - _startPoint.X) / CanvasWidth;
            var dy = (point.Y - _startPoint.Y) / CanvasHeight;
            var x = _startRect.X;
            var y = _startRect.Y;
            var width = _startRect.Width;
            var height = _startRect.Height;

            if (_resizing)
            {
                var maxWidth = 1 - x;
                var maxHeight = 1 - y;
                width = Math.Clamp(width + dx, Math.Min(MinimumSize, maxWidth), maxWidth);
                height = Math.Clamp(height + dy, Math.Min(MinimumSize, maxHeight), maxHeight);
            }
            else
            {
                x = Math.Clamp(x + dx, 0, 1 - width);
                y = Math.Clamp(y + dy, 0, 1 - height);
            }

            _changed(new StudioScreenRect(
                Math.Round(x, 3), Math.Round(y, 3), Math.Round(width, 3), Math.Round(height, 3)));
            e.Handled = true;
        }

        private void EndDrag(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _canvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }
}