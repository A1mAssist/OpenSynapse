using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Keyboard-lighting admission for the Blade 16 2023 generation.
///
/// This model has no per-key matrix report, so only firmware-rendered uniform effects
/// and brightness are admitted. Its stored effect RGB is not readable, so no color
/// readback, custom palette, frame-pumped effect or Chroma override is offered.
/// </summary>
public static class BladeLighting2023Admitted
{
    public const ushort Blade16_2023ProductId = 0x029F;

    public static bool IsBlade2023(ushort productId) => productId == Blade16_2023ProductId;

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