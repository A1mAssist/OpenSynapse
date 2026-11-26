using Microsoft.UI.Xaml;
using OpenSynapse.Core.Audio;
using OpenSynapse.Windows.Audio;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.ViewModels;

internal enum HeadsetAudioSetting
{
    EqPreset,
    EqBands,
    MicEqBands,
    Bass,
    Microphone,
    Source,
    Noise,
    Anc,
    Advanced,
    MicProcessing,
    CamyT1,
    MicMonitoring,
    ParametricEq,
    Compressor,
    Haptics,
    AutoSleep,
    ThxSpatial,
    ThxPreset,
    ThxBands,
    ThxNormalization,
    ThxVoiceClarity,
    ThxBassBoost,
    ThxSidetone,
    ThxCaptureNormalization,
    ThxCaptureVocalClarity,
    ThxCaptureNoiseReduction,
    ThxCaptureVoiceGate,
}
public sealed partial class RazerHeadsetAudioViewModel
{
#if DEBUG
    internal void SetAutoApplyPreviewStatus(string status) => OperationStatusText = status;

    internal void SetAutoApplyPreviewState(RazerHeadsetAudioState audio, ThxAudioState thx)
    {
        ApplyState(audio);
        ApplyThxStatus(new ThxAudioStatus(ThxAudioAvailability.Available, null, thx));
        SetProbeState(RazerHeadsetProbeState.Ready);
        NotifyThxStatus();
    }

    internal Func<CancellationToken, Task<string?>>? PrepareAutoApplyProfileCheck(Action onWrite) =>
        PrepareAudioSetting(HeadsetAudioSetting.ThxSpatial, !ThxSpatialEnabled,
            value => ThxSpatialEnabled = value, _ => { onWrite(); return Task.CompletedTask; });
#endif

    // Only readback or a successful write may advance a group's confirmed value.
    private readonly Dictionary<HeadsetAudioSetting, object> _confirmedAudioSettings = [];
    private Action? _confirmAudioWrite;
    private Action? _confirmAudioRollback;
    private Func<bool>? _audioAutoApplyContextCurrent;

    private void ConfirmAudioSetting<T>(HeadsetAudioSetting setting, T value) where T : notnull =>
        _confirmedAudioSettings[setting] = value;

    private T ConfirmedAudioValue<T>(HeadsetAudioSetting setting, T fallback) =>
        _confirmedAudioSettings.TryGetValue(setting, out var confirmed) && confirmed is T value
            ? value : fallback;

