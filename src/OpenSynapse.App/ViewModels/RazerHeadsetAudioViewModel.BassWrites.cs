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
        var desired = Bass;
        var applied = IsJolie
            ? await RunMxicWriteAsync(
                (service, token) => service.SetBassAsync((byte)Math.Round(Bass), token),
                cancellationToken)
            : await RunWriteAsync(
                async (service, token) =>
                {
                    if (IsNommoV2Audio)
                    {
                        previous = FromFirmwareBass((await service.ReadStateAsync(token)).Bass
                            ?? throw new IOException("Nommo bass could not be read."));
                    }
                    await service.SetBassAsync(ToFirmwareBass(desired), token);
                },
                cancellationToken);
        if (!applied)
        {
            if (IsNommoV2Audio) Bass = previous;
            return;
        }

        if (IsNommoV2Audio) Bass = FromFirmwareBass(ToFirmwareBass(desired));
        var persistedBass = IsNommoV2Audio ? ResolveHeadsetProfileSettings()?.Bass : null;
        await SaveHeadsetSettingAsync(
            settings => settings.Bass = (int)Math.Round(Bass),
            settings => settings.Bass = IsNommoV2Audio ? persistedBass : (int)Math.Round(previous),
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
