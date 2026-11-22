namespace OpenSynapse.Core.Devices;

public partial interface IRazerDeviceTelemetryReader
{
    /// <summary>
    /// Writes a curve target and re-declares manual fan mode first. A Blade that
    /// already reports Manual can still be following its own curve after a mode or
    /// owner change; the take-over write is what makes the target effective.
    /// </summary>
    ValueTask<BladeFanControlSnapshot> SetBladeFanCurveTargetsAsync(
        IReadOnlyList<DeviceDescriptor> devices,
        BladeFanMode mode,
        int cpuTargetRpm,
        int gpuTargetRpm,
        CancellationToken cancellationToken = default) =>
        SetBladeFanTargetsAsync(devices, mode, cpuTargetRpm, gpuTargetRpm, cancellationToken);
}
