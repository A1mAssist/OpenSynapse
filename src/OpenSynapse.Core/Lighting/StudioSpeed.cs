namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Chroma stores effect speed as a rate (wave/ripple 0..50, wheel 0..360).
/// Renderers work in a 0.1..5 multiplier, so translations go through here.
/// </summary>
public static class StudioSpeed
{
    public static double Multiplier(StudioEffectParams parameters) => parameters.Effect switch
    {
        StudioEffectKind.Wave or StudioEffectKind.Ripple or StudioEffectKind.Tidal =>
            Math.Clamp(parameters.Speed / 10, 0.1, 5),
        StudioEffectKind.Wheel => Math.Clamp(parameters.Speed / 60, 0.1, 5),
        _ => Math.Clamp(parameters.Speed, 0.1, 5),
    };
}
