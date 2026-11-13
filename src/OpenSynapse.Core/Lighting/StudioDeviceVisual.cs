using System.Globalization;
using System.Text.Json;

namespace OpenSynapse.Core.Lighting;

/// <summary>One drawable shape in a device visual, with SVG transforms already baked into <see cref="Data"/>.</summary>
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
    bool UsesSvg,
    StudioLedLayout? Layout = null);

/// <summary>
/// Turns a Chroma manifest into canvas render data. SVG geometry is flattened
/// with transforms baked into path data; when it cannot be drawn as LEDs, a
/// rectangular LED grid is synthesized.
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
        string? name = null,
        Func<string, string?>? svgFileResolver = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var leds = TryParseLeds(manifest.LedConfigJson);
        var svg = StudioSvgParts.Merge(StudioSvgParts.Parse(manifest, svgFileResolver));
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
                UsesSvg: true,
                Layout: leds);
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

    /// <summary>Builds a rectangular LED grid without an SVG payload.</summary>
    public static StudioDeviceVisual BuildGrid(
        ushort productId,
        string instanceId,
        string name,
        StudioLedLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Leds.Count == 0)
        {
            return new StudioDeviceVisual(
                instanceId,
                productId,
                name,
                EmptySize,
                EmptySize,
                Array.Empty<StudioVisualPath>(),
                Array.Empty<StudioVisualLed>(),
                UsesSvg: false,
                Layout: layout);
        }

        // Without an SVG the matrix is only electrical: keyboards also map logo and
        // side-strip LEDs, and their matrix is taller than the physical rows. Draw the
        // keyed LEDs, cropped to their bounds; other devices keep the whole table.
        var source = layout.Inputs.Any(input => input.InputType.Equals("kbd", StringComparison.OrdinalIgnoreCase))
            ? layout.Leds.Where(led => layout.FindInputAt(led.MatrixRow, led.MatrixCol) is not null).ToArray()
            : layout.Leds.ToArray();
        if (source.Length == 0) source = layout.Leds.ToArray();

        var minRow = source.Min(led => led.MatrixRow);
        var minCol = source.Min(led => led.MatrixCol);
        var rows = Math.Max(1, source.Max(led => led.MatrixRow) - minRow + 1);
        var columns = Math.Max(1, source.Max(led => led.MatrixCol) - minCol + 1);
        var paths = new StudioVisualPath[source.Length];
        var leds = new StudioVisualLed[source.Length];
        for (var index = 0; index < leds.Length; index++)
        {
            var led = source[index];
            var x = Margin + (led.MatrixCol - minCol) * (CellSize + CellGap);
            var y = Margin + (led.MatrixRow - minRow) * (CellSize + CellGap);
            var id = $"led-{led.MatrixRow}-{led.MatrixCol}";
            paths[index] = new StudioVisualPath(
                id,
                CellPath(x, y),
                // Unlit cells still need to read as a key, not vanish into the dark canvas.
                "#242424",
                null,
                1d,
                StudioSvgRole.Led);
            leds[index] = new StudioVisualLed(led.MatrixRow, led.MatrixCol, id, null);
        }

        return new StudioDeviceVisual(
            instanceId,
            productId,
            name,
            Margin * 2 + columns * CellSize + (columns - 1) * CellGap,
            Margin * 2 + rows * CellSize + (rows - 1) * CellGap,
            paths,
            leds,
            UsesSvg: false,
            Layout: layout);
    }

    private static StudioDeviceVisual BuildFallback(
        StudioManifestDevice manifest,
        string instanceId,
        string? name,
        StudioLedLayout? layout)
    {
        var resolvedName = ResolveName(manifest, name);
        return layout is null || layout.Leds.Count == 0
            ? new StudioDeviceVisual(
                instanceId,
                manifest.ProductId,
                resolvedName,
                EmptySize,
                EmptySize,
                Array.Empty<StudioVisualPath>(),
                Array.Empty<StudioVisualLed>(),
                UsesSvg: false,
                Layout: layout)
            : BuildGrid(manifest.ProductId, instanceId, resolvedName, layout);
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

    private static string ResolveName(StudioManifestDevice manifest, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name;
        if (!string.IsNullOrWhiteSpace(manifest.ProductName)) return manifest.ProductName;
        return "Device";
    }
}
