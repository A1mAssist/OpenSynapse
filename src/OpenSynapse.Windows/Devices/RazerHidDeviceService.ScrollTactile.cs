using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<RazerScrollTactileMode> GetScrollTactileModeAsync(
        RazerHidDeviceConnection connection,
        byte profileId,
        CancellationToken cancellationToken = default)
    {
        var request = RazerScrollWheelProtocol.CreateGetScrollTactileModeRequest(
            MiddlewareTransactionId, profileId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_scroll_get_tactile_mode", request), cancellationToken).ConfigureAwait(false);
        return RazerScrollWheelProtocol.ParseScrollTactileMode(response, request).Mode;
    }

    public Task SetScrollTactileModeAsync(
        RazerHidDeviceConnection connection,
        RazerScrollTactileMode mode,
        byte profileId,
        CancellationToken cancellationToken = default) =>
        ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_scroll_set_tactile_mode",
                RazerScrollWheelProtocol.CreateSetScrollTactileModeRequest(
                    MiddlewareTransactionId, mode, profileId)),
            cancellationToken);
}
