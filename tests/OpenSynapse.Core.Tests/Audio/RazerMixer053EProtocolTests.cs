using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerMixer053EProtocolTests
{
    [Fact]
    public void FeatureReadFramesMatchTheAudioMixerBundle()
    {
        AssertFeatureFrame(
            RazerMixer053EProtocol.CreateGetFirmware(),
            [7, 0, 0, 0, 0, 0, 2, 0, 129, 0, 0, 0, 0, 0]);
        AssertFeatureFrame(
            RazerMixer053EProtocol.CreateGetSerialNumber(),
            [7, 0, 31, 0, 0, 0, 22, 0, 130, 0, 0, 0, 0, 0]);
        AssertFeatureFrame(
            RazerMixer053EProtocol.CreateGetDeviceMode(),
            [7, 0, 0, 0, 0, 0, 2, 0, 132, 0, 0, 0]);
        AssertFeatureFrame(
            RazerMixer053EProtocol.CreateGetBrightness(),
            [7, 0, 31, 0, 0, 0, 3, 15, 132, 0, 0, 0]);
    }

    [Fact]
    public void FeatureWriteFramesMatchTheAudioMixerBundle()
    {
        AssertFeatureFrame(
            RazerMixer053EProtocol.CreateSetChannelMute(3, muted: true),
            [7, 0, 31, 0, 0, 0, 3, 8, 16, 0, 3, 1]);
        AssertFeatureFrame(
            RazerMixer053EProtocol.CreateSetChannelMute(5, muted: false),
            [7, 0, 31, 0, 0, 0, 3, 8, 16, 0, 5, 0]);
        AssertFeatureFrame(
            RazerMixer053EProtocol.CreateSetBrightnessFrame(1, 4, 50),
            [7, 0, 31, 0, 0, 0, 3, 15, 4, 1, 4, 50]);
    }

    [Fact]
    public void BrightnessAllRegionUsesTheBundleFanOutOrder()
    {
        var frames = RazerMixer053EProtocol.CreateSetBrightnessFrames(0, 0, 100);

        Assert.Equal(
            RazerMixer053EProtocol.AllLightingRegions,
            frames.Select(frame => frame[10]).ToArray());
        Assert.All(
            frames,
            frame =>
            {
                Assert.Equal(RazerMixer053EProtocol.FeatureReportLength, frame.Length);
                Assert.Equal(RazerMixer053EProtocol.FeatureReportId, frame[0]);
                Assert.Equal(100, frame[11]);
            });
    }

    [Fact]
    public void SerialNumberUsesLittleEndianLengthAndAsciiPayloadOffset()
    {
        var response = new byte[RazerMixer053EProtocol.FeatureReportLength];
        response[0] = RazerMixer053EProtocol.FeatureReportId;
        response[1] = 0x02;
        response[2] = 0x1F;
        response[6] = 5;
        response[7] = 0;
        response[8] = 0x82;
        "SN001"u8.CopyTo(response.AsSpan(9));

        Assert.Equal("SN001", RazerMixer053EProtocol.ReadSerialNumber(response));
    }

    [Fact]
    public void HardwareStatusParsesOnlyTheReportNineStatusBytes()
    {
        var report = new byte[RazerMixer053EProtocol.StatusReportLength];
        report[0] = RazerMixer053EProtocol.StatusReportId;
        report[1] = 0xFF;
        report[2] = 0xAF;
        report[3] = 10;
        report[4] = 20;
        report[5] = 30;
        report[6] = 40;
        report[7] = 0x0B;

        Assert.True(RazerMixer053EProtocol.TryReadHardwareStatus(report, out var status));
        Assert.NotNull(status);
        var parsed = status!;
        Assert.True(parsed.Mute1);
        Assert.True(parsed.Mute2);
        Assert.True(parsed.Mute3);
        Assert.True(parsed.Mute4);
        Assert.True(parsed.Beep);
        Assert.True(parsed.MicMute);
        Assert.True(parsed.PhantomPower48V);
        Assert.True(parsed.MicJack);
        Assert.True(parsed.LineIn);
        Assert.Equal(10, parsed.Slider1);
        Assert.Equal(20, parsed.Slider2);
        Assert.Equal(30, parsed.Slider3);
        Assert.Equal(40, parsed.Slider4);
        Assert.True(parsed.IsMuted(5));
    }

    [Fact]
    public void HardwareStatusRejectsAReportShorterThanSixteenBytes()
    {
        var report = new byte[RazerMixer053EProtocol.StatusReportLength - 1];
        report[0] = RazerMixer053EProtocol.StatusReportId;

        Assert.False(RazerMixer053EProtocol.TryReadHardwareStatus(report, out _));
    }

    [Fact]
    public void MicMuteUsesTheSevenByteBundleCompanion()
    {
        AssertOutputFrame(
            RazerMixer053EProtocol.CreateMicMuteCompanion(muted: true),
            [3, 24, 0, 192, 148, 196, 192]);
        AssertOutputFrame(
            RazerMixer053EProtocol.CreateMicMuteCompanion(muted: false),
            [3, 24, 0, 192, 148, 68, 64]);
    }

    [Fact]
    public async Task ReadStateUsesTheFeaturePathAndReportNineStatus()
    {
        var fake = new FakeTransport
        {
            Firmware = new Version(1, 9),
            SerialNumber = "MIXER-053E",
            DeviceMode = 3,
            Brightness = 77,
            Slider1 = 11,
            Slider2 = 22,
            Slider3 = 33,
            Slider4 = 44,
            JackStatus = 0x0B,
        };
        fake.SetMuteStatus(1, muted: true);
        await using var service = new RazerMixer053EService(fake);

        var state = await service.ReadStateAsync();

        Assert.True(state.Available);
        Assert.Equal(new Version(1, 9), state.Firmware);
        Assert.Equal("MIXER-053E", state.SerialNumber);
        Assert.Equal((byte?)3, state.DeviceMode);
        Assert.Equal((byte?)77, state.Brightness);
        Assert.NotNull(state.Hardware);
        var hardware = state.Hardware!;
        Assert.True(hardware.Mute1);
        Assert.True(hardware.PhantomPower48V);
        Assert.Equal(11, hardware.Slider1);
        Assert.Equal(44, hardware.Slider4);
        Assert.Empty(state.Errors);
        AssertOutputFrame(fake.OutputWrites[0], [4, 95, 252, 0, 28]);
    }

    [Fact]
    public async Task ProbeReportsUnavailableWhenReportNineIsMissing()
    {
        var fake = new FakeTransport { StatusAvailable = false };
        await using var service = new RazerMixer053EService(fake);

        Assert.False(await service.ProbeAsync());
        Assert.Empty(fake.FeatureQueries);
    }

    [Fact]
    public async Task ProbeReportsUnavailableWhenTheConnectHandshakeCannotBeWritten()
    {
        var fake = new FakeTransport { HandshakeSucceeds = false };
        await using var service = new RazerMixer053EService(fake);

        Assert.False(await service.ProbeAsync());
        Assert.Empty(fake.FeatureQueries);
    }

    [Fact]
    public async Task ChannelMuteRestoresThePreviousValueWhenReadbackDoesNotChange()
    {
        var fake = new FakeTransport { IgnoreChannelMuteWrites = true };
        await using var service = new RazerMixer053EService(fake);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetChannelMuteAsync(2, muted: true));

        var muteWrites = fake.FeatureWrites.Where(report => report[8] == 0x10).ToArray();
        Assert.Equal(2, muteWrites.Length);
        Assert.Equal(2, muteWrites[0][10]);
        Assert.Equal(1, muteWrites[0][11]);
        Assert.Equal(2, muteWrites[1][10]);
        Assert.Equal(0, muteWrites[1][11]);
    }

    [Fact]
    public async Task MicMuteRestoresThePreviousValueWhenReadbackDoesNotChange()
    {
        var fake = new FakeTransport { IgnoreMicMuteWrites = true };
        await using var service = new RazerMixer053EService(fake);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetMicMuteAsync(muted: true));

        var muteWrites = fake.FeatureWrites.Where(report => report[8] == 0x10).ToArray();
        Assert.Equal(2, muteWrites.Length);
        Assert.Equal(5, muteWrites[0][10]);
        Assert.Equal(1, muteWrites[0][11]);
        Assert.Equal(5, muteWrites[1][10]);
        Assert.Equal(0, muteWrites[1][11]);

        var companions = fake.OutputWrites
            .Where(report => report[0] == 0x03 && report[1] == 0x18)
            .ToArray();
        Assert.Equal(2, companions.Length);
        Assert.Equal(196, companions[0][5]);
        Assert.Equal(68, companions[1][5]);
    }

    [Fact]
    public async Task BrightnessRestoresThePreviousValueWhenReadbackDoesNotChange()
    {
        var fake = new FakeTransport
        {
            Brightness = 40,
            IgnoreBrightnessWrites = true,
        };
        await using var service = new RazerMixer053EService(fake);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetBrightnessAsync(80, profileId: 1, regionId: 4));

        var brightnessWrites = fake.FeatureWrites
            .Where(report => report[7] == 0x0F && report[8] == 0x04)
            .ToArray();
        Assert.Equal(2, brightnessWrites.Length);
        Assert.Equal(1, brightnessWrites[0][9]);
        Assert.Equal(4, brightnessWrites[0][10]);
        Assert.Equal(80, brightnessWrites[0][11]);
        Assert.Equal(1, brightnessWrites[1][9]);
        Assert.Equal(4, brightnessWrites[1][10]);
        Assert.Equal(40, brightnessWrites[1][11]);
    }

    [Fact]
    public void MixerProductUsesItsOwnChannelInsteadOfFeature91()
    {
        Assert.Equal(
            RazerHeadsetChannel.Mixer053E,
            RazerHeadsetChannelCatalog.Resolve(RazerMixer053EProtocol.ProductId));
        Assert.False(RazerHeadsetAudioService.UsesFeatureReportTransport(RazerMixer053EProtocol.ProductId));
    }

    private static void AssertFeatureFrame(byte[] actual, byte[] prefix)
    {
        Assert.Equal(RazerMixer053EProtocol.FeatureReportLength, actual.Length);
        Assert.Equal(prefix, actual[..prefix.Length]);
        Assert.All(actual[prefix.Length..], value => Assert.Equal(0, value));
    }

    private static void AssertOutputFrame(byte[] actual, byte[] prefix)
    {
        Assert.Equal(RazerMixer053EProtocol.OutputReportLength, actual.Length);
        Assert.Equal(prefix, actual[..prefix.Length]);
        Assert.All(actual[prefix.Length..], value => Assert.Equal(0, value));
    }

    private sealed class FakeTransport : IRazerMixer053ETransport
    {
        private readonly List<byte[]> _featureWrites = [];
        private readonly List<byte[]> _featureQueries = [];
        private readonly List<byte[]> _outputWrites = [];
        private byte _buttonStatus;
        private byte _jackStatus;

        public bool StatusAvailable { get; init; } = true;

        public bool HandshakeSucceeds { get; init; } = true;

        public bool IgnoreChannelMuteWrites { get; init; }

        public bool IgnoreMicMuteWrites { get; init; }

        public bool IgnoreBrightnessWrites { get; init; }

        public Version Firmware { get; init; } = new(1, 0);

        public string SerialNumber { get; init; } = "SN0001";

        public byte DeviceMode { get; init; }

        public byte Brightness { get; set; }

        public byte Slider1 { get; init; }

        public byte Slider2 { get; init; }

        public byte Slider3 { get; init; }

        public byte Slider4 { get; init; }

        public byte JackStatus
        {
            get => _jackStatus;
            init => _jackStatus = value;
        }

        public IReadOnlyList<byte[]> FeatureWrites => _featureWrites;

        public IReadOnlyList<byte[]> FeatureQueries => _featureQueries;

        public IReadOnlyList<byte[]> OutputWrites => _outputWrites;

        public void SetMuteStatus(int channel, bool muted)
        {
            var bit = channel switch
            {
                1 => 0x01,
                2 => 0x02,
                3 => 0x04,
                4 => 0x08,
                5 => 0x20,
                _ => throw new ArgumentOutOfRangeException(nameof(channel)),
            };
            _buttonStatus = muted
                ? (byte)(_buttonStatus | bit)
                : (byte)(_buttonStatus & ~bit);
        }

        public Task SendFeatureAsync(byte[] report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(RazerMixer053EProtocol.FeatureReportLength, report.Length);
            Assert.Equal(RazerMixer053EProtocol.FeatureReportId, report[0]);
            _featureWrites.Add(report.ToArray());

            if (report[8] == 0x10)
            {
                var channel = report[10];
                var muted = report[11] != 0;
                if ((channel == 5 && !IgnoreMicMuteWrites) ||
                    (channel is >= 1 and <= 4 && !IgnoreChannelMuteWrites))
                {
                    SetMuteStatus(channel, muted);
                }
            }
            else if (report[7] == 0x0F && report[8] == 0x04 && !IgnoreBrightnessWrites)
            {
                Brightness = report[11];
            }

            return Task.CompletedTask;
        }

        public Task<byte[]?> QueryFeatureAsync(
            byte[] request,
            Func<byte[], bool> isMatch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(RazerMixer053EProtocol.FeatureReportLength, request.Length);
            _featureQueries.Add(request.ToArray());

            var response = request[8] switch
            {
                0x81 => BuildResponse(0x00, 0x81, (byte)Firmware.Major, (byte)Firmware.Minor),
                0x82 => BuildSerialResponse(),
                0x84 when request[7] == 0x00 => BuildResponse(0x00, 0x84, DeviceMode),
                0x84 when request[7] == 0x0F => BuildResponse(0x0F, 0x84, brightnessIndex: 11),
                _ => null,
            };
            return Task.FromResult(response is not null && isMatch(response) ? response : null);
        }

        public Task SendOutputAsync(byte[] report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(RazerMixer053EProtocol.OutputReportLength, report.Length);
            _outputWrites.Add(report.ToArray());

            if (report[0] == 0x04 && report[1] == 0x5F && !HandshakeSucceeds)
            {
                throw new IOException("Fake connect handshake failed.");
            }

            if (report[0] == 0x03 && report[1] == 0x18 && !IgnoreMicMuteWrites)
            {
                SetMuteStatus(5, report[5] == 196);
            }

            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StatusAvailable)
            {
                return Task.FromResult<byte[]?>(null);
            }

            var report = new byte[RazerMixer053EProtocol.StatusReportLength];
            report[0] = RazerMixer053EProtocol.StatusReportId;
            report[2] = _buttonStatus;
            report[3] = Slider1;
            report[4] = Slider2;
            report[5] = Slider3;
            report[6] = Slider4;
            report[7] = _jackStatus;
            return Task.FromResult<byte[]?>(report);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static byte[] BuildResponse(byte commandClass, byte commandId, params byte[] payload)
        {
            var response = new byte[RazerMixer053EProtocol.FeatureReportLength];
            response[0] = RazerMixer053EProtocol.FeatureReportId;
            response[1] = 0x02;
            response[7] = commandClass;
            response[8] = commandId;
            for (var index = 0; index < payload.Length; index++)
            {
                response[9 + index] = payload[index];
            }

            return response;
        }

        private byte[] BuildSerialResponse()
        {
            var response = BuildResponse(0x00, 0x82);
            response[2] = 0x1F;
            var bytes = System.Text.Encoding.ASCII.GetBytes(SerialNumber);
            response[6] = (byte)bytes.Length;
            response[7] = 0;
            bytes.CopyTo(response.AsSpan(9));
            return response;
        }

        private byte[] BuildResponse(byte commandClass, byte commandId, int brightnessIndex)
        {
            var response = BuildResponse(commandClass, commandId);
            response[brightnessIndex] = Brightness;
            return response;
        }
    }
}