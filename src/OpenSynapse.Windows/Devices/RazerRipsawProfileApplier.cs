using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public static class RazerRipsawProfileApplier
{
    public static async Task ApplyAsync(IRazerRipsawService service, RazerRipsawProfileSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings.Validate();
        if (settings.ChannelVolumes.Count == 0 && settings.ChannelMuted.Count == 0) return;
        var previous = await service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
        var restore = new Stack<Func<Task>>();
        try
        {
            foreach (var (key, volume) in settings.ChannelVolumes)
            {
                var channel = Enum.Parse<RazerRipsawChannel>(key, true);
                var old = previous.Volumes[(int)channel];
                if (volume == old) continue;
                restore.Push(() => service.SetVolumeAsync(channel, old, CancellationToken.None));
                await service.SetVolumeAsync(channel, volume, cancellationToken).ConfigureAwait(false);
            }
            foreach (var (key, muted) in settings.ChannelMuted)
            {
                var channel = Enum.Parse<RazerRipsawChannel>(key, true);
                var old = previous.Muted[(int)channel];
                if (muted == old) continue;
                restore.Push(() => service.SetMuteAsync(channel, old, CancellationToken.None));
                await service.SetMuteAsync(channel, muted, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            while (restore.TryPop(out var undo))
            {
                try { await undo().ConfigureAwait(false); }
                catch (Exception rollbackFailure) { errors.Add(rollbackFailure); }
            }
            if (errors.Count > 1) throw new AggregateException("Ripsaw write and restore failed; refresh before retrying.", errors);
            throw;
        }
    }
}
