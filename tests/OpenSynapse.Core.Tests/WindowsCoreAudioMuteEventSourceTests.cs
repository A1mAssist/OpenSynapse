using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsCoreAudioMuteEventSourceTests
{
    [Fact]
    public void PublishesSpeakerAndMicrophoneMuteChangesThenStops()
    {
        var reader = new FakeReader(
            new(false, true),
            new(false, true),
            new(true, false));
        var published = new List<BladeAudioMuteState>();
        using (var source = new WindowsCoreAudioMuteEventSource(
            state =>
            {
                lock (published)
                {
                    published.Add(state);
                }
            },
            () => reader,
            TimeSpan.FromMilliseconds(1)))
        {
            source.Start();
            Assert.True(SpinWait.SpinUntil(() =>
            {
                lock (published)
                {
                    return published.Count >= 4;
                }
            }, TimeSpan.FromSeconds(2)));
        }

        BladeAudioMuteState[] snapshot;
        lock (published)
        {
            snapshot = published.ToArray();
        }
        Assert.Equal(
            [
                new(BladeAudioMuteTarget.Speaker, false),
                new(BladeAudioMuteTarget.Microphone, true),
                new(BladeAudioMuteTarget.Speaker, true),
                new(BladeAudioMuteTarget.Microphone, false),
            ],
            snapshot);
        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task ReportsRepeatedCoreAudioFailureOnce()
    {
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new WindowsCoreAudioMuteEventSource(
            _ => { },
            static () => throw new InvalidOperationException("capture unavailable"),
            TimeSpan.FromMilliseconds(1));
        var count = 0;
        source.ReadFailed += exception =>
        {
            Interlocked.Increment(ref count);
            failed.TrySetResult(exception);
        };

        source.Start();
        var exception = await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(20);

        Assert.Equal("capture unavailable", exception.Message);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    private sealed class FakeReader(params WindowsAudioMuteSnapshot[] snapshots)
        : IWindowsAudioMuteSnapshotReader
    {
        private int _index;

        internal bool Disposed { get; private set; }

        public WindowsAudioMuteSnapshot Read()
        {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, snapshots.Length - 1);
            return snapshots[index];
        }

        public void Dispose() => Disposed = true;
    }
}
