using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>Picks the vendor backend. The machine service uses the same entry point.</summary>
public static class CpuPowerLimitLocal
{
    public static CpuPowerLimitAvailability TryOpen(
        out ICpuPowerLimitController? controller,
        out string? detail) =>
        CpuVendor.IsAmd(out _)
            ? RyzenSmuCpuPowerLimit.TryOpen(out controller, out detail)
            : CpuPowerLimitController.TryOpen(out controller, out detail);
}
