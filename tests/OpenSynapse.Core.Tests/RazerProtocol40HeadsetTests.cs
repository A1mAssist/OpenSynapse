using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerProtocol40HeadsetTests
{
    [Fact]
    public void BatteryRequestUsesTheBundleEnvelope()
    {
        // bundle 对齐: rzDevice40Headset _createDataSend, 64-byte report id 2.
        var report = RazerProtocol40HeadsetProtocol.CreateGetBattery();

        Assert.Equal(64, report.Length);
        Assert.Equal(2, report[0]);
        Assert.Equal(0, report[1]);
        Assert.Equal(0x60, report[2]);
        Assert.Equal(0, report[5]);
        Assert.Equal(0, report[6]);
        Assert.Equal(7, report[7]);
        Assert.Equal(0x80, report[8]);
        Assert.Equal(Xor(report), report[62]);
        Assert.Equal(0, report[63]);
    }

    [Fact]
    public void ChargingRequestCarriesTheBatteryIndex()
    {
        // bundle 对齐: _e allocates DW[0] bytes and writes battery index 0.
        var report = RazerProtocol40HeadsetProtocol.CreateGetCharging();

        Assert.Equal(2, report[6]);
        Assert.Equal(7, report[7]);
        Assert.Equal(0x84, report[8]);
        Assert.Equal(0, report[9]);
        Assert.Equal(0, report[10]);
        Assert.Equal(Xor(report), report[62]);
    }

    [Fact]
    public void ConnectRequestMatchesTheBundleWakeCommand()
    {
        // bundle 对齐: rzDevice40Headset.connectDevice writes [0,0,135] once.
        var report = RazerProtocol40HeadsetProtocol.CreateConnect();

        Assert.Equal(new byte[] { 0x00, 0x00, 0x87 }, report[6..9]);
        Assert.Equal(Xor(report), report[62]);
    }

    [Fact]
    public void ResponseReadsTheProtocol40Payload()
    {
        var request = RazerProtocol40HeadsetProtocol.CreateGetBattery();
        var response = BuildResponse(request, 2, [64, 64]);

        Assert.True(RazerProtocol40HeadsetProtocol.TryReadValue(response, request, out var value));
        Assert.Equal(new byte[] { 64, 64 }, value);
        Assert.Equal((byte)64, RazerProtocol40HeadsetProtocol.ReadBatteryPercent(value));
    }

    [Fact]
    public void ResponseRejectsMismatchedCommand()
    {
        var request = RazerProtocol40HeadsetProtocol.CreateGetBattery();
        var response = BuildResponse(request, 2, [64, 64]);
        response[7] = 8;

        Assert.False(RazerProtocol40HeadsetProtocol.TryReadValue(response, request, out _));
    }

    [Fact]
    public async Task ReadStateReadsBatteryAndCharging()
    {
        var transport = new FakeTransport { Battery = 64, Charging = 1 };
        var service = new RazerMxicHeadsetService(transport, 0x0593);

        var state = await service.ReadStateAsync();

        Assert.Empty(state.Errors);
        Assert.Equal((byte)64, state.BatteryPercent);
        Assert.True(state.Charging);
        Assert.Null(state.EqPreset);
        Assert.Null(state.EqBands);
        Assert.Null(state.MicMuted);
        Assert.Equal(2, transport.Requests.Count);
        Assert.Equal(1, transport.Connects);
    }

    [Fact]
    public async Task WritesAreRejectedBecauseTheBundleExposesNoEqOrMute()
    {
        var service = new RazerMxicHeadsetService(new FakeTransport(), 0x0593);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetEqPresetAsync(1));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetEqBandsAsync([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetMicMuteAsync(true));
    }

    [Fact]
    public void OnlyTheProtocol40ProductSelectsTheDialect()
    {
        Assert.True(RazerMxicHeadsetService.IsSupported(0x0593));
        Assert.False(RazerMxicHeadsetProtocol.IsSupported(0x0593));
    }

    private static byte[] BuildResponse(byte[] request, byte status, byte[] data)
    {
        var response = new byte[64];
        response[0] = request[0];
        response[1] = status;
        response[2] = request[2];
        response[5] = request[5];
        response[6] = (byte)data.Length;
        response[7] = request[7];
        response[8] = request[8];
        data.CopyTo(response, 9);
        response[62] = Xor(response);
        return response;
    }

    private static byte Xor(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 3; index < 62; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private sealed class FakeTransport : IRazerMxicHeadsetTransport
    {
        public List<byte[]> Requests { get; } = [];
        public int Connects { get; private set; }
        public byte Battery { get; set; } = 50;
        public byte? Charging { get; set; } = 1;

        public Task<byte[]?> QueryAsync(
            byte[] report,
            byte[] expectedHeader,
            CancellationToken cancellationToken)
        {
            Requests.Add(report.ToArray());
            if (expectedHeader.Length != 0)
            {
                return Task.FromResult<byte[]?>(null);
            }

            byte[]? payload = (report[7], report[8]) switch
            {
                (7, 0x80) => [Battery, Battery],
                (7, 0x84) when Charging is byte charging => [(byte)0, charging],
                _ => null,
            };
            return Task.FromResult(payload);
        }

        public Task SendAsync(byte[] report, CancellationToken cancellationToken)
        {
            Connects++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
