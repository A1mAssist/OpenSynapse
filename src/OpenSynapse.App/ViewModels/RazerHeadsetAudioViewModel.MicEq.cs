using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private bool _hasMicEqBands;

    public ObservableCollection<RazerHeadsetEqBandViewModel> MicEqBands { get; } = new();

    public Visibility MicEqBandsVisibility =>
        _hasMicEqBands ? Visibility.Visible : Visibility.Collapsed;

    public Task ApplyMicEqBandsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsValkyrie || MicEqBands.Count != RazerValkyrieHeadsetProtocol.TenBandCount)
        {
            return Task.CompletedTask;
        }

        var bands = MicEqBands.Select(band => ToFirmwareBand(band.Level)).ToArray();
        return RunValkyrieWriteAsync(
            (service, token) => service.SetMicBandsAsync(bands, token),
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
