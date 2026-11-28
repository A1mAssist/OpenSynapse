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

        try
        {
            var response = await QueryAsync(
                RazerLegacyChromaProtocol.GetRegionIds, "read region ids", cancellationToken).ConfigureAwait(false);
            _legacyRegionId = RazerLegacyChromaProtocol.ReadFirstRegionId(response) ?? (byte)1;
        }
        catch (Exception exception) when (_productId == 0x0F51 && IsExpected(exception))
        {
            // FiverV2 explicitly uses LED 1 if the region-list command is unavailable.
            _legacyRegionId = 1;
        }
        return _legacyRegionId.Value;
    }

    private async Task SetLegacyPerLedBrightnessAsync(byte percent, CancellationToken cancellationToken)
    {
        var region = await ResolveLegacyRegionAsync(cancellationToken).ConfigureAwait(false);
        byte? previous = null;
        try
        {
            previous = RazerLegacyChromaProtocol.ReadPerLedBrightnessPercent(
                await QueryAsync(
                    transaction => RazerLegacyChromaProtocol.GetPerLedBrightness(
                        transaction, RazerLegacyChromaProtocol.ReadProfileId, region),
                    "read brightness",
                    cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke(
                $"per-led brightness readback unavailable ({exception.Message}); writing without restore");
        }

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
            if (previous is not null)
            {
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
        }
        catch (Exception updateException)
        {
            if (previous is not { } restore)
            {
                throw;
            }

            try
            {
                await QueryAsync(
                    transaction => RazerLegacyChromaProtocol.SetPerLedBrightness(
                        transaction, RazerLegacyChromaProtocol.ReadProfileId, region, restore),
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
