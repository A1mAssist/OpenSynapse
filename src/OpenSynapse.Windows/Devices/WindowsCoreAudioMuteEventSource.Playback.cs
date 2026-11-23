namespace OpenSynapse.Windows.Devices;

public sealed partial class WindowsCoreAudioMuteEventSource
{
    internal static IWindowsPlaybackEndpoint OpenPlaybackEndpoint() => new CoreAudioMuteSnapshotReader(observe: false);

    // Reuse the existing COM lifetime and interfaces; bind one endpoint for the whole operation.
    private sealed partial class CoreAudioMuteSnapshotReader : IWindowsPlaybackEndpoint
    {
        private string? _playbackEndpointId;

        public WindowsPlaybackAudioState ReadPlayback()
        {
            if (_speaker is null)
            {
                var enumerator = _enumerator ?? throw new ObjectDisposedException(nameof(CoreAudioMuteSnapshotReader));
                IMMDevice? device = null;
                try
                {
                    ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out device));
                    ThrowIfFailed(device.GetId(out var id));
                    var iid = AudioEndpointVolumeInterfaceId;
                    ThrowIfFailed(device.Activate(ref iid, ClsctxAll, 0, out var value));
                    _speaker = (IAudioEndpointVolume)value;
                    _playbackEndpointId = id;
                }
                finally { Release(device); }
            }
            ThrowIfFailed(_speaker.GetMasterVolumeLevelScalar(out var scalar));
            ThrowIfFailed(_speaker.GetMute(out var muted));
            return new(_playbackEndpointId!, (int)Math.Floor(scalar * 100d + 0.5), muted);
        }

        public void SetPlaybackVolume(int percent)
        {
            _ = ReadPlayback();
            ThrowIfFailed(_speaker!.SetMasterVolumeLevelScalar(percent / 100f, 0));
        }

        public void SetPlaybackMute(bool muted)
        {
            _ = ReadPlayback();
            ThrowIfFailed(_speaker!.SetMute(muted, 0));
        }
    }
}
