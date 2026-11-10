using Microsoft.UI.Xaml;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    [ProfileSetting("headset.eq.preset", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyEqPresetAsync(CancellationToken cancellationToken = default)
    {
        if (IsKrakenBase) return;
        var previousIndex = SelectedEqPresetIndex;
        var previous = ActivePresets[previousIndex].Value;
        var preset = ActivePresets[SelectedEqPresetIndex].Value;
        var applied = _connection.Transport switch
        {
            RazerHeadsetAudioTransport.Mxic64 or RazerHeadsetAudioTransport.ValkyrieT3Feature64 or RazerHeadsetAudioTransport.Jolie64 =>
                await RunMxicWriteAsync((service, token) => service.SetEqPresetAsync(preset, token), cancellationToken),
            RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
                await RunValkyrieWriteAsync((service, token) => service.SetEqPresetAsync(preset, token), cancellationToken),
            _ => await RunWriteAsync((service, token) => service.SetEqPresetAsync(preset, token), cancellationToken),
        };
        if (!applied) return;

        await SaveHeadsetSettingAsync(
            settings => settings.EqPreset = preset.ToString("X2"),
            settings => settings.EqPreset = previous.ToString("X2"),
            () => SelectedEqPresetIndex = previousIndex,
            token => WriteEqPresetHardwareAsync(previous, token),
            cancellationToken);
        // The ten-band card shows the active preset's curve, so switching
        // presets has to reload it or Custom keeps the previous preset's bands.
        await RefreshEqBandsAsync(cancellationToken);
    }

    [ProfileSetting("headset.eq.bands", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyEqBandsAsync(CancellationToken cancellationToken = default)
    {
        var previousPresetIndex = SelectedEqPresetIndex;
        var previousPreset = ActivePresets[previousPresetIndex].Value;
        var previousBands = EqBands.Select(band => (int)Math.Round(band.Level)).ToArray();
        var bands = EqBands.Select(band => ToFirmwareBand(band.Level)).ToArray();
        var customIndex = Array.FindIndex(ActivePresets, preset => preset.Key == "RazerHeadsetPresetCustom");
        var customPreset = customIndex >= 0 ? ActivePresets[customIndex].Value : (byte)0;
        var applied = false;
        switch (bands.Length)
        {
            case 10 when IsT3:
                applied = await RunMxicWriteAsync((service, token) => service.SetEqBandsAsync(bands, token), cancellationToken);
                break;
            case 10 when IsMxic:
                // The firmware only makes the edited curve audible when the selected preset
                // is Custom; write that selection before the ten values, as Razer's UI does.
                applied = await RunMxicWriteAsync((service, token) => service.SetCustomEqAsync(customPreset, bands, token), cancellationToken);
                break;
            case 10 when IsValkyrie:
                applied = await RunValkyrieWriteAsync((service, token) => service.SetCustomEqAsync(customPreset, bands, token), cancellationToken);
                break;
            case 10 or 8 when IsFeature91:
                applied = await RunWriteAsync((service, token) => service.SetCustomEqAsync(customPreset, bands, token), cancellationToken);
                break;
            default:
                return;
        }

        if (applied && customIndex >= 0)
        {
            SelectedEqPresetIndex = customIndex;
        }
        if (!applied) return;

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.EqBands = ToBandMap(EqBands.Select(band => band.Level));
                if (customIndex >= 0)
                {
                    settings.EqPreset = customPreset.ToString("X2");
                }
            },
            settings =>
            {
                settings.EqBands = ToBandMap(previousBands.Select(value => (double)value));
                settings.EqPreset = previousPreset.ToString("X2");
            },
            () =>
            {
                for (var index = 0; index < previousBands.Length && index < EqBands.Count; index++)
                {
                    EqBands[index].Level = previousBands[index];
                }
                SelectedEqPresetIndex = previousPresetIndex;
            },
            token => WriteCustomEqHardwareAsync(
                previousPreset,
                previousBands.Select(value => ToFirmwareBand(value)).ToArray(),
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.lighting.brightness", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMatrixBrightnessAsync(CancellationToken cancellationToken = default)
    {
        var previous = Brightness;
        var applied = false;
        await RunChromaWriteAsync(
            async (service, token) =>
            {
                await service.SetBrightnessAsync((byte)Math.Round(Brightness), token);
                applied = true;
            },
            cancellationToken);
        if (!applied) return;

        await SaveHeadsetSettingAsync(
            settings => settings.ChromaBrightness = (int)Math.Round(Brightness),
            settings => settings.ChromaBrightness = (int)Math.Round(previous),
            () => Brightness = previous,
            token => OpenChromaService().SetBrightnessAsync((byte)Math.Round(previous), token),
            cancellationToken);
    }

    [ProfileSetting("headset.lighting.effect", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMatrixLightingAsync(CancellationToken cancellationToken = default)
    {
        var previousEffectIndex = SelectedLightingEffectIndex;
        var previousPrimary = PrimaryColor;
        var previousSecondary = SecondaryColor;
        var applied = false;
        await RunChromaWriteAsync(async (service, token) =>
        {
            var effect = LightingEffects[SelectedLightingEffectIndex];
            var colors = effect switch
            {
                RazerChromaEffect.Static => new byte[] { PrimaryColor.R, PrimaryColor.G, PrimaryColor.B },
                RazerChromaEffect.Breathing =>
                    [PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, SecondaryColor.R, SecondaryColor.G, SecondaryColor.B],
                _ => [],
            };
            await service.SetEffectAsync(effect, colors, token);
            applied = true;
        }, cancellationToken);
        if (!applied) return;

        var effect = LightingEffects[SelectedLightingEffectIndex];
        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.ChromaEffect = effect.ToString();
                settings.ChromaParameters = new Dictionary<string, string>
                {
                    ["primary"] = $"{PrimaryColor.R:X2}{PrimaryColor.G:X2}{PrimaryColor.B:X2}",
                    ["secondary"] = $"{SecondaryColor.R:X2}{SecondaryColor.G:X2}{SecondaryColor.B:X2}",
                };
            },
            settings =>
            {
                settings.ChromaEffect = LightingEffects[previousEffectIndex].ToString();
                settings.ChromaParameters = new Dictionary<string, string>
                {
                    ["primary"] = $"{previousPrimary.R:X2}{previousPrimary.G:X2}{previousPrimary.B:X2}",
                    ["secondary"] = $"{previousSecondary.R:X2}{previousSecondary.G:X2}{previousSecondary.B:X2}",
                };
            },
            () =>
            {
                SelectedLightingEffectIndex = previousEffectIndex;
                PrimaryColor = previousPrimary;
                SecondaryColor = previousSecondary;
            },
            token => WriteChromaEffectHardwareAsync(
                LightingEffects[previousEffectIndex], previousPrimary, previousSecondary, token),
            cancellationToken);
    }

    [ProfileSetting("headset.bass", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyBassAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91 && !IsJolie) return;
        var previous = Bass;
        var applied = IsJolie
            ? await RunMxicWriteAsync(
                (service, token) => service.SetBassAsync((byte)Math.Round(Bass), token),
                cancellationToken)
            : await RunWriteAsync(
                (service, token) => service.SetBassAsync((byte)Math.Round(Bass), token),
                cancellationToken);
        if (!applied)
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.Bass = (int)Math.Round(Bass),
            settings => settings.Bass = (int)Math.Round(previous),
            () => Bass = previous,
            async token =>
            {
                if (IsJolie)
                {
                    await using var service = OpenMxicService();
                    await service.SetBassAsync((byte)Math.Round(previous), token);
                }
                else
                {
                    await OpenService().SetBassAsync((byte)Math.Round(previous), token);
                }
            },
            cancellationToken);
    }

    [ProfileSetting("headset.microphone", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicrophoneAsync(CancellationToken cancellationToken = default)
    {
        if (IsJolie) return;
        var previousMuted = MicMuted;
        var previousVolume = MicVolume;
        var applied = _connection.Transport switch
        {
            RazerHeadsetAudioTransport.Mxic64 or RazerHeadsetAudioTransport.ValkyrieT3Feature64 or RazerHeadsetAudioTransport.Jolie64 =>
                await RunMxicWriteAsync((service, token) => service.SetMicMuteAsync(MicMuted, token), cancellationToken),
            RazerHeadsetAudioTransport.Valkyrie64 or RazerHeadsetAudioTransport.Billie64 =>
                await RunValkyrieWriteAsync((service, token) => service.SetMicMuteAsync(MicMuted, token), cancellationToken),
            _ => await RunWriteAsync(async (service, token) =>
                {
                    await service.SetMicMuteAsync(MicMuted, token);
                    await service.SetMicVolumeAsync((byte)Math.Round(MicVolume), token);
                }, cancellationToken),
        };
        if (!applied) return;

        await SaveHeadsetSettingAsync(
            settings =>
            {
                if (IsFeature91)
                {
                    settings.MicrophoneVolumePercent = (int)Math.Round(MicVolume);
                }
                settings.MicrophoneMute = MicMuted;
            },
            settings =>
            {
                if (IsFeature91)
                {
                    settings.MicrophoneVolumePercent = (int)Math.Round(previousVolume);
                }
                settings.MicrophoneMute = previousMuted;
            },
            () =>
            {
                MicMuted = previousMuted;
                MicVolume = previousVolume;
            },
            async token =>
            {
                if (IsFeature91)
                {
                    var service = OpenService();
                    await service.SetMicMuteAsync(previousMuted, token);
                    await service.SetMicVolumeAsync((byte)Math.Round(previousVolume), token);
                }
                else if (IsMxic)
                {
                    await using var service = OpenMxicService();
                    await service.SetMicMuteAsync(previousMuted, token);
                }
                else
                {
                    await using var service = OpenValkyrieService();
                    await service.SetMicMuteAsync(previousMuted, token);
                }
            },
            cancellationToken);
    }

    [ProfileSetting("headset.source", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplySourceAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = SelectedSourceIndex;
        if (!await RunWriteAsync(
                (service, token) => service.SetAudioSourceAsync(Sources[SelectedSourceIndex], token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.AudioSourceIndex = SelectedSourceIndex,
            settings => settings.AudioSourceIndex = previous,
            () => SelectedSourceIndex = previous,
            token => OpenService().SetAudioSourceAsync(Sources[previous], token),
            cancellationToken);
    }

    [ProfileSetting("headset.mic-monitoring", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = MicMonitoringEnabled;
        if (!await RunWriteAsync(
                (service, token) => service.SetMicReturnSwitchAsync(MicMonitoringEnabled, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => settings.MicMonitoringEnabled = MicMonitoringEnabled,
            settings => settings.MicMonitoringEnabled = previous,
            () => MicMonitoringEnabled = previous,
            token => OpenService().SetMicReturnSwitchAsync(previous, token),
            cancellationToken);
    }

    [ProfileSetting("headset.parametric-eq", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyParametricEqAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = CurrentParametricEq();
        if (!await RunWriteAsync(
                (service, token) => service.SetParametricEqAsync(CurrentParametricEq(), token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => CopyParametricEq(settings, CurrentParametricEq()),
            settings => CopyParametricEq(settings, previous),
            () => RestoreParametricEq(previous),
            token => OpenService().SetParametricEqAsync(previous, token),
            cancellationToken);
    }

    [ProfileSetting("headset.mic-compressor", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicCompressorAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = CurrentMicCompressor();
        if (!await RunWriteAsync(
                (service, token) => service.SetMicCompressorAsync(CurrentMicCompressor(), token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => CopyMicCompressor(settings, CurrentMicCompressor()),
            settings => CopyMicCompressor(settings, previous),
            () => RestoreMicCompressor(previous),
            token => OpenService().SetMicCompressorAsync(previous, token),
            cancellationToken);
    }

    [NotProfileScoped]
    public Task ApplyHapticsAsync(CancellationToken cancellationToken = default) =>
        !IsFeature91
            ? Task.CompletedTask
            : RunWriteAsync(
                (service, token) => service.SetHapticsAsync(
                    HapticsEnabled, (ushort)Math.Round(HapticsStrength), token),
                cancellationToken);

    [ProfileSetting("headset.advanced", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyAdvancedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = (Dnd, AutoPause, GestureControl, Dolby, NightMode, VoicePrompts);
        if (!await RunWriteAsync(
                (service, token) => WriteAdvancedAsync(
                    service, Dnd, AutoPause, GestureControl, Dolby, NightMode, VoicePrompts, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => CopyAdvanced(settings, (Dnd, AutoPause, GestureControl, Dolby, NightMode, VoicePrompts)),
            settings => CopyAdvanced(settings, previous),
            () =>
            {
                Dnd = previous.Dnd;
                AutoPause = previous.AutoPause;
                GestureControl = previous.GestureControl;
                Dolby = previous.Dolby;
                NightMode = previous.NightMode;
                VoicePrompts = previous.VoicePrompts;
            },
            token => WriteAdvancedAsync(
                OpenService(),
                previous.Dnd, previous.AutoPause, previous.GestureControl,
                previous.Dolby, previous.NightMode, previous.VoicePrompts,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.mic-processing", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicProcessingAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = (MicDeEsser, MicLimiterDb, SelectedMicReverbIndex, MicReverbIntensity);
        if (!await RunWriteAsync(
                (service, token) => WriteMicProcessingAsync(
                    service, MicDeEsser, MicLimiterDb,
                    SelectedMicReverbIndex, MicReverbIntensity, token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings => CopyMicProcessing(
                settings, (MicDeEsser, MicLimiterDb, SelectedMicReverbIndex, MicReverbIntensity)),
            settings => CopyMicProcessing(settings, previous),
            () =>
            {
                MicDeEsser = previous.MicDeEsser;
                MicLimiterDb = previous.MicLimiterDb;
                SelectedMicReverbIndex = previous.SelectedMicReverbIndex;
                MicReverbIntensity = previous.MicReverbIntensity;
            },
            token => WriteMicProcessingAsync(
                OpenService(),
                previous.MicDeEsser, previous.MicLimiterDb,
                previous.SelectedMicReverbIndex, previous.MicReverbIntensity,
                token),
            cancellationToken);
    }

    [ProfileSetting("headset.dnd", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyDndAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = Dnd;
        if (!await RunWriteAsync((service, token) => service.SetAudioDndAsync(Dnd, token), cancellationToken)) return;
        await SaveSettingAsync(previous, Dnd, value => Dnd = value, (settings, value) => settings.Dnd = value,
            token => OpenService().SetAudioDndAsync(previous, token), cancellationToken);
    }

    [ProfileSetting("headset.auto-pause", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyAutoPauseAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = AutoPause;
        if (!await RunWriteAsync((service, token) => service.SetAudioAutoPauseAsync(AutoPause, token), cancellationToken)) return;
        await SaveSettingAsync(previous, AutoPause, value => AutoPause = value, (settings, value) => settings.AutoPause = value,
            token => OpenService().SetAudioAutoPauseAsync(previous, token), cancellationToken);
    }

    [ProfileSetting("headset.gesture-control", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyGestureControlAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = GestureControl;
        if (!await RunWriteAsync((service, token) => service.SetAudioGestureControlAsync(GestureControl, token), cancellationToken)) return;
        await SaveSettingAsync(previous, GestureControl, value => GestureControl = value,
            (settings, value) => settings.GestureControl = value,
            token => OpenService().SetAudioGestureControlAsync(previous, token), cancellationToken);
    }

    [ProfileSetting("headset.dolby", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyDolbyAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = Dolby;
        if (!await RunWriteAsync((service, token) => service.SetDolbyStateAsync(Dolby, token), cancellationToken)) return;
        await SaveSettingAsync(previous, Dolby, value => Dolby = value, (settings, value) => settings.Dolby = value,
            token => OpenService().SetDolbyStateAsync(previous, token), cancellationToken);
    }

    [ProfileSetting("headset.night-mode", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyNightModeAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = NightMode;
        if (!await RunWriteAsync((service, token) => service.SetEqNightModeAsync(NightMode, token), cancellationToken)) return;
        await SaveSettingAsync(previous, NightMode, value => NightMode = value, (settings, value) => settings.NightMode = value,
            token => OpenService().SetEqNightModeAsync(previous, token), cancellationToken);
    }

    [ProfileSetting("headset.voice-prompts", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyVoicePromptsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = VoicePrompts;
        if (!await RunWriteAsync((service, token) => service.SetAudioVoicePromptsAsync(VoicePrompts, cancellationToken: token), cancellationToken)) return;
        await SaveSettingAsync(previous, VoicePrompts, value => VoicePrompts = value, (settings, value) => settings.VoicePrompts = value,
            token => OpenService().SetAudioVoicePromptsAsync(previous, cancellationToken: token), cancellationToken);
    }

    [ProfileSetting("headset.mic.de-esser", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicDeEsserAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = MicDeEsser;
        if (!await RunWriteAsync((service, token) => service.SetMicDeEsserAsync((byte)Math.Round(MicDeEsser), token), cancellationToken)) return;
        await SaveSettingAsync(previous, MicDeEsser, value => MicDeEsser = value,
            (settings, value) => settings.MicDeEsser = (int)Math.Round(value),
            token => OpenService().SetMicDeEsserAsync((byte)Math.Round(previous), token), cancellationToken);
    }

    [ProfileSetting("headset.mic.limiter", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicLimiterAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previous = MicLimiterDb;
        if (!await RunWriteAsync((service, token) => service.SetMicLimiterAsync((sbyte)Math.Round(MicLimiterDb), token), cancellationToken)) return;
        await SaveSettingAsync(previous, MicLimiterDb, value => MicLimiterDb = value,
            (settings, value) => settings.MicLimiterDb = (int)Math.Round(value),
            token => OpenService().SetMicLimiterAsync((sbyte)Math.Round(previous), token), cancellationToken);
    }

    [ProfileSetting("headset.mic.reverb", DeviceCategory.Headset, powerScoped: true)]
    public async Task ApplyMicReverbAsync(CancellationToken cancellationToken = default)
    {
        if (!IsFeature91) return;
        var previousIndex = SelectedMicReverbIndex;
        var previousIntensity = MicReverbIntensity;
        if (!await RunWriteAsync(
                (service, token) => service.SetMicReverbAsync(
                    MicReverbModes[SelectedMicReverbIndex],
                    (byte)Math.Round(MicReverbIntensity),
                    token),
                cancellationToken))
        {
            return;
        }

        await SaveHeadsetSettingAsync(
            settings =>
            {
                settings.MicReverbIndex = SelectedMicReverbIndex;
                settings.MicReverbIntensity = (int)Math.Round(MicReverbIntensity);
            },
            settings =>
            {
                settings.MicReverbIndex = previousIndex;
                settings.MicReverbIntensity = (int)Math.Round(previousIntensity);
            },
            () =>
            {
                SelectedMicReverbIndex = previousIndex;
                MicReverbIntensity = previousIntensity;
            },
            token => OpenService().SetMicReverbAsync(
                MicReverbModes[previousIndex], (byte)Math.Round(previousIntensity), token),
            cancellationToken);
    }

}
