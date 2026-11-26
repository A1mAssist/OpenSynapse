namespace OpenSynapse.Core.Profiles;

public sealed class HeadsetProfileSettings
{
    public int? VolumePercent { get; set; }
    public int? SideTone { get; set; }
    public bool? GameModeEnabled { get; set; }
    public int? GameChatBalance { get; set; }
    public bool? AncEnabled { get; set; }
    public int? AncModeIndex { get; set; }
    public int? AncLevel { get; set; }
    public bool? ThxEnabled { get; set; }
    public bool? ThxSpatialEnabled { get; set; }
    public bool? ThxEqualizerEnabled { get; set; }
    public string? ThxPreset { get; set; }
    public Dictionary<string, double>? ThxBands { get; set; }
    public Dictionary<string, Dictionary<string, double>>? ThxBandsByPreset { get; set; }
    public bool? ThxNormalizationEnabled { get; set; }
    public int? ThxNormalizationLevel { get; set; }
    public bool? ThxVoiceClarityEnabled { get; set; }
    public int? ThxVoiceClarityLevel { get; set; }
    public bool? ThxBassBoostEnabled { get; set; }
    public int? ThxBassBoostLevel { get; set; }
    public bool? ThxSidetoneEnabled { get; set; }
    public int? ThxSidetoneLevel { get; set; }
    public bool? ThxCaptureNormalizationEnabled { get; set; }
    public int? ThxCaptureNormalizationLevel { get; set; }
    public bool? ThxCaptureVocalClarityEnabled { get; set; }
    public int? ThxCaptureVocalClarityLevel { get; set; }
    public bool? ThxCaptureNoiseReductionEnabled { get; set; }
    public int? ThxCaptureNoiseReductionLevel { get; set; }
    public bool? ThxCaptureVoiceGateEnabled { get; set; }
    public int? ThxCaptureVoiceGateLevel { get; set; }
    public string? EqPreset { get; set; }
    public Dictionary<string, int>? EqBands { get; set; }
    public Dictionary<string, int>? EqBandsTenths { get; set; }
    public string? MicEqPreset { get; set; }
    public Dictionary<string, int>? MicEqBands { get; set; }
    public int? Bass { get; set; }
    public int? MicrophoneVolumePercent { get; set; }
    public bool? MicrophoneMute { get; set; }
    public bool? NoiseSuppression { get; set; }
    public int? AudioSourceIndex { get; set; }
    public bool? Dolby { get; set; }
    public bool? NightMode { get; set; }
    public bool? VoicePrompts { get; set; }
    public int? MicDeEsser { get; set; }
    public int? MicLimiterDb { get; set; }
    public int? MicReverbIndex { get; set; }
    public int? MicReverbIntensity { get; set; }
    public bool? MicCompressorEnabled { get; set; }
    public int? MicCompressorProfile { get; set; }
    public int? MicCompressorDb { get; set; }
    public int? MicCompressorRatio { get; set; }
    public int? MicCompressorAttack { get; set; }
    public int? MicCompressorRelease { get; set; }
    public bool? MicMonitoringEnabled { get; set; }
    public bool? ParametricEqEnabled { get; set; }
    public int? ParametricEqBandIndex { get; set; }
    public int? ParametricEqFrequency { get; set; }
    public int? ParametricEqGain { get; set; }
    public int? ParametricEqQ { get; set; }
    public int? ParametricEqProfile { get; set; }
    public int? ParametricEqDirection { get; set; }
    public int? ParametricEqFilterType { get; set; }
    public int? HapticsStrength { get; set; }
    public bool? HapticsEnabled { get; set; }
    public bool? AutoPause { get; set; }
    public int? AutoShutdownMinutes { get; set; }
    public bool? AutoSleepEnabled { get; set; }
    public bool? Dnd { get; set; }
    public bool? GestureControl { get; set; }
    public int? ChromaBrightness { get; set; }
    public string? ChromaEffect { get; set; }
    public Dictionary<string, string>? ChromaParameters { get; set; }

