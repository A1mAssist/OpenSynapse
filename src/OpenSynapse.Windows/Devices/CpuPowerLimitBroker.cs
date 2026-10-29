using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// Picks the cheapest privileged path for the CPU power wall: the in-process PawnIO
/// executor when possible, otherwise the machine-wide service, but only after an
/// access-denied result. Every other failure is final and returned directly.
/// </summary>
public static class CpuPowerLimitBroker
{
    public static async Task<CpuPowerLimitReport> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var availability = CpuPowerLimitController.TryOpen(out var controller, out var detail);
        if (controller is not null)
        {
            using (controller)
            {
                return controller.Read();
            }
        }

        if (availability != CpuPowerLimitAvailability.AccessDenied)
        {
            return CpuPowerLimitReport.Unavailable(availability, detail);
        }

        var report = await DedicatedGpuModeServiceClient
            .TryGetCpuPowerLimitAsync(cancellationToken)
            .ConfigureAwait(false);
        return report ?? NoPrivilegedPath(detail);
    }

    public static async Task<CpuPowerLimitReport> ApplyAsync(
        CpuPowerLimit limit,
        CancellationToken cancellationToken = default)
    {
        var availability = CpuPowerLimitController.TryOpen(out var controller, out var detail);
        if (controller is not null)
        {
            using (controller)
            {
                return controller.Apply(limit);
            }
        }

        if (availability != CpuPowerLimitAvailability.AccessDenied)
        {
            return CpuPowerLimitReport.Unavailable(availability, detail);
        }

        var report = await DedicatedGpuModeServiceClient
            .TrySetCpuPowerLimitAsync(limit.Pl1Watts, limit.Pl2Watts, cancellationToken)
            .ConfigureAwait(false);
        return report ?? NoPrivilegedPath(detail);
    }

    public static async Task<CpuPowerLimitReport> RestoreAsync(
        CpuPowerLimit? baseline = null,
        CancellationToken cancellationToken = default)
    {
        var availability = CpuPowerLimitController.TryOpen(out var controller, out var detail);
        if (controller is not null)
        {
            using (controller)
            {
                return baseline is { } value
                    ? controller.RestoreTo(value.Pl1Watts, value.Pl2Watts)
                    : controller.RestoreFirmware();
            }
        }

        if (availability != CpuPowerLimitAvailability.AccessDenied)
        {
            return CpuPowerLimitReport.Unavailable(availability, detail);
        }

        var report = await DedicatedGpuModeServiceClient
            .TryRestoreCpuPowerLimitAsync(
                baseline?.Pl1Watts,
                baseline?.Pl2Watts,
                cancellationToken)
            .ConfigureAwait(false);
        return report ?? NoPrivilegedPath(detail);
    }

    private static CpuPowerLimitReport NoPrivilegedPath(string? detail) =>
        CpuPowerLimitReport.Unavailable(
            CpuPowerLimitAvailability.AccessDenied,
            $"No privileged path to the CPU power limit (PawnIO access denied: {detail}).");
}
