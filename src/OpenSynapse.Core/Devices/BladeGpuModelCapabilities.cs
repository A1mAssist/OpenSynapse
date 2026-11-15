using System.Collections.Frozen;
using OpenSynapse.Core.Displays;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Destructive GPU-model admissions kept separate from the fan/power contract. The
/// PnP switch requires an admitted MUX model and live display-policy checks; a MUX
/// alone does not protect an external display wired to the discrete GPU.
/// </summary>
public static class BladeGpuModelCapabilities
{
    // 028C's official UI exposes Optimus and Dedicated GPU, but its firmware does
    // not always answer the separate 0D:8A capability report. Its bundle still uses
    // the same 0D:89/09/8A VGA Configure family as the Blade 16 (2023).
    private static readonly FrozenDictionary<ushort, BladeDisplayMuxCapabilities> KnownDisplayMuxCapabilities =
        new Dictionary<ushort, BladeDisplayMuxCapabilities>
        {
            [0x028C] = BladeDisplayMuxCapabilities.Hybrid | BladeDisplayMuxCapabilities.Discrete,
        }.ToFrozenDictionary();

    private static readonly FrozenSet<ushort> DedicatedGpuPnpSwitchProductIds =
        new ushort[]
        {
            0x024C, 0x0276, 0x029D, 0x029F, 0x02A0, 0x02B6, 0x02B7, 0x02B8,
            0x02C5, 0x02C6, 0x02C7, 0x02E0, 0x02E1,
        }.ToFrozenSet();

    public static bool SupportsDedicatedGpuPnpSwitch(ushort productId) =>
        DedicatedGpuPnpSwitchProductIds.Contains(productId);

    public static BladeDisplayMuxCapabilities KnownDisplayMuxCapabilitiesFor(ushort productId) =>
        KnownDisplayMuxCapabilities.TryGetValue(productId, out var capabilities)
            ? capabilities
            : BladeDisplayMuxCapabilities.None;

    internal static IReadOnlyCollection<ushort> AdmittedProductIds =>
        DedicatedGpuPnpSwitchProductIds;
}
