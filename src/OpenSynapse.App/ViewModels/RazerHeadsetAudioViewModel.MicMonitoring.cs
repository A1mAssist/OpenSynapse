using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    [ProfileSetting("headset.mic-monitoring", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (IsMxic0555)
        {
            var previousEnabled = MicMonitoringEnabled;
            if (!await RunMxicWriteAsync(
                    (service, token) => service.SetSidetoneAsync(MicMonitoringEnabled, token),
                    cancellationToken))
            {
                return;
            }

            await SaveHeadsetSettingAsync(
                settings => settings.MicMonitoringEnabled = MicMonitoringEnabled,
                settings => settings.MicMonitoringEnabled = previousEnabled,
                () => MicMonitoringEnabled = previousEnabled,
                async token =>
                {
                    await using var service = OpenMxicService();
                    await service.SetSidetoneAsync(previousEnabled, token);
                },
                cancellationToken);
            return;
        }

        if (IsAvnera65)
        {
            var previousEnabled = MicMonitoringEnabled;
            var previousPercent = _sidetonePercent;
            var target = MicMonitoringEnabled
                ? (byte)Math.Max(1, (int)previousPercent)
                : (byte)0;
            if (!await RunAvneraWriteAsync(
                    (service, token) => service.SetSidetoneAsync(target, token),
                    cancellationToken))
            {
                return;
            }

            _sidetonePercent = target;
            await SaveHeadsetSettingAsync(
                settings => settings.MicMonitoringEnabled = MicMonitoringEnabled,
                settings => settings.MicMonitoringEnabled = previousEnabled,
                () =>
                {
                    MicMonitoringEnabled = previousEnabled;
                    _sidetonePercent = previousPercent;
                },
                async token =>
                {
                    await using var service = OpenAvneraService();
                    await service.SetSidetoneAsync(previousPercent, token);
                },
                cancellationToken);
            return;
        }

        if (!IsFeature91) return;
        var previous = MicMonitoringEnabled;
        if (!await RunWriteAsync(
                (service, token) => service.SetMicReturnSwitchAsync(MicMonitoringEnabled, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.MicMonitoringEnabled = MicMonitoringEnabled,
            settings => settings.MicMonitoringEnabled = previous,
            () => MicMonitoringEnabled = previous,
            token => OpenService().SetMicReturnSwitchAsync(previous, token),
            cancellationToken);
    }
}
