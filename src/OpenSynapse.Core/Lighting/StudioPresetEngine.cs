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
                ScaleKeys(input.Keys, layer.Speed),
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

    private static StudioKeyState? ScaleKeys(StudioKeyState? keys, double speed)
    {
        if (keys is null || speed == 1) return keys;
        return new StudioKeyState(keys.Rows, keys.Columns, keys.Cells, keys.LastRow, keys.LastColumn,
            keys.SecondsSincePress * speed,
            keys.Presses.Select(press => press with
            {
                Age = press.Age * speed,
                ReleaseAge = press.ReleaseAge * speed,
            }).ToArray());
    }

    private sealed class StudioPresetScratch(StudioPresetConfig? definition, int rows, int columns)
    {
        public StudioPresetConfig? Definition { get; } = definition;
        public int Rows { get; } = rows;
        public int Columns { get; } = columns;
        public StudioRgb[] Output { get; } = new StudioRgb[rows * columns];
    }
}
