using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerChromaStandardService
{
    private async Task<byte> ReadLegacyBrightnessAsync(CancellationToken cancellationToken)
    {
        return RazerLegacyChromaProtocol.ReadDirectBrightnessPercent(
            await QueryAsync(
                transaction => RazerLegacyChromaProtocol.GetDirectBrightness(transaction),
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
                transaction => RazerLegacyChromaProtocol.SetDirectBrightness(transaction, percent),
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
                    transaction => RazerLegacyChromaProtocol.SetDirectBrightness(transaction, restore),
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
