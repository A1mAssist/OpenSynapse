using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Keyboard-lighting admission for the firmware-effect fallback.
///
/// A matrix model whose firmware rejects the per-key channel drops to these effects
/// for the rest of the session. Their stored effect RGB is not readable, so no color
/// readback, custom palette, frame-pumped effect or Chroma override is offered.
/// </summary>
public static class BladeLighting2023Admitted
{
    /// <summary>
    /// True where the manifest declares the firmware-effect dialect. The transport
    /// controller must not infer this from a product id.
    /// </summary>
    public static bool UsesFirmwareKeyboardLighting(BladeLightingDialect dialect) =>
        dialect == BladeLightingDialect.FirmwareEffect;

    /// <summary>Effects this model can render in firmware, in display order.</summary>
    public static IReadOnlyList<BladeLightingMode> Effects { get; } =
    [
        BladeLightingMode.Off,
        BladeLightingMode.Breathing,
        BladeLightingMode.Spectrum,
        BladeLightingMode.Wave,
    ];

    public static bool Allows(BladeLightingMode mode) => Effects.Contains(mode);

    /// <summary>
    /// Maps an admitted mode onto its wire effect. Callers must check
    /// <see cref="Allows"/> first; an unadmitted mode has no representation here.
    /// </summary>
    public static BladeLighting2023Effect ToWireEffect(BladeLightingMode mode) => mode switch
    {
        BladeLightingMode.Off => BladeLighting2023Effect.Off,
        BladeLightingMode.Breathing => BladeLighting2023Effect.Breathing,
        BladeLightingMode.Spectrum => BladeLighting2023Effect.Spectrum,
        BladeLightingMode.Wave => BladeLighting2023Effect.Wave,
        _ => throw new ArgumentOutOfRangeException(
            nameof(mode),
            mode,
            "This Blade model does not render that effect in firmware."),
    };
}