    internal Func<CancellationToken, Task<string?>>? PrepareAutoApply(HeadsetAudioSetting setting) => setting switch
    {
        HeadsetAudioSetting.EqPreset => PrepareAudioSetting(setting,
            SelectedEqPresetIndex, value => SelectedEqPresetIndex = value, ApplyEqPresetAsync),
        HeadsetAudioSetting.EqBands => PrepareAudioSetting(setting,
            EqBands.Select(band => band.Level).ToArray(), value => RestoreAudioBands(EqBands, value), ApplyEqBandsAsync,
            static (left, right) => left.SequenceEqual(right)),
        HeadsetAudioSetting.MicEqBands => PrepareAudioSetting(setting,
            MicEqBands.Select(band => band.Level).ToArray(), value => RestoreAudioBands(MicEqBands, value), ApplyMicEqBandsAsync,
            static (left, right) => left.SequenceEqual(right)),
        HeadsetAudioSetting.Bass => PrepareAudioSetting(setting,
            FromFirmwareBass(ToFirmwareBass(Bass)), value => Bass = value, ApplyBassAsync),
        HeadsetAudioSetting.Microphone => PrepareAudioSetting(setting,
            (MicMuted, MicVolume), value => (MicMuted, MicVolume) = value, ApplyMicrophoneAsync),
        HeadsetAudioSetting.Source => PrepareAudioSetting(setting,
            SelectedSourceIndex, value => SelectedSourceIndex = value, ApplySourceAsync),
        HeadsetAudioSetting.Noise => PrepareAudioSetting(setting,
            NoiseSuppression, value => NoiseSuppression = value, ApplyNoiseSuppressionAsync),
        HeadsetAudioSetting.Anc => PrepareAudioSetting(setting,
            (SelectedAncIndex, AncLevel), value => (SelectedAncIndex, AncLevel) = value, ApplyAncAsync),
        HeadsetAudioSetting.Advanced => PrepareAudioSetting(setting,
            (Dnd, AutoPause, GestureControl, Dolby, NightMode, VoicePrompts), value => (Dnd, AutoPause, GestureControl, Dolby, NightMode, VoicePrompts) = value, ApplyAdvancedAsync),
        HeadsetAudioSetting.MicProcessing => PrepareAudioSetting(setting,
            (MicDeEsser, MicLimiterDb, SelectedMicReverbIndex, MicReverbIntensity), value => (MicDeEsser, MicLimiterDb, SelectedMicReverbIndex, MicReverbIntensity) = value, ApplyMicProcessingAsync),
        HeadsetAudioSetting.CamyT1 => PrepareAudioSetting(setting,
            (MicHpf, MicLimiterEnabled), value => (MicHpf, MicLimiterEnabled) = value, ApplyCamyT1Async),
        HeadsetAudioSetting.MicMonitoring => PrepareAudioSetting(setting,
            MicMonitoringEnabled, value => MicMonitoringEnabled = value, ApplyMicMonitoringAsync),
        HeadsetAudioSetting.ParametricEq when
            _confirmedAudioSettings.TryGetValue(setting, out var confirmedBand) &&
            confirmedBand is RazerParametricEqBand band && band.Index == ParametricEqBandIndex => PrepareAudioSetting(setting,
            CurrentParametricEq(), value => RestoreParametricEq(value), ApplyParametricEqAsync),
        HeadsetAudioSetting.Compressor => PrepareAudioSetting(setting,
            CurrentMicCompressor(), value => RestoreMicCompressor(value), ApplyMicCompressorAsync),
        HeadsetAudioSetting.Haptics => PrepareAudioSetting(setting,
            (HapticsEnabled, HapticsStrength), value => (HapticsEnabled, HapticsStrength) = value, ApplyHapticsAsync),
        HeadsetAudioSetting.AutoSleep => PrepareAudioSetting(setting,
            (AutoSleepEnabled, SelectedAutoSleepMinutesIndex), value => (AutoSleepEnabled, SelectedAutoSleepMinutesIndex) = value, ApplyAutoSleepAsync),
        HeadsetAudioSetting.ThxSpatial => PrepareAudioSetting(setting,
            ThxSpatialEnabled, value => ThxSpatialEnabled = value, ApplyThxSpatialAsync),
        HeadsetAudioSetting.ThxPreset when SelectedThxPresetId is { } presetId => PrepareAudioSetting(setting,
            presetId, SelectThxDraftPreset, ApplyThxPresetAsync),
        HeadsetAudioSetting.ThxBands => PrepareThxBandsAutoApply(),
        HeadsetAudioSetting.ThxNormalization => PrepareAudioSetting(setting,
            (ThxNormalizationEnabled, ThxNormalizationLevel), value => (ThxNormalizationEnabled, ThxNormalizationLevel) = value, ApplyThxNormalizationAsync),
        HeadsetAudioSetting.ThxVoiceClarity => PrepareAudioSetting(setting,
            (ThxVoiceClarityEnabled, ThxVoiceClarityLevel), value => (ThxVoiceClarityEnabled, ThxVoiceClarityLevel) = value, ApplyThxVoiceClarityAsync),
        HeadsetAudioSetting.ThxBassBoost => PrepareAudioSetting(setting,
            (ThxBassBoostEnabled, ThxBassBoostLevel), value => (ThxBassBoostEnabled, ThxBassBoostLevel) = value, ApplyThxBassBoostAsync),
        HeadsetAudioSetting.ThxSidetone => PrepareAudioSetting(setting,
            (ThxSidetoneEnabled, ThxSidetoneLevel), value => (ThxSidetoneEnabled, ThxSidetoneLevel) = value, ApplyThxSidetoneAsync),
        HeadsetAudioSetting.ThxCaptureNormalization => PrepareAudioSetting(setting,
            (ThxCaptureNormalizationEnabled, ThxCaptureNormalizationLevel), value => (ThxCaptureNormalizationEnabled, ThxCaptureNormalizationLevel) = value, ApplyThxCaptureNormalizationAsync),
        HeadsetAudioSetting.ThxCaptureVocalClarity => PrepareAudioSetting(setting,
            (ThxCaptureVocalClarityEnabled, ThxCaptureVocalClarityLevel), value => (ThxCaptureVocalClarityEnabled, ThxCaptureVocalClarityLevel) = value, ApplyThxCaptureVocalClarityAsync),
        HeadsetAudioSetting.ThxCaptureNoiseReduction => PrepareAudioSetting(setting,
            (ThxCaptureNoiseReductionEnabled, ThxCaptureNoiseReductionLevel), value => (ThxCaptureNoiseReductionEnabled, ThxCaptureNoiseReductionLevel) = value, ApplyThxCaptureNoiseReductionAsync),
        HeadsetAudioSetting.ThxCaptureVoiceGate => PrepareAudioSetting(setting,
            (ThxCaptureVoiceGateEnabled, ThxCaptureVoiceGateLevel), value => (ThxCaptureVoiceGateEnabled, ThxCaptureVoiceGateLevel) = value, ApplyThxCaptureVoiceGateAsync),
        _ => null,
    };

