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
        if (controller is not null)
        {
            return availability;
        }

        // The Razer component can be installed on a model whose firmware exposes no PPT
        // capability, and it is not installed at all on most AMD models. Its write path
        // now verifies the PM table instead of trusting the mailbox ack, so the direct
        // SMU path reads and writes the same wall without the vendor DLL.
        var smu = RyzenSmuCpuPowerLimit.TryOpen(out controller, out var smuDetail);
        if (controller is not null)
        {
            detail = null;
            return smu;
        }

        // Keep the component-install hint only when the SMU path cannot run either;
        // otherwise report why the SMU path failed.
        if (availability == CpuPowerLimitAvailability.AmdDriverMissing)
        {
            return availability;
        }

        detail = smuDetail;
        return smu;
    }
}
