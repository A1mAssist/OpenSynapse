using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeLightingSuspendTests
{
    [Fact]
    public async Task SuspendSendsBlackFrameBeforeClosingSession()
    {
        var session = new RecordingSession();
        await using var pump = new BladeMatrixFramePump(
            new RecordingTransport(session),
            "blade",
            _ => Task.CompletedTask);

        Assert.True(pump.TryPublish(Enumerable.Repeat(new RazerRgb(1, 2, 3), 6 * 17).ToArray()));
        await pump.FirstFrameApplied;
        pump.MarkTurnOffOnStop();
        await pump.StopAsync();

        var expected = BladeLightingProtocol.CreateMatrixFrameRequests(new RazerRgb[6 * 17]);
        Assert.True(session.Disposed);
        Assert.Equal(expected.Length * 2, session.Sent.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index], session.Sent[session.Sent.Count - expected.Length + index]);
        }
    }

    private sealed class RecordingTransport(RecordingSession session) : IRazerFeatureTransport
    {
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
            CancellationToken cancellationToken) => Task.FromResult<IRazerFeatureSession>(session);
    }

    private sealed class RecordingSession : IRazerFeatureSession
    {
        internal List<byte[]> Sent { get; } = [];
        internal bool Disposed { get; private set; }

        public byte NextTransactionId() => 0;

        public Task SendAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
        {
            Assert.False(Disposed);
            Sent.Add(request.ToArray());
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
