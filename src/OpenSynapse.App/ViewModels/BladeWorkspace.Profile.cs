using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.App.ViewModels;

public sealed partial class BladeWorkspace
{
    /// <summary>The power-state layer the current page edits without an explicit tab.</summary>
    internal BladeProfileSettings EditablePlatformProfile =>
        CurrentPowerOverrides?.Blade ?? GetActiveProfile().Global.Blade;

    private Task SavePlatformSettingAsync<T>(
        Func<T> read,
        Action<T> write,
        T value,
        Func<T, CancellationToken, Task>? restoreHardwareAsync,
        CancellationToken cancellationToken) =>
        ProfileSettingEdit.ApplyAsync(
            read,
            write,
            value,
            _ => Task.CompletedTask,
            SaveProfileAsync,
            restoreHardwareAsync,
            cancellationToken);

    private async Task RestoreStartupAnimationAsync(
        bool value,
        CancellationToken cancellationToken) =>
        await _deviceTelemetryReader.SetBladeStartupAnimationAsync(
            ActiveDescriptors, value, cancellationToken);

    private async Task RestoreAutoSleepAsync(
        int seconds,
        CancellationToken cancellationToken) =>
        await _deviceTelemetryReader.SetBladeAutoSleepAsync(
            ActiveDescriptors, seconds > 0, cancellationToken);

    private async Task RestoreTimeToDimAsync(
        int seconds,
        CancellationToken cancellationToken) =>
        await _deviceTelemetryReader.SetBladeTimeToDimAsync(
            ActiveDescriptors, seconds, cancellationToken);

    private async Task RestoreIndicatorLedAsync(
        bool value,
        CancellationToken cancellationToken) =>
        await _deviceTelemetryReader.SetBladeIndicatorLedAsync(
            ActiveDescriptors, value, cancellationToken);

    private async Task RestoreGpuCardDetectionAsync(
        bool value,
        CancellationToken cancellationToken) =>
        await _deviceTelemetryReader.SetBladeGpuCardDetectionAsync(
            ActiveDescriptors, value, cancellationToken);

    private async Task RestoreNativeDisplayModeAsync(
        bool fullHd,
        CancellationToken cancellationToken) =>
        await _deviceTelemetryReader.SetBladeNativeDisplayModeAsync(
            ActiveDescriptors,
            fullHd ? BladeNativeDisplayMode.Fhd : BladeNativeDisplayMode.Uhd,
            cancellationToken);

    private async Task RestoreLocalDimmingAsync(
        bool value,
        CancellationToken cancellationToken) =>
        await _deviceTelemetryReader.SetBladeLocalDimmingAsync(
            ActiveDescriptors, value, cancellationToken);

    private async Task RestoreCpuPowerBoostAsync(
        byte value,
        CancellationToken cancellationToken) =>
        await _deviceTelemetryReader.SetBladeCpuPowerBoostAsync(
            ActiveDescriptors, value != 0, cancellationToken);

    /// <summary>
    /// Applies the tuning values that live outside the Blade HID report. The
    /// page writes these through the privileged service; profile replay must use
    /// the same commands instead of silently skipping them.
    /// </summary>
    [NotProfileScoped]
    internal async Task<int> ApplyProfileTuningAsync(
        BladeProfileSettings profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var applied = 0;

        if (profile.GpuCoreClockOffsetMhz is not null ||
            profile.GpuMemoryClockOffsetMhz is not null)
        {
            if (!await OpenSynapseServiceClient.TrySetGpuClockOffsetAsync(
                    profile.GpuCoreClockOffsetMhz ?? 0,
                    profile.GpuMemoryClockOffsetMhz ?? 0,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    AppStrings.Text("BladeGpuClockOffsetFailed"));
            }

            applied++;
        }

        if (profile.GpuCoreClockLimitMhz is int limit)
        {
            var serviceValue = limit <= 0
                ? NvApiGpuClockLimit.MaximumMhz
                : limit;
            if (!await OpenSynapseServiceClient.TrySetGpuClockLimitAsync(
                    serviceValue, cancellationToken))
            {
                throw new InvalidOperationException(
                    AppStrings.Text("BladeGpuClockOffsetFailed"));
            }

            applied++;
        }

        if (profile.CpuCurveOffset is int curve)
        {
            var result = await OpenSynapseServiceClient.TrySetCurveOptimizerAsync(
                curve, cancellationToken);
            if (result is null || !result.Applied)
            {
                throw new InvalidOperationException(
                    result?.Error ?? AppStrings.Text("CpuCurveUnavailable"));
            }

            applied++;
        }

        if (profile.CpuPowerLimitWatts is int watts)
        {
            var report = watts <= 0
                ? await CpuPowerLimitBroker.RestoreAsync(
                    cancellationToken: cancellationToken)
                : await CpuPowerLimitBroker.ApplyAsync(
                    new CpuPowerLimit(
                        watts,
                        Math.Min(watts + 15, CpuPowerLimit.MaximumWatts),
                        CpuPowerLimit.MinimumTimeSeconds,
                        CpuPowerLimit.MaximumTimeSeconds),
                    cancellationToken);
            if (report.Availability != CpuPowerLimitAvailability.Available)
            {
                throw new InvalidOperationException(
                    report.Detail ?? AppStrings.Text("CpuPowerLimitUnavailable"));
            }

            applied++;
        }

        return applied;
    }
}