    private bool IsAudioSettingAvailable(HeadsetAudioSetting setting) => setting switch
    {
        HeadsetAudioSetting.EqPreset => !IsKrakenBase &&
            (!IsGip || RazerGipHeadsetProtocol.SupportsEqPreset(_connection.ProductId)) && EqPresetVisibility == Visibility.Visible,
        HeadsetAudioSetting.EqBands => !IsNommoV2Audio && EqBands.Count is 8 or 10 && EqBandsVisibility == Visibility.Visible,
        HeadsetAudioSetting.MicEqBands => IsValkyrie && MicEqBands.Count == 10 && MicEqBandsVisibility == Visibility.Visible,
        HeadsetAudioSetting.Bass => (IsFeature91 || IsProtocol25 || IsJolie) && BassVisibility == Visibility.Visible,
        HeadsetAudioSetting.Microphone => !IsJolie && !IsGip && !IsNxp && MicCardVisibility == Visibility.Visible,
        HeadsetAudioSetting.Source => IsFeature91 && AudioSourceVisibility == Visibility.Visible,
        HeadsetAudioSetting.Noise => (IsFeature91 || IsProtocol25) && NoiseSuppressionVisibility == Visibility.Visible,
        HeadsetAudioSetting.Anc => (IsFeature91 || IsMxicAnc || IsValkyrieAnc) && AncVisibility == Visibility.Visible,
        HeadsetAudioSetting.Advanced => (IsFeature91 || IsNxp || IsAvnera65 || IsMxicDnd) && AdvancedAudioVisibility == Visibility.Visible,
        HeadsetAudioSetting.MicProcessing => IsFeature91 && MicProcessingVisibility == Visibility.Visible,
        HeadsetAudioSetting.CamyT1 => _connection.Transport == RazerHeadsetAudioTransport.CamyT1 && MicDspVisibility == Visibility.Visible,
        HeadsetAudioSetting.MicMonitoring => MicMonitoringVisibility == Visibility.Visible,
        HeadsetAudioSetting.ParametricEq => IsFeature91 && ParametricEqVisibility == Visibility.Visible,
        HeadsetAudioSetting.Compressor => IsFeature91 && MicCompressorVisibility == Visibility.Visible,
        HeadsetAudioSetting.Haptics => HapticsVisibility == Visibility.Visible,
        HeadsetAudioSetting.AutoSleep => MakoXPowerVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxSpatial => ThxControlsVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxPreset => _thxState?.SelectedPresetId is not null && ThxControlsVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxBands => _thxState?.SelectedPresetId is not null && ThxBandsVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxNormalization => _thxState?.SoundNormalization is not null && ThxNormalizationVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxVoiceClarity => _thxState?.VoiceClarity is not null && ThxVoiceClarityVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxBassBoost => _thxState?.BassBoost is not null && ThxBassBoostVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxSidetone => _thxState?.Sidetone is not null && ThxSidetoneVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxCaptureNormalization => _thxState?.CaptureNormalization is not null && ThxCaptureNormalizationVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxCaptureVocalClarity => _thxState?.CaptureVocalClarity is not null && ThxCaptureVocalClarityVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxCaptureNoiseReduction => _thxState?.CaptureNoiseReduction is not null && ThxCaptureNoiseReductionVisibility == Visibility.Visible,
        HeadsetAudioSetting.ThxCaptureVoiceGate => _thxState?.CaptureVoiceGate is not null && ThxCaptureVoiceGateVisibility == Visibility.Visible,
        _ => false,
    };

