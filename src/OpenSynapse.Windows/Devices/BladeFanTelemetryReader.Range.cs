using System.Collections.Concurrent;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed partial class BladeFanTelemetryReader
{
    private readonly ConcurrentDictionary<(string DeviceId, BladePerformanceMode Mode), BladeFanTargetRange> _fanRanges = new();

    /// <summary>
    /// Reads the range the EC publishes for the mode it is currently in. Never switches
    /// modes, so a telemetry poll cannot disturb a running curve.
    /// </summary>
    internal async ValueTask<BladeFanTargetRange?> ReadFanRangeAsync(
        ResolvedRazerDevice blade,
        BladePerformanceMode mode,
        CancellationToken cancellationToken)
    {
        if (!blade.Owns(CapabilityIds.FanInformationGet))
        {
            return null;
        }

        if (_fanRanges.TryGetValue((blade.Descriptor.Id, mode), out var cached))
        {
            return cached;
        }

        try
        {
            var request = BladeThermalProtocol.CreateGetFanInformationRequest();
            var response = await _client.QueryBuiltRequestAsync(
                blade, CapabilityIds.FanInformationGet, request, cancellationToken);
            var info = BladeThermalProtocol.ParseFanInformation(response);
            var maximum = Math.Min(info.MaximumFanSpeedRpm, blade.MaximumWritableFanTargetRpm);
            var minimum = Math.Min(info.MinimumFanSpeedRpm, maximum);
            if (minimum <= 0 || maximum <= minimum)
            {
                return null;
            }

            var range = new BladeFanTargetRange(
                minimum,
                maximum,
                Math.Clamp(info.DefaultFanSpeedRpm, minimum, maximum));
            _fanRanges[(blade.Descriptor.Id, mode)] = range;
            return range;
        }
        catch (Exception exception) when (RazerCapabilityClient.IsExpectedHardwareException(exception))
        {
            // Firmware that refuses 0D:83 keeps the contract range; never block fan control.
            return null;
        }
    }
}
