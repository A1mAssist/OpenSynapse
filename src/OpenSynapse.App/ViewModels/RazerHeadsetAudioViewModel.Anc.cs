using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    [ProfileSetting("headset.anc", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyAncAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91 && !IsKrakenBase && !IsMxic0555) return;
        var previousIndex = SelectedAncIndex;
        var previousLevel = AncLevel;
        var state = DeviceAncStates[SelectedAncIndex];
        var level = (byte)Math.Round(AncLevel);
        var written = IsMxic0555
            ? await RunMxicWriteAsync(
                (service, token) => service.SetAncAsync(state, level, token), cancellationToken)
            : await RunWriteAsync(
                (service, token) => service.SetAncAsync(state, level, token), cancellationToken);
        if (!written)
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.AncModeIndex = SelectedAncIndex;
                settings.AncEnabled = DeviceAncStates[SelectedAncIndex] != RazerAncState.Off;
                settings.AncLevel = (int)Math.Round(AncLevel);
            },
            settings =>
            {
                settings.AncModeIndex = previousIndex;
                settings.AncEnabled = DeviceAncStates[previousIndex] != RazerAncState.Off;
                settings.AncLevel = (int)Math.Round(previousLevel);
            },
            () =>
            {
                SelectedAncIndex = previousIndex;
                AncLevel = previousLevel;
            },
            async token =>
            {
                var previousState = DeviceAncStates[previousIndex];
                var previousValue = (byte)Math.Round(previousLevel);
                if (IsMxic0555)
                {
                    await using var service = OpenMxicService();
                    await service.SetAncAsync(previousState, previousValue, token);
                }
                else
                {
                    await OpenService().SetAncAsync(previousState, previousValue, token);
                }
            },
            cancellationToken);
    }
}
