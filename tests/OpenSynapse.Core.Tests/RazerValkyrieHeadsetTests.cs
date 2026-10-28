using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerValkyrieHeadsetTests
{
    [Fact]
    public void RequestUsesTheBundleEnvelope()
    {
        // bundle 对齐: general sender, fixed 0x60 txn, byte 9 = 0 for wired/dongle audio.
        var report = RazerValkyrieHeadsetProtocol.CreateGetBattery();

        Assert.Equal(64, report.Length);
        Assert.Equal(2, report[0]);
        Assert.Equal(0x60, report[2]);
        Assert.Equal(4, report[6]);
        Assert.Equal(0, report[9]);
        Assert.Equal(new byte[] { 0x21, 0x00, 0x00 }, report[10..13]);
        Assert.Equal(Xor(report), report[62]);
        Assert.Equal(0, report[63]);
    }

    [Fact]
    public void EqPresetWriteUsesTheBundleTripletAndPayloadOffset()
    {
        // bundle 对齐: 93 00 01 + preset at 13.
        var report = RazerValkyrieHeadsetProtocol.CreateSetEqPreset(7);

        Assert.Equal(5, report[6]);
        Assert.Equal(new byte[] { 0x93, 0x00, 0x01 }, report[10..13]);
        Assert.Equal(7, report[13]);
        Assert.Equal(Xor(report), report[62]);
    }

    [Fact]
    public void EqBandsWithProfilePutsTheProfileBeforeTheTenBands()
    {
        // bundle 对齐: bo reads data[1..11] as the ten bands; fU writes profile at a[0].
        byte[] bands = [140, 12, 0, 1, 2, 3, 4, 5, 6, 7];

        var report = RazerValkyrieHeadsetProtocol.CreateSetEqBands(bands, withProfile: true);

        Assert.Equal(15, report[6]);
        Assert.Equal(new byte[] { 0x95, 0x00, 0x0B }, report[10..13]);
        Assert.Equal(0, report[13]);
        Assert.Equal(bands, report[14..24]);
        Assert.Equal(
            bands,
            RazerValkyrieHeadsetProtocol.ReadEqBands([0, .. bands], withProfile: true));
    }

    [Fact]
    public void EqBandsBareUsesOnlyTheTenBands()
    {
        // Bare alternative kept for the 10-byte shape; no current bundle selects it.
        byte[] bands = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        var report = RazerValkyrieHeadsetProtocol.CreateSetEqBands(bands, withProfile: false);

        Assert.Equal(14, report[6]);
        Assert.Equal(new byte[] { 0x95, 0x00, 0x0A }, report[10..13]);
        Assert.Equal(bands, report[13..23]);
        Assert.Equal(
            bands,
            RazerValkyrieHeadsetProtocol.ReadEqBands(bands, withProfile: false));
    }

    [Fact]
    public void SignMagnitudeEncodingMatchesTheBundle()
    {
        // bundle 对齐: level < 0 ? 128 + abs(level) : level.
        Assert.Equal((byte)140, RazerValkyrieHeadsetProtocol.EncodeEqBandLevel(-12));
        Assert.Equal((byte)12, RazerValkyrieHeadsetProtocol.EncodeEqBandLevel(12));
        Assert.Equal((byte)0, RazerValkyrieHeadsetProtocol.EncodeEqBandLevel(0));
        Assert.Equal((sbyte)-12, RazerValkyrieHeadsetProtocol.DecodeEqBandLevel(140));
        Assert.Equal((sbyte)12, RazerValkyrieHeadsetProtocol.DecodeEqBandLevel(12));
    }

    [Fact]
    public void ResponseReadsStatusAndDataAtOffsetThirteen()
    {
        // bundle 对齐: _getElectronTransferInResultGeneral reads o[1], then o.slice(10 + 3, ...).
        var response = BuildResponse(
            2,
            [0x15, 0x01, 0x0B],
            [0x00, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);

        Assert.True(RazerValkyrieHeadsetProtocol.TryReadResponse(
            response,
            RazerValkyrieHeadsetProtocol.EqBandsWithProfileHeader,
            out var status,
            out var data));
        Assert.Equal(2, status);
        Assert.Equal(11, data.Length);
        Assert.Equal(new byte[] { 0x00, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, data);
    }

    [Fact]
    public void ResponseRejectsAnUnexpectedHeader()
    {
        var response = BuildResponse(2, [0x2A, 0x01, 0x01], [1]);

        Assert.False(RazerValkyrieHeadsetProtocol.TryReadResponse(
            response,
            RazerValkyrieHeadsetProtocol.BatteryHeader,
            out _,
            out _));
    }

    [Fact]
    public void ResponseUsesTheExpectedHeaderLength()
    {
        // bundle 对齐: t.data length comes from the expected response header's third byte.
        var response = BuildResponse(
            2,
            [0x17, 0x01, 0x0A],
            [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xAA, 0xBB]);

        Assert.True(RazerValkyrieHeadsetProtocol.TryReadResponse(
            response,
            RazerValkyrieHeadsetProtocol.MicBandsHeader,
            out _,
            out var data));
        Assert.Equal(10, data.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, data);
    }

    [Fact]
    public void ResponseStatusesMatchTheBundle()
    {
        Assert.True(RazerValkyrieHeadsetProtocol.IsSuccess(2));
        Assert.True(RazerValkyrieHeadsetProtocol.IsResend(1));
        Assert.True(RazerValkyrieHeadsetProtocol.IsResend(3));
        Assert.False(RazerValkyrieHeadsetProtocol.IsResend(2));
    }

    [Fact]
    public void IsSupportedCoversOnlyTheAudioValkyrieAndBillieIds()
    {
        Assert.True(RazerValkyrieHeadsetService.IsSupported(0x0579));
        Assert.True(RazerValkyrieHeadsetService.IsSupported(0x0A4E));
        Assert.True(RazerValkyrieHeadsetService.IsSupported(0x0A51));
        Assert.True(RazerValkyrieHeadsetService.IsSupported(0x101E));
        Assert.True(RazerValkyrieHeadsetService.IsSupported(0x101F));
        Assert.True(RazerValkyrieHeadsetService.IsSupported(0x0567));
        Assert.True(RazerValkyrieHeadsetService.IsSupported(0x056B));
        Assert.False(RazerValkyrieHeadsetService.IsSupported(0x0576));
        Assert.False(RazerValkyrieHeadsetService.IsSupported(0x0555));
    }

    [Fact]
    public async Task ReadStateReadsBatteryPresetAndBands()
    {
        var transport = new FakeTransport
        {
            Battery = 64,
            Charging = 1,
            Preset = 7,
            Bands = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
        };
        var service = new RazerValkyrieHeadsetService(transport, 0x0567);

        var state = await service.ReadStateAsync();

        Assert.Empty(state.Errors);
        Assert.Equal((byte)64, state.BatteryPercent);
        Assert.True(state.Charging);
        Assert.Equal((byte)7, state.EqPreset);
        Assert.Equal<byte>(transport.Bands, state.EqBands!);
        Assert.Null(state.Firmware);
        Assert.Null(state.Bass);
        Assert.Null(state.MicMuted);
        Assert.Null(state.MicVolume);
        Assert.Null(state.AudioSource);
        Assert.Null(state.NoiseSuppression);
        Assert.Null(state.AncState);
        Assert.Null(state.AncLevel);
    }

    [Fact]
    public async Task MissingChargingResponseStaysNull()
    {
        var transport = new FakeTransport { Battery = 50, Charging = null, Preset = 1 };
        var service = new RazerValkyrieHeadsetService(transport, 0x0A4E);

        var state = await service.ReadStateAsync();

        Assert.Null(state.Charging);
        Assert.Contains(state.Errors, error => error.StartsWith("charging:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChargingZeroIsFalseWhenTheCommandAnswers()
    {
        var transport = new FakeTransport { Battery = 50, Charging = 0, Preset = 1 };
        var service = new RazerValkyrieHeadsetService(transport, 0x0A4E);

        var state = await service.ReadStateAsync();

        Assert.False(state.Charging);
        Assert.DoesNotContain(
            state.Errors,
            error => error.StartsWith("charging:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BatteryResendIsRetriedBeforeTheValueIsAccepted()
    {
        var transport = new FakeTransport { Battery = 73, ResendBatteryOnce = true };
        var service = new RazerValkyrieHeadsetService(transport, 0x0A4E);

        var state = await service.ReadStateAsync();

        Assert.Equal((byte)73, state.BatteryPercent);
        Assert.Equal(2, transport.Requests.Count(request => request[10] == 0x21));
    }

    [Theory]
    [InlineData(0x0A4E, true)]
    [InlineData(0x0A51, true)]
    [InlineData(0x101E, true)]
    [InlineData(0x101F, true)]
    [InlineData(0x0579, true)]
    [InlineData(0x0567, true)]
    [InlineData(0x056B, true)]
    public async Task ProfileSelectsTheBundledEqBandShape(ushort productId, bool withProfile)
    {
        var transport = new FakeTransport
        {
            Bands = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
        };
        var service = new RazerValkyrieHeadsetService(transport, productId);

        await service.SetEqBandsAsync([11, 12, 13, 14, 15, 16, 17, 18, 19, 20]);

        var write = transport.Requests.Single(request => request[10] == 0x95);
        Assert.Equal(withProfile ? (byte)0x0B : (byte)0x0A, write[12]);
        Assert.Equal(withProfile ? (byte)15 : (byte)14, write[6]);
        if (withProfile)
        {
            Assert.Equal(0, write[13]);
            Assert.Equal(new byte[] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, write[14..24]);
        }
        else
        {
            Assert.Equal(new byte[] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }, write[13..23]);
        }
    }

    [Theory]
    [InlineData(0x0579)]
    [InlineData(0x0A4E)]
    [InlineData(0x0567)]
    [InlineData(0x056B)]
    public async Task ProfileFramesShareTheFixedTransactionAndZeroDirection(ushort productId)
    {
        // bundle 对齐: Valkyrie, T1Wired and T2 all leave byte 9 zero for audio commands.
        var transport = new FakeTransport { Preset = 0 };
        var service = new RazerValkyrieHeadsetService(transport, productId);

        await service.SetEqPresetAsync(5);

        var write = transport.Requests.Single(request => request[10] == 0x93);
        Assert.Equal(2, write[0]);
        Assert.Equal(0x60, write[2]);
        Assert.Equal(0, write[9]);
        Assert.Equal(new byte[] { 0x93, 0x00, 0x01 }, write[10..13]);
        Assert.Equal(5, write[13]);
    }

    [Fact]
    public async Task EqPresetWriteRestoresThePreviousValueWhenIgnored()
    {
        var transport = new FakeTransport { Preset = 0, IgnorePresetWrites = true };
        var service = new RazerValkyrieHeadsetService(transport, 0x0567);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetEqPresetAsync(8));

        var writes = transport.Requests.Where(request => request[10] == 0x93).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(0, writes[^1][13]);
    }

    [Fact]
    public async Task EqBandsWriteRestoresThePreviousValuesWhenReadbackMismatches()
    {
        byte[] previous = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var transport = new FakeTransport { Bands = previous, IgnoreBandWrites = true };
        var service = new RazerValkyrieHeadsetService(transport, 0x0567);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetEqBandsAsync([11, 12, 13, 14, 15, 16, 17, 18, 19, 20]));

        var writes = transport.Requests.Where(request => request[10] == 0x95).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(0, writes[^1][13]);
        Assert.Equal(previous, writes[^1][14..24]);
    }

    [Fact]
    public async Task EqBandBytesPassThroughWithoutUiMapping()
    {
        var transport = new FakeTransport();
        var service = new RazerValkyrieHeadsetService(transport, 0x0567);
        byte[] raw = [140, 12, 0, 1, 2, 3, 4, 5, 6, 7];

        await service.SetEqBandsAsync(raw);

        Assert.Equal(raw, transport.Bands);
    }

    [Fact]
    public void MicProtocolTuplesMatchTheBundle()
    {
        Assert.Equal(new byte[] { 0x16, 0x00, 0x00 }, RazerValkyrieHeadsetProtocol.CreateGetMicPreset()[10..13]);
        Assert.Equal(new byte[] { 0x96, 0x00, 0x01 }, RazerValkyrieHeadsetProtocol.CreateSetMicPreset(3)[10..13]);
        Assert.Equal(new byte[] { 0x17, 0x00, 0x00 }, RazerValkyrieHeadsetProtocol.CreateGetMicBands()[10..13]);
        Assert.Equal(
            new byte[] { 0x97, 0x00, 0x0A },
            RazerValkyrieHeadsetProtocol.CreateSetMicBands([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])[10..13]);
        Assert.Equal(new byte[] { 0x55, 0x00, 0x00 }, RazerValkyrieHeadsetProtocol.CreateGetMicMute()[10..13]);
        Assert.Equal(new byte[] { 0xE9, 0x00, 0x01 }, RazerValkyrieHeadsetProtocol.CreateSetMicMute(true)[10..13]);
    }

    private static byte[] BuildResponse(byte status, byte[] header, byte[] data)
    {
        var response = new byte[64];
        response[0] = RazerValkyrieHeadsetProtocol.ReportId;
        response[1] = status;
        response[2] = RazerValkyrieHeadsetProtocol.TransactionId;
        header.CopyTo(response, 10);
        data.CopyTo(response, 13);
        response[62] = Xor(response);
        return response;
    }

    private static byte Xor(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 0; index < 62; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }

    private sealed class FakeTransport : IRazerHeadsetHidTransport
    {
        public List<byte[]> Requests { get; } = [];
        public byte Battery { get; set; } = 50;
        public byte? Charging { get; set; } = 1;
        public byte Preset { get; set; }
        public byte[] Bands { get; set; } = new byte[10];
        public byte MicPreset { get; set; }
        public byte[] MicBands { get; set; } = new byte[10];
        public bool MicMuted { get; set; }
        public bool IgnorePresetWrites { get; set; }
        public bool IgnoreBandWrites { get; set; }
        public bool ResendBatteryOnce { get; set; }
        private bool _batteryResent;

        public Task<byte[]?> QueryAsync(
            byte[] report,
            Func<byte[], bool> isMatch,
            CancellationToken cancellationToken)
        {
            Requests.Add(report.ToArray());
            var command = report[10];
            if (command == 0x2A && Charging is null)
            {
                return Task.FromResult<byte[]?>(null);
            }

            var status = (byte)2;
            if (command == 0x21 && ResendBatteryOnce && !_batteryResent)
            {
                _batteryResent = true;
                status = 1;
            }

            var response = Build(command, report, status);
            return Task.FromResult(
                response is not null && isMatch(response) ? response : null);
        }

        private byte[]? Build(byte command, byte[] request, byte status)
        {
            var withProfile = command == 0x95
                ? request[12] == 0x0B
                : request[12] == 1;
            return command switch
            {
                0x21 => Response(status, [0x21, 0x01, 0x01], [Battery]),
                0x2A => Response(status, [0x2A, 0x01, 0x01], [Charging!.Value]),
                0x13 => Response(status, [0x13, 0x01, 0x01], [Preset]),
                0x93 => WritePreset(request),
                0x15 => ReadBands(withProfile),
                0x95 => WriteBands(request, withProfile),
                0x16 => Response(status, [0x16, 0x01, 0x01], [MicPreset]),
                0x96 => WriteMicPreset(request),
                0x17 => Response(status, [0x17, 0x01, 0x0A], MicBands),
                0x97 => WriteMicBands(request),
                0x55 => Response(status, [0x55, 0x01, 0x01], [MicMuted ? (byte)1 : (byte)0]),
                0xE9 => WriteMicMute(request),
                _ => null,
            };
        }

        private byte[] ReadBands(bool withProfile)
        {
            if (withProfile)
            {
                return Response(2, [0x15, 0x01, 0x0B], [0, .. Bands]);
            }

            return Response(2, [0x15, 0x01, 0x0A], Bands);
        }

        private byte[] WritePreset(byte[] request)
        {
            if (!IgnorePresetWrites)
            {
                Preset = request[13];
            }

            return Response(2, [0x93, 0x01, 0x01], [0]);
        }

        private byte[] WriteBands(byte[] request, bool withProfile)
        {
            var payload = request.AsSpan(13, request[12]).ToArray();
            if (!IgnoreBandWrites)
            {
                Bands = withProfile ? payload[1..] : payload;
            }

            return Response(2, [0x95, 0x01, 0x01], [0]);
        }

        private byte[] WriteMicPreset(byte[] request)
        {
            MicPreset = request[13];
            return Response(2, [0x96, 0x01, 0x01], [0]);
        }

        private byte[] WriteMicBands(byte[] request)
        {
            MicBands = request.AsSpan(13, request[12]).ToArray();
            return Response(2, [0x97, 0x01, 0x01], [0]);
        }

        private byte[] WriteMicMute(byte[] request)
        {
            MicMuted = request[13] == 1;
            return Response(2, [0xE9, 0x01, 0x01], [0]);
        }

        private static byte[] Response(byte status, byte[] header, byte[] data) =>
            BuildResponse(status, header, data);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
