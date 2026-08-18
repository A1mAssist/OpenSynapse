using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenSynapse.Windows.Lighting;

namespace OpenSynapse.Core.Tests;

public sealed class WasapiAudioMeterAdapterTests
{
    [Fact]
    public void ComputesChannelIndependentRmsAndPeak()
    {
        var format = new AudioSampleFormat(AudioSampleEncoding.IeeeFloat, 2, 32, 32, 8);
        var data = FloatBytes(0.5f, -1f, 0f, 0.5f);

        var sample = WasapiAudioMeterAdapter.ComputeSample(data, format, 2);

        Assert.Equal(Math.Sqrt(0.375), sample.Rms, 6);
        Assert.Equal(1, sample.Peak, 6);
    }

    [Fact]
    public void HandlesPcmSilenceAndClipping()
    {
        var format = new AudioSampleFormat(AudioSampleEncoding.Pcm, 1, 16, 16, 2);
        var silence = new byte[4];
        var clipping = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(clipping, short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(clipping[2..], short.MaxValue);

        Assert.Equal(AudioMeterSample.Silence, WasapiAudioMeterAdapter.ComputeSample(silence, format, 2));
        var clipped = WasapiAudioMeterAdapter.ComputeSample(clipping, format, 2);
        Assert.Equal(Math.Sqrt(0.5), clipped.Rms, 6);
        Assert.Equal(1, clipped.Peak, 6);
    }

    [Fact]
    public void ClampsInvalidAndOutOfRangeSamples()
    {
        var format = new AudioSampleFormat(AudioSampleEncoding.IeeeFloat, 1, 32, 32, 4);
        var data = FloatBytes(float.NaN, 2f);

        var sample = WasapiAudioMeterAdapter.ComputeSample(data, format, 2);

        Assert.Equal(1, sample.Rms, 6);
        Assert.Equal(1, sample.Peak, 6);
        Assert.Equal(0, WasapiAudioMeterAdapter.NormalizeLevel(double.NaN));
        Assert.Equal(0, WasapiAudioMeterAdapter.NormalizeLevel(double.NegativeInfinity));
    }

    [Fact]
    public async Task ReopensAfterCaptureInvalidationAndStopsOwnedSession()
    {
        var first = new FakeSession("first", [new AudioMeterSample(0.2, 0.25)], failFirstRead: true);
        var recovered = new FakeSession("first", [new AudioMeterSample(0.6, 0.8)]);
        var sessions = new Queue<IAudioLoopbackSession>([first, recovered]);
        var adapter = new WasapiAudioMeterAdapter(
            () => "first",
            _ => sessions.Dequeue(),
            static () => DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(1));

        await adapter.StartAsync(CancellationToken.None);
        await EventuallyAsync(() => adapter.ReadSample().Peak >= 0.8);
        await adapter.StopAsync();

        Assert.True(first.Disposed);
        Assert.True(recovered.Disposed);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task ReplacesSessionWhenDefaultEndpointChanges()
    {
        var endpoint = "first";
        var first = new FakeSession("first", [new AudioMeterSample(0.1, 0.2)]);
        var second = new FakeSession("second", [new AudioMeterSample(0.4, 0.7)]);
        var sessions = new Queue<IAudioLoopbackSession>([first, second]);
        var adapter = new WasapiAudioMeterAdapter(
            () => endpoint,
            _ => sessions.Dequeue(),
            static () => DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(1));

        await adapter.StartAsync(CancellationToken.None);
        await EventuallyAsync(() => adapter.ReadSample().Peak >= 0.2);
        endpoint = "second";
        await EventuallyAsync(() => adapter.ReadSample().Peak >= 0.7);
        await adapter.DisposeAsync();

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task PublishesSilenceWhileEndpointIsRemovedThenRecovers()
    {
        var removed = 0;
        var first = new FakeSession("same", [new AudioMeterSample(0.3, 0.4)]);
        var recovered = new FakeSession("same", [new AudioMeterSample(0.8, 0.9)]);
        var sessions = new Queue<IAudioLoopbackSession>([first, recovered]);
        var adapter = new WasapiAudioMeterAdapter(
            () => Volatile.Read(ref removed) != 0 ? throw new COMException("no default endpoint") : "same",
            _ => sessions.Dequeue(),
            static () => DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(1));

        await adapter.StartAsync(CancellationToken.None);
        await EventuallyAsync(() => adapter.ReadSample().Peak >= 0.4);
        Volatile.Write(ref removed, 1);
        first.Invalidate();
        await EventuallyAsync(() => adapter.ReadSample().Peak == 0);
        Volatile.Write(ref removed, 0);
        await EventuallyAsync(() => adapter.ReadSample().Peak >= 0.9);
        await adapter.DisposeAsync();

        Assert.True(first.Disposed);
        Assert.True(recovered.Disposed);
    }

    [Fact]
    public async Task KeepsOnlyTheLatestCapturedSample()
    {
        var session = new FakeSession(
            "same",
            Enumerable.Range(1, 100).Select(value => new AudioMeterSample(value / 100d, value / 100d)));
        var adapter = new WasapiAudioMeterAdapter(
            () => "same",
            _ => session,
            static () => DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(1));

        await adapter.StartAsync(CancellationToken.None);
        Assert.True(session.Drained.Wait(TimeSpan.FromSeconds(1)));

        Assert.Equal(1, adapter.ReadSample().Peak);
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task IgnoresCaptureInvalidationAfterStopRequested()
    {
        var session = new StopRaceSession();
        var adapter = new WasapiAudioMeterAdapter(
            () => "same",
            _ => session,
            static () => DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(1));

        await adapter.StartAsync(CancellationToken.None);
        Assert.True(session.ReadStarted.Wait(TimeSpan.FromSeconds(1)));
        var stop = adapter.StopAsync().AsTask();
        session.ReleaseRead.Set();

        await stop;
        Assert.True(session.Disposed);
        await adapter.DisposeAsync();
    }

    private static byte[] FloatBytes(params float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float)),
                BitConverter.SingleToInt32Bits(values[index]));
        }
        return bytes;
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(5);
        }
        Assert.Fail("Audio Meter 没有在规定时间内产生预期样本。");
    }

    private sealed class FakeSession(
        string endpointId,
        Queue<AudioMeterSample> samples,
        bool failFirstRead = false) : IAudioLoopbackSession
    {
        public FakeSession(string endpointId, IEnumerable<AudioMeterSample> samples, bool failFirstRead = false)
            : this(endpointId, new Queue<AudioMeterSample>(samples), failFirstRead)
        {
        }

        public string EndpointId { get; } = endpointId;
        public bool Disposed { get; private set; }
        public ManualResetEventSlim Drained { get; } = new();
        private bool _failFirstRead = failFirstRead;
        private int _invalidated;

        public void Invalidate() => Interlocked.Exchange(ref _invalidated, 1);

        public bool TryReadSample(out AudioMeterSample sample)
        {
            if (_failFirstRead || Interlocked.Exchange(ref _invalidated, 0) != 0)
            {
                _failFirstRead = false;
                throw new COMException("device invalidated");
            }
            if (samples.TryDequeue(out sample))
            {
                return true;
            }
            Drained.Set();
            sample = default;
            return false;
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class StopRaceSession : IAudioLoopbackSession
    {
        public string EndpointId => "same";
        public bool Disposed { get; private set; }
        public ManualResetEventSlim ReadStarted { get; } = new();
        public ManualResetEventSlim ReleaseRead { get; } = new();

        public bool TryReadSample(out AudioMeterSample sample)
        {
            sample = default;
            ReadStarted.Set();
            Assert.True(ReleaseRead.Wait(TimeSpan.FromSeconds(1)));
            throw new COMException("resources invalidated", unchecked((int)0x88890008));
        }

        public void Dispose() => Disposed = true;
    }
}
