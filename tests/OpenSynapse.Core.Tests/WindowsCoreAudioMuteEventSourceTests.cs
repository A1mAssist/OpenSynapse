using System.Collections.Concurrent;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class WindowsCoreAudioMuteEventSourceTests
{
    [Fact]
    public void RetriesFailedInitializationAndReportsFailure()
    {
        var reader = new FakeReader();
        var attempts = 0;
        var failures = 0;
        using var source = new WindowsCoreAudioMuteEventSource(
            _ => { },
            () => Interlocked.Increment(ref attempts) == 1
                ? throw new InvalidOperationException("endpoint unavailable")
                : reader,
            TimeSpan.FromMilliseconds(20));
        source.ReadFailed += _ => Interlocked.Increment(ref failures);

        source.Start();
        Assert.True(SpinWait.SpinUntil(() => reader.ReadCount == 1, TimeSpan.FromSeconds(2)));
        Assert.Equal(2, attempts);
        Assert.Equal(1, failures);
        Assert.Null(source.LastError);
    }

    [Fact]
    public void ReadsInitiallyAndOnlyWhenEndpointOrMuteChanges()
    {
        var reader = new FakeReader();
        var published = new ConcurrentQueue<BladeAudioMuteState>();
        using var source = new WindowsCoreAudioMuteEventSource(
            published.Enqueue,
            () => reader,
            TimeSpan.FromMilliseconds(20));

        source.Start();
        Assert.True(SpinWait.SpinUntil(() => published.Count == 2, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, reader.ReadCount);

        Thread.Sleep(100);
        Assert.Equal(1, reader.ReadCount);

        reader.Snapshot = new WindowsAudioMuteSnapshot(true, false);
        reader.RaiseChanged();
        Assert.True(SpinWait.SpinUntil(() => published.Count == 3, TimeSpan.FromSeconds(2)));
        Assert.Equal(new BladeAudioMuteState(BladeAudioMuteTarget.Speaker, true), published.Last());

        reader.Snapshot = new WindowsAudioMuteSnapshot(true, true);
        reader.RaiseChanged();
        Assert.True(SpinWait.SpinUntil(() => published.Count == 4, TimeSpan.FromSeconds(2)));
        Assert.Equal(new BladeAudioMuteState(BladeAudioMuteTarget.Microphone, true), published.Last());

        source.Dispose();
        var readsAfterStop = reader.ReadCount;
        reader.RaiseChanged();
        Assert.Equal(readsAfterStop, reader.ReadCount);
        Assert.True(reader.Disposed);
    }

    [Fact]
    public void RefreshRepublishesBothCurrentStates()
    {
        var reader = new FakeReader
        {
            Snapshot = new WindowsAudioMuteSnapshot(true, true),
        };
        var published = new ConcurrentQueue<BladeAudioMuteState>();
        using var source = new WindowsCoreAudioMuteEventSource(
            published.Enqueue,
            () => reader,
            TimeSpan.FromMilliseconds(20));

        source.Start();
        Assert.True(SpinWait.SpinUntil(() => published.Count == 2, TimeSpan.FromSeconds(2)));
        source.Refresh();
        Assert.True(SpinWait.SpinUntil(() => published.Count == 4, TimeSpan.FromSeconds(2)));
        Assert.Equal(2, reader.ReadCount);
    }

    private sealed class FakeReader : IWindowsAudioMuteSnapshotReader
    {
        private int _readCount;

        public event Action? Changed;
        public WindowsAudioMuteSnapshot Snapshot { get; set; }
        public int ReadCount => Volatile.Read(ref _readCount);
        public bool Disposed { get; private set; }

        public WindowsAudioMuteSnapshot Read()
        {
            Interlocked.Increment(ref _readCount);
            return Snapshot;
        }

        public void RaiseChanged() => Changed?.Invoke();
        public void Dispose() => Disposed = true;
    }
}
