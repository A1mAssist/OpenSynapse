namespace OpenSynapse.Core.Lighting;

/// <summary>
/// Chroma addresses an effect through the layer "value" (UI id) and through the
/// param key names. Gradient is an OpenSynapse extension; it exports as spectrum.
/// </summary>
public static class StudioEffectMap
{
    public static int ToUiValue(StudioEffectKind effect) => effect switch
    {
        StudioEffectKind.Ripple => 1,
        StudioEffectKind.Wave => 2,
        StudioEffectKind.Reactive => 3,
        StudioEffectKind.Spectrum => 4,
        StudioEffectKind.Breathing => 5,
        StudioEffectKind.Solid => 6,
        StudioEffectKind.Starlight => 7,
        StudioEffectKind.Fire => 8,
        StudioEffectKind.Ambient => 11,
        StudioEffectKind.Audio => 12,
        StudioEffectKind.Wheel => 13,
        StudioEffectKind.Gradient => 4,
        StudioEffectKind.ChromaGenerate => 18,
        StudioEffectKind.Tidal => 19,
        StudioEffectKind.Radiate => 17,
        _ => 6,
    };

    public static StudioEffectKind FromUiValue(int value) =>
        TryFromUiValue(value, out var effect) ? effect : StudioEffectKind.Solid;

    public static bool TryFromUiValue(int value, out StudioEffectKind effect)
    {
        effect = value switch
        {
            1 => StudioEffectKind.Ripple,
            2 => StudioEffectKind.Wave,
            3 => StudioEffectKind.Reactive,
            4 => StudioEffectKind.Spectrum,
            5 => StudioEffectKind.Breathing,
            6 => StudioEffectKind.Solid,
            7 => StudioEffectKind.Starlight,
            8 => StudioEffectKind.Fire,
            11 => StudioEffectKind.Ambient,
            12 => StudioEffectKind.Audio,
            13 => StudioEffectKind.Wheel,
            17 => StudioEffectKind.Radiate,
            18 => StudioEffectKind.ChromaGenerate,
            19 => StudioEffectKind.Tidal,
            _ => (StudioEffectKind)(-1),
        };
        return effect != (StudioEffectKind)(-1);
    }

    public static string ToName(StudioEffectKind effect) => effect switch
    {
        StudioEffectKind.Ripple => "ripple",
        StudioEffectKind.Wave => "wave",
        StudioEffectKind.Reactive => "reactive",
        StudioEffectKind.Spectrum => "spectrum",
        StudioEffectKind.Breathing => "breathing",
        StudioEffectKind.Solid => "static",
        StudioEffectKind.Starlight => "starlight",
        StudioEffectKind.Fire => "fire",
        StudioEffectKind.Ambient => "ambient",
        StudioEffectKind.Audio => "audio",
        StudioEffectKind.Wheel => "wheel",
        StudioEffectKind.Gradient => "spectrum",
        StudioEffectKind.ChromaGenerate => "chroma-generate",
        StudioEffectKind.Tidal => "tidal",
        StudioEffectKind.Radiate => "radiate",
        _ => "static",
    };

    public static StudioEffectKind FromName(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "ripple" => StudioEffectKind.Ripple,
        "wave" => StudioEffectKind.Wave,
        "reactive" => StudioEffectKind.Reactive,
        "spectrum" => StudioEffectKind.Spectrum,
        "breathing" => StudioEffectKind.Breathing,
        "static" => StudioEffectKind.Solid,
        "starlight" => StudioEffectKind.Starlight,
        "fire" => StudioEffectKind.Fire,
        "ambient" => StudioEffectKind.Ambient,
        "audio" => StudioEffectKind.Audio,
        "wheel" => StudioEffectKind.Wheel,
        "chroma-generate" or "generate" => StudioEffectKind.ChromaGenerate,
        "tidal" => StudioEffectKind.Tidal,
        "radiate" => StudioEffectKind.Radiate,
        "gradient" => StudioEffectKind.Gradient,
        _ => StudioEffectKind.Solid,
    };
}
