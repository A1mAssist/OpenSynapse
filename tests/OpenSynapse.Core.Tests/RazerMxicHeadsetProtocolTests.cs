using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerMxicHeadsetProtocolTests
{
    [Fact]
    public void RequestUsesTheMxicEnvelope()
    {
        var report = RazerMxicHeadsetProtocol.CreateRequest([0x21, 0x00, 0x00]);

        Assert.Equal(64, report.Length);
        Assert.Equal(2, report[0]);
        Assert.Equal(0x80, report[1]);
        Assert.Equal(8, report[2]);
        Assert.Equal(0x50, report[5]);
        Assert.Equal(0x41, report[6]);
        Assert.Equal(8, report[7]);
        Assert.Equal(3, report[9]);
        Assert.Equal(new byte[] { 0x21, 0x00, 0x00 }, report[10..13]);
    }

    [Fact]
    public void RemoteModeUsesCommandType14()
    {
        var report = RazerMxicHeadsetProtocol.CreateRemoteMode(true);

        Assert.Equal(14, report[7]);
        Assert.Equal(2, report[9]);
        Assert.Equal(0xE1, report[10]);
        Assert.Equal(1, report[11]);
    }

    [Fact]
    public void ResponseCanCarryAStatusPrefix()
    {
        var response = BuildResponse((0x21, [0x21, 0x01, 0x01, 73]));
        var withStatus = new byte[response.Length + 1];
        withStatus[0] = RazerMxicHeadsetProtocol.StatusOk;
        response.CopyTo(withStatus, 1);

        Assert.True(RazerMxicHeadsetProtocol.TryReadValue(
            withStatus, RazerMxicHeadsetProtocol.BatteryHeader, out var value));
        Assert.Equal(new byte[] { 73 }, value);
    }

    [Fact]
    public void ResponseSkipsEarlierSubResponses()
    {
        var response = BuildResponse(
            (0x2A, [0x2A, 0x01, 0x01, 0]),
            (0x21, [0x21, 0x01, 0x01, 88]));

        Assert.True(RazerMxicHeadsetProtocol.TryReadValue(
            response, RazerMxicHeadsetProtocol.BatteryHeader, out var value));
        Assert.Equal(new byte[] { 88 }, value);
    }

    [Fact]
    public void ResponseRejectsAnUnexpectedHeader()
    {
        var response = BuildResponse((0x2A, [0x2A, 0x01, 0x01, 1]));

        Assert.False(RazerMxicHeadsetProtocol.TryReadValue(
            response, RazerMxicHeadsetProtocol.BatteryHeader, out _));
    }

    [Fact]
    public void FirmwareUsesTheFirstFourVersionBytes()
    {
        Assert.Equal(
            new Version(1, 2, 3, 4),
            RazerMxicHeadsetProtocol.ReadFirmware([1, 2, 3, 4]));
    }

    [Fact]
    public void MxicPidIsNotAdmittedToTheBlackSharkV3Page()
    {
        Assert.False(BlackSharkHeadsetService.IsBlackSharkControlInterface(
            0x0555, "Razer BlackShark V2 Pro", 0xFF14, 1, 64));
    }

    [Fact]
    public async Task ReadStateReadsTheMxicValues()
    {
        var transport = new FakeTransport
        {
            Battery = 64,
            Charging = 1,
            Preset = 7,
            Bands = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
            MicMuted = true,
        };
        var service = new RazerMxicHeadsetService(transport);

        var state = await service.ReadStateAsync();

        Assert.Empty(state.Errors);
        Assert.Equal(new Version(1, 2, 3, 4), state.Firmware);
        Assert.Equal((byte)64, state.BatteryPercent);
        Assert.True(state.Charging);
        Assert.Equal((byte)7, state.EqPreset);
        Assert.Equal<byte>(transport.Bands, state.EqBands!);
        Assert.True(state.MicMuted);
        Assert.Null(state.Bass);
        Assert.Null(state.MicVolume);
        Assert.True(transport.SawRemoteMode);
    }

    [Fact]
    public async Task EqPresetWriteRestoresThePreviousValueWhenTheDeviceIgnoresIt()
    {
        var transport = new FakeTransport { Preset = 0, IgnorePresetWrites = true };
        var service = new RazerMxicHeadsetService(transport);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetEqPresetAsync(8));

        var writes = transport.Requests.Where(request => request[10] == 0x93).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(0, writes[^1][13]);
    }

    [Fact]
    public async Task EqBandsAndMicMuteWriteAndReadBack()
    {
        var transport = new FakeTransport();
        var service = new RazerMxicHeadsetService(transport);
        byte[] bands = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

        await service.SetEqBandsAsync(bands);
        await service.SetMicMuteAsync(true);

        Assert.Equal(bands, transport.Bands);
        Assert.True(transport.MicMuted);
    }

    private static byte[] BuildResponse(params (byte Type, byte[] Data)[] subResponses)
    {
        var report = new byte[64];
        report[0] = RazerMxicHeadsetProtocol.ReportId;
        var position = 2;
        foreach (var (type, data) in subResponses)
        {
            report[position] = 0x50;
            report[position + 1] = 0x49;
            report[position + 2] = type;
            report[position + 3] = 0x01;
            report[position + 8] = (byte)(data.Length & 0xFF);
            report[position + 9] = (byte)(data.Length >> 8);
            data.CopyTo(report, position + 10);
            position += 10 + data.Length;
        }

        report[1] = (byte)(position - 2);
        return report;
    }

    private sealed class FakeTransport : IRazerMxicHeadsetTransport
    {
        public List<byte[]> Requests { get; } = [];
        public byte Battery { get; set; } = 50;
        public byte Charging { get; set; }
        public byte Preset { get; set; }
        public byte[] Bands { get; set; } = new byte[10];
        public bool MicMuted { get; set; }
        public bool IgnorePresetWrites { get; set; }
        public bool SawRemoteMode { get; private set; }

        public Task SendAsync(byte[] report, CancellationToken cancellationToken)
        {
            Requests.Add(report);
            if (report[7] == RazerMxicHeadsetProtocol.RemoteCommand)
            {
                SawRemoteMode = true;
            }

            return Task.CompletedTask;
        }

        public Task<byte[]?> QueryAsync(
            byte[] report,
            byte[] expectedHeader,
            CancellationToken cancellationToken)
        {
            Requests.Add(report);
            var command = report[10];
            var dataLength = report[12];
            var data = report.AsSpan(13, dataLength).ToArray();
            var responseReport = command switch
            {
                0x02 => BuildResponse((0x02, [0x02, 0x01, 0x18, 1, 2, 3, 4, .. new byte[20]])),
                0x21 => BuildResponse((0x21, [0x21, 0x01, 0x01, Battery])),
                0x2A => BuildResponse((0x2A, [0x2A, 0x01, 0x01, Charging])),
                0x13 => BuildResponse((0x13, [0x13, 0x01, 0x01, Preset])),
                0x93 => WritePreset(data),
                0x15 => BuildResponse((0x15, [0x15, 0x01, 0x0A, .. Bands])),
                0x95 => WriteBands(data),
                0x55 => BuildResponse((0x55, [0x55, 0x01, 0x01, MicMuted ? (byte)1 : (byte)0])),
                0xE9 => WriteMicMute(data),
                _ => null,
            };
            if (responseReport is null)
            {
                return Task.FromResult<byte[]?>(null);
            }

            return Task.FromResult(RazerMxicHeadsetProtocol.TryReadValue(
                responseReport, expectedHeader, out var value)
                ? value
                : null);
        }

        private byte[] WritePreset(byte[] data)
        {
            if (!IgnorePresetWrites)
            {
                Preset = data[0];
            }

            return BuildResponse((0x93, [0x93, 0x01, 0x01]));
        }

        private byte[] WriteBands(byte[] data)
        {
            Bands = data;
            return BuildResponse((0x95, [0x95, 0x01, 0x01]));
        }

        private byte[] WriteMicMute(byte[] data)
        {
            MicMuted = data[0] == 1;
            return BuildResponse((0xE9, [0xE9, 0x01, 0x01]));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
