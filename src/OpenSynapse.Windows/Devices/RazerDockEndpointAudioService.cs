namespace OpenSynapse.Windows.Devices;

public sealed record WindowsPlaybackAudioState(string EndpointId, int VolumePercent, bool Muted);

internal sealed class WindowsPlaybackEndpointChangedException() : InvalidOperationException(
    "The default playback device changed. Refresh the audio controls before applying.") { }

internal interface IWindowsPlaybackEndpoint : IDisposable
{
    WindowsPlaybackAudioState ReadPlayback();
    void SetPlaybackVolume(int percent);
    void SetPlaybackMute(bool muted);
}

/// <summary>
/// The MappingEngine dock bundles control the Windows default multimedia playback endpoint.
/// No HID audio commands or device-specific microphone capabilities are implied.
/// </summary>
public sealed class RazerDockEndpointAudioService
{
    private readonly Func<IWindowsPlaybackEndpoint> _openEndpoint;

    public static bool SupportsProduct(ushort productId) => productId is 0x0F52 or 0x0F59 or 0x0F5A or 0x0F5C;

    public RazerDockEndpointAudioService(ushort productId)
        : this(productId, WindowsCoreAudioMuteEventSource.OpenPlaybackEndpoint) { }

    internal RazerDockEndpointAudioService(ushort productId, Func<IWindowsPlaybackEndpoint> openEndpoint)
    {
        if (!SupportsProduct(productId)) throw new NotSupportedException($"PID {productId:X4} has no verified default-playback route.");
        _openEndpoint = openEndpoint ?? throw new ArgumentNullException(nameof(openEndpoint));
    }

    public Task<WindowsPlaybackAudioState> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var endpoint = _openEndpoint();
            return endpoint.ReadPlayback();
        }, cancellationToken);

    public Task<WindowsPlaybackAudioState> ApplyAsync(
        string expectedEndpointId, int? volumePercent, bool? muted,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEndpointId);
        if (volumePercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(volumePercent));
        return Task.Run(() =>
        {
            using var endpoint = _openEndpoint();
            var before = endpoint.ReadPlayback();
            if (!StringComparer.OrdinalIgnoreCase.Equals(before.EndpointId, expectedEndpointId))
                throw new WindowsPlaybackEndpointChangedException();
            cancellationToken.ThrowIfCancellationRequested();
            if (volumePercent is { } volume && volume != before.VolumePercent) endpoint.SetPlaybackVolume(volume);
            if (muted is { } mute && mute != before.Muted) endpoint.SetPlaybackMute(mute);
            var actual = endpoint.ReadPlayback();
            if ((volumePercent is { } requestedVolume && actual.VolumePercent != requestedVolume) ||
                (muted is { } requestedMute && actual.Muted != requestedMute))
                throw new InvalidOperationException("Windows playback volume or mute readback did not match.");
            return actual;
        }, cancellationToken);
    }
}
