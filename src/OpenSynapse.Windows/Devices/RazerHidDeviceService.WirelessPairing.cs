using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    // Scan (0x46) and pairing mode (0x41) take 0x00; the transport rewrites the
    // DualLink keyboard and 00A4 dock branches itself.
    private const byte PairingTransactionId = 0x00;

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
                RazerPairingProtocol.CreateSetDevicePairingModeRequest(PairingTransactionId, mode, productId)),
            cancellationToken);
    }

    public Task SetDeviceScanAsync(
        RazerHidDeviceConnection connection,
        byte mode,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        // A dock already scanning answers busy; PairWirelessCandidateAsync treats that
        // as success, so this command must not spend the write retry budget first.
        return ExecuteOnceWithoutResultAsync(connection,
            new RazerHidRequest("razer_device_set_device_scan",
                RazerPairingProtocol.CreateSetDeviceScanRequest(PairingTransactionId, mode)),
            cancellationToken);
    }

    public async Task PairWirelessCandidateAsync(
        RazerHidDeviceConnection connection,
        ushort candidateProductId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SetDeviceScanAsync(connection, RazerPairingProtocol.ScanStart, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RazerDeviceTimeoutException exception) when (
            connection.Definition.ProductId == MouseDockProProductId && exception.IsRetryableBusy)
        {
            // A Dock left scanning by an earlier attempt answers Busy; pairing can still proceed.
            WriteDiagnostic(connection.Definition.ProductId,
                "wireless pairing scan is already in progress; sending pairing mode");
        }
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        await SetDevicePairingModeAsync(connection, RazerPairingProtocol.PairModeDefault,
            candidateProductId, cancellationToken).ConfigureAwait(false);
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
