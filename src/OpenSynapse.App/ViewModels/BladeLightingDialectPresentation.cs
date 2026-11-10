using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

/// <summary>
/// Presentation rules for the lighting dialect declared by one Blade manifest.
/// Wire-format decisions stay in the lighting controller.
/// </summary>
internal static class BladeLightingDialectPresentation
{
    private static readonly BladeLightingMode[] GeometryModes =
    [
        BladeLightingMode.Off,
        BladeLightingMode.Static,
        BladeLightingMode.Breathing,
        BladeLightingMode.Spectrum,
        BladeLightingMode.Wave,
        BladeLightingMode.Fire,
        BladeLightingMode.Reactive,
        BladeLightingMode.Ripple,
        BladeLightingMode.AudioMeter,
        BladeLightingMode.Ambient,
        BladeLightingMode.Wheel,
        BladeLightingMode.Starlight,
        BladeLightingMode.Tidal,
        BladeLightingMode.ChromaFile,
    ];

    public static IReadOnlyList<BladeLightingMode> Modes(
        BladeLightingDialect dialect,
        bool hasChromaAnimation) => dialect switch
    {
        BladeLightingDialect.FirmwareEffect => BladeLighting2023Admitted.Effects,
        BladeLightingDialect.LedMatrixBlade => hasChromaAnimation
            ? [
                BladeLightingMode.Off,
                BladeLightingMode.Static,
                BladeLightingMode.Breathing,
                BladeLightingMode.Spectrum,
                BladeLightingMode.Wave,
                BladeLightingMode.Reactive,
                BladeLightingMode.ChromaFile,
            ]
            : [
                BladeLightingMode.Off,
                BladeLightingMode.Static,
                BladeLightingMode.Breathing,
                BladeLightingMode.Spectrum,
                BladeLightingMode.Wave,
                BladeLightingMode.Reactive,
            ],
        BladeLightingDialect.LedMatrix710 or
            BladeLightingDialect.LedMatrixBlade2 or
            BladeLightingDialect.LedMatrix2026 => hasChromaAnimation
                ? GeometryModes
                : GeometryModes[..^1],
        _ => [],
    };

    public static IReadOnlyList<string> ModeTextKeys(
        BladeLightingDialect dialect,
        bool hasChromaAnimation) =>
        Modes(dialect, hasChromaAnimation)
            .Select(TextKey)
            .ToArray();

    public static bool ShowsPrimaryColor(
        BladeLightingDialect dialect,
        BladeLightingMode? mode) => dialect switch
    {
        BladeLightingDialect.LedMatrixBlade =>
            mode is BladeLightingMode.Static or BladeLightingMode.Breathing or BladeLightingMode.Reactive,
        _ => false,
    };

    public static bool ShowsWaveDirection(
        BladeLightingDialect dialect,
        BladeLightingMode? mode) =>
        (dialect is BladeLightingDialect.LedMatrixBlade or BladeLightingDialect.LedMatrix710 or
            BladeLightingDialect.LedMatrixBlade2 or BladeLightingDialect.LedMatrix2026) &&
        mode is BladeLightingMode.Wave or BladeLightingMode.Wheel;

    private static string TextKey(BladeLightingMode mode) => mode switch
    {
        BladeLightingMode.Off => "Text_39B523BD",
        BladeLightingMode.Static => "Text_8FF9138F",
        BladeLightingMode.Breathing => "Text_7EDA32B9",
        BladeLightingMode.Spectrum => "Text_21BE26F0",
        BladeLightingMode.Wave => "Text_165FCE9D",
        BladeLightingMode.Fire => "Text_5DCB722E",
        BladeLightingMode.Reactive => "Text_E2309DE4",
        BladeLightingMode.Ripple => "Text_D1D985CC",
        BladeLightingMode.AudioMeter => "Text_F3845B2A",
        BladeLightingMode.Ambient => "Text_33F4E277",
        BladeLightingMode.Wheel => "Text_2A3A07A1",
        BladeLightingMode.Starlight => "Text_51212D25",
        BladeLightingMode.Tidal => "Text_388555B3",
        BladeLightingMode.ChromaFile => "BladeLightingModeChromaFile",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string LogoEffectTextKey(BladeLogoEffect effect) => effect switch
    {
        BladeLogoEffect.Static => "Text_6295D9CB",
        BladeLogoEffect.Breathing => "Text_7EDA32B9",
        BladeLogoEffect.Spectrum => "Text_21BE26F0",
        BladeLogoEffect.Wave => "Text_165FCE9D",
        BladeLogoEffect.Reactive => "Text_E2309DE4",
        BladeLogoEffect.Ripple => "Text_D1D985CC",
        BladeLogoEffect.Starlight => "Text_51212D25",
        BladeLogoEffect.Fire => "Text_5DCB722E",
        BladeLogoEffect.Ambient => "Text_33F4E277",
        BladeLogoEffect.AudioMeter => "Text_F3845B2A",
        BladeLogoEffect.Wheel => "Text_2A3A07A1",
        BladeLogoEffect.BatteryLevel => "BladeLogoEffectBatteryLevel",
        BladeLogoEffect.Lamborghini => "BladeLogoEffectLamborghini",
        BladeLogoEffect.Radiate => "LightingStudioEffectRadiate",
        BladeLogoEffect.Tidal => "Text_388555B3",
        BladeLogoEffect.CustomFrame => "BladeLogoEffectCustomFrame",
        BladeLogoEffect.Destiny2 => "BladeLogoEffectDestiny2",
        BladeLogoEffect.ExtendedBreathing => "BladeLogoEffectExtendedBreathing",
        _ => throw new ArgumentOutOfRangeException(nameof(effect)),
    };
}
