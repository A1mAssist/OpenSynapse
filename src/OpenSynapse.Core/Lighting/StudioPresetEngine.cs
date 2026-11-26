namespace OpenSynapse.Core.Lighting;

/// <summary>Renders a JSON preset stack; module scratch buffers are kept per studio layer.</summary>
public static class StudioPresetEngine
{
    public static StudioRgb[] Render(
        string? presetId,
        int rows,
        int columns,
        double seconds,
        StudioEffectInput input = default,
        StudioEffectState? state = null) =>
        Render(StudioPresetCatalog.Default.Find(presetId), rows, columns, seconds, input, state);

    public static StudioRgb[] Render(
        StudioPresetConfig? preset,
        int rows,
        int columns,
        double seconds,
        StudioEffectInput input = default,
        StudioEffectState? state = null)
    {
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns <= 0 || (long)rows * columns > StudioCompositor.MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(columns));
        var frame = new StudioRgb[rows * columns];
        var owner = state ?? new StudioEffectState();
        var scratch = owner.Tag as StudioPresetScratch;
        if (scratch is null || !ReferenceEquals(scratch.Definition, preset) ||
            scratch.Rows != rows || scratch.Columns != columns)
        {
            owner.Clear();
            scratch = new StudioPresetScratch(preset, rows, columns);
            owner.Tag = scratch;
        }
        if (preset is null) return frame;

        var time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        foreach (var layer in preset.Layers)
        {
            var output = scratch.Output;
            Array.Clear(output);
            var context = new RenderModuleContext(
                rows,
                columns,
                time * layer.Speed,
                input.Audio,
                ScaleKeys(input.Keys, layer.Speed, rows, columns),
                input.ScreenSampler,
                input.ScreenSamplerUv,
                layer.RenderPalette,
                output,
                owner.Scope(layer.StateKey),
                input.System);
            layer.Renderer.Render(context);

            for (var pixel = 0; pixel < frame.Length; pixel++)
            {
                frame[pixel] = StudioCompositor.Blend(
                    frame[pixel],
                    output[pixel],
                    layer.Opacity,
                    layer.Blend);
            }
        }

        return frame;
    }

    private static StudioKeyState? ScaleKeys(StudioKeyState? keys, double speed, int rows, int columns)
    {
        if (keys is null) return null;
        var sameGrid = keys.Rows == rows && keys.Columns == columns;
        if (speed == 1 && sameGrid) return keys;

        var cells = sameGrid ? keys.Cells : new float[rows * columns];
        if (!sameGrid)
        {
            for (var row = 0; row < keys.Rows; row++)
            {
                for (var column = 0; column < keys.Columns; column++)
                {
                    var value = keys.Cells[row * keys.Columns + column];
                    if (value <= 0) continue;
                    var targetRow = MapIndex(row, keys.Rows, rows);
                    var targetColumn = MapIndex(column, keys.Columns, columns);
                    var index = targetRow * columns + targetColumn;
                    cells[index] = Math.Max(cells[index], value);
                }
            }
        }

        return new StudioKeyState(rows, columns, cells,
            sameGrid || keys.LastRow < 0 ? keys.LastRow : MapIndex(keys.LastRow, keys.Rows, rows),
            sameGrid || keys.LastColumn < 0 ? keys.LastColumn : MapIndex(keys.LastColumn, keys.Columns, columns),
            keys.SecondsSincePress * speed,
            keys.Presses.Select(press => press with
            {
                Row = sameGrid ? press.Row : MapIndex(press.Row, keys.Rows, rows),
                Column = sameGrid ? press.Column : MapIndex(press.Column, keys.Columns, columns),
                Age = press.Age * speed,
                ReleaseAge = press.ReleaseAge * speed,
            }).ToArray());
    }

    private static int MapIndex(int value, int count, int targetCount) =>
        count <= 1 || targetCount <= 1
            ? 0
            : Math.Clamp(
                (int)Math.Round(value * (targetCount - 1d) / (count - 1d)),
                0,
                targetCount - 1);

    private sealed class StudioPresetScratch(StudioPresetConfig? definition, int rows, int columns)
    {
        public StudioPresetConfig? Definition { get; } = definition;
        public int Rows { get; } = rows;
        public int Columns { get; } = columns;
        public StudioRgb[] Output { get; } = new StudioRgb[rows * columns];
    }
}
