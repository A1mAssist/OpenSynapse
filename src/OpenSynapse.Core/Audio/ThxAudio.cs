namespace OpenSynapse.Core.Audio;

public enum ThxAudioEndpoint
{
    Speakers,
    Headphones,
}

public enum ThxAudioAvailability
{
    Disabled,
    UnsupportedModel,
    ComponentsUnavailable,
    ControllerActive,
    Available,
    AvailableReadOnly,
}

public enum ThxAudioEnhancementKind
{
    VoiceClarity,
    SoundNormalization,
    Sidetone,
}

public sealed record ThxAudioPreset(string Id, string Name);

public sealed record ThxAudioBand(int FrequencyHertz, double GainDb);

public sealed record ThxAudioEnhancement(bool Enabled, int LevelPercent);

public sealed record ThxAudioState(
    ThxAudioEndpoint Endpoint,
    bool SpatialEnabled,
    bool EqualizerEnabled,
    string? SelectedPresetId,
    IReadOnlyList<ThxAudioPreset> Presets,
    IReadOnlyList<ThxAudioBand> EqualizerBands,
    IReadOnlyDictionary<string, IReadOnlyList<double>> FactoryCurves,
    ThxAudioEnhancement? SoundNormalization,
    ThxAudioEnhancement VoiceClarity,
    bool CanEdit,
    ThxAudioEnhancement? Sidetone = null);

public sealed record ThxAudioStatus(
    ThxAudioAvailability Availability,
    string? Error,
    ThxAudioState? State)
{
    public bool IsAvailable => Availability is
        ThxAudioAvailability.Available or ThxAudioAvailability.AvailableReadOnly;
}

public interface IThxAudioController
{
    Task<ThxAudioStatus> ReadAsync(ThxAudioEndpoint endpoint, CancellationToken cancellationToken = default);

    Task<ThxAudioStatus> SetSpatialAsync(ThxAudioEndpoint endpoint, bool enabled, CancellationToken cancellationToken = default);

    Task<ThxAudioStatus> SetEqualizerAsync(ThxAudioEndpoint endpoint, bool enabled, CancellationToken cancellationToken = default);

    Task<ThxAudioStatus> SelectPresetAsync(
        ThxAudioEndpoint endpoint,
        string presetId,
        IReadOnlyList<double> gainsDb,
        CancellationToken cancellationToken = default);

    Task<ThxAudioStatus> SetEnhancementAsync(
        ThxAudioEndpoint endpoint,
        ThxAudioEnhancementKind kind,
        bool enabled,
        int? levelPercent,
        CancellationToken cancellationToken = default);

    Task<ThxAudioStatus> InstallNativeComponentAsync(
        ThxAudioEndpoint endpoint,
        CancellationToken cancellationToken = default);
}
