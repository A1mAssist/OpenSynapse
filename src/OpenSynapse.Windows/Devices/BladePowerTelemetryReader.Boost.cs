using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class BladePowerTelemetryReader
{
    /// <summary>
    /// Outside Custom the EC owns the level per mode, so the captured values were never
    /// ours to put back. Writing them anyway fails the readback and reports a false alarm.
    /// </summary>
    private async Task<bool> BoostWritesApplyAsync(ResolvedRazerDevice device) =>
        (await _fan.ReadThermalStateAsync(device, CancellationToken.None)).PerformanceMode ==
        BladePerformanceMode.Custom;

    private async Task<bool> TryRestoreBoostStateAsync(
        ResolvedRazerDevice device,
        BladeBoostState state)
    {
        try
        {
            if (!await BoostWritesApplyAsync(device))
            {
                return false;
            }

            await WriteBoostModeAsync(
                device, BladeBoostProtocol.CpuCluster, (byte)state.Cpu, CancellationToken.None);
            await WriteBoostModeAsync(
                device, BladeBoostProtocol.GpuCluster, (byte)state.Gpu, CancellationToken.None);
            return await ReadBoostStateAsync(device, CancellationToken.None) == state;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }

    internal async Task<bool> TryRestoreBoostLevelsAsync(ResolvedRazerDevice device, BladeBoostState state)
    {
        try
        {
            if (!await BoostWritesApplyAsync(device))
            {
                return true;
            }

            await WriteBoostModeAsync(
                device, BladeBoostProtocol.CpuCluster, (byte)state.Cpu, CancellationToken.None);
            await WriteBoostModeAsync(
                device, BladeBoostProtocol.GpuCluster, (byte)state.Gpu, CancellationToken.None);
            return await ReadBoostStateAsync(device, CancellationToken.None) == state;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }
}
