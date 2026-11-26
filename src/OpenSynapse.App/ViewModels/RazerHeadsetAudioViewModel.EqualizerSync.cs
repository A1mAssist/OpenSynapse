using OpenSynapse.Core.Audio;
using OpenSynapse.Windows.Devices;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private readonly EqualizerReadback _eqReadback = new();
    private readonly EqualizerReadback _thxReadback = new();

    private string EqDraftPreset => SelectedEqPresetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private string ThxDraftPreset => SelectedThxPresetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void AcceptEqReadback()
    {
        _eqReadback.Accept(EqDraftPreset, EqBands.Select(band => band.Level));
        ConfirmAudioSetting(HeadsetAudioSetting.EqPreset, SelectedEqPresetIndex);
        ConfirmAudioSetting(HeadsetAudioSetting.EqBands, EqBands.Select(band => band.Level).ToArray());
    }

    internal async Task PollEqualizerAsync(Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        if (!isCurrent() || IsBusy) return;
        if (CanEdit && (_hasEqPreset || _hasEqBands) &&
            _eqReadback.Begin(EqDraftPreset, EqBands.Select(band => band.Level)) is { } revision)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var state = await ReadEqualizerAsync(timeout.Token);
                if (isCurrent() && !IsBusy && _eqReadback.CanApply(
                    revision, EqDraftPreset, EqBands.Select(band => band.Level)))
                {
                    ApplyEqualizerReadback(state);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (IsExpectedException(exception) ||
                exception is OperationCanceledException or TimeoutException or NotSupportedException)
            {
                _diagnostic?.Invoke($"headset EQ poll failed: {exception.Message}");
            }
        }
        if (isCurrent()) await PollThxAsync(isCurrent, cancellationToken);
    }

    private async Task<RazerHeadsetEqualizerState> ReadEqualizerAsync(CancellationToken token)
    {
        var bandCount = _hasEqBands ? EqBands.Count : 0;
        if (IsMxic)
        {
            await using var service = OpenMxicService();
            return await service.ReadEqualizerAsync(bandCount, token);
        }
        if (IsValkyrie)
        {
            await using var service = OpenValkyrieService();
            return await service.ReadEqualizerAsync(bandCount, token);
        }
        return await OpenService().ReadEqualizerAsync(bandCount, token);
    }

    private void ApplyEqualizerReadback(RazerHeadsetEqualizerState state)
    {
        if (state.Preset is { } preset)
        {
            var index = Array.FindIndex(ActivePresets, item => item.Value == preset);
            if (index < 0) return;
            SelectedEqPresetIndex = index;
            if (IsCarol) _carolPreset = preset;
        }
        if (state.Bands is { Count: 8 or 10 } bands &&
            !EqBands.Select(band => ToFirmwareBand(band.Level)).SequenceEqual(bands))
        {
            ApplyEqBands(bands);
        }
        AcceptEqReadback();
    }
}