    private Func<CancellationToken, Task<string?>>? PrepareAudioSetting<T>(
        HeadsetAudioSetting setting, T desired, Action<T> restoreDraft,
        Func<CancellationToken, Task> apply, Func<T, T, bool>? equals = null) where T : notnull
    {
        if (!IsAudioSettingAvailable(setting) ||
            !_confirmedAudioSettings.TryGetValue(setting, out var confirmed) || confirmed is not T previous ||
            (equals?.Invoke(desired, previous) ?? EqualityComparer<T>.Default.Equals(desired, previous)))
            return null;

        var profile = _getActiveProfile?.Invoke();
        bool IsCurrentProfile() => ReferenceEquals(profile, _getActiveProfile?.Invoke());
        var attempted = false;
        return async token =>
        {
            token.ThrowIfCancellationRequested();
            if (!IsCurrentProfile()) return AppStrings.Text("SettingAutoApplyCancelled");
            var thx = (int)setting >= (int)HeadsetAudioSetting.ThxSpatial;
            if (IsBusy || !IsAudioSettingAvailable(setting) || (thx ? !CanApplyThx : !CanEdit) ||
                !_confirmedAudioSettings.TryGetValue(setting, out var previousConfirmed))
                return AppStrings.Text("RazerHeadsetTransportUnavailable");
            // An explicit import/restore may have applied this value while the job waited.
            if (!attempted && previousConfirmed is T current &&
                (equals?.Invoke(desired, current) ?? EqualityComparer<T>.Default.Equals(desired, current)))
                return null;
            attempted = true;

            ErrorText = string.Empty;
            OperationStatusText = string.Empty;
            if (thx) _thxErrorText = string.Empty;
            restoreDraft(desired);
            var confirmsWrite = !thx && setting is not (HeadsetAudioSetting.EqPreset or
                HeadsetAudioSetting.EqBands or HeadsetAudioSetting.AutoSleep);
            if (confirmsWrite)
            {
                // The hardware acknowledgement precedes profile persistence and re-enabling controls.
                _confirmAudioWrite = () => ConfirmAudioSetting(setting, desired);
                _confirmAudioRollback = () => _confirmedAudioSettings[setting] = previousConfirmed;
            }
            _audioAutoApplyContextCurrent = IsCurrentProfile;
            try { await apply(token); }
            finally
            {
                _confirmAudioWrite = null;
                _confirmAudioRollback = null;
                _audioAutoApplyContextCurrent = null;
            }
            token.ThrowIfCancellationRequested();
            if (HasError) return ErrorText;
            if (OperationStatusText != AppStrings.Text("BlackSharkSaved"))
                return thx && !string.IsNullOrWhiteSpace(_thxErrorText)
                    ? _thxErrorText : AppStrings.Text("RazerHeadsetTransportUnavailable");

            return null;
        };
    }

