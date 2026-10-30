using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerSeirenEmoteTests
{
    [Fact]
    public void CreateFrameRequests_BuildsFourSixteenPixelRowsAndLatch()
    {
        var pixels = TestPixels();

        var requests = RazerSeirenEmoteProtocol.CreateFrameRequests(pixels);

        Assert.Equal(5, requests.Length);
        for (var row = 0; row < RazerSeirenEmoteProtocol.RowCount; row++)
        {
            var report = requests[row];
            AssertReport(report);
            Assert.Equal((byte)RazerSeirenEmoteProtocol.RowDataSize, report[6]);
            Assert.Equal(RazerSeirenEmoteProtocol.CommandClass, report[7]);
            Assert.Equal(RazerSeirenEmoteProtocol.RowCommandId, report[8]);
            Assert.Equal(
                new byte[] { 0, 0, (byte)row, 0, 15 },
                report.AsSpan(9, 5).ToArray());

            var rowPixels = report.AsSpan(14, RazerSeirenEmoteProtocol.PixelsPerRow * 3);
            for (var column = 0; column < RazerSeirenEmoteProtocol.PixelsPerRow; column++)
            {
                var color = pixels[(row * RazerSeirenEmoteProtocol.PixelsPerRow) + column];
                Assert.Equal(color.Red, rowPixels[column * 3]);
                Assert.Equal(color.Green, rowPixels[(column * 3) + 1]);
                Assert.Equal(color.Blue, rowPixels[(column * 3) + 2]);
            }
        }

        var latch = requests[^1];
        AssertReport(latch);
        Assert.Equal((byte)RazerSeirenEmoteProtocol.LatchDataSize, latch[6]);
        Assert.Equal(RazerSeirenEmoteProtocol.CommandClass, latch[7]);
        Assert.Equal(RazerSeirenEmoteProtocol.LatchCommandId, latch[8]);
        Assert.Equal(
            new byte[] { 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            latch.AsSpan(9, RazerSeirenEmoteProtocol.LatchDataSize).ToArray());
    }

    [Fact]
    public void CreateFrameRequests_AcceptsPackedRgbAndPixelListEqually()
    {
        var pixels = TestPixels();
        var packed = new byte[RazerSeirenEmoteProtocol.PackedRgbLength];
        for (var index = 0; index < pixels.Length; index++)
        {
            var offset = index * 3;
            packed[offset] = pixels[index].Red;
            packed[offset + 1] = pixels[index].Green;
            packed[offset + 2] = pixels[index].Blue;
        }

        var fromPixels = RazerSeirenEmoteProtocol.CreateFrameRequests(pixels);
        var fromPacked = RazerSeirenEmoteProtocol.CreateFrameRequests(packed);

        Assert.Equal(fromPixels.Length, fromPacked.Length);
        for (var index = 0; index < fromPixels.Length; index++)
        {
            Assert.Equal(fromPixels[index], fromPacked[index]);
        }
    }

    [Fact]
    public void CreateFrameRequests_RejectsWrongPixelAndPackedLengths()
    {
        Assert.Throws<ArgumentException>(
            () => RazerSeirenEmoteProtocol.CreateFrameRequests(new RazerRgb[63]));
        Assert.Throws<ArgumentException>(
            () => RazerSeirenEmoteProtocol.CreateFrameRequests(new byte[191]));
    }

    [Fact]
    public async Task SendFrameAsync_UsesFixedTransactionAndFiveMillisecondBatchDelay()
    {
        var session = new RecordingSession();
        var transport = new RecordingTransport(session);
        await using var service = new RazerSeirenEmoteService(transport, "seiren");

        await service.SendFrameAsync(TestPixels());
        await service.SendFrameAsync(TestPixels());

        Assert.Equal(1, transport.OpenCount);
        Assert.Equal(2, session.BatchCount);
        Assert.Equal(TimeSpan.FromMilliseconds(5), session.Delay);
        Assert.Equal(10, session.Requests.Count);
        foreach (var frame in session.Requests.Chunk(5))
        {
            Assert.Equal(
                new byte[]
                {
                    RazerSeirenEmoteProtocol.RowCommandId,
                    RazerSeirenEmoteProtocol.RowCommandId,
                    RazerSeirenEmoteProtocol.RowCommandId,
                    RazerSeirenEmoteProtocol.RowCommandId,
                    RazerSeirenEmoteProtocol.LatchCommandId,
                },
                frame.Select(report => report[8]).ToArray());
            Assert.All(
                frame,
                report => Assert.Equal(RazerSeirenEmoteProtocol.TransactionId, report[2]));
        }
    }

    [Fact]
    public async Task ClearAsync_SendsAllBlackRowsAndLatch()
    {
        var session = new RecordingSession();
        await using var service = new RazerSeirenEmoteService(
            new RecordingTransport(session),
            "seiren");

        await service.ClearAsync();

        Assert.Equal(5, session.Requests.Count);
        foreach (var report in session.Requests[..4])
        {
            Assert.Equal(RazerSeirenEmoteProtocol.RowCommandId, report[8]);
            Assert.All(report[14..62], value => Assert.Equal((byte)0, value));
        }

        Assert.Equal(RazerSeirenEmoteProtocol.LatchCommandId, session.Requests[^1][8]);
        Assert.Equal((byte)8, session.Requests[^1][11]);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheOwnedSession()
    {
        var session = new RecordingSession();
        var service = new RazerSeirenEmoteService(new RecordingTransport(session), "seiren");
        await service.SendFrameAsync(TestPixels());

        await service.DisposeAsync();

        Assert.True(session.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.SendFrameAsync(TestPixels()));
    }

    private static RazerRgb[] TestPixels() =>
        Enumerable.Range(0, RazerSeirenEmoteProtocol.PixelCount)
            .Select(index => new RazerRgb(
                (byte)(index + 1),
                (byte)(0x40 + index),
                (byte)(0x80 + index)))
            .ToArray();

    private static void AssertReport(byte[] report)
    {
        Assert.Equal(91, report.Length);
        Assert.Equal((byte)0, report[0]);
        Assert.Equal(RazerSeirenEmoteProtocol.TransactionId, report[2]);
        Assert.Equal(ExpectedChecksum(report), report[89]);
        Assert.Equal((byte)0, report[90]);
    }

    private static byte ExpectedChecksum(byte[] report)
    {
        byte checksum = 0;
        for (var index = 3; index <= 88; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private sealed class RecordingTransport(RecordingSession session) : IRazerFeatureTransport
    {
        public int OpenCount { get; private set; }

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<IRazerFeatureSession> OpenSessionAsync(
            string devicePath,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult<IRazerFeatureSession>(session);
        }
    }

    private sealed class RecordingSession : IRazerFeatureSession
    {
        public List<byte[]> Requests { get; } = [];
        public int BatchCount { get; private set; }
        public TimeSpan? Delay { get; private set; }
        public bool Disposed { get; private set; }

        public byte NextTransactionId() =>
            throw new InvalidOperationException("The Seiren Emote protocol uses a fixed transaction id.");

        public Task SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.ToArray());
            return Task.CompletedTask;
        }

        public Task SendBatchAsync(
            IReadOnlyList<byte[]> requests,
            TimeSpan rowDelay,
            CancellationToken cancellationToken)
        {
            BatchCount++;
            Delay = rowDelay;
            Requests.AddRange(requests.Select(request => request.ToArray()));
            return Task.CompletedTask;
        }

        public Task<byte[]> QueryAsync(
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            byte responseReportId,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
