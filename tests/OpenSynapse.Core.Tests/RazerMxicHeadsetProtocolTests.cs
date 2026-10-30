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
        var service = new RazerMxicHeadsetService(transport, RazerMxicHeadsetProtocol.ProductId);

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
        var service = new RazerMxicHeadsetService(transport, RazerMxicHeadsetProtocol.ProductId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetEqPresetAsync(8));

        var writes = transport.Requests.Where(request => request[10] == 0x93).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(0, writes[^1][13]);
    }

    [Fact]
    public async Task EqBandsAndMicMuteWriteAndReadBack()
    {
        var transport = new FakeTransport();
        var service = new RazerMxicHeadsetService(transport, RazerMxicHeadsetProtocol.ProductId);
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

    [Fact]
    public void Class18EqPresetRequestUsesTheRzDevice40Tuple()
    {
        var report = RazerMxicHeadsetProtocol.CreateGetEqPresetClass18();

        Assert.Equal(64, report.Length);
        Assert.Equal(2, report[0]);
        Assert.Equal(0x50, report[5]);
        Assert.Equal(0x41, report[6]);
        Assert.Equal(8, report[7]);
        Assert.Equal(5, report[9]);
        Assert.Equal(new byte[] { 0x02, 0x18, 0x80, 0x00, 0x00 }, report[10..15]);
    }

    [Fact]
    public void Class18EqPresetSetCarriesProfileSourceAndPreset()
    {
        var report = RazerMxicHeadsetProtocol.CreateSetEqPresetClass18(0x03);

        Assert.Equal(7, report[9]);
        Assert.Equal(new byte[] { 0x04, 0x18, 0x00, 0x00, 0x01, 0x00, 0x03 }, report[10..17]);
    }

    [Fact]
    public void Class18EqBandsUseTheTwelveBytePayload()
    {
        byte[] bands = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        var set = RazerMxicHeadsetProtocol.CreateSetEqBandsClass18(bands);
        Assert.Equal(15, set[9]);
        Assert.Equal(new byte[] { 0x0C, 0x18, 0x01, 0x00, 0x01, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, set[10..25]);

        var read = RazerMxicHeadsetProtocol.CreateGetEqBandsClass18();
        Assert.Equal(15, read[9]);
        Assert.Equal(new byte[] { 0x0C, 0x18, 0x81, 0x00 }, read[10..14]);
        Assert.All(read[14..25], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Class18MicMuteUsesTheMicrophoneSource()
    {
        var read = RazerMxicHeadsetProtocol.CreateGetMicMuteClass18();
        Assert.Equal(new byte[] { 0x02, 0x18, 0x84, 0x00, 0x02 }, read[10..15]);

        var set = RazerMxicHeadsetProtocol.CreateSetMicMuteClass18(true);
        Assert.Equal(6, set[9]);
        Assert.Equal(new byte[] { 0x03, 0x18, 0x04, 0x00, 0x02, 0x01 }, set[10..16]);
    }

    [Fact]
    public void Class18PresetReplySkipsTheGroupByte()
    {
        // TryReadValue already stripped the [profile, source] header, so the reply value
        // starts at the equalizer group number.
        Assert.Equal(3, RazerMxicHeadsetProtocol.ReadEqPresetClass18([0x02, 0x03]));
        Assert.Throws<InvalidDataException>(
            () => RazerMxicHeadsetProtocol.ReadEqPresetClass18([0x02]));
    }

    [Fact]
    public async Task Class18ServiceReadsAndWritesThroughTheDialect()
    {
        var transport = new FakeTransport { Preset = 1 };
        var service = new RazerMxicHeadsetService(transport, 0x0A61);

        var state = await service.ReadStateAsync();
        Assert.Equal((byte)1, state.EqPreset);
        Assert.False(state.MicMuted);

        await service.SetEqPresetAsync(3);
        Assert.Equal((byte)3, transport.Preset);

        await service.SetMicMuteAsync(true);
        Assert.True(transport.MicMuted);

        var read = transport.Requests.First(request => request[11] == 0x18 && request[12] == 0x80);
        Assert.Equal(new byte[] { 0x02, 0x18, 0x80, 0x00, 0x00 }, read[10..15]);
    }

    [Fact]
    public void ValkyrieRequestsUseReportId7()
    {
        var getPreset = RazerMxicHeadsetProtocol.CreateGetEqPresetValkyrie();
        Assert.Equal(7, getPreset[0]);
        Assert.Equal(5, getPreset[9]);
        Assert.Equal(new byte[] { 0x02, 0x18, 0x80, 0x00, 0x00 }, getPreset[10..15]);

        var setPreset = RazerMxicHeadsetProtocol.CreateSetEqPresetValkyrie(0x02);
        Assert.Equal(7, setPreset[0]);
        Assert.Equal(7, setPreset[9]);
        Assert.Equal(new byte[] { 0x04, 0x18, 0x00, 0x00, 0x00, 0x00, 0x02 }, setPreset[10..17]);

        var getBands = RazerMxicHeadsetProtocol.CreateGetEqBandsValkyrie();
        Assert.Equal(new byte[] { 0x02, 0x18, 0x81, 0x00, 0x00 }, getBands[10..15]);

        var getMute = RazerMxicHeadsetProtocol.CreateGetMicMuteClass18(
            RazerMxicHeadsetProtocol.ValkyrieReportId);
        Assert.Equal(7, getMute[0]);
        Assert.Equal(new byte[] { 0x02, 0x18, 0x84, 0x00, 0x02 }, getMute[10..15]);

        byte[] bands = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var setBands = RazerMxicHeadsetProtocol.CreateSetEqBandsValkyrie(bands);
        Assert.Equal(7, setBands[0]);
        Assert.Equal(15, setBands[9]);
        Assert.Equal(
            new byte[] { 0x0C, 0x18, 0x01, 0x00, 0x00, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            setBands[10..25]);
    }

    [Fact]
    public async Task ValkyrieServiceReadsAndWritesEqOverReportId7()
    {
        var transport = new FakeTransport { Preset = 1 };
        var service = new RazerMxicHeadsetService(transport, 0x0A54);

        var state = await service.ReadStateAsync();
        Assert.Equal((byte)1, state.EqPreset);
        // The Valkyrie T3 chunk has no mute setter, so the page leaves the toggle out.
        Assert.Null(state.MicMuted);

        await service.SetEqPresetAsync(2);
        Assert.Equal((byte)2, transport.Preset);

        var read = transport.Requests.First(request => request[0] == 7 && request[11] == 0x18);
        Assert.Equal(new byte[] { 0x02, 0x18, 0x80, 0x00, 0x00 }, read[10..15]);
        Assert.DoesNotContain(transport.Requests, request => request[0] is not (2 or 7));
    }

    [Fact]
    public async Task ValkyrieMicMuteWriteIsRejectedBeforeItReachesTheDevice()
    {
        var transport = new FakeTransport();
        var service = new RazerMxicHeadsetService(transport, 0x0A54);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetMicMuteAsync(true));

        Assert.DoesNotContain(
            transport.Requests,
            request => request[11] == 0x18 && request[12] == 0x04);
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
                return Task.CompletedTask;
            }

            // Valkyrie T3 setters are fire-and-forget; emulate the device state so the
            // service's readback can verify the write.
            if (report.Length >= 13 && report[11] == 0x18)
            {
                ApplyClass18Payload(report);
            }

            return Task.CompletedTask;
        }

        private void ApplyClass18Payload(byte[] report)
        {
            var id = report[12];
            var payload = report.AsSpan(13, report[10]).ToArray();
            switch (id)
            {
                case 0x00 when payload.Length >= 4 && !IgnorePresetWrites:
                    Preset = payload[3];
                    break;
                case 0x01 when payload.Length >= 12:
                    Bands = payload[2..].ToArray();
                    break;
                case 0x04 when payload.Length >= 3:
                    MicMuted = payload[2] == 1;
                    break;
            }
        }

        public Task<byte[]?> QueryAsync(
            byte[] report,
            byte[] expectedHeader,
            CancellationToken cancellationToken)
        {
            Requests.Add(report);
            if (report[11] == 0x18)
            {
                return Task.FromResult(QueryClass18(report, expectedHeader));
            }

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

            // The input report id echoes the request, so report id 7 devices must be read
            // with their own id instead of the 0555 constant.
            responseReport[0] = report[0];
            return Task.FromResult(RazerMxicHeadsetProtocol.TryReadValue(
                responseReport, report[0], expectedHeader, out var value)
                ? value
                : null);
        }

        private byte[]? QueryClass18(byte[] report, byte[] expectedHeader)
        {
            var id = report[12];
            var payload = report.AsSpan(13, report[10]).ToArray();
            // rzDevice40 replies echo [profile, source=1]; ValkyrieT3 echoes [profile, group=0].
            var prefix = report[0] == RazerMxicHeadsetProtocol.ValkyrieReportId ? (byte)0x00 : (byte)0x01;
            var responseReport = id switch
            {
                0x80 => BuildResponse((0x18, [0x00, prefix, 0x00, Preset])),
                0x00 => WritePreset18(payload, prefix),
                0x81 => BuildResponse((0x18, [0x00, prefix, .. Bands])),
                0x01 => WriteBands18(payload, prefix),
                0x84 => BuildResponse((0x18, [0x00, 0x02, MicMuted ? (byte)1 : (byte)0])),
                0x04 => WriteMicMute18(payload),
                _ => null,
            };
            if (responseReport is null)
            {
                return null;
            }

            responseReport[0] = report[0];
            return RazerMxicHeadsetProtocol.TryReadValue(
                responseReport, report[0], expectedHeader, out var value)
                ? value
                : null;
        }

        private byte[] WritePreset18(byte[] payload, byte prefix)
        {
            if (!IgnorePresetWrites)
            {
                Preset = payload[3];
            }

            return BuildResponse((0x18, [0x00, prefix, 0x00, Preset]));
        }

        private byte[] WriteBands18(byte[] payload, byte prefix)
        {
            Bands = payload[2..];
            return BuildResponse((0x18, [0x00, prefix, .. Bands]));
        }

        private byte[] WriteMicMute18(byte[] payload)
        {
            MicMuted = payload[2] == 1;
            return BuildResponse((0x18, [0x00, 0x02, MicMuted ? (byte)1 : (byte)0]));
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
