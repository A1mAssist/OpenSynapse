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
        var local = await RunLocalAsync(controller => controller.Read(), cancellationToken)
            .ConfigureAwait(false);
        if (local.Report is { } report)
        {
            return report;
        }

        if (local.Availability != CpuPowerLimitAvailability.AccessDenied)
        {
            return CpuPowerLimitReport.Unavailable(local.Availability, local.Detail);
        }

        var served = await DedicatedGpuModeServiceClient
            .TryGetCpuPowerLimitAsync(cancellationToken)
            .ConfigureAwait(false);
        return served ?? NoPrivilegedPath(local.Detail);
    }

    public static async Task<CpuPowerLimitReport> ApplyAsync(
        CpuPowerLimit limit,
        CancellationToken cancellationToken = default)
    {
        var local = await RunLocalAsync(controller => controller.Apply(limit), cancellationToken)
            .ConfigureAwait(false);
        if (local.Report is { } report)
        {
            return report;
        }

        if (local.Availability != CpuPowerLimitAvailability.AccessDenied)
        {
            return CpuPowerLimitReport.Unavailable(local.Availability, local.Detail);
        }

        var served = await DedicatedGpuModeServiceClient
            .TrySetCpuPowerLimitAsync(limit.Pl1Watts, limit.Pl2Watts, cancellationToken)
            .ConfigureAwait(false);
        return served ?? NoPrivilegedPath(local.Detail);
    }

    public static async Task<CpuPowerLimitReport> RestoreAsync(
        CpuPowerLimit? baseline = null,
        CancellationToken cancellationToken = default)
    {
        var local = await RunLocalAsync(
                controller => baseline is { } value
                    ? controller.RestoreTo(value.Pl1Watts, value.Pl2Watts)
                    : controller.RestoreFirmware(),
                cancellationToken)
            .ConfigureAwait(false);
        if (local.Report is { } report)
        {
            return report;
        }

        if (local.Availability != CpuPowerLimitAvailability.AccessDenied)
        {
            return CpuPowerLimitReport.Unavailable(local.Availability, local.Detail);
        }

        var served = await DedicatedGpuModeServiceClient
            .TryRestoreCpuPowerLimitAsync(
                baseline?.Pl1Watts,
                baseline?.Pl2Watts,
                cancellationToken)
            .ConfigureAwait(false);
        return served ?? NoPrivilegedPath(local.Detail);
    }

    /// <summary>
    /// Opening PawnIO and refreshing the AMD power table take hundreds of milliseconds,
    /// so the UI thread must not be the one waiting for them.
    /// </summary>
    private static Task<(CpuPowerLimitAvailability Availability, string? Detail, CpuPowerLimitReport? Report)>
        RunLocalAsync(
            Func<ICpuPowerLimitController, CpuPowerLimitReport> action,
            CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var availability = CpuPowerLimitLocal.TryOpen(out var controller, out var detail);
                if (controller is null)
                {
                    return (availability, detail, Report: (CpuPowerLimitReport?)null);
                }

                using (controller)
                {
                    return (availability, detail, Report: (CpuPowerLimitReport?)action(controller));
                }
            },
            cancellationToken);

    private static CpuPowerLimitReport NoPrivilegedPath(string? detail) =>
        CpuPowerLimitReport.Unavailable(
            CpuPowerLimitAvailability.AccessDenied,
            $"No privileged path to the CPU power limit (PawnIO access denied: {detail}).");
}