    internal void ApplySafeDefaults()
    {
        MouseProfileSettings.ValidatePercent(VolumePercent, nameof(VolumePercent));
        MouseProfileSettings.ValidatePercent(MicrophoneVolumePercent, nameof(MicrophoneVolumePercent));
        MouseProfileSettings.ValidatePercent(GameChatBalance, nameof(GameChatBalance));
        MouseProfileSettings.ValidatePercent(SideTone, nameof(SideTone));
        MouseProfileSettings.ValidatePercent(Bass, nameof(Bass));
        MouseProfileSettings.ValidateRange(AncModeIndex, 0, 2, nameof(AncModeIndex));
        MouseProfileSettings.ValidateRange(AncLevel, 0, 10, nameof(AncLevel));
        MouseProfileSettings.ValidatePercent(ThxNormalizationLevel, nameof(ThxNormalizationLevel));
        MouseProfileSettings.ValidatePercent(ThxVoiceClarityLevel, nameof(ThxVoiceClarityLevel));
        MouseProfileSettings.ValidatePercent(ThxBassBoostLevel, nameof(ThxBassBoostLevel));
        MouseProfileSettings.ValidatePercent(ThxSidetoneLevel, nameof(ThxSidetoneLevel));
        MouseProfileSettings.ValidatePercent(
            ThxCaptureNormalizationLevel, nameof(ThxCaptureNormalizationLevel));
        MouseProfileSettings.ValidatePercent(
            ThxCaptureVocalClarityLevel, nameof(ThxCaptureVocalClarityLevel));
        MouseProfileSettings.ValidatePercent(
            ThxCaptureNoiseReductionLevel, nameof(ThxCaptureNoiseReductionLevel));
        MouseProfileSettings.ValidatePercent(
            ThxCaptureVoiceGateLevel, nameof(ThxCaptureVoiceGateLevel));
        MouseProfileSettings.ValidateRange(AudioSourceIndex, 0, 4, nameof(AudioSourceIndex));
        MouseProfileSettings.ValidatePercent(MicDeEsser, nameof(MicDeEsser));
        MouseProfileSettings.ValidateRange(MicLimiterDb, -60, 0, nameof(MicLimiterDb));
        MouseProfileSettings.ValidateRange(MicReverbIndex, 0, 3, nameof(MicReverbIndex));
        MouseProfileSettings.ValidatePercent(MicReverbIntensity, nameof(MicReverbIntensity));
        MouseProfileSettings.ValidateRange(MicCompressorProfile, 0, byte.MaxValue, nameof(MicCompressorProfile));
        MouseProfileSettings.ValidateRange(MicCompressorDb, -128, 0, nameof(MicCompressorDb));
        MouseProfileSettings.ValidateRange(MicCompressorRatio, 0, ushort.MaxValue, nameof(MicCompressorRatio));
        MouseProfileSettings.ValidateRange(MicCompressorAttack, 0, ushort.MaxValue, nameof(MicCompressorAttack));
        MouseProfileSettings.ValidateRange(MicCompressorRelease, 0, ushort.MaxValue, nameof(MicCompressorRelease));
        MouseProfileSettings.ValidateRange(ParametricEqBandIndex, 0, 9, nameof(ParametricEqBandIndex));
        MouseProfileSettings.ValidateRange(ParametricEqFrequency, 0, ushort.MaxValue, nameof(ParametricEqFrequency));
        MouseProfileSettings.ValidateRange(ParametricEqGain, -128, 127, nameof(ParametricEqGain));
        MouseProfileSettings.ValidateRange(ParametricEqQ, 0, byte.MaxValue, nameof(ParametricEqQ));
        MouseProfileSettings.ValidateRange(ParametricEqProfile, 0, byte.MaxValue, nameof(ParametricEqProfile));
        MouseProfileSettings.ValidateRange(ParametricEqDirection, 0, byte.MaxValue, nameof(ParametricEqDirection));
        MouseProfileSettings.ValidateRange(ParametricEqFilterType, 0, byte.MaxValue, nameof(ParametricEqFilterType));
        MouseProfileSettings.ValidateRange(HapticsStrength, 0, ushort.MaxValue, nameof(HapticsStrength));
        MouseProfileSettings.ValidateNonNegative(AutoShutdownMinutes, nameof(AutoShutdownMinutes));
        MouseProfileSettings.ValidatePercent(ChromaBrightness, nameof(ChromaBrightness));
        EqBands = NormalizeBands(EqBands, nameof(EqBands));
        if (EqBandsTenths is not null)
        {
            foreach (var value in EqBandsTenths.Values)
                MouseProfileSettings.ValidateRange(value, -128, 127, nameof(EqBandsTenths));
            EqBandsTenths = new Dictionary<string, int>(EqBandsTenths, StringComparer.OrdinalIgnoreCase);
        }
        ThxBands = NormalizeThxBands(ThxBands);
        ThxBandsByPreset = NormalizeBandsByPreset(ThxBandsByPreset);
        MicEqBands = NormalizeBands(MicEqBands, nameof(MicEqBands));
        ChromaParameters = ChromaParameters is null
            ? null
            : ProfileDictionary.Normalize(ChromaParameters);
    }

    private static Dictionary<string, int>? NormalizeBands(
        IReadOnlyDictionary<string, int>? source,
        string name)
    {
        if (source is null)
        {
            return null;
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }
            if (value is < -24 or > 100)
            {
                throw new InvalidDataException($"{name} values must be between -24 and 100.");
            }
            result[key] = value;
        }
        return result;
    }

    private static Dictionary<string, double>? NormalizeThxBands(IReadOnlyDictionary<string, double>? source)
    {
        if (source is null) return null;
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!double.IsFinite(value) || value is < -24 or > 100)
                throw new InvalidDataException("THX band gains must be finite and between -24 and 100.");
            result[key] = value;
        }
        return result;
    }

    private static Dictionary<string, Dictionary<string, double>>? NormalizeBandsByPreset(
        IReadOnlyDictionary<string, Dictionary<string, double>>? source)
    {
        if (source is null)
        {
            return null;
        }

        var result = new Dictionary<string, Dictionary<string, double>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (preset, bands) in source)
        {
            if (string.IsNullOrWhiteSpace(preset) ||
                NormalizeThxBands(bands) is not { } normalized)
            {
                continue;
            }
            result[preset] = normalized;
        }
        return result;
    }
}
