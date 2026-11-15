using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class RazerHidTelemetryReader
{
    internal sealed record RazerHidLiveTelemetryReadResult(
        int? BatteryPercent,
        bool WirelessLinkDown,
        IReadOnlyList<string> Errors);

    public async ValueTask<RazerHidLiveTelemetryReadResult> ReadLiveAsync(
        ResolvedRazerDevice viper,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viper);
        // While the receiver reports no link, every device command would burn its
        // full retry budget for nothing. One dongle read is enough to keep saying so.
        if (_wirelessLinkDown && await IsWirelessLinkDownAsync(viper, cancellationToken).ConfigureAwait(false))
        {
            return new RazerHidLiveTelemetryReadResult(null, true, []);
        }

        int? batteryPercent = null;
        var errors = new List<string>();
        try
        {
            var response = await _client.QueryAsync(viper, CapabilityIds.BatteryGet, cancellationToken);
            batteryPercent = ViperProduct184Protocol.ParseBatteryPercent(
                response, RazerCapabilityClient.CreateRequest(viper, CapabilityIds.BatteryGet));
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            errors.Add($"Mouse battery: {exception.Message}");
        }

        var wirelessLinkDown =
            batteryPercent is null && errors.Count > 0 &&
            await IsWirelessLinkDownAsync(viper, cancellationToken).ConfigureAwait(false);
        _wirelessLinkDown = wirelessLinkDown;
        if (wirelessLinkDown)
        {
            errors.Clear();
        }

        return new RazerHidLiveTelemetryReadResult(batteryPercent, wirelessLinkDown, errors);
    }
}