    private static void RestoreAudioBands<T>(IList<T> bands, double[] values) where T : IEqualizerBand
    {
        if (bands.Count != values.Length || values.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException(AppStrings.Text("RazerHeadsetTransportUnavailable"));
        for (var index = 0; index < values.Length; index++) bands[index].Level = values[index];
    }

    private sealed record ThxBandDraft(string? PresetId, int[] Frequencies, double[] Levels);

    private string? SelectedThxPresetId =>
        _thxState?.Presets.ElementAtOrDefault(SelectedThxPresetIndex)?.Id;

    private void SelectThxDraftPreset(string presetId)
    {
        var index = _thxState?.Presets.ToList().FindIndex(preset => preset.Id == presetId) ?? -1;
        if (index < 0) throw new InvalidOperationException(AppStrings.Text("RazerHeadsetTransportUnavailable"));
        SelectedThxPresetIndex = index;
    }

    private ThxBandDraft CaptureThxBands() => new(
        SelectedThxPresetId,
        ThxBands.Select(band => band.FrequencyHertz).ToArray(),
        ThxBands.Select(band => band.Level).ToArray());

    private Func<CancellationToken, Task<string?>>? PrepareThxBandsAutoApply()
    {
        var draft = CaptureThxBands();
        return draft.PresetId is null ? null : PrepareAudioSetting(HeadsetAudioSetting.ThxBands,
            draft, RestoreThxBandDraft, token => ApplyCapturedThxBandsAsync(draft, token),
            static (left, right) => left.PresetId == right.PresetId &&
                left.Frequencies.SequenceEqual(right.Frequencies) && left.Levels.SequenceEqual(right.Levels));
    }

    private void RestoreThxBandDraft(ThxBandDraft draft)
    {
        if (!draft.Frequencies.SequenceEqual(ThxBands.Select(band => band.FrequencyHertz)))
            throw new InvalidOperationException(AppStrings.Text("RazerHeadsetTransportUnavailable"));
        RestoreAudioBands(ThxBands, draft.Levels);
    }

    private async Task ApplyCapturedThxBandsAsync(ThxBandDraft draft, CancellationToken token)
    {
        if (_thxState is not { } state || draft.PresetId is null ||
            !draft.Frequencies.SequenceEqual(ThxBands.Select(band => band.FrequencyHertz)))
            return;
        if (state.SelectedPresetId != draft.PresetId)
        {
            var index = state.Presets.ToList().FindIndex(preset => preset.Id == draft.PresetId);
            if (index < 0) return;
            SelectedThxPresetIndex = index;
            await ApplyThxPresetAsync(token);
            if (HasError || OperationStatusText != AppStrings.Text("BlackSharkSaved")) return;
            RestoreThxBandDraft(draft);
        }
        OperationStatusText = string.Empty;
        await ApplyThxBandsAsync(token);
    }

    private void AcceptAudioReadback(RazerHeadsetAudioState state)
    {
        if (state.MicBands is not null) ConfirmAudioSetting(HeadsetAudioSetting.MicEqBands, MicEqBands.Select(band => band.Level).ToArray());
        if (state.Bass is not null) ConfirmAudioSetting(HeadsetAudioSetting.Bass, Bass);
        if (state.MicMuted is not null || state.MicVolume is not null)
            ConfirmAudioSetting(HeadsetAudioSetting.Microphone, (MicMuted, MicVolume));
        if (state.AudioSource is not null) ConfirmAudioSetting(HeadsetAudioSetting.Source, SelectedSourceIndex);
        if (state.NoiseSuppression is not null) ConfirmAudioSetting(HeadsetAudioSetting.Noise, NoiseSuppression);
        if (state.AncState is not null) ConfirmAudioSetting(HeadsetAudioSetting.Anc, (SelectedAncIndex, AncLevel));
        if (state.Dnd is not null || state.AutoPause is not null || state.GestureControl is not null ||
            state.Dolby is not null || state.NightMode is not null || state.VoicePrompts is not null)
            ConfirmAudioSetting(HeadsetAudioSetting.Advanced, (Dnd, AutoPause, GestureControl, Dolby, NightMode, VoicePrompts));
        if (state.MicDeEsser is not null || state.MicLimiter is not null || state.MicReverb is not null)
            ConfirmAudioSetting(HeadsetAudioSetting.MicProcessing, (MicDeEsser, MicLimiterDb, SelectedMicReverbIndex, MicReverbIntensity));
        if (state.MicHpf is not null || state.MicLimiterEnabled is not null)
            ConfirmAudioSetting(HeadsetAudioSetting.CamyT1, (MicHpf, MicLimiterEnabled));
        if (state.MicReturnSwitch is not null || state.SidetonePercent is not null)
            ConfirmAudioSetting(HeadsetAudioSetting.MicMonitoring, MicMonitoringEnabled);
        if (state.MicCompressor is not null) ConfirmAudioSetting(HeadsetAudioSetting.Compressor, CurrentMicCompressor());
        if (state.Haptics is not null) ConfirmAudioSetting(HeadsetAudioSetting.Haptics, (HapticsEnabled, HapticsStrength));
    }

    private void AcceptThxAutoApplyReadback(ThxAudioState state)
    {
        ConfirmAudioSetting(HeadsetAudioSetting.ThxSpatial, state.SpatialEnabled);
        if (state.SoundNormalization is { } normalization)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxNormalization, (normalization.Enabled, normalization.LevelPercent));
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxNormalization);
        if (state.VoiceClarity is { } voiceClarity)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxVoiceClarity, (voiceClarity.Enabled, voiceClarity.LevelPercent));
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxVoiceClarity);
        if (state.BassBoost is { } bassBoost)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxBassBoost, (bassBoost.Enabled, bassBoost.LevelPercent));
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxBassBoost);
        if (state.Sidetone is { } sidetone)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxSidetone, (sidetone.Enabled, sidetone.LevelPercent));
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxSidetone);
        if (state.CaptureNormalization is { } captureNormalization)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxCaptureNormalization, (captureNormalization.Enabled, captureNormalization.LevelPercent));
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxCaptureNormalization);
        if (state.CaptureVocalClarity is { } captureVocalClarity)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxCaptureVocalClarity, (captureVocalClarity.Enabled, captureVocalClarity.LevelPercent));
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxCaptureVocalClarity);
        if (state.CaptureNoiseReduction is { } captureNoiseReduction)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxCaptureNoiseReduction, (captureNoiseReduction.Enabled, captureNoiseReduction.LevelPercent));
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxCaptureNoiseReduction);
        if (state.CaptureVoiceGate is { } captureVoiceGate)
            ConfirmAudioSetting(HeadsetAudioSetting.ThxCaptureVoiceGate, (captureVoiceGate.Enabled, captureVoiceGate.LevelPercent));
        else
            _confirmedAudioSettings.Remove(HeadsetAudioSetting.ThxCaptureVoiceGate);
    }
}
