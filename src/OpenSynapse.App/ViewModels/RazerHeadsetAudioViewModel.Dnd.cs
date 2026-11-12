namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    /// <summary>DND write for the MXIC dialects, saved once like every other single setting.</summary>
    private async Task WriteMxicDndAsync(CancellationToken cancellationToken)
    {
        var previous = Dnd;
        if (!await RunMxicWriteAsync(
                (service, token) => service.SetAudioDndAsync(Dnd, token), cancellationToken))
        {
            return;
        }

        await SaveSettingAsync(previous, Dnd, value => Dnd = value, (settings, value) => settings.Dnd = value,
            token => RestoreMxicDndAsync(previous, token), cancellationToken);
    }

    private async Task RestoreMxicDndAsync(bool enabled, CancellationToken cancellationToken)
    {
        await using var service = OpenMxicService();
        await service.SetAudioDndAsync(enabled, cancellationToken);
    }
}
