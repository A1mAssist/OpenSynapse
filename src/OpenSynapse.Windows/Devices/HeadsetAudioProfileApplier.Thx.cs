using OpenSynapse.Core.Audio;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class HeadsetAudioProfileApplier
{
    private async Task ApplyThxAsync(
        RazerHeadsetAudioConnection connection,
        HeadsetProfileSettings profile,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (profile.ThxSpatialEnabled is null &&
            profile.ThxEqualizerEnabled is null &&
            profile.ThxPreset is null &&
            profile.ThxBands is null &&
            profile.ThxNormalizationEnabled is null &&
            profile.ThxVoiceClarityEnabled is null &&
            profile.ThxBassBoostEnabled is null &&
            profile.ThxSidetoneEnabled is null)
        {
            return;
        }

        await using var controller = new WindowsThxHeadsetController(
            connection.ProductId, connection.ContainerId);
        ThxAudioState? state;
        try
        {
            var status = await controller.ReadAsync(ThxAudioEndpoint.Headphones, cancellationToken);
            state = status.State;
            if (state is null)
            {
                results.Add(new ProfileSettingApplyResult(
                    "headset.thx", ProfileSettingApplyStatus.Skipped, status.Error));
                return;
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            results.Add(new ProfileSettingApplyResult(
                "headset.thx", ProfileSettingApplyStatus.Skipped, exception.Message));
            return;
        }

        if (profile.ThxSpatialEnabled is { } spatial && spatial != state.SpatialEnabled)
        {
            await ApplyAsync(
                "headset.thx.spatial",
                async token =>
                {
                    var status = await controller.SetSpatialAsync(
                        ThxAudioEndpoint.Headphones, spatial, token);
                    EnsureThxApplied(status);
                },
                results,
                cancellationToken);
        }
        else if (profile.ThxSpatialEnabled is not null)
        {
            results.Add(Skipped("headset.thx.spatial"));
        }

        if (profile.ThxEqualizerEnabled is { } equalizer && equalizer != state.EqualizerEnabled)
        {
            await ApplyAsync(
                "headset.thx.equalizer",
                async token =>
                {
                    var status = await controller.SetEqualizerAsync(
                        ThxAudioEndpoint.Headphones, equalizer, token);
                    EnsureThxApplied(status);
                },
                results,
                cancellationToken);
        }
        else if (profile.ThxEqualizerEnabled is not null)
        {
            results.Add(Skipped("headset.thx.equalizer"));
        }

        var bands = ReadThxBands(profile.ThxBands) ??
            state.EqualizerBands.Select(band => band.GainDb).ToArray();
        if (profile.ThxPreset is { Length: > 0 } preset &&
            !StringComparer.Ordinal.Equals(preset, state.SelectedPresetId))
        {
            await ApplyAsync(
                "headset.thx.preset",
                async token =>
                {
                    var status = await controller.SelectPresetAsync(
                        ThxAudioEndpoint.Headphones, preset, bands, token);
                    EnsureThxApplied(status);
                },
                results,
                cancellationToken);
        }
        else if (profile.ThxPreset is not null)
        {
            results.Add(Skipped("headset.thx.preset"));
        }

        var desiredBands = ReadThxBands(profile.ThxBands);
        if (desiredBands is not null)
        {
            if (!desiredBands.SequenceEqual(state.EqualizerBands.Select(band => band.GainDb)))
            {
                await ApplyAsync(
                    "headset.thx.bands",
                    async token =>
                    {
                        var status = await controller.SetGainsAsync(
                            ThxAudioEndpoint.Headphones, desiredBands, token);
                        EnsureThxApplied(status);
                    },
                    results,
                    cancellationToken);
            }
            else
            {
                results.Add(Skipped("headset.thx.bands"));
            }
        }

        await ApplyThxEnhancementAsync(
            controller, state.SoundNormalization, "headset.thx.normalization",
            ThxAudioEnhancementKind.SoundNormalization,
            profile.ThxNormalizationEnabled, profile.ThxNormalizationLevel,
            results, cancellationToken);
        await ApplyThxEnhancementAsync(
            controller, state.VoiceClarity, "headset.thx.voice-clarity",
            ThxAudioEnhancementKind.VoiceClarity,
            profile.ThxVoiceClarityEnabled, profile.ThxVoiceClarityLevel,
            results, cancellationToken);
        await ApplyThxEnhancementAsync(
            controller, state.BassBoost, "headset.thx.bass-boost",
            ThxAudioEnhancementKind.BassBoost,
            profile.ThxBassBoostEnabled, profile.ThxBassBoostLevel,
            results, cancellationToken);
        await ApplyThxEnhancementAsync(
            controller, state.Sidetone, "headset.thx.sidetone",
            ThxAudioEnhancementKind.Sidetone,
            profile.ThxSidetoneEnabled, profile.ThxSidetoneLevel,
            results, cancellationToken);
    }

    private async Task ApplyThxEnhancementAsync(
        IThxAudioController controller,
        ThxAudioEnhancement? current,
        string key,
        ThxAudioEnhancementKind kind,
        bool? enabled,
        int? level,
        List<ProfileSettingApplyResult> results,
        CancellationToken cancellationToken)
    {
        if (enabled is null && level is null)
        {
            return;
        }

        var desiredEnabled = enabled ?? current?.Enabled;
        var desiredLevel = level ?? current?.LevelPercent;
        if (desiredEnabled is null || desiredLevel is null)
        {
            results.Add(new ProfileSettingApplyResult(
                key, ProfileSettingApplyStatus.Skipped, "No current enhancement state."));
            return;
        }

        if (current is not null &&
            current.Enabled == desiredEnabled.Value &&
            current.LevelPercent == desiredLevel.Value)
        {
            results.Add(Skipped(key));
            return;
        }

        await ApplyAsync(
            key,
            async token =>
            {
                var status = await controller.SetEnhancementAsync(
                    ThxAudioEndpoint.Headphones, kind, desiredEnabled.Value,
                    desiredLevel.Value, token);
                EnsureThxApplied(status);
            },
            results,
            cancellationToken);
    }

    private static void EnsureThxApplied(ThxAudioStatus status)
    {
        if (!status.IsAvailable)
        {
            throw new InvalidOperationException(status.Error ?? "THX rejected the setting.");
        }
    }

}
