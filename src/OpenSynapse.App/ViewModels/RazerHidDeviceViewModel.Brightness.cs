using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    /// <summary>
    /// A zone without a hardware brightness transaction can still dim the
    /// effects whose colours the software chooses.
    /// </summary>
    public bool IsVirtualBrightness => SupportsVirtualBrightness;

    private bool SupportsVirtualBrightness =>
        SelectedZoneCapabilities is { CanWriteBrightness: false } &&
        UsesColor(SelectedLightingEffect);

    private byte EffectBrightness => SupportsVirtualBrightness ? Brightness : byte.MaxValue;

    private static bool UsesColor(RazerHidLightingEffect effect) =>
        IsSoftwareLightingEffect(effect) || effect is
            RazerHidLightingEffect.Static or
            RazerHidLightingEffect.Blinking or
            RazerHidLightingEffect.Reactive or
            RazerHidLightingEffect.BreathingSingle or
            RazerHidLightingEffect.BreathingDual or
            RazerHidLightingEffect.StarlightSingle or
            RazerHidLightingEffect.StarlightDual;

    internal void NotifyBrightnessPolicyChanged()
    {
        OnPropertyChanged(nameof(BrightnessVisibility));
        OnPropertyChanged(nameof(BrightnessWriteVisibility));
        OnPropertyChanged(nameof(BrightnessEditorVisibility));
        OnPropertyChanged(nameof(CanWriteBrightness));
        OnPropertyChanged(nameof(CanEditBrightness));
        OnPropertyChanged(nameof(BrightnessText));
    }

    public Task ApplyBrightnessAsync(CancellationToken cancellationToken = default)
    {
        var zone = SelectedLightingZone;
        var capabilities = SelectedZoneCapabilities;
        if (capabilities?.CanWriteBrightness != true || _unsupportedBrightnessWrites.Contains(zone))
        {
            // No hardware slider: re-send the effect, whose colours EffectBrightness scales.
            return SupportsVirtualBrightness
                ? RunZoneWriteAsync(() => SupportsVirtualBrightness,
                    () => IsBrightnessBusy, value => IsBrightnessBusy = value,
                    async () =>
                    {
                        await ApplyLightingAsync(cancellationToken);
                        _brightnessKnown = true;
                        OnPropertyChanged(nameof(BrightnessText));
                    }, cancellationToken)
                : Task.CompletedTask;
        }
        return RunZoneWriteAsync(() => capabilities.CanWriteBrightness &&
                !_unsupportedBrightnessWrites.Contains(zone),
            () => IsBrightnessBusy, value => IsBrightnessBusy = value,
            async () =>
            {
                if (IsSelectedLightingPowerActive)
                {
                    await _service.SetBrightnessAsync(Connection, Brightness,
                        Connection.Definition.DefaultStorage, zone, cancellationToken);
                    if (capabilities.CanReadBrightness)
                    {
                        try
                        {
                            Brightness = await _service.GetBrightnessAsync(Connection,
                                Connection.Definition.DefaultStorage, zone, cancellationToken);
                        }
                        catch (NotSupportedException)
                        {
                            _unsupportedBrightnessReads.Add(zone);
                            OnLightingSelectionChanged();
                        }
                    }
                }
                await SaveLightingProfileAsync(cancellationToken);
                OnPropertyChanged(nameof(BrightnessText));
            }, cancellationToken,
            () =>
            {
                _unsupportedBrightnessWrites.Add(zone);
                OnLightingSelectionChanged();
            });
    }
}
