using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private bool _hasMicEqBands;

    public ObservableCollection<RazerHeadsetEqBandViewModel> MicEqBands { get; } = new();

    public Visibility MicEqBandsVisibility =>
        _hasMicEqBands ? Visibility.Visible : Visibility.Collapsed;

    [ProfileSetting("headset.mic.eq-bands", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicEqBandsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsValkyrie || MicEqBands.Count != RazerValkyrieHeadsetProtocol.TenBandCount)
        {
            return;
        }

        var previous = MicEqBands.Select(band => (int)Math.Round(band.Level)).ToArray();
        var bands = MicEqBands.Select(band => ToFirmwareBand(band.Level)).ToArray();
        if (!await RunValkyrieWriteAsync(
                (service, token) => service.SetMicBandsAsync(bands, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.MicEqBands = ToBandMap(MicEqBands.Select(band => band.Level)),
            settings => settings.MicEqBands = ToBandMap(previous.Select(value => (double)value)),
            () =>
            {
                for (var index = 0; index < previous.Length && index < MicEqBands.Count; index++)
                {
                    MicEqBands[index].Level = previous[index];
                }
            },
            async token =>
            {
                await using var service = OpenValkyrieService();
                await service.SetMicBandsAsync(
                    previous.Select(value => ToFirmwareBand(value)).ToArray(),
                    token);
            },
            cancellationToken);
    }

    private void ApplyMicEqBands(RazerHeadsetAudioState state)
    {
        if (state.MicBands is not { Count: RazerValkyrieHeadsetProtocol.TenBandCount } bands)
        {
            return;
        }

        MicEqBands.Clear();
        for (var index = 0; index < bands.Count; index++)
        {
            MicEqBands.Add(new RazerHeadsetEqBandViewModel(
                RazerHeadsetAudioProtocol.TenBandFrequencies[index],
                -12d,
                12d,
                1d,
                decibels: true)
            {
                Level = FromFirmwareBand(bands[index]),
            });
        }

        _hasMicEqBands = true;
        OnPropertyChanged(nameof(MicEqBandsVisibility));
    }
}
