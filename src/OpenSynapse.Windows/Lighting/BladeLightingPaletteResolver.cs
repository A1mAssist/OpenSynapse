using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Keeps legacy Blade lighting profiles on their native firmware path until a
/// palette is explicitly selected. A non-null palette means the software
/// renderer must own the effect.
/// </summary>
internal static class BladeLightingPaletteResolver
{
    internal static LightingPalette GetDefault(BladeLightingMode mode) => mode switch
    {
        BladeLightingMode.Fire => LightingPalette.Create(
            new RazerRgb(0xFF, 0x20, 0x00), new RazerRgb(0xFF, 0x90, 0x00), new RazerRgb(0xFF, 0xF0, 0xA0)),
        BladeLightingMode.Wheel => LightingPalette.Create(
            new RazerRgb(0xFF, 0x00, 0x33), new RazerRgb(0x00, 0xFF, 0xAA), new RazerRgb(0x00, 0x33, 0xFF)),
        BladeLightingMode.AudioMeter => LightingPalette.Create(
            new RazerRgb(0x00, 0xFF, 0x66), new RazerRgb(0xFF, 0xFF, 0x00), new RazerRgb(0xFF, 0x00, 0x33)),
        _ => LightingPalette.Create(
            new RazerRgb(0xFF, 0x00, 0x00), new RazerRgb(0x00, 0xFF, 0x00), new RazerRgb(0x00, 0x00, 0xFF)),
    };

    internal static LightingPalette? Resolve(
        BladeLightingMode mode,
        RazerRgb primary,
        RazerRgb secondary,
        RazerRgb tertiary,
        LightingProfile? existingProfile)
    {
        if (!IsPaletteMode(mode))
        {
            return null;
        }

        var selected = LightingPalette.Create(primary, secondary, tertiary);
        var defaults = GetDefault(mode);
        var hasCustomColors = !ColorsEqual(selected, defaults);
        var profileHasPalette = false;
        if (existingProfile is not null)
        {
            try
            {
                var existing = BladeLightingProfileCodec.Parse(existingProfile);
                profileHasPalette = existing.Mode == mode && existing.Palette is not null;
            }
            catch (InvalidOperationException)
            {
                // The normal profile refresh already reports malformed profiles.
                // Do not make applying an otherwise valid UI selection fail here.
            }
        }

        return hasCustomColors || profileHasPalette ? selected : null;
    }

    private static bool IsPaletteMode(BladeLightingMode mode) => mode is
        BladeLightingMode.Spectrum or BladeLightingMode.Wave or BladeLightingMode.Fire or
        BladeLightingMode.Wheel or BladeLightingMode.AudioMeter;

    private static bool ColorsEqual(LightingPalette left, LightingPalette right) =>
        left.Colors.Count == right.Colors.Count &&
        left.Colors.Zip(right.Colors).All(pair => pair.First == pair.Second);
}
