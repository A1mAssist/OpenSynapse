using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    public Task<bool> ApplyCamyT1Async(CancellationToken cancellationToken) =>
        _connection.Transport != RazerHeadsetAudioTransport.CamyT1
            ? Task.FromResult(false)
            : RunWriteAsync(async (service, token) =>
            {
                if (_hasMicHpf)
                {
                    await service.SetMicHpfAsync(MicHpf, token).ConfigureAwait(false);
                }

                if (_hasMicLimiterEnabled)
                {
                    await service.SetMicLimiterEnabledAsync(MicLimiterEnabled, token)
                        .ConfigureAwait(false);
                }
            }, cancellationToken);
}
