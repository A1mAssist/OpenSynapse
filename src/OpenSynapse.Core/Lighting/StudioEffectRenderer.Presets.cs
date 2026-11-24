namespace OpenSynapse.Core.Lighting;

public static partial class StudioEffectRenderer
{
    private static StudioRgb[] RenderPreset(
        StudioEffectParams parameters,
        int rows,
        int columns,
        double time,
        StudioEffectInput input,
        StudioEffectState? state) =>
        parameters.PresetDefinition is { } definition
            ? StudioPresetEngine.Render(definition, rows, columns, time, input, state)
            : StudioPresetEngine.Render(parameters.Preset, rows, columns, time, input, state);
}
