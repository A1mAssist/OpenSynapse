using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class HeadsetAudioProfileApplier
{
    private async Task ApplyEqualizerAsync(
        RazerHeadsetAudioService service,
        RazerHeadsetAudioState? state,
        HeadsetProfileSettings profile,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        var preset = TryParseByte(profile.EqPreset);
        var bands = ReadBands(
            profile.EqBands,
            state?.EqBands?.Count ?? profile.EqBands?.Count ?? 10,
            out var bandError);

        if (profile.EqPreset is not null && preset is null)
        {
            results.Add(Failed("headset.eq.preset", "Unknown EQ preset value."));
        }
        else if (bands is not null)
        {
            if (state?.EqBands is { } currentBands && currentBands.SequenceEqual(bands))
            {
                results.Add(Skipped("headset.eq.bands"));
            }
            else
            {
                var customPreset = preset ?? (byte)0x10;
                await ApplyAsync(
                    "headset.eq.bands",
                    token => service.SetCustomEqAsync(customPreset, bands, token),
                    results,
                    cancellationToken);
            }
        }
        else if (bandError is not null)
        {
            results.Add(Failed("headset.eq.bands", bandError));
        }
        else if (preset is { } value)
        {
            if (state?.EqPreset == value)
            {
                results.Add(Skipped("headset.eq.preset"));
            }
            else
            {
                await ApplyAsync(
                    "headset.eq.preset",
                    token => service.SetEqPresetAsync(value, token),
                    results,
                    cancellationToken);
            }
        }
    }

    private async Task ApplyMicrophoneAsync(
        RazerHeadsetAudioService service,
        RazerHeadsetAudioState? state,
        HeadsetProfileSettings profile,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.MicrophoneMute is { } muted && state?.MicMuted != muted)
        {
            await ApplyAsync(
                "headset.microphone-mute",
                token => service.SetMicMuteAsync(muted, token),
                results,
                cancellationToken);
        }
        else if (profile.MicrophoneMute is not null)
        {
            results.Add(Skipped("headset.microphone-mute"));
        }

        if (profile.MicrophoneVolumePercent is { } volume && state?.MicVolume != volume)
        {
            await ApplyAsync(
                "headset.microphone-volume",
                token => service.SetMicVolumeAsync((byte)Math.Clamp(volume, 0, 100), token),
                results,
                cancellationToken);
        }
        else if (profile.MicrophoneVolumePercent is not null)
        {
            results.Add(Skipped("headset.microphone-volume"));
        }

        if (profile.Bass is { } bass && state?.Bass != bass)
        {
            await ApplyAsync(
                "headset.bass",
                token => service.SetBassAsync((byte)Math.Clamp(bass, 0, 100), token),
                results,
                cancellationToken);
        }
        else if (profile.Bass is not null)
        {
            results.Add(Skipped("headset.bass"));
        }

        if (profile.AudioSourceIndex is { } sourceIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= Sources.Length)
            {
                results.Add(Failed("headset.source", "Unknown audio source."));
            }
            else if (state?.AudioSource != Sources[sourceIndex])
            {
                await ApplyAsync(
                    "headset.source",
                    token => service.SetAudioSourceAsync(Sources[sourceIndex], token),
                    results,
                    cancellationToken);
            }
            else
            {
                results.Add(Skipped("headset.source"));
            }
        }

        if (profile.MicMonitoringEnabled is { } monitoring && state?.MicReturnSwitch != monitoring)
        {
            await ApplyAsync(
                "headset.mic-monitoring",
                token => service.SetMicReturnSwitchAsync(monitoring, token),
                results,
                cancellationToken);
        }
        else if (profile.MicMonitoringEnabled is not null)
        {
            results.Add(Skipped("headset.mic-monitoring"));
        }

        if (profile.NoiseSuppression is { } noise && state?.NoiseSuppression != noise)
        {
            await ApplyAsync(
                "headset.noise-suppression",
                token => service.SetNoiseSuppressionAsync(noise, token),
                results,
                cancellationToken);
        }
        else if (profile.NoiseSuppression is not null)
        {
            results.Add(Skipped("headset.noise-suppression"));
        }

        if (TryBuildParametricEq(profile, state?.ParametricEq, out var parametricEq))
        {
            if (state?.ParametricEq == parametricEq)
            {
                results.Add(Skipped("headset.parametric-eq"));
            }
            else
            {
                await ApplyAsync(
                    "headset.parametric-eq",
                    token => service.SetParametricEqAsync(parametricEq, token),
                    results,
                    cancellationToken);
            }
        }
        else if (HasParametricEq(profile))
        {
            results.Add(Failed("headset.parametric-eq", "The parametric EQ profile is incomplete."));
        }

        if (TryBuildMicCompressor(profile, state?.MicCompressor, out var compressor))
        {
            if (state?.MicCompressor == compressor)
            {
                results.Add(Skipped("headset.mic-compressor"));
            }
            else
            {
                await ApplyAsync(
                    "headset.mic-compressor",
                    token => service.SetMicCompressorAsync(compressor, token),
                    results,
                    cancellationToken);
            }
        }
        else if (profile.MicCompressorEnabled is not null)
        {
            results.Add(Failed("headset.mic-compressor", "The microphone compressor profile is incomplete."));
        }
    }

}
