using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class BladeFanTelemetryReader
{
    internal Func<ResolvedRazerDevice, CancellationToken, Task<BladeBoostState>>? CaptureBoostState { get; set; }
    internal Func<ResolvedRazerDevice, BladeBoostState, Task<bool>>? RestoreBoostState { get; set; }

    internal async Task<byte> ReadAdvancedFanModeAsync(
        ResolvedRazerDevice device,
        byte fanId,
        CancellationToken cancellationToken)
    {
        var request = BladeThermalProtocol.CreateGetAdvancedFanModeRequest(fanId);
        var response = await _client.QueryBuiltRequestAsync(
            device, CapabilityIds.AdvancedFanModeGet, request, cancellationToken);
        return BladeThermalProtocol.ParseAdvancedFanMode(
            response,
            fanId,
            RazerCapabilityClient.CreateConfiguredRequest(device, CapabilityIds.AdvancedFanModeGet, request));
    }

    private async Task<BladeBoostState?> CaptureCustomBoostAsync(
        ResolvedRazerDevice blade,
        BladeFanTransactionState original,
        CancellationToken cancellationToken)
    {
        if (original.Thermal.PerformanceMode != BladePerformanceMode.Custom ||
            !blade.SupportsCpuBoost ||
            CaptureBoostState is not { } capture)
        {
            return null;
        }

        try
        {
            return await capture(blade, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return null;
        }
    }

    private async Task<bool> TryRestoreCustomBoostAsync(
        ResolvedRazerDevice blade,
        BladeBoostState? state)
    {
        if (state is null || RestoreBoostState is not { } restore)
        {
            return true;
        }

        try
        {
            return await restore(blade, state).ConfigureAwait(false);
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            return false;
        }
    }
}
