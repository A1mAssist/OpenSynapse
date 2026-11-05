using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class AudioSpecialAuditRegressionTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void BleShortFirstFragmentDoesNotMasqueradeAsCompact(int payloadLength)
    {
        var request = RazerBleProtocol30.CreateGetPollingRate(3);
        var payload = Enumerable.Range(0, payloadLength).Select(i => (byte)(0x42 + i)).ToArray();
        var response = BleReply(request, payload);
        var buffer = new RazerBleResponseBuffer(request);
        Assert.Null(buffer.Append(response[..(8 + payloadLength)]));
        Assert.Equal(response, buffer.Append(response[(8 + payloadLength)..]));
    }

    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(5)]
    public void BleDiscardsLateReplyWithWrongIdentity(int offset)
    {
        var request = RazerBleProtocol30.CreateGetPollingRate(3);
        var wrong = BleReply(request, [1]);
        wrong[offset]++;
        var correct = BleReply(request, [2]);
        var buffer = new RazerBleResponseBuffer(request);
        Assert.Null(buffer.Append(wrong));
        Assert.Equal(correct, buffer.Append(correct));
    }

    [Fact]
    public void BleWrongCompactThenCorrectReplyResynchronizesAtNotificationBoundary()
    {
        var request = RazerBleProtocol30.CreateGetPollingRate(3);
        var wrong = BleReply(request, [1], compact: true);
        wrong[0]++;
        var buffer = new RazerBleResponseBuffer(request);
        Assert.Null(buffer.Append(wrong));
        var correct = BleReply(request, [2]);
        Assert.Equal(correct, buffer.Append(correct));
    }

    [Fact]
    public void BleCompactReplyRemainsAvailableWhenWindowCloses()
    {
        var request = RazerBleProtocol30.CreateGetPollingRate(3);
        var reply = BleReply(request, [2], compact: true);
        var buffer = new RazerBleResponseBuffer(request);
        Assert.Null(buffer.Append(reply));
        Assert.Equal(reply, buffer.Finish());
    }

    [Fact]
    public async Task BleSessionRejectsWrongIdentityEvenForAnInjectedTransport()
    {
        using var session = new RazerBleSession(new WrongBleTransport(), retryDelay: TimeSpan.Zero);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SendAsync(RazerBleProtocol30.CreateGetPollingRate, "test"));
        Assert.Contains("identity mismatch", error.Message);
    }

    [Theory]
    [InlineData(0x0501)] [InlineData(0x0504)] [InlineData(0x0506)]
    [InlineData(0x0510)] [InlineData(0x0520)] [InlineData(0x0527)] [InlineData(0x0560)]
    public async Task LegacyKrakenServiceNeverSendsAV4Handshake(ushort productId)
    {
        var reports = new List<byte[]>();
        var service = new RazerHidSpecialLightingService((_, report, _) =>
        { reports.Add(report.ToArray()); return Task.CompletedTask; });
        var connection = new RazerHidSpecialLightingConnection(productId, "test",
            RazerHidSpecialLightingKind.Kraken37, "test", "fake", null, null,
            new HashSet<RazerHidLightingEffect> { RazerHidLightingEffect.Off }, true, false, null);
        await service.SetKrakenLightingAsync(connection, new(RazerHidLightingEffect.Off));
        Assert.NotEmpty(reports);
        Assert.All(reports, report => { Assert.Equal(37, report.Length); Assert.Equal(4, report[0]); });
    }

    [Theory]
    [InlineData(0x056B)] [InlineData(0x056C)]
    public async Task V4ServiceStillSendsItsOwnHandshake(ushort productId)
    {
        var reports = new List<byte[]>();
        var service = new RazerHidSpecialLightingService((_, report, _) =>
        { reports.Add(report.ToArray()); return Task.CompletedTask; });
        var connection = new RazerHidSpecialLightingConnection(productId, "test",
            RazerHidSpecialLightingKind.KrakenV4, "test", "fake", null, null,
            new HashSet<RazerHidLightingEffect> { RazerHidLightingEffect.Off }, true, false, null);
        await service.SetKrakenLightingAsync(connection, new(RazerHidLightingEffect.Off));
        Assert.Equal(RazerHidKrakenV4Protocol.CreateConnect(productId == 0x056C), reports[0]);
    }

    [Theory]
    [InlineData(0x056B)] [InlineData(0x056C)]
    public async Task V4ServiceSendsTheHandshakeOncePerPath(ushort productId)
    {
        var reports = new List<byte[]>();
        var service = new RazerHidSpecialLightingService((_, report, _) =>
        {
            reports.Add(report.ToArray());
            return Task.CompletedTask;
        });
        var connection = new RazerHidSpecialLightingConnection(productId, "test",
            RazerHidSpecialLightingKind.KrakenV4, $"test-{productId}", "fake", null, null,
            new HashSet<RazerHidLightingEffect> { RazerHidLightingEffect.Off }, true, false, null);

        await service.SetKrakenLightingAsync(connection, new(RazerHidLightingEffect.Off));
        var firstCallCount = reports.Count;
        await service.SetKrakenLightingAsync(connection, new(RazerHidLightingEffect.Off));

        Assert.Equal(firstCallCount + 1, reports.Count);
        Assert.DoesNotContain(reports.Skip(firstCallCount), report =>
            report.SequenceEqual(RazerHidKrakenV4Protocol.CreateConnect(productId == 0x056C)));
    }

    [Fact]
    public async Task MotorFiveAlreadyAtTargetDoesNotSkipTheOtherMotors()
    {
        var transport = new MotorTransport();
        transport.Motors[5] = 75;
        var service = new RazerGamepadService(transport, "fake", TimeSpan.Zero);
        await service.SetVibrationScaleAsync(75);
        Assert.All(transport.Motors.Values, value => Assert.Equal(75, value));
        Assert.Equal(4, transport.Writes.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MotorFailureAndCancellationRestoreEachOriginalValue(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var transport = new MotorTransport { FailMotor = 7, Cancellation = cancel ? cancellation : null };
        var previous = transport.Motors.ToArray();
        var service = new RazerGamepadService(transport, "fake", TimeSpan.Zero);
        await Assert.ThrowsAnyAsync<Exception>(() => service.SetVibrationScaleAsync(75, cancellation.Token));
        Assert.Equal(previous, transport.Motors.ToArray());
        Assert.DoesNotContain(transport.Writes, write => write.Motor == 8);
        Assert.Equal(new byte[] { 7, 6, 5 }, transport.Writes.TakeLast(3).Select(w => w.Motor));
    }

    [Fact]
    public async Task IgnoredMotorSixWriteIsDetectedByReadback()
    {
        var transport = new MotorTransport { IgnoreMotor = 6 };
        var previous = transport.Motors.ToArray();
        var service = new RazerGamepadService(transport, "fake", TimeSpan.Zero);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetVibrationScaleAsync(75));
        Assert.Equal(previous, transport.Motors.ToArray());
    }

    private static byte[] BleReply(byte[] request, byte[] payload, bool compact = false)
    {
        var reply = new byte[(compact ? 8 : 20) + payload.Length];
        request.AsSpan(0, 8).CopyTo(reply);
        reply[1] = (byte)payload.Length; reply[2] = reply[3] = 0; reply[7] = 2;
        payload.CopyTo(reply, compact ? 8 : 20);
        return reply;
    }

    private sealed class WrongBleTransport : IRazerBleTransport
    {
        public Task<byte[]?> ExchangeAsync(ReadOnlyMemory<byte> frame, TimeSpan timeout, CancellationToken cancellationToken)
        { var reply = BleReply(frame.ToArray(), [1]); reply[0]++; return Task.FromResult<byte[]?>(reply); }
        public void Dispose() { }
    }

    private sealed class MotorTransport : IRazerFeatureTransport
    {
        internal Dictionary<byte, byte> Motors { get; } = new() { [5] = 50, [6] = 100, [7] = 25, [8] = 0 };
        internal List<(byte Motor, byte Scale)> Writes { get; } = [];
        internal byte? FailMotor, IgnoreMotor;
        internal CancellationTokenSource? Cancellation;
        public Task<byte[]> QueryAsync(string path, byte transactionId, byte dataSize, byte commandClass, byte commandId,
            ReadOnlyMemory<byte> arguments, TimeSpan wait, CancellationToken token, bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();
        public Task<byte[]> QueryPreparedAsync(string path, ReadOnlyMemory<byte> memory, TimeSpan wait,
            CancellationToken token, bool allowRemainingPacketsMismatch = false)
        {
            token.ThrowIfCancellationRequested();
            var request = memory.ToArray(); var motor = request[10];
            if (request[8] == 6)
            {
                Writes.Add((motor, request[11]));
                if (motor != IgnoreMotor) Motors[motor] = request[11];
                if (motor == FailMotor)
                {
                    FailMotor = null;
                    if (Cancellation is not null) { Cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                    throw new IOException("injected motor failure");
                }
            }
            var reply = request.ToArray(); reply[1] = 2; reply[6] = 3;
            reply[9] = 0x0C; reply[10] = motor; reply[11] = Motors[motor];
            reply[89] = RazerFeatureReport.CalculateCrc(reply);
            return Task.FromResult(reply);
        }
    }
}
