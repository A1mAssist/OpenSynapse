using OpenSynapse.Core.Profiles;

namespace OpenSynapse.Windows.Devices;

public sealed partial class HeadsetAudioProfileApplier
{
    internal static async Task ApplyNxpAsync(
        RazerNxpHeadsetService service,
        HeadsetProfileSettings profile,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.MicMonitoringEnabled is not null || profile.SideTone is not null)
        {
            await ApplyAsync("headset.mic-monitoring", async token =>
            {
                await service.UpdateSidetoneAsync(profile.MicMonitoringEnabled, profile.SideTone, token);
            }, results, cancellationToken);
        }

        if (profile.AutoPause is { } enabled)
        {
            await ApplyAsync("headset.auto-pause", async token =>
            {
                await service.UpdateAutoPauseAsync(enabled, null, token);
            }, results, cancellationToken);
        }

        if (profile.HapticsEnabled is not null || profile.HapticsStrength is not null)
        {
            await ApplyAsync("headset.haptics", async token =>
            {
                await service.UpdateHapticsAsync(profile.HapticsEnabled, profile.HapticsStrength, token);
            }, results, cancellationToken);
        }
    }
}
