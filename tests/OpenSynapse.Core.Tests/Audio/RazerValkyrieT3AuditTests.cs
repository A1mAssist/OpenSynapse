using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerValkyrieT3AuditTests
{
    [Fact]
    public void Feature64FixtureMatchesLocalVendorCreateDataSend()
    {
        // Independent source: 056B/rzDevice25ValkyrieT3.6a7d2e9e843af8d2634a.js
        // _createDataSend, profile/group 0, transaction 1, get preset [2,24,128].
        var fixture = Convert.FromHexString("0700010000000218800000" + new string('0', 102) + "9A00");
        Assert.Equal(fixture, RazerValkyrieT3Protocol.Create(0x80, [0, 0]));
        var groupOne = RazerMxicHeadsetProtocol.CreateGetEqPresetValkyrie();
        // 0A54/main.484322...js useHardwareEqualizer standard.index=1.
        fixture[10] = 1; fixture[62] = 0x9B;
        Assert.Equal(fixture, groupOne);
    }

    [Theory]
    [InlineData(0x0A54)] [InlineData(0x1022)]
    public async Task T3ServiceUsesFeatureFramesWithoutMxicRemoteAndAwaitsSetters(ushort productId)
    {
        var io = new FeatureIo();
        await using var transport = new RazerValkyrieT3Transport(io);
        await using var service = new RazerMxicHeadsetService(transport, productId);
        var state = await service.ReadStateAsync();
        Assert.Empty(state.Errors);
        Assert.Equal((byte)1, state.EqPreset);
        Assert.False(state.MicMuted);
        await service.SetEqPresetAsync(2);
        await service.SetEqBandsAsync([0, 1, 2, 3, 4, 5, 6, 129, 132, 134]);
        Assert.Equal((byte)2, io.Preset);
        Assert.All(io.Requests, request =>
        {
            Assert.Equal(64, request.Length); Assert.Equal(7, request[0]);
            Assert.Equal(0x18, request[7]);
        });
        Assert.Contains(io.Requests, r => r[6] == 3 && r[8] == 0x84 && r[9..12].SequenceEqual(new byte[] { 0, 2, 0 }));
        Assert.Equal(io.Requests.Count, io.AcknowledgedCommands);
        Assert.Equal(RazerHeadsetChannel.ValkyrieT3Feature64, RazerHeadsetChannelCatalog.Resolve(productId));
    }

    [Theory]
    [InlineData(2)] [InlineData(7)] [InlineData(8)]
    public async Task StaleTransactionClassOrCommandCannotCompleteAnExchange(int offset)
    {
        var io = new FeatureIo { WrongOffsetOnce = offset };
        await using var transport = new RazerValkyrieT3Transport(io);
        var value = await transport.QueryAsync(RazerMxicHeadsetProtocol.CreateGetEqPresetValkyrie(), [0, 1], default);
        Assert.Equal(new byte[] { 0, 1 }, value);
        Assert.Equal(2, io.Reads);
    }

    [Fact]
    public async Task BusyAckIsPolledButFailureIsNotReportedAsSaved()
    {
        var io = new FeatureIo { BusyOnce = true };
        await using var transport = new RazerValkyrieT3Transport(io);
        await transport.QueryAsync(RazerMxicHeadsetProtocol.CreateGetEqPresetValkyrie(), [0, 1], default);
        Assert.Equal(2, io.Reads);
        io.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => transport.QueryAsync(
            RazerMxicHeadsetProtocol.CreateSetEqPresetValkyrie(2), [], default));
    }

    [Fact]
    public async Task UnsupportedMuteSetterAndOutOfRangeBandsAreRejectedWithoutReports()
    {
        var io = new FeatureIo();
        await using var transport = new RazerValkyrieT3Transport(io);
        await using var service = new RazerMxicHeadsetService(transport, 0x0A54);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetMicMuteAsync(true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetEqBandsAsync(Enumerable.Repeat((byte)10, 10).ToArray()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetEqPresetAsync(0xFF));
        Assert.Empty(io.Requests);
    }

    [Fact]
    public async Task TransactionsWrapLikeTheLocalRzDevice25Base()
    {
        var io = new FeatureIo();
        await using var transport = new RazerValkyrieT3Transport(io);
        for (var index = 0; index < 32; index++)
            await transport.QueryAsync(RazerMxicHeadsetProtocol.CreateGetEqPresetValkyrie(), [0, 1], default);
        Assert.Equal(Enumerable.Range(0, 31).Select(i => (byte)i).Append((byte)0), io.Requests.Select(r => r[2]));
    }

    internal sealed class FeatureIo : IRazerValkyrieT3FeatureIo
    {
        internal List<byte[]> Requests { get; } = [];
        internal byte Preset = 1;
        private byte[] _bands = new byte[10];
        internal int? WrongOffsetOnce;
        internal bool BusyOnce, Fail;
        internal int Reads, AcknowledgedCommands;
        public void Send(byte[] report)
        {
            Requests.Add(report.ToArray());
            if (report[8] == 0) Preset = report[12];
            if (report[8] == 1) _bands = report[11..21];
        }
        public byte[] Read()
        {
            Reads++;
            var request = Requests[^1];
            var reply = request.ToArray(); reply[1] = 2;
            byte[] payload = request[8] switch
            {
                0x80 => [0, 1, 0, Preset], 0x81 => [0, 1, .. _bands],
                0x84 => [0, 2, 0], _ => [],
            };
            reply[6] = (byte)payload.Length; payload.CopyTo(reply, 9);
            if (WrongOffsetOnce is { } offset) { reply[offset]++; WrongOffsetOnce = null; }
            else if (BusyOnce) { reply[1] = 1; BusyOnce = false; }
            else { if (Fail) reply[1] = 3; AcknowledgedCommands++; }
            return reply;
        }
        public void Dispose() { }
    }
}
