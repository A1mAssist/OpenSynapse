using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class BladeFanTelemetryReader
{
    public async ValueTask<BladeFanControlSnapshot> SetBladeFanCurveTargetsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
        CancellationToken cancellationToken = default) =>
        await SetBladeFanTargetsCoreAsync(
            devices, mode, cpuTargetRpm, gpuTargetRpm, forceModeWrite: true, cancellationToken);

    private async ValueTask<BladeFanControlSnapshot> SetBladeFanTargetsCoreAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
        bool forceModeWrite,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        var blade = _resolver.FindReadyBlade(devices)
            ?? throw new InvalidOperationException("The Blade fan control channel is unavailable.");
        if (!blade.SupportsFanCurve)
        {
            throw new InvalidOperationException("Fan-curve control is not admitted for this Blade model.");
        }
        if (mode == BladeFanMode.Manual)
        {
            BladeFanProtocol.ValidateCurveTargetRpm(cpuTargetRpm, blade.MaximumWritableFanTargetRpm);
            BladeFanProtocol.ValidateCurveTargetRpm(gpuTargetRpm, blade.MaximumWritableFanTargetRpm);
        }
        var original = await ReadFanTransactionStateAsync(blade, cancellationToken);
        if (mode == BladeFanMode.Manual)
        {
            // The EC stops answering the target report below its published floor.
            // Clamp to that range so a point below it cannot silence the readback.
            var range = await ReadFanRangeAsync(
                    blade, original.Thermal.PerformanceMode, cancellationToken)
                ?? blade.GetBladeFanCurveTargetRange(original.Thermal.PerformanceMode);
            cpuTargetRpm = Math.Clamp(cpuTargetRpm, range.MinimumRpm, range.MaximumRpm);
            gpuTargetRpm = Math.Clamp(gpuTargetRpm, range.MinimumRpm, range.MaximumRpm);
        }
        var actual = await SetFanTargetsCoreAsync(
            blade,
            mode,
            cpuTargetRpm,
            gpuTargetRpm,
            writeTargets: mode == BladeFanMode.Manual,
            forceModeWrite,
            original,
            cancellationToken);
        return actual.ToPublic();
    }
}
