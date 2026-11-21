using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed record RazerGamepadWirelessState(
    RazerWirelessConnectionState? Active,
    IReadOnlyList<string> Errors);

public sealed partial class RazerGamepadService
{
    /// <summary>
    /// Protocol 25 class-0x00 pairing commands from the gamepad bundles:
    /// scan [1,0,70], pair [3,0,65] (mode, product-id high, product-id low),
    /// active wireless state [2,0,199].
    /// </summary>
    public async Task<RazerGamepadWirelessState> ReadWirelessStateAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        RazerWirelessConnectionState? active = null;
        try
        {
            active = await QueryPairingAsync(
                RazerPairingProtocol.CreateGetActiveWirelessConnectionStateRequest,
                "active wireless state",
                (response, request) =>
                    RazerPairingProtocol.ParseActiveWirelessConnectionState(response, request),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            errors.Add("active wireless state");
            _diagnostic?.Invoke($"gamepad active wireless state unavailable: {exception.Message}");
        }

        return new RazerGamepadWirelessState(active, errors);
    }

    /// <summary>
    /// Puts the current receiver into scan mode, then asks it to pair the requested
    /// gamepad product id. The middleware sends pairing with transaction id 0.
    /// </summary>
    public async Task PairWirelessAsync(
        ushort productId,
        byte mode = RazerPairingProtocol.PairModeDefault,
        CancellationToken cancellationToken = default)
    {
        if (productId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productId), "A gamepad product id is required.");
        }

        await SendPairingAsync(
            RazerPairingProtocol.CreateSetDeviceScanRequest(0, RazerPairingProtocol.ScanStart),
            "start wireless scan",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        await SendPairingAsync(
            RazerPairingProtocol.CreateSetDevicePairingModeRequest(0, mode, productId),
            "set wireless pairing mode",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> QueryPairingAsync<T>(
        Func<byte, byte[]> createRequest,
        string label,
        Func<byte[], byte[], T> parse,
        CancellationToken cancellationToken)
    {
        var request = createRequest(NextTransactionId());
        request[0] = _reportId;
        _diagnostic?.Invoke(
            $"gamepad {label}: report={request[0]}, size={request[6]}, " +
            $"class=0x{request[7]:X2}, command=0x{request[8]:X2}");
        var response = await _transport.QueryPreparedAsync(
            _devicePath, request, _deviceWait, cancellationToken).ConfigureAwait(false);
        return parse(response, request);
    }

    private async Task SendPairingAsync(
        byte[] request,
        string label,
        CancellationToken cancellationToken)
    {
        request[0] = _reportId;
        _diagnostic?.Invoke(
            $"gamepad {label}: report={request[0]}, size={request[6]}, " +
            $"class=0x{request[7]:X2}, command=0x{request[8]:X2}");
        await _transport.QueryPreparedAsync(
            _devicePath, request, _deviceWait, cancellationToken).ConfigureAwait(false);
    }
}
