using System.Collections.Frozen;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidSpecialLightingService
{
    /// <summary>
    /// AudioBillieT1 (0567) and AudioBastetT1 (0587) share the Kraken V4 0F:03 frame,
    /// the C0 00 01 effect command and a nine-LED ring; their dongles inherit the
    /// same lighting methods.
    /// </summary>
    private static void AddBillieDevices(Dictionary<ushort, KrakenDefinition> devices)
    {
        foreach (var (productId, name) in new (ushort, string)[]
        {
            (0x0567, "Razer Kraken V4 Pro (Wired)"),
            (0x0568, "Razer Kraken V4 Pro (2.4 GHz)"),
            (0x0587, "Razer Kraken Kitty V3 Pro (Wired)"),
            (0x0588, "Razer Kraken Kitty V3 Pro (2.4 GHz)"),
        })
        {
            devices[productId] = new(
                name,
                RazerHidKrakenLayout.Kylie,
                Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
                    RazerHidLightingEffect.BreathingSingle, RazerHidLightingEffect.Spectrum,
                    RazerHidLightingEffect.Wave, RazerHidLightingEffect.StarlightRandom,
                    RazerHidLightingEffect.Fire, RazerHidLightingEffect.AudioMeter,
                    RazerHidLightingEffect.Wheel),
                true,
                RazerHidSpecialLightingKind.KrakenV4,
                LedCount: 9);
        }
    }

    private static FrozenSet<RazerHidLightingEffect> Effects(params RazerHidLightingEffect[] effects) =>
        effects.ToFrozenSet();

    private static KrakenDefinition Kylie(string name) => new(name, RazerHidKrakenLayout.Kylie,
        Effects(RazerHidLightingEffect.Off, RazerHidLightingEffect.Static,
            RazerHidLightingEffect.Spectrum, RazerHidLightingEffect.BreathingSingle,
            RazerHidLightingEffect.BreathingDual, RazerHidLightingEffect.BreathingTriple,
            RazerHidLightingEffect.Custom), true);
}
