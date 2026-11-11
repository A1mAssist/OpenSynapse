using System.Globalization;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Profiles;
using OpenSynapse.Core.Sensors;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Windows.UI;

namespace OpenSynapse.App.ViewModels;

public sealed partial class RazerHeadsetAudioViewModel
{
    private Func<ProfileDocument>? _getActiveProfile;
    private Func<CancellationToken, Task<bool>>? _saveProfileAsync;

    private string HeadsetProfileKey => $"1532:{_connection.ProductId:X4}";

    [NotProfileScoped]
    public async Task ApplyPersistedProfileAsync(
        PowerSourceKind powerSource,
        CancellationToken cancellationToken = default)
    {
        if (_getActiveProfile?.Invoke() is not { } document)
        {
            return;
        }

        if (Mixer053E is { } mixer)
        {
            await mixer.ApplyPersistedProfileAsync(powerSource, cancellationToken);
            return;
        }

        var descriptor = new DeviceDescriptor(
            HeadsetProfileKey,
            _connection.Name,
            0x1532,
            _connection.ProductId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            _connection.FeatureReportByteLength,
            1,
            2,
            "headset-audio",
            DeviceCategory.Headset);
        var resolved = ProfileResolver.Resolve(document, descriptor, powerSource);
        if (!IsFeature91)
        {
            // THX is a Windows-side component, so it replays through the applier even
            // when the device settings themselves use a 37/64-byte transport.
            var thxApplier = new HeadsetAudioProfileApplier(_ => _connection);
            await thxApplier.ApplyAsync(
                new DeviceProfileApplyContext(document, descriptor, powerSource, resolved),
                cancellationToken);
            await ApplyPersistedTransportProfileAsync(resolved.Headset, cancellationToken);
            return;
        }

        var applier = new HeadsetAudioProfileApplier(_ => _connection);
        await applier.ApplyAsync(
            new DeviceProfileApplyContext(document, descriptor, powerSource, resolved),
            cancellationToken);
    }

    // The Feature91 applier cannot talk to the 64-byte Mxic/Valkyrie/KrakenBase
    // transports, so those headsets replay the settings their own services write.
    // KrakenBase has no EQ writer and keeps its Chroma on the 37-byte lighting card.
    [NotProfileScoped]
    private async Task ApplyPersistedTransportProfileAsync(
        HeadsetProfileSettings profile,
        CancellationToken cancellationToken)
    {
        async Task AttemptAsync(string key, Func<CancellationToken, Task> write)
        {
            try
            {
                await write(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                _diagnostic?.Invoke($"profile {key}: {exception.Message}");
            }
        }

        if (IsJolie)
        {
            await ApplyPersistedJolietEqAsync(profile, cancellationToken);
            return;
        }

        if (ReadBandLevels(profile.EqBands) is { } levels)
        {
            var bands = levels.Select(level => ToFirmwareBand(level)).ToArray();
            if (IsT3)
            {
                await AttemptAsync("headset.eq.bands", async token =>
                {
                    await using var service = OpenMxicService();
                    await service.SetEqBandsAsync(bands, token);
                });
            }
            else if (!IsKrakenBase)
            {
                var customIndex = Array.FindIndex(ActivePresets, item => item.Key == "RazerHeadsetPresetCustom");
                if (customIndex >= 0)
                {
                    var customPreset = ActivePresets[customIndex].Value;
                    await AttemptAsync(
                        "headset.eq.bands",
                        token => WriteCustomEqHardwareAsync(customPreset, bands, token));
                }
            }
        }

        // A custom-band write also selects the Custom preset, so the stored preset has
        // to go last to reproduce whichever selection the user made last.
        if (!IsKrakenBase && TryParseHexByte(profile.EqPreset) is { } preset)
        {
            await AttemptAsync("headset.eq.preset", token => WriteEqPresetHardwareAsync(preset, token));
        }

        if (IsValkyrie && ReadBandLevels(profile.MicEqBands) is { } micLevels)
        {
            var bands = micLevels.Select(level => ToFirmwareBand(level)).ToArray();
            await AttemptAsync("headset.mic.eq-bands", async token =>
            {
                await using var service = OpenValkyrieService();
                await service.SetMicBandsAsync(bands, token);
            });
        }

        if (profile.MicrophoneMute is { } muted)
        {
            await AttemptAsync("headset.microphone", token => WriteMicMuteAsync(muted, token));
        }

        if (IsKrakenBase &&
            profile.AncModeIndex is { } ancIndex &&
            ancIndex >= 0 && ancIndex < DeviceAncStates.Length)
        {
            var level = (byte)Math.Clamp(profile.AncLevel ?? 0, 0, 10);
            await AttemptAsync(
                "headset.anc",
                token => OpenService().SetAncAsync(DeviceAncStates[ancIndex], level, token));
        }
    }

    private async Task WriteMicMuteAsync(bool muted, CancellationToken cancellationToken)
    {
        switch (_connection.Transport)
        {
            case RazerHeadsetAudioTransport.Mxic64:
            case RazerHeadsetAudioTransport.ValkyrieT3Feature64:
            case RazerHeadsetAudioTransport.Jolie64:
                await using (var service = OpenMxicService())
                {
                    await service.SetMicMuteAsync(muted, cancellationToken);
                }
                break;
            case RazerHeadsetAudioTransport.Valkyrie64:
            case RazerHeadsetAudioTransport.Billie64:
                await using (var service = OpenValkyrieService())
                {
                    await service.SetMicMuteAsync(muted, cancellationToken);
                }
                break;
            default:
                await OpenService().SetMicMuteAsync(muted, cancellationToken);
                break;
        }
    }

    private static byte? TryParseHexByte(string? value) =>
        byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static double[]? ReadBandLevels(IReadOnlyDictionary<string, int>? source)
    {
        if (source is not { Count: 10 })
        {
            return null;
        }

        var levels = new double[source.Count];
        for (var index = 0; index < levels.Length; index++)
        {
            if (!source.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out var level))
            {
                return null;
            }
            levels[index] = level;
        }
        return levels;
    }

