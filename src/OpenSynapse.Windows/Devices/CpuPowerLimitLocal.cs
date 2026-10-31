using OpenSynapse.Core.Devices;

namespace OpenSynapse.Windows.Devices;

/// <summary>Picks the vendor backend. The machine service uses the same entry point.</summary>
public static class CpuPowerLimitLocal
{
    public static CpuPowerLimitAvailability TryOpen(
        out ICpuPowerLimitController? controller,
        out string? detail)
    {
        if (!CpuVendor.IsAmd(out _))
        {
            return CpuPowerLimitController.TryOpen(out controller, out detail);
        }

        var availability = RazerAmdPowerLimitController.TryOpen(out controller, out detail);
        if (controller is not null || availability != CpuPowerLimitAvailability.AmdDriverMissing)
        {
            return availability;
        }

        // Keep the direct SMU path for models where the Razer component is not offered;
        // its write path now verifies the PM table instead of trusting the mailbox ack.
        return RyzenSmuCpuPowerLimit.TryOpen(out controller, out detail);
    }
}
