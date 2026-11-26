using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    internal async Task RestoreLightingAfterExternalAsync(
        PowerSourceKind powerSource, CancellationToken cancellationToken)
    {
        if (!IsJolie)
            throw new InvalidOperationException("This headset does not expose the Studio strip lighting path.");
        var document = _getActiveProfile?.Invoke() ??
            throw new InvalidOperationException("The headset lighting profile is unavailable.");
        var profile = ProfileResolver.ResolveHeadset(document, powerSource, HeadsetProfileKey);
        if (!Enum.TryParse<RazerChromaEffect>(profile.ChromaEffect, true, out var effect) ||
            !LightingEffects.Contains(effect) || effect == RazerChromaEffect.CustomFrame)
            throw new InvalidOperationException("The headset has no restorable saved lighting effect.");

        var primary = TryGetChromaColor(profile.ChromaParameters, "primary", PrimaryColor);
        var secondary = TryGetChromaColor(profile.ChromaParameters, "secondary", SecondaryColor);
        var direction = profile.ChromaParameters is { } parameters &&
            parameters.TryGetValue("direction", out var rawDirection) && int.TryParse(rawDirection, out var index)
                ? index : SelectedWaveDirectionIndex;
        if (profile.ChromaBrightness is int brightness)
            await WriteChromaBrightnessHardwareAsync(Math.Clamp(brightness, 0, 100), cancellationToken);
        await WriteChromaEffectHardwareAsync(effect, primary, secondary, direction, cancellationToken);
    }
}
