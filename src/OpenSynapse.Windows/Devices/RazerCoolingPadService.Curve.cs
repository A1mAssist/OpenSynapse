using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerCoolingPadService
{
    /// <summary>Writes one smart-curve target and verifies it with the normal speed read.</summary>
    public async Task<int> SetCurveSpeedAsync(int rpm, CancellationToken cancellationToken = default)
    {
        _ = RazerCoolingPadProtocol.CreateSetCurveSpeedRequest(rpm);
        try
        {
            await QueryAsync(
                RazerCoolingPadProtocol.CreateSetCurveSpeedRequest(rpm),
                "set curve speed",
                cancellationToken).ConfigureAwait(false);
            var actual = await ReadSpeedRpmAsync(cancellationToken).ConfigureAwait(false);
            if (actual != rpm)
            {
                throw new InvalidOperationException(
                    $"Cooling pad curve speed readback mismatch: wrote {rpm}, read {actual} RPM.");
            }

            return actual;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            throw new InvalidOperationException(
                $"Cooling pad curve speed update failed: {exception.Message}", exception);
        }
    }
}
