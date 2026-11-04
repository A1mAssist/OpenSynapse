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
    private StudioScreenRect _captureRegion = StudioScreenRect.FullScreen;

    public StudioScreenRect CaptureRegion
    {
        get => _captureRegion;
        set => _captureRegion = value.Clamp();
    }

    public event Action<StudioScreenRect>? CaptureRegionChanged;

    private static double PercentValue(NumberBox box) =>
        (double.IsNaN(box.Value) ? 0 : Math.Clamp(box.Value, 0, 100)) / 100;

    private static NumberBox PercentBox(string header, double unit, string automationPrefix)
    {
        var box = new NumberBox
        {
            Header = header,
            Minimum = 0,
            Maximum = 100,
            SmallChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = Math.Clamp(unit, 0, 1) * 100,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(box, $"{automationPrefix}{header}");
        return box;
    }

    private FrameworkElement BuildScreenRectRow()
    {
        var section = new StackPanel { Spacing = 12 };
        section.Children.Add(BuildRectRow(
            "LightingStudioParamScreenRect",
            "LightingStudioParamScreenRectHint",
            "LightingStudioParamScreenRectReset",
            "StudioEffectRect",
            _parameters?.ScreenRect ?? StudioScreenRect.FullScreen,
            value =>
            {
                if (_parameters is { } parameters) parameters.ScreenRect = value;
                RaiseChanged();
            }));
        section.Children.Add(BuildCaptureRegionRow());
        return section;
    }

    private FrameworkElement BuildCaptureRegionRow() => BuildRectRow(
        "LightingStudioParamCaptureRegion",
        "LightingStudioParamCaptureRegionHint",
        "LightingStudioParamCaptureRegionReset",
        "StudioCapture",
        CaptureRegion,
        value =>
        {
            CaptureRegion = value;
            CaptureRegionChanged?.Invoke(value);
            RaiseChanged();
        });

    private FrameworkElement BuildRectRow(
        string labelKey,
        string hintKey,
        string resetKey,
        string automationPrefix,
        StudioScreenRect initial,
        Action<StudioScreenRect> changed)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(CreateLabel(AppStrings.Text(labelKey)));
        var hint = CreateLabel(AppStrings.Text(hintKey));
        hint.FontSize = 11;
        hint.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(hint);
        var rect = initial.Clamp();
        var x = PercentBox("X", rect.X, automationPrefix);
        var y = PercentBox("Y", rect.Y, automationPrefix);
        var width = PercentBox("W", rect.Width, automationPrefix);
        var height = PercentBox("H", rect.Height, automationPrefix);

        var syncing = false;
        ScreenRectPreview? preview = null;

        void Apply(StudioScreenRect value, bool syncBoxes)
        {
            value = value.Clamp();
            changed(value);
            preview?.Render(value);
            if (syncBoxes)
            {
                syncing = true;
                x.Value = value.X * 100;
                y.Value = value.Y * 100;
                width.Value = value.Width * 100;
                height.Value = value.Height * 100;
                syncing = false;
            }
        }

        void ApplyBoxes()
        {
            if (syncing) return;
            Apply(
                new StudioScreenRect(
                    PercentValue(x),
                    PercentValue(y),
                    PercentValue(width),
                    PercentValue(height)),
                syncBoxes: true);
        }

        x.ValueChanged += (_, _) => ApplyBoxes();
        y.ValueChanged += (_, _) => ApplyBoxes();
        width.ValueChanged += (_, _) => ApplyBoxes();
        height.ValueChanged += (_, _) => ApplyBoxes();

        preview = new ScreenRectPreview(
            rect,
            value => Apply(value, syncBoxes: true),
            AppStrings.Text(labelKey));

        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(x);
        Grid.SetColumn(y, 1);
        grid.Children.Add(y);
        Grid.SetRow(width, 1);
        grid.Children.Add(width);
        Grid.SetRow(height, 1);
        Grid.SetColumn(height, 1);
        grid.Children.Add(height);
        row.Children.Add(grid);

        var reset = new Button
        {
            Content = AppStrings.Text(resetKey),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(reset, $"{automationPrefix}Reset");
        reset.Click += (_, _) => Apply(StudioScreenRect.FullScreen, syncBoxes: true);
        row.Children.Add(reset);
        row.Children.Add(preview.View);
        return row;
    }

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

        public ScreenRectPreview(
            StudioScreenRect value,
            Action<StudioScreenRect> changed,
            string automationName)
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
            AutomationProperties.SetName(_canvas, automationName);
            AutomationProperties.SetHelpText(_canvas, automationName);

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
                MaxHeight = 150,
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