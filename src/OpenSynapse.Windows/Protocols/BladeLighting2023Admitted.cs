using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Keyboard-lighting admission for models that render uniform effects in firmware.
///
/// These models have no per-key matrix path in the native Blade channel, so only
/// firmware-rendered effects and brightness are admitted. Their stored effect RGB is
/// not readable, so no color readback, custom palette, frame-pumped effect or Chroma
/// override is offered.
/// </summary>
public static class BladeLighting2023Admitted
{
    public const ushort Blade16_2023ProductId = 0x029F;
    public const ushort Blade14_2022ProductId = 0x028C;

    /// <summary>
    /// True where the manifest's selector-prefixed keyboard-effect reports drive the
    /// lighting, instead of the 710 matrix controller. 028C reports Spectrum through
    /// those same reports.
    /// </summary>
    public static bool UsesFirmwareKeyboardLighting(ushort productId) =>
        productId is Blade16_2023ProductId or Blade14_2022ProductId;

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
