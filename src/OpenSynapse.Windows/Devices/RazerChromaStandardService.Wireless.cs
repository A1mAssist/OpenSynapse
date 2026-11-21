using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerChromaStandardService
{
    /// <summary>
    /// Reads the IoT device's active wireless link: class 0x00 command 0xC7, size 2.
    /// A wired-only endpoint or firmware without the command returns null.
    /// </summary>
    public async Task<bool?> TryReadWirelessOnlineAsync(CancellationToken cancellationToken = default)
    {
        if (_productId is not { } productId || !IsIotProduct(productId))
        {
            return null;
        }

        try
        {
            var request = RazerPairingProtocol.CreateGetActiveWirelessConnectionStateRequest(
                NextTransactionId());
            _diagnostic?.Invoke(
                $"chroma wireless state: size={request[6]}, " +
                $"class=0x{request[7]:X2}, command=0x{request[8]:X2}");
            var response = await _transport.QueryPreparedAsync(
                _devicePath, request, _deviceWait, cancellationToken).ConfigureAwait(false);
            var state = RazerPairingProtocol.ParseActiveWirelessConnectionState(response, request);
            return state.Status == RazerPairingProtocol.WirelessLinkConnected;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke($"chroma wireless state unavailable: {exception.Message}");
            return null;
        }
    }
}
