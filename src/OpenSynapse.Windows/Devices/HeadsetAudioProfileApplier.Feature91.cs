using System.Globalization;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class HeadsetAudioProfileApplier
{
    private async Task ApplyFeature91ProductAsync(
        RazerHeadsetAudioService service, RazerHeadsetAudioState? state,
        HeadsetProfileSettings profile, List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        var productId = service.FeatureAudioProductId;
        var preset = TryParseByte(profile.EqPreset);
        // The JS THX custom route selects Nommo's Treble preset; no HID band write follows.
        if (RazerAudioFeatureProtocol.IsNommoV2(productId) && preset == 0x10) preset = 4;
        if (profile.EqPreset is not null &&
            (preset is null || !RazerAudioFeatureProtocol.IsSupportedPreset(productId, preset.Value)))
        {
            results.Add(Failed("headset.eq.preset", "Unknown EQ preset value for this product."));
        }
        else if (RazerAudioFeatureProtocol.SupportsHidEqBands(productId) &&
            preset is null or 0x10 && (profile.EqBandsTenths ?? profile.EqBands) is { } levels)
        {
            var tenths = profile.EqBandsTenths is not null;
            if (levels.Count != 10)
            {
                results.Add(Failed("headset.eq.bands", "Marci requires exactly ten EQ bands."));
                return;
            }
            var bands = new byte[10];
            for (var index = 0; index < bands.Length; index++)
            {
                if (!levels.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out var value) ||
                    (tenths ? value is < -128 or > 127 : value is < -12 or > 12))
                {
                    results.Add(Failed("headset.eq.bands", "Marci requires ten EQ bands in signed tenths of a decibel (-128 to 127)."));
                    return;
                }
                bands[index] = RazerAudioFeatureProtocol.EncodeMarciEqBand(tenths ? value / 10d : value);
            }
            if (state?.EqPreset == 0x10 && state.EqBands is { } current && current.SequenceEqual(bands))
                results.Add(Skipped("headset.eq.bands"));
            else
                await ApplyAsync("headset.eq.bands", token => service.SetCustomEqAsync(0x10, bands, token), results, cancellationToken);
        }
        else if (preset is { } value)
        {
            if (state?.EqPreset == value) results.Add(Skipped("headset.eq.preset"));
            else await ApplyAsync("headset.eq.preset", token => service.SetEqPresetAsync(value, token), results, cancellationToken);
        }

        if (RazerAudioFeatureProtocol.IsNommoV2(productId) && profile.Bass is { } bass)
        {
            var level = RazerAudioFeatureProtocol.EncodeNommoBass(bass);
            if (state?.Bass == level) results.Add(Skipped("headset.bass"));
            else await ApplyAsync("headset.bass", token => service.SetBassAsync(level, token), results, cancellationToken);
        }
    }
}
