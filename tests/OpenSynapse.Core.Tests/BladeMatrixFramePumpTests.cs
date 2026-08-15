using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMatrixFramePumpTests
{
    [Fact]
    public async Task DropsQueuedStaleFrameAndRestoresOnce()
    {
        var transport = new FrameTransport { HoldFirstRow = true };
        var restored = 0;
        var pump = new BladeMatrixFramePump(
            transport,
            "blade",
            _ =>
            {
                Interlocked.Increment(ref restored);
                return Task.CompletedTask;
            });

        Assert.True(pump.TryPublish(CreateFrame(1)));
        await transport.FirstRowStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(pump.TryPublish(CreateFrame(2)));
        Assert.True(pump.TryPublish(CreateFrame(3)));
        transport.ReleaseFirstRow.TrySetResult();

        await transport.WaitForRowsAsync(12);
        await pump.StopAsync();

        Assert.Equal(2, transport.CustomModeCount);
        Assert.Equal(1, restored);
        Assert.Equal(
            new byte[] { 1, 1, 1, 1, 1, 1, 3, 3, 3, 3, 3, 3 },
            transport.RowMarkers);
        Assert.False(pump.TryPublish(CreateFrame(4)));
    }

    [Fact]
    public async Task RestoresAfterTransportFailureAndRejectsMoreFrames()
    {
        var transport = new FrameTransport { FailOnRow = 2 };
        var restored = 0;
        var pump = new BladeMatrixFramePump(
            transport,
            "blade",
            _ =>
            {
                Interlocked.Increment(ref restored);
                return Task.CompletedTask;
            });

        Assert.True(pump.TryPublish(CreateFrame(7)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pump.Completion);

        Assert.Equal(1, restored);
        Assert.False(pump.TryPublish(CreateFrame(8)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pump.StopAsync());
    }

    [Fact]
    public async Task CommitsCustomFrameAfterEveryMatrixFrame()
    {
        var transport = new FrameTransport();
        var restored = 0;
        var pump = new BladeMatrixFramePump(
            transport,
            "blade",
            _ =>
            {
                Interlocked.Increment(ref restored);
                return Task.CompletedTask;
            });

        Assert.True(pump.TryPublish(CreateFrame(7)));
        await pump.FirstFrameApplied.WaitAsync(TimeSpan.FromSeconds(2));
        await pump.StopAsync();

        Assert.Equal(1, restored);
        Assert.Equal(1, transport.CustomModeCount);
    }

    [Fact]
    public async Task RejectsIncompleteFrameBeforeStartingHardwareWork()
    {
        var transport = new FrameTransport();
        var pump = new BladeMatrixFramePump(transport, "blade", _ => Task.CompletedTask);

        Assert.Throws<ArgumentException>(() => pump.TryPublish(new RazerRgb[101]));

        await pump.DisposeAsync();
        Assert.Empty(transport.RowMarkers);
    }

    [Fact]
    public async Task ResetsOfficialTransactionIdsForEveryFrame()
    {
        var transport = new FrameTransport();
        var pump = new BladeMatrixFramePump(transport, "blade", _ => Task.CompletedTask);

        for (byte marker = 1; marker <= 6; marker++)
        {
            Assert.True(pump.TryPublish(CreateFrame(marker)));
            await transport.WaitForRowsAsync(marker * BladeLightingProtocol.Rows);
        }
        await pump.StopAsync();

        Assert.Equal(
            Enumerable.Repeat(Enumerable.Range(1, 6).Select(value => (byte)value), 6).SelectMany(ids => ids),
            transport.TransactionIds);
        Assert.All(transport.Commands, command => Assert.Equal((0x03, 0x0B, 0x37), command));
    }

    private static RazerRgb[] CreateFrame(byte marker) =>
        Enumerable.Repeat(
            new RazerRgb(marker, (byte)(marker + 1), (byte)(marker + 2)),
            BladeLightingProtocol.Rows * BladeLightingProtocol.Columns).ToArray();

    private sealed class FrameTransport : IRazerFeatureTransport
    {
        private readonly List<byte> _rowMarkers = new();
        private readonly List<byte> _transactionIds = new();
        private readonly List<(byte Class, byte Id, byte Size)> _commands = new();

        public bool HoldFirstRow { get; init; }
        public byte? FailOnRow { get; init; }
        public int CustomModeCount { get; private set; }
        public TaskCompletionSource FirstRowStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRow { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<byte> RowMarkers
        {
            get
            {
                lock (_rowMarkers)
                {
                    return _rowMarkers.ToArray();
                }
            }
        }
        public IReadOnlyList<byte> TransactionIds
        {
            get
            {
                lock (_rowMarkers)
                {
                    return _transactionIds.ToArray();
                }
            }
        }
        public IReadOnlyList<(byte Class, byte Id, byte Size)> Commands
        {
            get
            {
                lock (_rowMarkers)
                {
                    return _commands.ToArray();
                }
            }
        }

        public async Task<byte[]> QueryAsync(
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
            if (commandId == 0x0A)
            {
                CustomModeCount++;
            }
            else if ((commandClass, commandId) == (0x03, 0x0B))
            {
                var row = arguments.Span[1];
                if (row == 0)
                {
                    FirstRowStarted.TrySetResult();
                    if (HoldFirstRow)
                    {
                        await ReleaseFirstRow.Task.WaitAsync(cancellationToken);
                    }
                }
                if (FailOnRow == row)
                {
                    throw new InvalidOperationException("Simulated matrix write failure.");
                }
                lock (_rowMarkers)
                {
                    _rowMarkers.Add(arguments.Span[4]);
                    _transactionIds.Add(transactionId);
                    _commands.Add((commandClass, commandId, dataSize));
                }
            }

            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = transactionId;
            response[6] = dataSize;
            response[7] = commandClass;
            response[8] = commandId;
            arguments.CopyTo(response.AsMemory(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }

        public async Task WaitForRowsAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (RowMarkers.Count < count)
            {
                await Task.Delay(5, timeout.Token);
            }
        }
    }
}
