using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerBleProtocol30Tests
{
    [Fact]
    public void RequestHeaderMatchesTheMiddlewareLayout()
    {
        Assert.Equal(
            new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x84, 0x00, 0x00 },
            RazerBleProtocol30.CreateGetPollingRate(0x00));

        var setPolling = RazerBleProtocol30.CreateSetPollingRate(0x1F, 500);
        Assert.Equal(new byte[] { 0x1F, 0x01, 0x00, 0x00, 0x01, 0x04, 0x00, 0x00, 0x02 }, setPolling);

        var setDpi = RazerBleProtocol30.CreateSetDpi(0x02, 800, 1600);
        Assert.Equal(
            new byte[] { 0x02, 0x06, 0x00, 0x00, 0x0B, 0x01, 0x00, 0x00, 0x20, 0x03, 0x40, 0x06, 0x00, 0x00 },
            setDpi);
    }

    [Fact]
    public void ResponsePrefersTheTwentyByteHeader()
    {
        var reply = Response(0x03, 0x05, 0x81, 0x02, 0x80);

        var parsed = RazerBleProtocol30.ParseResponse(reply);

        Assert.Equal(20, parsed.HeaderLength);
        Assert.Equal((byte)0x03, parsed.TransactionId);
        Assert.Equal((byte)0x05, parsed.CommandClass);
        Assert.Equal((byte)0x81, parsed.CommandId);
        Assert.Equal(new byte[] { 0x80 }, parsed.Payload);
        Assert.True(parsed.IsSuccess);
    }

    [Fact]
    public void ResponseFallsBackToTheEightByteHeader()
    {
        var reply = new byte[] { 0x03, 0x01, 0x00, 0x00, 0x05, 0x81, 0x00, 0x02, 0x80 };

        var parsed = RazerBleProtocol30.ParseResponse(reply);

        Assert.Equal(8, parsed.HeaderLength);
        Assert.Equal(new byte[] { 0x80 }, parsed.Payload);
    }

    [Fact]
    public void EmptyPayloadKeepsTheTwentyByteHeader()
    {
        var reply = new byte[20];
        reply[4] = 0x01;
        reply[5] = 0x02;
        reply[7] = 0x02;

        var parsed = RazerBleProtocol30.ParseResponse(reply);

        Assert.Equal(20, parsed.HeaderLength);
        Assert.Empty(parsed.Payload);
    }

    [Theory]
    [InlineData(0x01, 1000)]
    [InlineData(0x02, 500)]
    [InlineData(0x03, 333)]
    [InlineData(0x04, 250)]
    [InlineData(0x05, 200)]
    [InlineData(0x08, 125)]
    [InlineData(0x0A, 100)]
    [InlineData(0x7F, 100)]
    public void PollingCodesFollowTheMiddlewareTable(byte code, int hertz)
    {
        Assert.Equal(hertz, RazerBleProtocol30.ReadPollingRate(Parse(0x01, 0x84, code)));
    }

    [Fact]
    public void PollingWriterRejectsRatesTheMiddlewareDoesNotSend()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerBleProtocol30.PollingCode(4000));
        Assert.Equal((byte)0x08, RazerBleProtocol30.PollingCode(125));
    }

    [Theory]
    [InlineData(0x00, 0)]
    [InlineData(0x80, 50)]
    [InlineData(0xFF, 100)]
    public void BatteryPercentScalesLikeTheMiddleware(byte raw, int percent)
    {
        Assert.Equal(percent, RazerBleProtocol30.ReadBatteryPercent(Parse(0x05, 0x81, raw)));
    }

    [Fact]
    public void DpiReplyIsThreeLittleEndianPairs()
    {
        var dpi = RazerBleProtocol30.ReadDpi(Parse(0x0B, 0x81, 0x20, 0x03, 0x40, 0x06, 0x00, 0x00));

        Assert.Equal((800, 1600, 0), dpi);
    }

    [Fact]
    public async Task SessionRetriesABusyReplyWithAFreshTransactionId()
    {
        var transport = new FakeTransport((frame, _) => frame[0] == 0
            ? Response(0x00, frame[4], frame[5], 0x03, 0x00)
            : Response(frame[0], frame[4], frame[5], 0x02, 0x00));
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);

        var response = await session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetPollingRate(transaction),
            "get polling");

        Assert.True(response.IsSuccess);
        Assert.Equal(2, transport.Frames.Count);
        Assert.Equal(new byte[] { 0x00, 0x01 }, transport.Frames.Select(frame => frame[0]));
    }

    [Fact]
    public async Task SessionGivesUpAfterTwoTimeouts()
    {
        var transport = new FakeTransport((_, _) => null);
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync(
            transaction => RazerBleProtocol30.CreateGetPollingRate(transaction),
            "get polling"));

        Assert.Contains("timeout", error.Message);
        Assert.Equal(2, transport.Frames.Count);
    }

    [Fact]
    public async Task PollingWriteIsVerifiedAndKept()
    {
        byte applied = 0x0A;
        var transport = new FakeTransport((frame, _) =>
        {
            if (frame[5] == 0x04) applied = frame[8];
            return Response(frame[0], frame[4], frame[5], 0x02, applied);
        });
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);
        using var service = new RazerBleDeviceService(session, "test");

        var hertz = await service.SetPollingRateAsync(1000);

        Assert.Equal(1000, hertz);
        Assert.Contains(transport.Frames, frame => frame[5] == 0x04 && frame[8] == 0x01);
    }

    [Fact]
    public async Task DpiWriteRestoresThePreviousValueWhenTheDeviceIgnoresIt()
    {
        var device = (X: 400, Y: 800);
        var transport = new FakeTransport((frame, _) =>
        {
            byte[] payload = frame[5] switch
            {
                0x81 => [(byte)device.X, (byte)(device.X >> 8), (byte)device.Y, (byte)(device.Y >> 8), 0, 0],
                0x01 => [(byte)(device.X >> 8), (byte)device.X, (byte)(device.Y >> 8), (byte)device.Y, 0, 0],
                _ => [],
            };
            return Response(frame[0], frame[4], frame[5], 0x02, payload);
        });
        using var session = new RazerBleSession(transport, retryDelay: TimeSpan.Zero);
        using var service = new RazerBleDeviceService(session, "test");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetDpiAsync(800, 1600));

        Assert.Contains("restored", error.Message);
        var writes = transport.Frames.Where(frame => frame[5] == 0x01).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(new byte[] { 0x90, 0x01, 0x20, 0x03, 0x00, 0x00 }, writes[1][8..]);
    }

    private static RazerBleResponse Parse(byte commandClass, byte commandId, params byte[] payload) =>
        RazerBleProtocol30.ParseResponse(Response(0x00, commandClass, commandId, 0x02, payload));

    private static byte[] Response(
        byte transactionId,
        byte commandClass,
        byte commandId,
        byte status,
        params byte[] payload)
    {
        var reply = new byte[20 + payload.Length];
        reply[0] = transactionId;
        reply[1] = (byte)(payload.Length & 0xFF);
        reply[2] = (byte)(payload.Length >> 8);
        reply[4] = commandClass;
        reply[5] = commandId;
        reply[7] = status;
        payload.CopyTo(reply, 20);
        return reply;
    }

    private sealed class FakeTransport(
        Func<byte[], TimeSpan, byte[]?> handler) : IRazerBleTransport
    {
        public List<byte[]> Frames { get; } = [];

        public Task<byte[]?> ExchangeAsync(
            ReadOnlyMemory<byte> frame,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Frames.Add(frame.ToArray());
            return Task.FromResult(handler(frame.ToArray(), timeout));
        }

        public void Dispose()
        {
        }
    }
}

