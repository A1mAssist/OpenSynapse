using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    public async Task<RazerWirelessConnectionState> GetActiveWirelessConnectionStateAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerPairingProtocol.CreateGetActiveWirelessConnectionStateRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_device_get_active_wireless_connection_state", request),
            cancellationToken).ConfigureAwait(false);
        return RazerPairingProtocol.ParseActiveWirelessConnectionState(response, request);
    }

    public async Task<IReadOnlyList<RazerWirelessDeviceConnectionStatus>>
        GetMultipleDeviceWirelessConnectionStatusV2Async(
            RazerHidDeviceConnection connection,
            CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request =
            RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new RazerHidRequest("razer_device_get_multiple_wireless_connection_status_v2", request),
            cancellationToken).ConfigureAwait(false);
        return RazerPairingProtocol.ParseMultipleDeviceWirelessConnectionStatusV2(response, request);
    }

    public Task SetDevicePairingModeAsync(
        RazerHidDeviceConnection connection,
        byte mode,
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_device_set_pairing_mode",
                RazerPairingProtocol.CreateSetDevicePairingModeRequest(MiddlewareTransactionId, mode, productId)),
            cancellationToken);
    }

    public Task UnpairWirelessDeviceAsync(
        RazerHidDeviceConnection connection,
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_device_unpair",
                RazerPairingProtocol.CreateSetDeviceUnpairRequest(MiddlewareTransactionId, productId)),
            cancellationToken);
    }

    public Task SetActiveWirelessConnectionStateAsync(
        RazerHidDeviceConnection connection,
        byte connectionType,
        byte state,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new RazerHidRequest("razer_device_set_active_wireless_connection_state",
                RazerPairingProtocol.CreateSetActiveWirelessConnectionStateRequest(
                    MiddlewareTransactionId, connectionType, state)),
            cancellationToken);
    }

    internal async Task ProbeWirelessPairingAsync(
        RazerHidDeviceDefinition definition,
        string devicePath,
        ISet<RazerHidBackendCapability> result,
        CancellationToken cancellationToken)
    {
        if (definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard))
        {
            return;
        }

        var request = RazerPairingProtocol.CreateGetActiveWirelessConnectionStateRequest(MiddlewareTransactionId);
        // Wired devices commonly reject or time out on this GET; ProbeMiddlewareAsync records that as unsupported.
        if (await ProbeMiddlewareAsync(definition.ProductId, devicePath, definition.DeviceWait,
                "wireless.activeState", request, response =>
                {
                    _ = RazerPairingProtocol.ParseActiveWirelessConnectionState(response, request);
                    return true;
                }, cancellationToken).ConfigureAwait(false))
        {
            result.Add(RazerHidBackendCapability.WirelessConnectionStateRead);
            result.Add(RazerHidBackendCapability.WirelessPairingWrite);
        }
    }
}
