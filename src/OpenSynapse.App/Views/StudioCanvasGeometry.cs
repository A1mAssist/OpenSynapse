using System.Globalization;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace OpenSynapse.App.Views;

/// <summary>Pure geometry, hit-testing, selection and color helpers for <see cref="StudioCanvas"/>.</summary>
internal static class StudioCanvasGeometry
{
    public static Rect ComputeBounds(
        Geometry geometry, int rotation, double width, double height, double hostX, double hostY)
    {
        var rotated = RotateBounds(geometry.Bounds, rotation, width, height);
        return new Rect(rotated.X + hostX, rotated.Y + hostY, rotated.Width, rotated.Height);
    }

    public static bool Contains(Rect rect, double x, double y) =>
        x >= rect.X && x <= rect.X + rect.Width && y >= rect.Y && y <= rect.Y + rect.Height;

    public static bool Intersects(Rect left, Rect right) =>
        left.Width > 0 && left.Height > 0 &&
        left.X < right.X + right.Width && right.X < left.X + left.Width &&
        left.Y < right.Y + right.Height && right.Y < left.Y + left.Height;

    public static Rect NormalizeRect(Point start, Point end) => new(
        Math.Min(start.X, end.X),
        Math.Min(start.Y, end.Y),
        Math.Abs(start.X - end.X),
        Math.Abs(start.Y - end.Y));

    public static Point ClampPoint(Point point, double width, double height) => new(
        Math.Clamp(double.IsFinite(point.X) ? point.X : 0, 0, width),
        Math.Clamp(double.IsFinite(point.Y) ? point.Y : 0, 0, height));


    public static int NormalizeRotation(int rotation) => rotation is 90 or 180 or 270 ? rotation : 0;

    public static int RoleOrder(StudioSvgRole role) => role switch
    {
        StudioSvgRole.Product => 0,
        StudioSvgRole.Led => 2,
        StudioSvgRole.Selection => 3,
        _ => 1,
    };

    public static double Extent(double value) => double.IsFinite(value) && value > 0 ? value : 1;

    public static bool SameSelection(IReadOnlyList<StudioLedRef> current, List<StudioLedRef> next)
    {
        if (next.Count != current.Count) return false;
        foreach (var led in next)
        {
            if (IndexOfCell(current, led.Row, led.Col) < 0) return false;
        }

        return true;
    }

    public static int IndexOfCell(IReadOnlyList<StudioLedRef> leds, int row, int column)
    {
        for (var index = 0; index < leds.Count; index++)
        {
            if (leds[index].Row == row && leds[index].Col == column) return index;
        }

        return -1;
    }

    public static List<StudioLedRef> SelectCell(
        IReadOnlyList<StudioLedRef> current, int row, int column, bool toggle)
    {
        var next = new List<StudioLedRef>(current);
        var index = IndexOfCell(next, row, column);
        if (!toggle)
        {
            next.Clear();
            next.Add(new StudioLedRef(row, column));
        }
        else if (index >= 0)
        {
            next.RemoveAt(index);
        }
        else
        {
            next.Add(new StudioLedRef(row, column));
        }

        return next;
    }

    public static List<StudioLedRef> PenCell(
        IReadOnlyList<StudioLedRef> current, int row, int column, bool erase)
    {
        var next = new List<StudioLedRef>(current);
        var index = IndexOfCell(next, row, column);
        if (index >= 0) next.RemoveAt(index);
        else if (!erase) next.Add(new StudioLedRef(row, column));
        return next;
    }

    public static List<StudioLedRef> DeviceCells(StudioDeviceVisual device)
    {
        var next = new List<StudioLedRef>();
        foreach (var led in device.Leds)
        {
            var cell = new StudioLedRef(led.MatrixRow, led.MatrixCol);
            if (IndexOfCell(next, cell.Row, cell.Col) < 0) next.Add(cell);
        }

        return next;
    }

    public static Color? ParseColor(string value)
    {
        var text = value.Trim();
        if (text.Length < 4 || text[0] != '#') return null;

        var hex = text[1..];
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)) return null;
        return hex.Length switch
        {
            3 => Color.FromArgb(
                255,
                (byte)(((packed >> 8) & 0xF) * 17),
                (byte)(((packed >> 4) & 0xF) * 17),
                (byte)((packed & 0xF) * 17)),
            6 => Color.FromArgb(255, (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed),
            8 => Color.FromArgb((byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed),
            _ => null,
        };
    }

    public static Path? FindSelectionPath(StudioVisualLed led, Dictionary<string, Path> selectionPaths) =>
        string.IsNullOrEmpty(led.KeyName)
            ? null
            : selectionPaths.TryGetValue("sel" + led.KeyName, out var path) ? path : null;

    private static Rect RotateBounds(Rect rect, int rotation, double width, double height)
    {
        if (rotation == 0) return rect;

        var centerX = width / 2;
        var centerY = height / 2;
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        for (var index = 0; index < 4; index++)
        {
            var x = index is 0 or 3 ? rect.X : rect.X + rect.Width;
            var y = index < 2 ? rect.Y : rect.Y + rect.Height;
            var dx = x - centerX;
            var dy = y - centerY;
            var (rx, ry) = rotation switch
            {
                90 => (centerX - dy, centerY + dx),
                180 => (centerX - dx, centerY - dy),
                _ => (centerX + dy, centerY - dx),
            };
            minX = Math.Min(minX, rx);
            minY = Math.Min(minY, ry);
            maxX = Math.Max(maxX, rx);
            maxY = Math.Max(maxY, ry);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
