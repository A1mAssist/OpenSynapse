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
        // rzDevice25 resolves AllRegion to the first advertised region for reads.
        var region = _productId is { } productId && IsWirelessArgbProduct(productId)
            ? await ResolveLegacyRegionAsync(cancellationToken).ConfigureAwait(false)
            : RazerLegacyChromaProtocol.ReadRegionId;
        return await ReadLegacyBrightnessAsync(region, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte> ReadLegacyBrightnessAsync(byte region, CancellationToken cancellationToken)
    {
        return RazerLegacyChromaProtocol.ReadDirectBrightnessPercent(
            await QueryAsync(
                transaction => RazerLegacyChromaProtocol.GetDirectBrightness(
                    transaction, LegacyBrightnessProfileId, region),
                "read brightness",
                cancellationToken).ConfigureAwait(false));
    }

    private async Task SetLegacyBrightnessAsync(byte percent, CancellationToken cancellationToken)
    {
        // A global ARGB write changes all three independently configured ports.
        byte[] regions = _productId is { } productId && IsWirelessArgbProduct(productId)
            ? [26, 27, 28]
            : [RazerLegacyChromaProtocol.ReadRegionId];
        var previous = new Dictionary<byte, byte>();
        try
        {
            foreach (var region in regions)
            {
                previous[region] = await ReadLegacyBrightnessAsync(region, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            previous.Clear();
            _diagnostic?.Invoke(
                $"direct brightness readback unavailable ({exception.Message}); writing without restore");
        }

        if (previous.Count == regions.Length && previous.Values.All(value => value == percent))
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
            foreach (var region in previous.Keys)
            {
                var actual = await ReadLegacyBrightnessAsync(region, cancellationToken).ConfigureAwait(false);
                if (Math.Abs(actual - percent) > 1)
                {
                    throw new InvalidOperationException(
                        $"Brightness readback was {actual}%, expected {percent}%.");
                }
            }
        }
        catch (Exception updateException)
        {
            if (previous.Count == 0)
            {
                throw;
            }

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                foreach (var (region, restore) in previous)
                {
                    await QueryAsync(
                        transaction => RazerLegacyChromaProtocol.SetDirectBrightness(
                            transaction, restore, LegacyBrightnessProfileId, region),
                        "restore brightness",
                        timeout.Token).ConfigureAwait(false);
                }
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
