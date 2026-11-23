using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class HeadsetAudioProfileApplier
{
    internal static async Task ApplyCarolAsync(RazerHeadsetAudioService service, HeadsetProfileSettings profile,
        List<ProfileSettingApplyResult> results, CancellationToken cancellationToken)
    {
        if (profile.EqPreset is not null || profile.EqBandsTenths is not null || profile.EqBands is not null)
        {
            await ApplyAsync("headset.eq", async token =>
            {
                var preset = TryParseByte(profile.EqPreset);
                if (profile.EqPreset is not null && (preset is null || !RazerCarolAudioProtocol.IsPreset(preset.Value)))
                    throw new ArgumentException("Unknown Carol EQ preset.");
                var map = profile.EqBandsTenths ?? profile.EqBands;
                if (map is not null && (preset is null or 16))
                {
                    if (map.Count != 10) throw new ArgumentException("Carol requires ten EQ bands.");
                    var bands = new byte[10];
                    for (var index = 0; index < bands.Length; index++)
                    {
                        if (!map.TryGetValue(index.ToString(System.Globalization.CultureInfo.InvariantCulture), out var value))
                            throw new ArgumentException("The Carol EQ band map is incomplete.");
                        bands[index] = RazerCarolAudioProtocol.EncodeBand(profile.EqBandsTenths is null ? value : value / 10d);
                    }
                    await service.SetCustomEqAsync(16, bands, token);
                }
                else if (preset is { } selected) await service.SetEqPresetAsync(selected, token);
            }, results, cancellationToken);
        }
        if (profile.AutoSleepEnabled is not null || profile.AutoShutdownMinutes is not null)
            await ApplyAsync("headset.auto-sleep", token => service.SetCarolPowerAsync(
                profile.AutoSleepEnabled, profile.AutoShutdownMinutes, token), results, cancellationToken);
    }
}
