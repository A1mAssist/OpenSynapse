using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerChromaStandardService
{
    private byte? _legacyRegionId;

    private async Task<byte> ReadLegacyPerLedBrightnessAsync(CancellationToken cancellationToken)
    {
        var region = await ResolveLegacyRegionAsync(cancellationToken).ConfigureAwait(false);
        return RazerLegacyChromaProtocol.ReadPerLedBrightnessPercent(
            await QueryAsync(
                transaction => RazerLegacyChromaProtocol.GetPerLedBrightness(
                    transaction, RazerLegacyChromaProtocol.ReadProfileId, region),
                "read per-led brightness",
                cancellationToken).ConfigureAwait(false));
    }

    private async Task<byte> ResolveLegacyRegionAsync(CancellationToken cancellationToken)
    {
        if (_legacyRegionId is { } cached)
        {
            return cached;
        }

        var response = await QueryAsync(
            RazerLegacyChromaProtocol.GetRegionIds, "read region ids", cancellationToken).ConfigureAwait(false);
        // The bundle falls back to region 1 when the list is empty.
        _legacyRegionId = RazerLegacyChromaProtocol.ReadFirstRegionId(response) ?? (byte)1;
        return _legacyRegionId.Value;
    }

    private async Task SetLegacyPerLedBrightnessAsync(byte percent, CancellationToken cancellationToken)
    {
        var region = await ResolveLegacyRegionAsync(cancellationToken).ConfigureAwait(false);
        var previous = RazerLegacyChromaProtocol.ReadPerLedBrightnessPercent(
            await QueryAsync(
                transaction => RazerLegacyChromaProtocol.GetPerLedBrightness(
                    transaction, RazerLegacyChromaProtocol.ReadProfileId, region),
                "read brightness",
                cancellationToken).ConfigureAwait(false));
        if (previous == percent)
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerLegacyChromaProtocol.SetPerLedBrightness(
                    transaction, RazerLegacyChromaProtocol.ReadProfileId, region, percent),
                "set brightness",
                cancellationToken).ConfigureAwait(false);
            var actual = RazerLegacyChromaProtocol.ReadPerLedBrightnessPercent(
                await QueryAsync(
                    transaction => RazerLegacyChromaProtocol.GetPerLedBrightness(
                        transaction, RazerLegacyChromaProtocol.ReadProfileId, region),
                    "read back brightness",
                    cancellationToken).ConfigureAwait(false));
            if (Math.Abs(actual - percent) > 1)
            {
                throw new InvalidOperationException($"Brightness readback was {actual}%, expected {percent}%.");
            }
        }
        catch (Exception updateException)
        {
            try
            {
                await QueryAsync(
                    transaction => RazerLegacyChromaProtocol.SetPerLedBrightness(
                        transaction, RazerLegacyChromaProtocol.ReadProfileId, region, previous),
                    "restore brightness",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception restoreException)
            {
                throw new InvalidOperationException(
                    "Brightness update failed and the previous brightness could not be restored.",
                    new AggregateException(updateException, restoreException));
            }

            throw;
        }
    }
}
