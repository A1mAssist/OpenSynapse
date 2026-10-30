using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerAwBleHeadsetViewModel
{
    public Task ApplyCustomEqAsync(CancellationToken cancellationToken = default)
    {
        var levels = CustomEqBands
            .Select(band => (sbyte)Math.Round(band.Level))
            .ToArray();
        var enabled = CustomEqEnabled;
        return RunWriteAsync(
            service => service.SetCustomEqAsync(levels, enabled, cancellationToken),
            applied =>
            {
                ApplyCustomEqBands(applied);
                SelectedEqPresetIndex = IndexOfPreset(
                    enabled ? RazerAwBleProtocol.CustomEqPreset : RazerAwBleProtocol.DefaultEqPreset);
            });
    }

    public Task ApplyMicCustomEqAsync(CancellationToken cancellationToken = default)
    {
        var levels = MicCustomEqBands
            .Select(band => (sbyte)Math.Round(band.Level))
            .ToArray();
        var enabled = MicCustomEqEnabled;
        return RunWriteAsync(
            service => service.SetMicCustomEqAsync(levels, enabled, cancellationToken),
            applied =>
            {
                ApplyMicCustomEqBands(applied);
                SelectedMicEqPresetIndex = IndexOfMicPreset(
                    enabled ? RazerAwBleProtocol.CustomEqPreset : RazerAwBleProtocol.DefaultEqPreset);
            });
    }
}
