#if DEBUG
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenSynapse.Core.Lighting;
using Windows.Foundation;
using Windows.UI;
using static OpenSynapse.Core.Lighting.StudioLedSelection;

namespace OpenSynapse.App.Views;

public sealed partial class StudioCanvas
{
    internal static void PointerQaLog(string message)
    {
        var path = Environment.GetEnvironmentVariable("OPENSSYNAPSE_STUDIO_POINTER_LOG");
        if (string.IsNullOrEmpty(path)) return;
        try { File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch (IOException) { }
    }

    internal int DebugHostCount => _hosts.Count;
    internal int DebugLedCount => _ledEntries.Count;
    internal string DebugFirstLedColor =>
        _ledEntries.Count == 0 ? "none" : _ledEntries[0].Brush.Color.ToString();

    internal string DebugFirstLedFill =>
        _ledEntries.Count == 0 ? "none" : _ledEntries[0].Element.Fill?.ToString() ?? "null";

    internal string DebugFirstLedOpacity =>
        _ledEntries.Count == 0 ? "none" : _ledEntries[0].Element.Opacity.ToString("0.##");

    internal static StudioDeviceVisual DebugCreateGridVisual(string instanceId, int rows, int columns)
    {
        var leds = new List<StudioLedMap>(rows * columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                leds.Add(new StudioLedMap(row, column, row, column));
            }
        }

        var layout = StudioLedLayout.Create(rows, columns, rows, columns, leds);
        return StudioDeviceVisualBuilder.BuildGrid(0x710, instanceId, "Studio QA Grid", layout);
    }

    internal void DebugPaintAll(byte red, byte green, byte blue)
    {
        var color = Color.FromArgb(255, red, green, blue);
        foreach (var entry in _ledEntries) entry.Brush.Color = color;
    }

    internal void DebugSelectBlock(int row, int column, int rowCount, int columnCount)
    {
        var next = new List<StudioLedRef>();
        foreach (var entry in _ledEntries)
        {
            var led = entry.Led;
            if (led.MatrixRow < row || led.MatrixRow >= row + rowCount ||
                led.MatrixCol < column || led.MatrixCol >= column + columnCount) continue;
            var cell = StudioLedSelection.CellFor(entry.Host.Device, led.MatrixRow, led.MatrixCol);
            if (!Contains(next, cell)) next.Add(cell);
        }

        SelectLeds(next);
    }

    internal string DebugLedBounds() =>
        string.Join(Environment.NewLine, _ledEntries.Select(entry =>
        {
            var bounds = entry.AbsoluteBounds();
            return FormattableString.Invariant(
                $"{entry.InstanceId}|{entry.Led.MatrixRow}|{entry.Led.MatrixCol}|{bounds.X:0.###}|{bounds.Y:0.###}|{bounds.Width:0.###}|{bounds.Height:0.###}");
        }));

    internal string DebugSelectionState()
    {
        var selected = _ledEntries.FirstOrDefault(entry =>
            IndexOfCell(_selection, new StudioLedRef(
                entry.Led.MatrixRow, entry.Led.MatrixCol, InstanceId: entry.InstanceId)) >= 0);
        var unselected = _ledEntries.FirstOrDefault(entry =>
            IndexOfCell(_selection, new StudioLedRef(
                entry.Led.MatrixRow, entry.Led.MatrixCol, InstanceId: entry.InstanceId)) < 0);
        var cells = string.Join(";", _selection.Select(cell =>
            FormattableString.Invariant($"{cell.InstanceId}|{cell.Row}|{cell.Col}")));
        var deviceLeds = _hosts.Sum(host => host.Device.Leds.Count);
        return FormattableString.Invariant(
            $"selected={_selection.Count};leds={_ledEntries.Count};deviceLeds={deviceLeds};selectedStroke={selected?.Element.StrokeThickness ?? 0};selectedColor={selected?.Element.Stroke};unselectedStroke={unselected?.Element.StrokeThickness ?? 0};unselectedColor={unselected?.Element.Stroke};cells={cells}");
    }

    internal void DebugCenterFirstHost()
    {
        var host = _hosts.FirstOrDefault();
        if (host is null) return;
        var bounds = host.Element
            .TransformToVisual(Scroller)
            .TransformBounds(new Rect(0, 0, host.Width, host.Height));
        Scroller.ChangeView(
            Math.Max(0, Scroller.HorizontalOffset + bounds.X + bounds.Width / 2 - Scroller.ViewportWidth / 2),
            Math.Max(0, Scroller.VerticalOffset + bounds.Y + bounds.Height / 2 - Scroller.ViewportHeight / 2),
            null,
            true);
    }

    internal bool DebugFirstHostVisible
    {
        get
        {
            if (_hosts.FirstOrDefault() is not { } host) return false;
            try
            {
                var bounds = host.Element.TransformToVisual(Scroller).TransformBounds(
                    new Rect(0, 0, host.Width, host.Height));
                return bounds.Right > 0 && bounds.Bottom > 0 &&
                    bounds.X < Scroller.ViewportWidth && bounds.Y < Scroller.ViewportHeight;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal string DebugHostState()
    {
        var host = _hosts.FirstOrDefault();
        if (host is null) return "host=none";
        var element = host.Element;
        Point origin;
        try
        {
            origin = element.TransformToVisual(Scroller).TransformPoint(new Point());
        }
        catch (Exception)
        {
            origin = new Point(double.NaN, double.NaN);
        }

        return FormattableString.Invariant(
            $"z={Surface.Children.IndexOf(element)}/{Surface.Children.Count};gridZ={( _grid is null ? -1 : Surface.Children.IndexOf(_grid))};loaded={element.IsLoaded};visibility={element.Visibility};opacity={element.Opacity:0.##};actual={element.ActualWidth:0}x{element.ActualHeight:0};left={Canvas.GetLeft(element):0.#};top={Canvas.GetTop(element):0.#};origin=({origin.X:0.#},{origin.Y:0.#});children={element.Children.Count};scroll=({Scroller.HorizontalOffset:0.#},{Scroller.VerticalOffset:0.#});viewport=({Scroller.ViewportWidth:0}x{Scroller.ViewportHeight:0})");
    }

    internal string DebugCanvasState()
    {
        var host = _hosts.FirstOrDefault();
        var led = _ledEntries.FirstOrDefault();
        var origin = host is null
            ? new Point()
            : host.Element.TransformToVisual(Scroller).TransformPoint(new Point());
        return FormattableString.Invariant(
            $"host=({host?.BaseX},{host?.BaseY},{host?.Width},{host?.Height});origin=({origin.X},{origin.Y});ledBounds={led?.Element.Data.Bounds};ledOpacity={led?.Element.Opacity};scroll=({Scroller.HorizontalOffset},{Scroller.VerticalOffset});viewport=({Scroller.ViewportWidth},{Scroller.ViewportHeight});surface=({Surface.ActualWidth},{Surface.ActualHeight})");
    }
}
#endif