    private static Dictionary<string, int> ToBandMap(IEnumerable<double> levels) =>
        levels
            .Select((level, index) => (Key: index.ToString(), Value: (int)Math.Round(level)))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private async Task WriteEqPresetHardwareAsync(byte preset, CancellationToken cancellationToken)
    {
        switch (_connection.Transport)
        {
            case RazerHeadsetAudioTransport.Mxic64:
            case RazerHeadsetAudioTransport.ValkyrieT3Feature64:
            case RazerHeadsetAudioTransport.Jolie64:
                await using (var service = OpenMxicService())
                {
                    await service.SetEqPresetAsync(preset, cancellationToken);
                }
                break;
            case RazerHeadsetAudioTransport.Valkyrie64:
            case RazerHeadsetAudioTransport.Billie64:
                await using (var service = OpenValkyrieService())
                {
                    await service.SetEqPresetAsync(preset, cancellationToken);
                }
                break;
            default:
                await OpenService().SetEqPresetAsync(preset, cancellationToken);
                break;
        }
    }

    private async Task WriteCustomEqHardwareAsync(
        byte preset,
        IReadOnlyList<byte> bands,
        CancellationToken cancellationToken)
    {
        switch (_connection.Transport)
        {
            case RazerHeadsetAudioTransport.Mxic64:
            case RazerHeadsetAudioTransport.ValkyrieT3Feature64:
            case RazerHeadsetAudioTransport.Jolie64:
                await using (var service = OpenMxicService())
                {
                    await service.SetCustomEqAsync(preset, bands, cancellationToken);
                }
                break;
            case RazerHeadsetAudioTransport.Valkyrie64:
            case RazerHeadsetAudioTransport.Billie64:
                await using (var service = OpenValkyrieService())
                {
                    await service.SetCustomEqAsync(preset, bands, cancellationToken);
                }
                break;
            default:
                await OpenService().SetCustomEqAsync(preset, bands, cancellationToken);
                break;
        }
    }

    private async Task WriteChromaEffectHardwareAsync(
        RazerChromaEffect effect,
        Color primary,
        Color secondary,
        CancellationToken cancellationToken)
    {
        if (IsAvnera65)
        {
            await using var service = OpenAvneraService();
            await service.SetChromaEffectAsync(
                effect, primary.R, primary.G, primary.B, cancellationToken);
            return;
        }

        var colors = effect switch
        {
            RazerChromaEffect.Static => new byte[] { primary.R, primary.G, primary.B },
            RazerChromaEffect.Breathing =>
                [primary.R, primary.G, primary.B, secondary.R, secondary.G, secondary.B],
            _ => [],
        };
        await OpenChromaService().SetEffectAsync(effect, colors, cancellationToken);
    }

