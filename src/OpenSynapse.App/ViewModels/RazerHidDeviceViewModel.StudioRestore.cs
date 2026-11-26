using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHidDeviceViewModel
{
    internal async Task<bool> RestoreConfiguredLightingAfterExternalAsync(CancellationToken cancellationToken)
    {
        if (_lightingProfileResolver is null || !IsReady || RequiresRescan) return false;
        if (!LightingEnabled)
        {
            var capabilities = SelectedZoneCapabilities;
            if (!(capabilities?.CanWriteState == true && !_unsupportedLedStateWrites.Contains(SelectedLightingZone)) &&
                !(Has(RazerHidBackendCapability.LightingEffectWrite) &&
                  capabilities?.LightingEffects.Contains(RazerHidLightingEffect.Off) == true &&
                  !_unsupportedLighting.Contains((SelectedLightingZone, RazerHidLightingEffect.Off))))
                return false;
            await DisableLightingAsync(cancellationToken).ConfigureAwait(true);
            return true;
        }

        // The editor may be showing another power profile; restore the active one.
        var profile = _lightingProfileResolver(
            _powerStateProvider?.Invoke() ?? PowerSourceKind.Unknown, SelectedLightingZone);
        if (profile is null || !Enum.TryParse<RazerHidLightingEffect>(profile.Effect, true, out var effect))
            return false;
        var software = IsSoftwareLightingEffect(effect);
        if (!software && (!Has(RazerHidBackendCapability.LightingEffectWrite) ||
                Connection.LightingZones.GetValueOrDefault(SelectedLightingZone)?.LightingEffects.Contains(effect) != true ||
                _unsupportedLighting.Contains((SelectedLightingZone, effect))))
            return false;

        ApplyLightingProfileToEditor(profile);
        var editorPower = _lightingPowerProfileIndex;
        try
        {
            _lightingPowerProfileIndex = 0;
            await ApplyLightingProfileToHardwareAsync(profile, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _lightingPowerProfileIndex = editorPower;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return (software ? HasRunningSoftwareLighting && _operationSucceeded : !_unsupportedLighting.Contains((SelectedLightingZone, effect))) &&
            (!TryGetByte(profile, "brightness", out _) || !_unsupportedBrightnessWrites.Contains(SelectedLightingZone)) &&
            (!profile.Parameters.ContainsKey("enabled") || !_unsupportedLedStateWrites.Contains(SelectedLightingZone));
    }
}
