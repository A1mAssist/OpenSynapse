using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerChromaStandardService
{
    /// <summary>
    /// Session end only needs the device dark. Skip the read/restore/verify round
    /// trip: nothing will be restored, and a device sitting in an effect the
    /// protocol cannot re-encode must still be able to turn off.
    /// </summary>
    public Task TurnOffForShutdownAsync(CancellationToken cancellationToken = default) =>
        _channel is RazerChromaChannel.Legacy or RazerChromaChannel.LegacyPerLed
            ? SetLegacyEffectAsync(RazerChromaEffect.Off, [], cancellationToken)
            : QueryAsync(
                transaction => RazerChromaStandardProtocol.SetEffect(
                    transaction, RazerChromaEffect.Off, []),
                "shutdown off",
                cancellationToken);
}
