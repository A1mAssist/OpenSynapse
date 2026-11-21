using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerChromaStandardService
{
    // rzDeviceIoT writes IoT brightness with profileId 0 and the AllRegion id (0);
    // its legacy effect page still reads and writes effects with profile 1, region 0.
    private byte LegacyBrightnessProfileId =>
        _productId is { } productId && IsIotProduct(productId)
            ? (byte)0
            : RazerLegacyChromaProtocol.ReadProfileId;

    private async Task<byte> ReadLegacyBrightnessAsync(CancellationToken cancellationToken)
    {
        return RazerLegacyChromaProtocol.ReadDirectBrightnessPercent(
            await QueryAsync(
                transaction => RazerLegacyChromaProtocol.GetDirectBrightness(
                    transaction, LegacyBrightnessProfileId, RazerLegacyChromaProtocol.ReadRegionId),
                "read brightness",
                cancellationToken).ConfigureAwait(false));
    }

    private async Task SetLegacyBrightnessAsync(byte percent, CancellationToken cancellationToken)
    {
        byte? previous = null;
        try
        {
            previous = await ReadLegacyBrightnessAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _diagnostic?.Invoke(
                $"direct brightness readback unavailable ({exception.Message}); writing without restore");
        }

        if (previous == percent)
        {
            return;
        }

        try
        {
            await QueryAsync(
                transaction => RazerLegacyChromaProtocol.SetDirectBrightness(
                    transaction, percent, LegacyBrightnessProfileId, RazerLegacyChromaProtocol.ReadRegionId),
                "set brightness",
                cancellationToken).ConfigureAwait(false);
            if (previous is not null)
            {
                var actual = await ReadLegacyBrightnessAsync(cancellationToken).ConfigureAwait(false);
                if (Math.Abs(actual - percent) > 1)
                {
                    throw new InvalidOperationException(
                        $"Brightness readback was {actual}%, expected {percent}%.");
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
                    transaction => RazerLegacyChromaProtocol.SetDirectBrightness(
                        transaction, restore, LegacyBrightnessProfileId, RazerLegacyChromaProtocol.ReadRegionId),
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
