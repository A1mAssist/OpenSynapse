using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private async Task SaveNxpHapticsAsync(CancellationToken cancellationToken)
    {
        var enabled = HapticsEnabled;
        var strength = (ushort)Math.Round(HapticsStrength);
        RazerHapticsControlState previous = default;
        if (!await RunNxpWriteAsync(async (service, token) =>
        {
            previous = await service.UpdateHapticsAsync(enabled, strength, token);
        }, cancellationToken)) return;

        await SaveHeadsetSettingAsync(
            settings => { settings.HapticsEnabled = enabled; settings.HapticsStrength = strength; },
            settings => { settings.HapticsEnabled = previous.Enabled; settings.HapticsStrength = previous.Strength; },
            () => { HapticsEnabled = previous.Enabled; HapticsStrength = previous.Strength; },
            async token =>
            {
                await using var service = OpenNxpService();
                await service.SetHapticsAsync(previous.Enabled, previous.Strength, token);
            }, cancellationToken);
    }

    private async Task SaveNxpMicMonitoringAsync(CancellationToken cancellationToken)
    {
        var target = MicMonitoringEnabled;
        (bool Enabled, byte Percent) previous = default;
        if (!await RunNxpWriteAsync(async (service, token) =>
        {
            previous = await service.UpdateSidetoneAsync(target, null, token);
        }, cancellationToken)) return;

        _sidetonePercent = previous.Percent;
        await SaveHeadsetSettingAsync(
            settings => settings.MicMonitoringEnabled = target,
            settings => settings.MicMonitoringEnabled = previous.Enabled,
            () => MicMonitoringEnabled = previous.Enabled,
            async token =>
            {
                await using var service = OpenNxpService();
                await service.SetSidetoneAsync(previous.Enabled, previous.Percent, token);
            }, cancellationToken);
    }

    private async Task SaveNxpAutoPauseAsync(CancellationToken cancellationToken)
    {
        var target = AutoPause;
        (bool Enabled, ushort Seconds) previous = default;
        if (!await RunNxpWriteAsync(async (service, token) =>
        {
            previous = await service.UpdateAutoPauseAsync(target, null, token);
        }, cancellationToken)) return;

        _silenceDetectionPeriod = previous.Seconds;
        await SaveSettingAsync(previous.Enabled, target,
            value => AutoPause = value, (settings, value) => settings.AutoPause = value,
            async token =>
            {
                await using var service = OpenNxpService();
                await service.SetAutoPauseAsync(previous.Enabled, previous.Seconds, token);
            }, cancellationToken);
    }
}
