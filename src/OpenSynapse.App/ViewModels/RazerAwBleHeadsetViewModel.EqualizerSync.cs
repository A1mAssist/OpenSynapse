using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerAwBleHeadsetViewModel
{
    private readonly EqualizerReadback _eqReadback = new();
    private string EqDraftPreset => $"{SelectedEqPresetIndex}:{CustomEqEnabled}";

    private void AcceptEqReadback() => _eqReadback.Accept(EqDraftPreset, CustomEqBands.Select(band => band.Level));

    internal async Task PollEqualizerAsync(Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        if (!isCurrent() || IsBusy || _service is not { } service || EqPresetVisibility != Visibility.Visible ||
            _eqReadback.Begin(EqDraftPreset, CustomEqBands.Select(band => band.Level)) is not { } revision) return;
        try
        {
            var preset = await service.ReadEqPresetAsync(cancellationToken);
            var bands = CustomEqVisibility == Visibility.Visible
                ? await service.ReadCustomEqBandsAsync(cancellationToken) : null;
            var index = IndexOfPreset(preset);
            if (index < 0 || !isCurrent() || IsBusy || !ReferenceEquals(service, _service) ||
                !_eqReadback.CanApply(revision, EqDraftPreset, CustomEqBands.Select(band => band.Level))) return;
            SelectedEqPresetIndex = index;
            CustomEqEnabled = RazerAwBleProtocol.IsCustomEqPreset(preset);
            if (bands is not null) ApplyCustomEqBands(bands);
            AcceptEqReadback();
        }
        catch (Exception exception) when (IsExpected(exception) || exception is TimeoutException)
        {
            // A transient poll failure keeps the last good state and any draft.
        }
    }
}
