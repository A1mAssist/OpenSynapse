using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    [ProfileSetting("headset.bass", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyBassAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91 && !IsProtocol25 && !IsJolie) return;
        var previous = Bass;
        var applied = IsJolie
            ? await RunMxicWriteAsync(
                (service, token) => service.SetBassAsync((byte)Math.Round(Bass), token),
                cancellationToken)
            : await RunWriteAsync(
                (service, token) => service.SetBassAsync(ToFirmwareBass(Bass), token),
                cancellationToken);
        if (!applied)
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.Bass = (int)Math.Round(Bass),
            settings => settings.Bass = (int)Math.Round(previous),
            () => Bass = previous,
            async token =>
            {
                if (IsJolie)
                {
                    await using var service = OpenMxicService();
                    await service.SetBassAsync((byte)Math.Round(previous), token);
                }
                else
                {
                    await OpenService().SetBassAsync(ToFirmwareBass(previous), token);
                }
            },
            cancellationToken);
    }
}
