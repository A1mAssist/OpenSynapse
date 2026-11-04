using OpenSynapse.Core.Lighting;

namespace OpenSynapse.App.ViewModels;

/// <summary>Effect switching defaults, kept out of the main view-model file.</summary>
public sealed partial class LightingStudioViewModel
{
    /// <summary>Resets effect-specific fields but keeps colors both effects understand.</summary>
    private static void ApplyEffectDefaults(
        StudioEffectParams parameters,
        StudioEffectKind previousEffect)
    {
        var previous = StudioEffectCatalog.Get(previousEffect);
        var next = StudioEffectCatalog.Get(parameters.Effect);
        var keepColor = next.Fields.Contains(StudioParameterField.Color) &&
            previous.Fields.Contains(StudioParameterField.Color);
        var keepSecondary = next.Fields.Contains(StudioParameterField.Color2) &&
            previous.Fields.Contains(StudioParameterField.Color2);
        var keepRandom = next.Fields.Contains(StudioParameterField.RandomColor) &&
            previous.Fields.Contains(StudioParameterField.RandomColor);
        var keepStops = next.Fields.Contains(StudioParameterField.ColorStops) &&
            previous.Fields.Contains(StudioParameterField.ColorStops);
        var primary = parameters.Primary;
        var secondary = parameters.Secondary;
        var randomColor = parameters.RandomColor;
        var stops = parameters.ColorStops.ToArray();
        var stopsCustom = parameters.ColorStopsCustom;

        StudioEffectCatalog.ApplyDefaults(parameters);
        if (keepColor) parameters.Primary = primary;
        if (keepSecondary) parameters.Secondary = secondary;
        if (keepRandom) parameters.RandomColor = randomColor;
        if (!keepStops) return;
        parameters.ColorStops.Clear();
        parameters.ColorStops.AddRange(stops);
        parameters.ColorStopsCustom = stopsCustom;
    }
}
