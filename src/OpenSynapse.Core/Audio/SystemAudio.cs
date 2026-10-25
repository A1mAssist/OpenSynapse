namespace OpenSynapse.Core.Audio;

public enum SystemAudioDirection
{
    Render,
    Capture,
}

public sealed record SystemAudioEndpoint(
    string Id,
    string Name,
    SystemAudioDirection Direction,
    bool IsMuted,
    int VolumePercent,
    bool IsDefault);

public sealed record SystemAudioSnapshot(
    IReadOnlyList<SystemAudioEndpoint> RenderEndpoints,
    IReadOnlyList<SystemAudioEndpoint> CaptureEndpoints);

/// <summary>
/// Windows default-endpoint and volume control. Implementations must keep the
/// caller's previous endpoint intact until a new endpoint write succeeds.
/// </summary>
public interface ISystemAudioController
{
    Task<SystemAudioSnapshot> ReadAsync(CancellationToken cancellationToken = default);

    Task SetDefaultAsync(
        string endpointId,
        CancellationToken cancellationToken = default);

    Task SetVolumeAsync(
        string endpointId,
        int volumePercent,
        CancellationToken cancellationToken = default);

    Task SetMuteAsync(
        string endpointId,
        bool muted,
        CancellationToken cancellationToken = default);
}
