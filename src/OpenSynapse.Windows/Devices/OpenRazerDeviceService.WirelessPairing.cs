using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class OpenRazerDeviceService
{
    public async Task<RazerWirelessConnectionState> GetActiveWirelessConnectionStateAsync(
        OpenRazerDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request = RazerPairingProtocol.CreateGetActiveWirelessConnectionStateRequest(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_device_get_active_wireless_connection_state", request),
            cancellationToken).ConfigureAwait(false);
        return RazerPairingProtocol.ParseActiveWirelessConnectionState(response, request);
    }

    public async Task<IReadOnlyList<RazerWirelessDeviceConnectionStatus>>
        GetMultipleDeviceWirelessConnectionStatusV2Async(
            OpenRazerDeviceConnection connection,
            CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var request =
            RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(MiddlewareTransactionId);
        var response = await ExecuteAsync(connection,
            new OpenRazerRequest("razer_device_get_multiple_wireless_connection_status_v2", request),
            cancellationToken).ConfigureAwait(false);
        return RazerPairingProtocol.ParseMultipleDeviceWirelessConnectionStatusV2(response, request);
    }

    public Task SetDevicePairingModeAsync(
        OpenRazerDeviceConnection connection,
        byte mode,
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_device_set_pairing_mode",
                RazerPairingProtocol.CreateSetDevicePairingModeRequest(MiddlewareTransactionId, mode, productId)),
            cancellationToken);
    }

    public Task UnpairWirelessDeviceAsync(
        OpenRazerDeviceConnection connection,
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_device_unpair",
                RazerPairingProtocol.CreateSetDeviceUnpairRequest(MiddlewareTransactionId, productId)),
            cancellationToken);
    }

    public Task SetActiveWirelessConnectionStateAsync(
        OpenRazerDeviceConnection connection,
        byte connectionType,
        byte state,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        return ExecuteWithoutResultAsync(connection,
            new OpenRazerRequest("razer_device_set_active_wireless_connection_state",
                RazerPairingProtocol.CreateSetActiveWirelessConnectionStateRequest(
                    MiddlewareTransactionId, connectionType, state)),
            cancellationToken);
    }

    internal async Task ProbeWirelessPairingAsync(
        OpenRazerDeviceDefinition definition,
        string devicePath,
        ISet<OpenRazerBackendCapability> result,
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
            result.Add(OpenRazerBackendCapability.WirelessConnectionStateRead);
            result.Add(OpenRazerBackendCapability.WirelessPairingWrite);
        }
    }
}