    private async Task WriteChromaBrightnessHardwareAsync(
        double brightness,
        CancellationToken cancellationToken)
    {
        if (IsAvnera65)
        {
            await using var service = OpenAvneraService();
            await service.SetChromaBrightnessAsync((byte)Math.Round(brightness), cancellationToken);
            return;
        }

        await OpenChromaService().SetBrightnessAsync((byte)Math.Round(brightness), cancellationToken);
    }

    private RazerParametricEqBand CurrentParametricEq() =>
        new(
            _parametricEqProfile,
            _parametricEqDirection,
            (byte)ParametricEqBandIndex,
            ParametricEqEnabled ? (byte)1 : (byte)0,
            (ushort)Math.Round(ParametricEqFrequency),
            (sbyte)Math.Round(ParametricEqGain),
            (byte)Math.Round(ParametricEqQ),
            _parametricEqFilterType);

    private void RestoreParametricEq(RazerParametricEqBand band)
    {
        _parametricEqProfile = band.Profile;
        _parametricEqDirection = band.Direction;
        _parametricEqFilterType = band.FilterType;
        ParametricEqEnabled = band.Enabled;
        ParametricEqBandIndex = band.Index;
        ParametricEqFrequency = band.Frequency;
        ParametricEqGain = band.GainTenths;
        ParametricEqQ = band.QFactorTenths;
    }

    private static void CopyParametricEq(HeadsetProfileSettings settings, RazerParametricEqBand band)
    {
        settings.ParametricEqEnabled = band.Enabled;
        settings.ParametricEqBandIndex = band.Index;
        settings.ParametricEqFrequency = band.Frequency;
        settings.ParametricEqGain = band.GainTenths;
        settings.ParametricEqQ = band.QFactorTenths;
        settings.ParametricEqProfile = band.Profile;
        settings.ParametricEqDirection = band.Direction;
        settings.ParametricEqFilterType = band.FilterType;
    }

    private RazerMicCompressor CurrentMicCompressor() =>
        new(
            _micCompressorProfile,
            MicCompressorEnabled ? (byte)1 : (byte)0,
            (sbyte)Math.Round(MicCompressorDb),
            (ushort)Math.Round(MicCompressorRatio),
            (ushort)Math.Round(MicCompressorAttack),
            (ushort)Math.Round(MicCompressorRelease));

    private void RestoreMicCompressor(RazerMicCompressor compressor)
    {
        _micCompressorProfile = compressor.Profile;
        MicCompressorEnabled = compressor.Enabled;
        MicCompressorDb = compressor.Db;
        MicCompressorRatio = compressor.RatioTenths;
        MicCompressorAttack = compressor.AttackTime;
        MicCompressorRelease = compressor.ReleaseTime;
    }

    private static void CopyMicCompressor(HeadsetProfileSettings settings, RazerMicCompressor compressor)
    {
        settings.MicCompressorEnabled = compressor.Enabled;
        settings.MicCompressorProfile = compressor.Profile;
        settings.MicCompressorDb = compressor.Db;
        settings.MicCompressorRatio = compressor.RatioTenths;
        settings.MicCompressorAttack = compressor.AttackTime;
        settings.MicCompressorRelease = compressor.ReleaseTime;
    }

    private async Task WriteAdvancedAsync(
        RazerHeadsetAudioService service,
        bool dnd,
        bool autoPause,
        bool gestureControl,
        bool dolby,
        bool nightMode,
        bool voicePrompts,
        CancellationToken cancellationToken)
    {
        if (_hasDnd) await service.SetAudioDndAsync(dnd, cancellationToken);
        if (_hasAutoPause) await service.SetAudioAutoPauseAsync(autoPause, cancellationToken);
        if (_hasGestureControl) await service.SetAudioGestureControlAsync(gestureControl, cancellationToken);
        if (_hasDolby) await service.SetDolbyStateAsync(dolby, cancellationToken);
        if (_hasNightMode) await service.SetEqNightModeAsync(nightMode, cancellationToken);
        if (_hasVoicePrompts) await service.SetAudioVoicePromptsAsync(voicePrompts, cancellationToken: cancellationToken);
    }

