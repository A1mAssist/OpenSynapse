using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private void ApplyEqBands(IReadOnlyList<byte> bands)
    {
        var frequencies = bands.Count == 10
            ? RazerHeadsetAudioProtocol.TenBandFrequencies
            : RazerHeadsetAudioProtocol.EightBandFrequencies;
        var (minimum, maximum, step) = _connection.Transport switch
        {
            RazerHeadsetAudioTransport.ValkyrieT3Feature64 or RazerHeadsetAudioTransport.Jolie64 =>
                (-6d, 6d, 1d),
            RazerHeadsetAudioTransport.Mxic64 =>
                (RazerHeadsetEqScale.MinimumDb, RazerHeadsetEqScale.MaximumDb, RazerHeadsetEqScale.StepDb),
            RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
                (-12d, 12d, 1d),
            RazerHeadsetAudioTransport.Protocol25Feature65 => (-12d, 12d, 1d),
            _ => (0d, 100d, 1d),
        };
        EqBands.Clear();
        for (var index = 0; index < bands.Count; index++)
        {
            EqBands.Add(new RazerHeadsetEqBandViewModel(
                frequencies[index],
                minimum,
                maximum,
                step,
                decibels: _connection.Transport != RazerHeadsetAudioTransport.Feature91)
            {
                Level = FromFirmwareBand(bands[index]),
            });
        }

        _hasEqBands = true;
        OnPropertyChanged(nameof(EqBandsVisibility));
    }

    private async Task RefreshEqBandsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = _connection.Transport switch
            {
                RazerHeadsetAudioTransport.Mxic64 or
                    RazerHeadsetAudioTransport.ValkyrieT3Feature64 or
                    RazerHeadsetAudioTransport.Jolie64 => await ReadMxicStateAsync(cancellationToken),
                RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
                    await ReadValkyrieStateAsync(cancellationToken),
                _ => await OpenService().ReadStateAsync(cancellationToken),
            };
            if (state.EqBands is { Count: 8 or 10 } bands)
            {
                ApplyEqBands(bands);
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            _diagnostic?.Invoke($"refresh EQ bands: {exception.Message}");
        }
    }
}
