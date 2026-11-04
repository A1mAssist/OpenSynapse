using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OpenSynapse.Core.Lighting;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace OpenSynapse.App.Views;

/// <summary>Draws the device's manifest silhouette when the product photo is unavailable.</summary>
public sealed partial class StudioDeviceGlyph : UserControl
{
    public static readonly DependencyProperty VisualProperty = DependencyProperty.Register(
        nameof(Visual),
        typeof(StudioDeviceVisual),
        typeof(StudioDeviceGlyph),
        new PropertyMetadata(null, OnVisualChanged));

    private static readonly Color ProductColor = Color.FromArgb(255, 0xC7, 0xD0, 0xD8);
    private static readonly Color LetteringColor = Color.FromArgb(255, 0x44, 0xD6, 0x2C);
    private static readonly Color OutlineColor = Color.FromArgb(255, 0x6E, 0x7A, 0x86);

    public StudioDeviceGlyph()
    {
        InitializeComponent();
        Rebuild();
    }

    public StudioDeviceVisual? Visual
    {
        get => (StudioDeviceVisual?)GetValue(VisualProperty);
        set => SetValue(VisualProperty, value);
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StudioDeviceGlyph)d).Rebuild();

    private void Rebuild()
    {
        GlyphCanvas.Children.Clear();
        if (Visual is not { } visual || visual.Width <= 0 || visual.Height <= 0)
        {
            DrawPlaceholder();
            return;
        }

        var paths = visual.Paths
            .Where(path => path.Role is StudioSvgRole.Product or StudioSvgRole.Lettering)
            .ToList();
        if (paths.Count == 0)
        {
            paths = visual.Paths
                .Where(path => path.Role is not (StudioSvgRole.Led or StudioSvgRole.Selection))
                .ToList();
        }

        if (paths.Count == 0)
        {
            DrawPlaceholder();
            return;
        }

        GlyphCanvas.Width = visual.Width;
        GlyphCanvas.Height = visual.Height;
        foreach (var path in paths)
        {
            if (StudioCanvas.ConvertGeometry(path.Data) is not { } geometry) continue;
            GlyphCanvas.Children.Add(new Path
            {
                Data = geometry,
                Fill = new SolidColorBrush(path.Role == StudioSvgRole.Lettering ? LetteringColor : ProductColor),
                Opacity = path.Role == StudioSvgRole.Lettering ? 0.9 : 0.55,
            });
        }
    }

    private void DrawPlaceholder()
    {
        GlyphCanvas.Width = 100;
        GlyphCanvas.Height = 64;
        var body = new Rectangle
        {
            Width = 88,
            Height = 52,
            RadiusX = 8,
            RadiusY = 8,
            Fill = new SolidColorBrush(ProductColor),
            Opacity = 0.45,
            Stroke = new SolidColorBrush(OutlineColor),
            StrokeThickness = 1,
        };
        Canvas.SetLeft(body, 6);
        Canvas.SetTop(body, 6);
        GlyphCanvas.Children.Add(body);
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 5; column++)
        {
            var key = new Rectangle
            {
                Width = 12,
                Height = 8,
                RadiusX = 2,
                RadiusY = 2,
                Fill = new SolidColorBrush(OutlineColor),
                Opacity = 0.7,
            };
            Canvas.SetLeft(key, 16 + column * 14);
            Canvas.SetTop(key, 18 + row * 14);
            GlyphCanvas.Children.Add(key);
        }
    }
}