    private void CopyAdvanced(
        HeadsetProfileSettings settings,
        (bool Dnd, bool AutoPause, bool GestureControl, bool Dolby, bool NightMode, bool VoicePrompts) values)
    {
        if (_hasDnd) settings.Dnd = values.Dnd;
        if (_hasAutoPause) settings.AutoPause = values.AutoPause;
        if (_hasGestureControl) settings.GestureControl = values.GestureControl;
        if (_hasDolby) settings.Dolby = values.Dolby;
        if (_hasNightMode) settings.NightMode = values.NightMode;
        if (_hasVoicePrompts) settings.VoicePrompts = values.VoicePrompts;
    }

    private async Task WriteMicProcessingAsync(
        RazerHeadsetAudioService service,
        double deEsser,
        double limiterDb,
        int reverbIndex,
        double reverbIntensity,
        CancellationToken cancellationToken)
    {
        if (_hasMicDeEsser) await service.SetMicDeEsserAsync((byte)Math.Round(deEsser), cancellationToken);
        if (_hasMicLimiter) await service.SetMicLimiterAsync((sbyte)Math.Round(limiterDb), cancellationToken);
        if (_hasMicReverb)
        {
            await service.SetMicReverbAsync(
                MicReverbModes[reverbIndex], (byte)Math.Round(reverbIntensity), cancellationToken);
        }
    }

    private void CopyMicProcessing(
        HeadsetProfileSettings settings,
        (double MicDeEsser, double MicLimiterDb, int SelectedMicReverbIndex, double MicReverbIntensity) values)
    {
        if (_hasMicDeEsser) settings.MicDeEsser = (int)Math.Round(values.MicDeEsser);
        if (_hasMicLimiter) settings.MicLimiterDb = (int)Math.Round(values.MicLimiterDb);
        if (_hasMicReverb)
        {
            settings.MicReverbIndex = values.SelectedMicReverbIndex;
            settings.MicReverbIntensity = (int)Math.Round(values.MicReverbIntensity);
        }
    }

    private HeadsetProfileSettings? ResolveHeadsetProfileSettings()
    {
        if (_getActiveProfile is null)
        {
            return null;
        }

        var active = _getActiveProfile();
        if (!active.Devices.TryGetValue(HeadsetProfileKey, out var settings))
        {
            settings = new DeviceProfileSettings();
            active.Devices[HeadsetProfileKey] = settings;
        }

        return settings.Headset;
    }

    private Task<bool> SaveSettingAsync<T>(
        T previous,
        T current,
        Action<T> revertUi,
        Action<HeadsetProfileSettings, T> edit,
        Func<CancellationToken, Task>? revertHardware,
        CancellationToken cancellationToken) =>
        SaveHeadsetSettingAsync(
            settings => edit(settings, current),
            settings => edit(settings, previous),
            () => revertUi(previous),
            revertHardware,
            cancellationToken);

    private async Task<bool> SaveHeadsetSettingAsync(
        Action<HeadsetProfileSettings> edit,
        Action<HeadsetProfileSettings> revert,
        Action? revertUi,
        Func<CancellationToken, Task>? revertHardware,
        CancellationToken cancellationToken)
    {
        if (_getActiveProfile is null || _saveProfileAsync is null ||
            ResolveHeadsetProfileSettings() is not { } settings)
        {
            return true;
        }

        edit(settings);
        Exception? failure = null;
        try
        {
            if (!await _saveProfileAsync(cancellationToken))
            {
                failure = new IOException("The headset profile could not be saved.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            failure = new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (failure is null)
        {
            return true;
        }

        revert(settings);
        revertUi?.Invoke();
        if (revertHardware is not null)
        {
            try
            {
                await revertHardware(CancellationToken.None);
            }
            catch
            {
                // The save failure is what the user must see; a failed hardware
                // rollback must not replace it.
            }
        }

        OperationStatusText = string.Empty;
        if (failure is OperationCanceledException)
        {
            throw failure;
        }

        ErrorText = AppStrings.Text("RazerHidSettingProfileSaveFailed");
        return false;
    }
}
