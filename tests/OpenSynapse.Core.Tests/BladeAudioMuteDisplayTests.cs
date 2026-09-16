using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAudioMuteDisplayTests
{
    [Fact]
    public async Task DisplayOffDrainsBothIndicatorsAndSuppressesMuteEvents()
    {
        var session = new RecordingSession();
        await using var synchronizer = new BladeAudioMuteSynchronizer(session);

        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Speaker, true)));
        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, true)));
        await synchronizer.SetDisplayAvailableAsync(false);

        Assert.Equal(
            [
                new BladeAudioMuteState(BladeAudioMuteTarget.Speaker, false),
                new BladeAudioMuteState(BladeAudioMuteTarget.Microphone, false),
            ],
            session.States.TakeLast(2));
        Assert.False(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, true)));
        Assert.Equal(2, session.States.Count(state => !state.Muted));
    }

    private sealed class RecordingSession : IRazerFeatureSession
    {
        private byte _transactionId;

        internal List<BladeAudioMuteState> States { get; } = [];

        public byte NextTransactionId() => _transactionId++;

        public Task SendAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryAsync(
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            byte responseReportId,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var state = new BladeAudioMuteState(
                (BladeAudioMuteTarget)arguments.Span[1],
                arguments.Span[2] != 0);
            States.Add(state);
            var response = RazerFeatureReport.CreateRequest(
                transactionId,
                dataSize,
                commandClass,
                commandId,
                arguments.Span);
            response[0] = responseReportId;
            response[1] = 0x02;
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return Task.FromResult(response);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
