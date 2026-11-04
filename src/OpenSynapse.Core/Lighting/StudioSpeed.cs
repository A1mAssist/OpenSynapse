namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Chroma stores effect speed as a rate (wave/ripple 0..50, wheel 0..360).
/// Kept for callers that need the legacy 0.1..5 multiplier; the native-effect
/// renderers consume the rate directly.
/// </summary>
public static class StudioSpeed
{
    public static double Multiplier(StudioEffectParams parameters) => parameters.Effect switch
    {
        StudioEffectKind.Wave or StudioEffectKind.Ripple or StudioEffectKind.Tidal =>
            Math.Clamp(parameters.Speed / 10, 0, 5),
        StudioEffectKind.Wheel => Math.Clamp(parameters.Speed / 60, 0, 5),
        _ => Math.Clamp(parameters.Speed, 0, 5),
    };
}
