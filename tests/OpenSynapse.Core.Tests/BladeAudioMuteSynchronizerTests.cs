using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAudioMuteSynchronizerTests
{
    [Fact]
    public async Task UsesOfficialMicrophoneCommandTransportSemantics()
    {
        var session = new FakeSession();
        await using var synchronizer = new BladeAudioMuteSynchronizer(session);
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        synchronizer.Synchronized += _ =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                completed.TrySetResult(0);
            }
        };

        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, true)));
        Assert.True(SpinWait.SpinUntil(
            () => session.Requests.Count == 1,
            TimeSpan.FromSeconds(2)));
        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, false)));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            [
                new Request(BladeAudioMuteTarget.Microphone, true, 0, TimeSpan.FromMilliseconds(5), 2),
                new Request(BladeAudioMuteTarget.Microphone, false, 1, TimeSpan.FromMilliseconds(5), 2),
            ],
            session.Requests);
        Assert.Null(synchronizer.LastError);
    }

    [Fact]
    public async Task UsesOfficialOpenHandshakeAndConnectionTransactionSequence()
    {
        var session = new FakeSession();
        await BladeAudioMuteRuntime.InitializeSessionAsync(session, CancellationToken.None);
        await BladeAudioMuteRuntime.SetDeviceModeAsync(
            session,
            softwareMode: true,
            CancellationToken.None);
        await using var synchronizer = new BladeAudioMuteSynchronizer(session);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        synchronizer.Synchronized += _ => completed.TrySetResult();

        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, true)));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var expectedHandshake = new byte[RazerFeatureReport.Length];
        expectedHandshake[0] = 0x02;
        expectedHandshake[6] = 0x02;
        expectedHandshake[8] = 0x81;
        expectedHandshake[89] = 0x83;
        Assert.Equal(expectedHandshake, Assert.Single(session.WriteOnlyReports));
        Assert.Equal(2, session.Queries.Count);
        var driverMode = session.Queries[0];
        Assert.Equal(1, driverMode.TransactionId);
        Assert.Equal(0x02, driverMode.DataSize);
        Assert.Equal(0x00, driverMode.CommandClass);
        Assert.Equal(0x04, driverMode.CommandId);
        Assert.Equal(new byte[] { 0x03, 0x00 }, driverMode.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(5), driverMode.DeviceWait);
        Assert.Equal(0x02, driverMode.ResponseReportId);
        var microphone = session.Queries[1];
        Assert.Equal(2, microphone.TransactionId);
        Assert.Equal(0x03, microphone.DataSize);
        Assert.Equal(0x18, microphone.CommandClass);
        Assert.Equal(0x04, microphone.CommandId);
        Assert.Equal(new byte[] { 0x00, 0x02, 0x01 }, microphone.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(5), microphone.DeviceWait);
        Assert.Equal(0x02, microphone.ResponseReportId);
        Assert.Equal(
            new Request(BladeAudioMuteTarget.Microphone, true, 2,
                TimeSpan.FromMilliseconds(5), 2),
            Assert.Single(session.Requests));
    }

    [Fact]
    public async Task SendsSpeakerIndicatorWithTargetOne()
    {
        var session = new FakeSession();
        await using var synchronizer = new BladeAudioMuteSynchronizer(session);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        synchronizer.Synchronized += _ => completed.TrySetResult();

        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Speaker, true)));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            new Request(BladeAudioMuteTarget.Speaker, true, 0, TimeSpan.FromMilliseconds(5), 2),
            Assert.Single(session.Requests));
    }

    [Fact]
    public async Task TransportFailureIsReportedAndDoesNotKillTheWorker()
    {
        var session = new FakeSession { Fail = true };
        await using var synchronizer = new BladeAudioMuteSynchronizer(session);
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        synchronizer.SynchronizationFailed += _ => failed.TrySetResult(true);

        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, true)));
        Assert.True(await failed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("fake transport", synchronizer.LastError, StringComparison.Ordinal);
        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, false)));
    }

    [Fact]
    public async Task EventHandlerFailureDoesNotKillTheWorker()
    {
        var session = new FakeSession();
        await using var synchronizer = new BladeAudioMuteSynchronizer(session);
        synchronizer.Synchronized += _ => throw new InvalidOperationException("callback failure");

        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, true)));
        Assert.True(SpinWait.SpinUntil(
            () => session.Requests.Count == 1,
            TimeSpan.FromSeconds(2)));

        Assert.True(synchronizer.Publish(new(BladeAudioMuteTarget.Microphone, false)));
        Assert.True(SpinWait.SpinUntil(
            () => session.Requests.Count == 2,
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task DisposeWaitsForStartAndReleasesTheSession()
    {
        var session = new FakeSession();
        var transport = new DelayedSessionTransport(session);
        var runtime = new BladeAudioMuteRuntime(transport, "blade");

        var start = runtime.StartAsync();
        await transport.OpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var dispose = runtime.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);

        transport.AllowOpen.TrySetResult();
        await start;
        await dispose;

        Assert.True(session.IsDisposed);
        Assert.Contains(session.Queries, query =>
            query.CommandClass == 0x00 &&
            query.CommandId == 0x04 &&
            query.Arguments.SequenceEqual(new byte[] { 0x00, 0x00 }));
    }

    private sealed class DelayedSessionTransport(FakeSession session) : IRazerFeatureTransport
    {
        internal TaskCompletionSource OpenStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowOpen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<byte[]> QueryAsync(
            string devicePath,
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public async Task<IRazerFeatureSession> OpenSessionAsync(
            string devicePath,
            CancellationToken cancellationToken)
        {
            OpenStarted.TrySetResult();
            await AllowOpen.Task.WaitAsync(cancellationToken);
            return session;
        }
    }

    private sealed class FakeSession : IRazerFeatureSession
    {
        private byte _transactionId;

        internal List<Request> Requests { get; } = [];
        internal List<Query> Queries { get; } = [];
        internal List<byte[]> WriteOnlyReports { get; } = [];
        internal bool Fail { get; init; }
        internal bool IsDisposed { get; private set; }

        public byte NextTransactionId()
        {
            var current = _transactionId;
            _transactionId = current == 30 ? (byte)0 : (byte)(current + 1);
            return current;
        }

        public Task SendAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
        {
            WriteOnlyReports.Add(request.ToArray());
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
            bool allowRemainingPacketsMismatch = false)
        {
            if (Fail)
            {
                throw new InvalidOperationException("fake transport failure");
            }

            Queries.Add(new(
                transactionId,
                dataSize,
                commandClass,
                commandId,
                arguments.ToArray(),
                deviceWait,
                responseReportId));
            if ((commandClass, commandId) == (0x18, 0x04))
            {
                var target = (BladeAudioMuteTarget)arguments.Span[1];
                var muted = arguments.Span[2] == 1;
                Requests.Add(new(target, muted, transactionId, deviceWait, responseReportId));
            }
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

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed record Request(
        BladeAudioMuteTarget Target,
        bool Muted,
        byte TransactionId,
        TimeSpan DeviceWait,
        byte ResponseReportId);

    private sealed record Query(
        byte TransactionId,
        byte DataSize,
        byte CommandClass,
        byte CommandId,
        byte[] Arguments,
        TimeSpan DeviceWait,
        byte ResponseReportId);
}
