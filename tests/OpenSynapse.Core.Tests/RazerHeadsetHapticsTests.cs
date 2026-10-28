using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHeadsetHapticsTests
{
    [Fact]
    public void HapticsRequests_CarryClass04AndRawLittleEndianPayload()
    {
        var get = RazerHapticsProtocol.CreateGetHapticsControlRequest(0x2A);
        var set = RazerHapticsProtocol.CreateSetHapticsControlRequest(0x2A, true, 0x1234);

        Assert.Equal(RazerFeatureReport.Length, get.Length);
        Assert.Equal(0, get[6]);
        Assert.Equal(0x04, get[7]);
        Assert.Equal(0x82, get[8]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(get), get[89]);

        Assert.Equal(3, set[6]);
        Assert.Equal(0x04, set[7]);
        Assert.Equal(0x02, set[8]);
        Assert.Equal(0x01, set[9]);
        Assert.Equal(0x34, set[10]);
        Assert.Equal(0x12, set[11]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(set), set[89]);
    }

    [Fact]
    public async Task ReadStateAsync_PopulatesHapticsFromTheRawLittleEndianResponse()
    {
        var transport = new FakeHapticsTransport(enabled: true, strength: 0x1234);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.NotNull(state.Haptics);
        Assert.True(state.Haptics.Value.Enabled);
        Assert.Equal(0x1234, state.Haptics.Value.Strength);
        Assert.Contains(transport.Requests, request =>
            request[7] == 0x04 && request[8] == 0x82);
    }

    [Fact]
    public async Task ReadStateAsync_OmitsUnsupportedHapticsWithoutAnError()
    {
        var transport = new FakeHapticsTransport(available: false);
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Null(state.Haptics);
        Assert.DoesNotContain(
            state.Errors,
            error => error.Contains("haptics", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SetHapticsAsync_ReadsWritesAndVerifiesTheReadback()
    {
        var transport = new FakeHapticsTransport();
        var service = new RazerHeadsetAudioService(transport, "test", TimeSpan.Zero);

        await service.SetHapticsAsync(true, 0x1234);

        Assert.True(transport.Enabled);
        Assert.Equal(0x1234, transport.Strength);
        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal(0x82, transport.Requests[0][8]);
        Assert.Equal(0x02, transport.Requests[1][8]);
        Assert.Equal(0x01, transport.Requests[1][9]);
        Assert.Equal(0x34, transport.Requests[1][10]);
        Assert.Equal(0x12, transport.Requests[1][11]);
        Assert.Equal(0x82, transport.Requests[2][8]);
    }

    private sealed class FakeHapticsTransport(
        bool available = true,
        bool enabled = false,
        ushort strength = 0) : IRazerFeatureTransport
    {
        public bool Enabled { get; private set; } = enabled;
        public ushort Strength { get; private set; } = strength;
        public List<byte[]> Requests { get; } = [];

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

        public Task<byte[]> QueryPreparedAsync(
            string devicePath,
            ReadOnlyMemory<byte> request,
            TimeSpan deviceWait,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var copy = request.ToArray();
            Requests.Add(copy);

            if (!available || copy[7] != RazerHapticsProtocol.CommandClass)
            {
                throw new NotSupportedException();
            }

            if (copy[8] == RazerHapticsProtocol.SetCommandId)
            {
                Enabled = copy[9] != 0;
                Strength = (ushort)(copy[10] | (copy[11] << 8));
            }
            else if (copy[8] != RazerHapticsProtocol.GetCommandId)
            {
                throw new NotSupportedException();
            }

            return Task.FromResult(Response(copy));
        }

        private byte[] Response(byte[] request)
        {
            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = request[2];
            response[3] = request[3];
            response[4] = request[4];
            // The getter declares no request payload; its reply declares the three state bytes.
            response[6] = 3;
            response[7] = request[7];
            response[8] = request[8];
            response[9] = Enabled ? (byte)0x01 : (byte)0x00;
            response[10] = (byte)Strength;
            response[11] = (byte)(Strength >> 8);
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }
    }
}
