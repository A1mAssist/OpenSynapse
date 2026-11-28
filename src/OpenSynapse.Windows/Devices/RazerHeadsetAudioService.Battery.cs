using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHeadsetAudioService
{
    // USB endpoints of battery headsets; the wireless sibling still reports the battery.
    private static readonly IReadOnlySet<ushort> WiredEndpointProductIds = new HashSet<ushort>
    {
        0x052D, // Kraken V3 Pro USB
        0x0567, // Kraken V4 Pro USB
        0x056E, // BlackShark V2 HyperSpeed USB
    };

    /// <summary>
    /// True when the identity has a battery read path. Wired audio hardware
    /// (sound cards, mixers, speakers, microphones) must stay out of battery lists.
    /// </summary>
    public static bool SupportsBattery(RazerHeadsetAudioConnection connection) =>
        connection.HasControlInterface &&
        !WiredEndpointProductIds.Contains(connection.ProductId) &&
        SupportsBattery(connection.Transport, connection.ProductId);

    private static bool SupportsBattery(RazerHeadsetAudioTransport transport, ushort productId) =>
        transport switch
        {
            RazerHeadsetAudioTransport.Protocol25Feature65 =>
                RazerCarolAudioProtocol.HasBattery(productId),
            RazerHeadsetAudioTransport.AvneraFeature65 or
                RazerHeadsetAudioTransport.Valkyrie64 or
                RazerHeadsetAudioTransport.Billie64 or
                RazerHeadsetAudioTransport.NxpSerial or
                RazerHeadsetAudioTransport.Gip60 => true,
            RazerHeadsetAudioTransport.Mxic64 or
                RazerHeadsetAudioTransport.ValkyrieT3Feature64 or
                RazerHeadsetAudioTransport.Jolie64 => RazerMxicHeadsetService.SupportsBattery(productId),
            _ => false,
        };

    /// <summary>
    /// Reads only the battery level for the tray poll. The page keeps its full probe
    /// set in <see cref="ReadStateAsync"/>.
    /// </summary>
    public async Task<int?> TryReadBatteryPercentAsync(CancellationToken cancellationToken = default)
    {
        if (!SupportsBattery(_transportKind, _productId)) return null;
        if (RazerCarolAudioProtocol.IsSupported(_productId))
            return _protocol25Transport is null ? null :
                await TryReadCarolBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
        if (_protocol25Transport is not null ||
            _transportKind is RazerHeadsetAudioTransport.KrakenBase37 or
                RazerHeadsetAudioTransport.Mixer053E or
                RazerHeadsetAudioTransport.NxpSerial or
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
        if (!SupportsBattery(connection)) return null;

        try
        {
            switch (connection.Transport)
            {
                case RazerHeadsetAudioTransport.Protocol25Feature65 when
                    RazerCarolAudioProtocol.IsSupported(connection.ProductId):
                {
                    var service = new RazerHeadsetAudioService(new RazerFeatureTransport(),
                        connection.DevicePath, TimeSpan.FromMilliseconds(20), diagnostic,
                        connection.Transport, connection.ProductId);
                    return await service.TryReadCarolBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
                }
                case RazerHeadsetAudioTransport.NxpSerial:
                {
                    await using var service = new RazerNxpHeadsetService(connection.DevicePath, diagnostic);
                    return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
                }
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
                        connection.DevicePath, diagnostic, connection.FeatureReportByteLength, connection.ProductId);
                    return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
                }
                case RazerHeadsetAudioTransport.Valkyrie64:
                case RazerHeadsetAudioTransport.Billie64:
                {
                    await using var service = new RazerValkyrieHeadsetService(
                        connection.ProductId, connection.DevicePath, diagnostic);
                    return await service.TryReadBatteryPercentAsync(cancellationToken).ConfigureAwait(false);
                }
                case RazerHeadsetAudioTransport.Gip60:
                {
                    await using var service = new RazerGipHeadsetService(
                        connection.ProductId, connection.DevicePath,
                        connection.OutputReportByteLength, diagnostic);
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
