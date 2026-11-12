using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    /// <summary>
    /// Reads only the battery level for the tray poll. The page keeps its full probe
    /// set in <see cref="ReadStateAsync"/>.
    /// </summary>
    public async Task<int?> TryReadBatteryPercentAsync(CancellationToken cancellationToken = default)
    {
        if (_protocol25Transport is not null ||
            _transportKind is RazerHeadsetAudioTransport.KrakenBase37 or
                RazerHeadsetAudioTransport.Mixer053E or
                RazerHeadsetAudioTransport.NativeDll)
        {
            return null;
        }

        var errors = new List<string>();
        var battery = await ProbeAsync(
            "battery",
            RazerHeadsetAudioProtocol.GetBatteryLevel,
            RazerHeadsetAudioProtocol.ReadBatteryPercent,
            errors,
            cancellationToken).ConfigureAwait(false);
        return battery.Ok ? battery.Value : (int?)null;
    }

    public static async Task<int?> TryReadBatteryPercentAsync(
        RazerHeadsetAudioConnection connection,
        Action<string>? diagnostic,
        CancellationToken cancellationToken = default)
    {
        try
        {
            switch (connection.Transport)
            {
                case RazerHeadsetAudioTransport.Feature91:
                {
                    var service = new RazerHeadsetAudioService(
                        new RazerFeatureTransport(),
                        connection.DevicePath,
                        TimeSpan.FromMilliseconds(20),
                        diagnostic,
                        connection.Transport,
                        connection.ProductId);
                    return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
                }
                case RazerHeadsetAudioTransport.Mxic64:
                case RazerHeadsetAudioTransport.ValkyrieT3Feature64:
                case RazerHeadsetAudioTransport.Jolie64:
                {
                    await using var service = new RazerMxicHeadsetService(
                        connection.ProductId, connection.DevicePath, diagnostic);
                    return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
                }
                case RazerHeadsetAudioTransport.AvneraFeature65:
                {
                    await using var service = new RazerAvneraHeadsetService(
                        connection.DevicePath, diagnostic);
                    return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
                }
                case RazerHeadsetAudioTransport.Valkyrie64:
                case RazerHeadsetAudioTransport.Billie64:
                {
                    await using var service = new RazerValkyrieHeadsetService(
                        connection.ProductId, connection.DevicePath, diagnostic);
                    return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
                }
                default:
                    return null;
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            diagnostic?.Invoke($"headset battery read failed: {exception.Message}");
            return null;
        }
    }
}
