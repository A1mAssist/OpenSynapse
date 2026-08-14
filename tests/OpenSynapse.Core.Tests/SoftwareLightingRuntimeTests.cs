using System.Diagnostics;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class SoftwareLightingRuntimeTests
{
    [Fact]
    public async Task PublishesOneFramePerCadenceAndStopsWithoutASecondRender()
    {
        var transport = new RuntimeTransport();
        var restored = 0;
        await using var pump = new BladeMatrixFramePump(
            transport,
            "blade",
            _ =>
            {
                Interlocked.Increment(ref restored);
                return Task.CompletedTask;
            });
        var source = new SequenceSource(CreateFrame(1));
        var delays = new List<TimeSpan>();
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new SoftwareLightingRuntime(
            pump,
            source,
            TimeSpan.FromMilliseconds(16),
            (delay, cancellationToken) =>
            {
                delays.Add(delay);
                delayEntered.TrySetResult();
                return new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
            },
            static () => Stopwatch.GetTimestamp());

        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await runtime.StopAsync();

        Assert.Equal(1, source.RenderCount);
        Assert.Equal([TimeSpan.FromMilliseconds(16)], delays);
        Assert.Equal(BladeLightingProtocol.Rows, transport.MatrixFrames.Count);
        Assert.All(transport.MatrixFrames, marker => Assert.Equal(1, marker));
        Assert.Equal(1, restored);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task InputFailureStopsPumpAndIsReportedByCompletion()
    {
        var transport = new RuntimeTransport();
        var restored = 0;
        await using var pump = new BladeMatrixFramePump(
            transport,
            "blade",
            _ =>
            {
                Interlocked.Increment(ref restored);
                return Task.CompletedTask;
            });
        var expected = new InvalidOperationException("capture unavailable");
        var source = new SequenceSource(CreateFrame(3), expected);
        var runtime = new SoftwareLightingRuntime(
            pump,
            source,
            TimeSpan.FromMilliseconds(1),
            static (_, cancellationToken) => new ValueTask(Task.CompletedTask),
            static () => Stopwatch.GetTimestamp());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Completion);

        Assert.Same(expected, failure);
        Assert.Equal(2, source.RenderCount);
        Assert.Equal(BladeLightingProtocol.Rows, transport.MatrixFrames.Count);
        Assert.All(transport.MatrixFrames, marker => Assert.Equal(3, marker));
        Assert.Equal(1, restored);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StopAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task CancellationIsNormalAndRestoresPersistentEffect()
    {
        var transport = new RuntimeTransport();
        var restored = 0;
        await using var pump = new BladeMatrixFramePump(
            transport,
            "blade",
            _ =>
            {
                Interlocked.Increment(ref restored);
                return Task.CompletedTask;
            });
        var source = new SequenceSource(CreateFrame(5));
        var runtime = new SoftwareLightingRuntime(
            pump,
            source,
            TimeSpan.FromMilliseconds(10),
            static (_, cancellationToken) =>
                new ValueTask(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)),
            static () => Stopwatch.GetTimestamp());

        await Task.Delay(20);
        await runtime.StopAsync();

        Assert.Equal(1, restored);
        Assert.True(source.RenderCount >= 1);
    }

    private static RazerRgb[] CreateFrame(byte marker) =>
        Enumerable.Repeat(
            new RazerRgb(marker, (byte)(marker + 1), (byte)(marker + 2)),
            BladeLightingProtocol.Rows * BladeLightingProtocol.Columns).ToArray();

    private sealed class SequenceSource : ISoftwareLightingFrameSource
    {
        private readonly IReadOnlyList<RazerRgb>[] _frames;
        private int _renderCount;

        public SequenceSource(params object[] values)
        {
            _frames = values
                .Select(value => value switch
                {
                    IReadOnlyList<RazerRgb> frame => frame,
                    Exception exception => throw new ArgumentException("Exceptions must be supplied through Failure.", exception),
                    _ => throw new ArgumentException("Unsupported source value.")
                })
                .ToArray();
        }

        public SequenceSource(IReadOnlyList<RazerRgb> frame, Exception failure)
        {
            _frames = [frame];
            Failure = failure;
        }

        private Exception? Failure { get; }
        public int RenderCount => Volatile.Read(ref _renderCount);

        public ValueTask<IReadOnlyList<RazerRgb>> RenderAsync(
            TimeSpan elapsed,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Interlocked.Increment(ref _renderCount);
            if (Failure is not null && count > _frames.Length)
            {
                return ValueTask.FromException<IReadOnlyList<RazerRgb>>(Failure);
            }

            return ValueTask.FromResult(_frames[Math.Min(count - 1, _frames.Length - 1)]);
        }
    }

    private sealed class RuntimeTransport : IRazerFeatureTransport
    {
        public List<byte> MatrixFrames { get; } = [];

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            if ((commandClass, commandId) == (0x03, 0x0B))
            {
                MatrixFrames.Add(arguments.Span[4]);
            }

            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = transactionId;
            response[6] = dataSize;
            response[7] = commandClass;
            response[8] = commandId;
            arguments.CopyTo(response.AsMemory(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }
    }
}
