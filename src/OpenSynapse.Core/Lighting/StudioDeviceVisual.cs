using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

/// <summary>One drawable shape in a device visual, in document order.</summary>
public sealed record StudioVisualPath(
    string Id,
    string Data,
    string? Fill,
    string? Stroke,
    double Opacity,
    StudioSvgRole Role);

/// <summary>One physical LED and the shape that represents it.</summary>
public sealed record StudioVisualLed(
    int MatrixRow,
    int MatrixCol,
    string? PathId,
    string? KeyName,
    string? SelectionGroup = null);

/// <summary>Flat render data for one device: drawable paths plus the physical LED table.</summary>
public sealed record StudioDeviceVisual(
    string InstanceId,
    ushort ProductId,
    string Name,
    double Width,
    double Height,
    IReadOnlyList<StudioVisualPath> Paths,
    IReadOnlyList<StudioVisualLed> Leds,
    bool UsesSvg);

/// <summary>
/// Turns a Chroma manifest into canvas render data. SVG geometry is flattened
/// as-is; when it cannot be drawn as LEDs, a rectangular LED grid is synthesized.
/// </summary>
public static class StudioDeviceVisualBuilder
{
    private const double CellSize = 24;
    private const double CellGap = 4;
    private const double Margin = 8;
    private const double EmptySize = 100;

    public static StudioDeviceVisual Build(
        StudioManifestDevice manifest,
        string instanceId,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var leds = TryParseLeds(manifest.LedConfigJson);
        var svg = TryParseSvg(manifest.SvgBase64);
        if (svg is not null && svg.Paths.Any(path => path.Role == StudioSvgRole.Led))
        {
            return new StudioDeviceVisual(
                instanceId,
                manifest.ProductId,
                ResolveName(manifest, name),
                svg.Width,
                svg.Height,
                svg.Paths.Select(ToVisualPath).ToArray(),
                BuildLedTable(leds, svg.Paths),
                UsesSvg: true);
        }

        return BuildFallback(manifest, instanceId, name, leds);
    }

    private static StudioVisualPath ToVisualPath(StudioSvgPath path) =>
        new(path.Id, path.Data, path.Fill, path.Stroke, path.Opacity, path.Role);

    private static IReadOnlyList<StudioVisualLed> BuildLedTable(
        StudioLedLayout? layout,
        IReadOnlyList<StudioSvgPath> paths)
    {
        if (layout is null) return Array.Empty<StudioVisualLed>();

        var byCell = new Dictionary<(int Row, int Col), StudioSvgPath>();
        foreach (var path in paths)
        {
            if (path.Role != StudioSvgRole.Led || path.Row is not { } row || path.Col is not { } col) continue;
            byCell.TryAdd((row, col), path);
        }

        var leds = new StudioVisualLed[layout.Leds.Count];
        for (var index = 0; index < leds.Length; index++)
        {
            var led = layout.Leds[index];
            byCell.TryGetValue((led.MatrixRow, led.MatrixCol), out var path);
            leds[index] = new StudioVisualLed(
                led.MatrixRow, led.MatrixCol, path?.Id, path?.KeyName, path?.SelectionGroup);
        }

        return leds;
    }

    private static StudioDeviceVisual BuildFallback(
        StudioManifestDevice manifest,
        string instanceId,
        string? name,
        StudioLedLayout? layout)
    {
        var resolvedName = ResolveName(manifest, name);
        if (layout is null || layout.Leds.Count == 0)
        {
            return new StudioDeviceVisual(
                instanceId,
                manifest.ProductId,
                resolvedName,
                EmptySize,
                EmptySize,
                Array.Empty<StudioVisualPath>(),
                Array.Empty<StudioVisualLed>(),
                UsesSvg: false);
        }

        var rows = Math.Max(layout.MatrixRows, layout.Leds.Max(led => led.MatrixRow) + 1);
        var columns = Math.Max(layout.MatrixColumns, layout.Leds.Max(led => led.MatrixCol) + 1);
        var paths = new StudioVisualPath[layout.Leds.Count];
        var leds = new StudioVisualLed[layout.Leds.Count];
        for (var index = 0; index < leds.Length; index++)
        {
            var led = layout.Leds[index];
            var x = Margin + led.MatrixCol * (CellSize + CellGap);
            var y = Margin + led.MatrixRow * (CellSize + CellGap);
            var id = $"led-{led.MatrixRow}-{led.MatrixCol}";
            paths[index] = new StudioVisualPath(
                id,
                CellPath(x, y),
                "#000000",
                null,
                1d,
                StudioSvgRole.Led);
            leds[index] = new StudioVisualLed(led.MatrixRow, led.MatrixCol, id, null);
        }

        return new StudioDeviceVisual(
            instanceId,
            manifest.ProductId,
            resolvedName,
            Margin * 2 + columns * CellSize + (columns - 1) * CellGap,
            Margin * 2 + rows * CellSize + (rows - 1) * CellGap,
            paths,
            leds,
            UsesSvg: false);
    }

    private static string CellPath(double x, double y) =>
        FormattableString.Invariant($"M {x},{y} H {x + CellSize} V {y + CellSize} H {x} Z");

    private static StudioLedLayout? TryParseLeds(string? ledConfigJson)
    {
        if (string.IsNullOrWhiteSpace(ledConfigJson)) return null;

        try
        {
            return StudioLedLayout.Parse(ledConfigJson);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException or FormatException)
        {
            // A broken LED map must not hide the SVG geometry.
            return null;
        }
    }

    private static StudioSvgLayout? TryParseSvg(string? svgBase64)
    {
        if (string.IsNullOrWhiteSpace(svgBase64)) return null;

        try
        {
            var svg = Encoding.UTF8.GetString(Convert.FromBase64String(svgBase64));
            return string.IsNullOrWhiteSpace(svg) ? null : StudioSvgLayoutParser.Parse(svg);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Non-payload values and malformed XML both fall back to the grid.
            return null;
        }
    }

    private static string ResolveName(StudioManifestDevice manifest, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name;
        if (!string.IsNullOrWhiteSpace(manifest.ProductName)) return manifest.ProductName;
        return "Device";
    }
}