using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    internal const string WirelessLinkDownMessage =
        "The device is asleep or powered off (receiver online).";

    private const string WirelessLinkProbe = "razer_device_get_multiple_wireless_connection_status_v2";
    // A refresh asks this after every timed-out command in the same pass. One answer per
    // half minute is enough to keep the classification stable without extra polling.
    private static readonly TimeSpan WirelessLinkProbeLifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (bool Down, DateTimeOffset ExpiresAt)>
        _wirelessLinkProbeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs one command and, when it times out, asks the receiver whether the device is
    /// still linked. A sleeping or switched-off device keeps the receiver answering 0xBF,
    /// so that read is what separates the state from a firmware fault. A busy reply already
    /// proves the device is awake, so it skips the probe.
    /// </summary>
    private async Task<byte[]> ExecuteWithWirelessLinkClassificationAsync(
        RazerHidDeviceConnection connection,
        RazerHidRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _executor.ExecuteAsync(connection, request, cancellationToken).ConfigureAwait(false);
        }
        catch (RazerDeviceTimeoutException exception) when (!exception.IsRetryableBusy)
        {
            if (await IsWirelessLinkDownAsync(connection, cancellationToken).ConfigureAwait(false))
            {
                throw new RazerDeviceTimeoutException(WirelessLinkDownMessage, wirelessLinkDown: true);
            }

            throw;
        }
    }

    internal static bool IsWirelessLinkDownFailure(Exception exception) =>
        exception is RazerDeviceTimeoutException { WirelessLinkDown: true };

    private async Task<bool> IsWirelessLinkDownAsync(
        RazerHidDeviceConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.Definition.Category is not (DeviceCategory.Mouse or DeviceCategory.Keyboard) ||
            connection.DevicePath is not { Length: > 0 } path)
        {
            return false;
        }
        if (_wirelessLinkProbeCache.TryGetValue(path, out var cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Down;
        }

        var down = false;
        try
        {
            var request = RazerPairingProtocol.CreateGetMultipleDeviceWirelessConnectionStatusV2Request(
                MiddlewareTransactionId);
            var response = await _executor.ExecuteAsync(connection,
                new RazerHidRequest(WirelessLinkProbe, request), cancellationToken).ConfigureAwait(false);
            var statuses = RazerPairingProtocol.ParseMultipleDeviceWirelessConnectionStatusV2(response, request);
            down = statuses.Any(status =>
                status.ProductId == connection.Definition.ProductId &&
                status.Status != RazerPairingProtocol.WirelessLinkConnected);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or NotSupportedException)
        {
            // A wired endpoint or an older receiver does not answer 0xBF; keep the original
            // timeout rather than guessing at a state the hardware never reported.
        }

        _wirelessLinkProbeCache[path] = (down, DateTimeOffset.UtcNow + WirelessLinkProbeLifetime);
        return down;
    }
}
