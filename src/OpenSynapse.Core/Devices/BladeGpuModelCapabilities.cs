using System.Collections.Frozen;

namespace OpenSynapse.Core.Devices;

/// <summary>
/// Destructive GPU-model admissions kept separate from the fan/power contract. The
/// PnP switch requires an admitted MUX model and live display-policy checks; a MUX
/// alone does not protect an external display wired to the discrete GPU.
/// </summary>
public static class BladeGpuModelCapabilities
{
    private static readonly FrozenSet<ushort> DedicatedGpuPnpSwitchProductIds =
        new ushort[]
        {
            0x0276, 0x029D, 0x029F, 0x02B6, 0x02B7, 0x02B8,
            0x02C5, 0x02C6, 0x02C7, 0x02E0, 0x02E1,
        }.ToFrozenSet();

    public static bool SupportsDedicatedGpuPnpSwitch(ushort productId) =>
        DedicatedGpuPnpSwitchProductIds.Contains(productId);

    internal static IReadOnlyCollection<ushort> AdmittedProductIds =>
        DedicatedGpuPnpSwitchProductIds;
}
