using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class CoreAudioPartialEndpointTests
{
    [Fact]
    public void EndpointComFailureInvalidatesOnlyThatEndpointAndOtherErrorsRemainVisible()
    {
        var invalidations = 0;
        Assert.Null(WindowsCoreAudioMuteEventSource.ReadAvailableEndpoint(
            () => throw new COMException("capture endpoint missing"), () => invalidations++));
        Assert.True(WindowsCoreAudioMuteEventSource.ReadAvailableEndpoint(() => true, () => invalidations++));
        Assert.Equal(1, invalidations);
        Assert.Throws<InvalidOperationException>(() => WindowsCoreAudioMuteEventSource.ReadAvailableEndpoint(
            () => throw new InvalidOperationException("programming error"), () => invalidations++));
        Assert.Equal(1, invalidations);
    }

    [Fact]
    public void MissingMicrophoneDoesNotBlockSpeakerOrPublishFakeMicrophoneState()
    {
        using var reader = new FakeReader();
        reader.Set(new(false, false) { SpeakerAvailable = true, MicrophoneAvailable = false });
        var published = new ConcurrentQueue<BladeAudioMuteState>();
        var creations = 0;
        using var source = new WindowsCoreAudioMuteEventSource(published.Enqueue, () =>
        {
            Interlocked.Increment(ref creations);
            return reader;
        }, TimeSpan.FromMilliseconds(20));
        source.Start();
        Wait(() => published.Count == 1);
        Wait(() => source.LastError is not null);
        Assert.Equal(new(BladeAudioMuteTarget.Speaker, false), Assert.Single(published));
        reader.Set(new(true, false) { SpeakerAvailable = true, MicrophoneAvailable = false });
        Wait(() => published.Count == 2);
        Assert.DoesNotContain(published, item => item.Target == BladeAudioMuteTarget.Microphone);
        // Recovery publishes false even though the absent endpoint used false as a placeholder.
        reader.Set(new(true, false) { SpeakerAvailable = true, MicrophoneAvailable = true });
        Wait(() => published.Count == 3);
        Wait(() => source.LastError is null);
        Assert.Equal(new(BladeAudioMuteTarget.Microphone, false), published.Last());
        Assert.Equal(1, creations);
        source.Dispose();
        var count = published.Count;
        reader.Set(new(false, true));
        Assert.Equal(count, published.Count);
        Assert.True(reader.Disposed);
        Assert.Equal(0, reader.SubscriberCount);
    }

    [Fact]
    public void MissingSpeakerDoesNotBlockMicrophoneChangesAndPollingRecoversWithoutNotification()
    {
        using var reader = new FakeReader();
        reader.Set(new(false, false) { SpeakerAvailable = false, MicrophoneAvailable = true });
        var published = new ConcurrentQueue<BladeAudioMuteState>();
        using var source = new WindowsCoreAudioMuteEventSource(published.Enqueue, () => reader, TimeSpan.FromMilliseconds(20));
        source.Start();
        Wait(() => reader.ReadCount > 1);
        Assert.Empty(published);
        reader.Set(new(false, true) { SpeakerAvailable = false, MicrophoneAvailable = true });
        Wait(() => published.Count == 1);
        Assert.Equal(new(BladeAudioMuteTarget.Microphone, true), Assert.Single(published));
        reader.Set(new(false, true) { SpeakerAvailable = true, MicrophoneAvailable = true }, signal: false);
        Wait(() => published.Count == 2);
        Assert.Equal(new(BladeAudioMuteTarget.Speaker, false), published.Last());
        Assert.False(reader.Disposed);
    }

    private static void Wait(Func<bool> condition) => Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(3)));

    private sealed class FakeReader : IWindowsAudioMuteSnapshotReader
    {
        private readonly object _gate = new();
        private WindowsAudioMuteSnapshot _snapshot;
        private int _readCount;
        public event Action? Changed;
        internal int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
        internal int ReadCount => Volatile.Read(ref _readCount);
        internal bool Disposed { get; private set; }
        internal void Set(WindowsAudioMuteSnapshot snapshot, bool signal = true)
        {
            lock (_gate) _snapshot = snapshot;
            if (signal) Changed?.Invoke();
        }
        public WindowsAudioMuteSnapshot Read()
        {
            lock (_gate)
            {
                Interlocked.Increment(ref _readCount);
                return _snapshot;
            }
        }
        public void Dispose() => Disposed = true;
    }
}
