using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace OpenSynapse.App.Views;

/// <summary>Design-space grid drawn as one path of line figures.</summary>
public sealed partial class StudioCanvas
{
    private readonly SolidColorBrush _gridBrush = new(Color.FromArgb(52, 0x8A, 0x8A, 0x8A));
    private Path? _grid;

    private void RebuildGrid()
    {
        var geometry = new PathGeometry();
        for (var offset = 0d; offset <= StudioCanvasPlacement.DesignSize; offset += StudioCanvasPlacement.GridStep)
        {
            geometry.Figures.Add(Line(offset, 0, offset, StudioCanvasPlacement.DesignSize));
            geometry.Figures.Add(Line(0, offset, StudioCanvasPlacement.DesignSize, offset));
        }

        _grid = new Path
        {
            Data = geometry,
            Stroke = _gridBrush,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Surface.Children.Add(_grid);
    }

    private void ScaleGrid(double factor)
    {
        if (_grid is null) return;
        _grid.RenderTransform = new ScaleTransform { ScaleX = factor, ScaleY = factor };
        // Keep one screen pixel at every zoom step instead of fading out at 10%.
        _grid.StrokeThickness = 1 / factor;
    }

    private static PathFigure Line(double x1, double y1, double x2, double y2) => new()
    {
        StartPoint = new Point(x1, y1),
        IsClosed = false,
        IsFilled = false,
        Segments = { new LineSegment { Point = new Point(x2, y2) } },
    };
}
