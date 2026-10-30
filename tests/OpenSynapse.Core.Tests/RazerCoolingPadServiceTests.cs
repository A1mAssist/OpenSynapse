using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerCoolingPadServiceTests
{
    [Fact]
    public async Task ReadSpeed_ReturnsTheReported50RpmUnits()
    {
        var pad = new CoolingPadSimulator { SpeedUnits = 24 };
        var service = CreateService(pad);

        var rpm = await service.ReadSpeedRpmAsync();

        Assert.Equal(1200, rpm);
        Assert.Contains(pad.Transport.Requests, request => request[7] == 0x0D && request[8] == 0x81);
    }

    [Fact]
    public async Task SetSpeed_WritesTheTargetAndReturnsTheReadback()
    {
        var pad = new CoolingPadSimulator { SpeedUnits = 24 };
        var service = CreateService(pad);

        var applied = await service.SetSpeedRpmAsync(1800);

        Assert.Equal(1800, applied);
        var write = Assert.Single(pad.Transport.Requests, request => request[7] == 0x0D && request[8] == 0x01);
        Assert.Equal(new byte[] { 1, 5, 36 }, write[9..12]);
        Assert.Equal(36, pad.SpeedUnits);
    }

    [Fact]
    public async Task SetSpeed_ReportsAReadbackMismatchAndRestoresTheOriginalSpeed()
    {
        var pad = new CoolingPadSimulator { SpeedUnits = 24, IgnoreWrites = true };
        var service = CreateService(pad);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetSpeedRpmAsync(1800));

        Assert.Contains("wrote 1800", exception.Message, StringComparison.Ordinal);
        Assert.Contains("read 1200", exception.Message, StringComparison.Ordinal);
        var writes = pad.Transport.Requests
            .Where(request => request[7] == 0x0D && request[8] == 0x01)
            .ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(24, pad.SpeedUnits);
    }

    [Fact]
    public async Task LightingReadsRegionsAndWritesWithReadback()
    {
        var pad = new CoolingPadSimulator();
        var service = CreateService(pad);

        var state = await service.ReadLightingAsync();
        Assert.NotNull(state);
        Assert.Equal((byte)7, state.RegionId);
        Assert.Equal(50, state.BrightnessPercent);
        Assert.Equal((byte)3, state.EffectId);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, state.EffectIds);

        var applied = await service.SetLightingAsync(100, 1, 0, 0, [0, 255, 0]);
        Assert.Equal(100, applied.BrightnessPercent);
        Assert.Equal((byte)1, applied.EffectId);
        Assert.Equal(new byte[] { 0, 255, 0 }, applied.Colors);
        Assert.Equal(255, pad.LightingBrightnessRaw);
    }

    [Fact]
    public async Task ReadCoolingMode_ReturnsTheBundleFields()
    {
        var pad = new CoolingPadSimulator { CoolingModeId = 3, CoolingModeUnits = 10 };
        var service = CreateService(pad);

        var state = await service.ReadCoolingModeAsync();

        Assert.Equal((byte)1, state.Source);
        Assert.Equal((byte)3, state.ModeId);
        Assert.Equal(500, state.Rpm);
        Assert.Contains(pad.Transport.Requests, request => request[7] == 0x0D && request[8] == 0x90);
    }

    [Fact]
    public async Task SetFixedMode_WritesSourceFlagAndVerifiesTheReadback()
    {
        var pad = new CoolingPadSimulator();
        var service = CreateService(pad);

        var actual = await service.SetFixedModeAsync(RazerCoolingPadFixedMode.High);

        Assert.Equal((byte)2, actual.ModeId);
        Assert.Equal(2700, actual.Rpm);
        var write = Assert.Single(pad.Transport.Requests, request => request[7] == 0x0D && request[8] == 0x10);
        Assert.Equal(new byte[] { 1, 2, 54 }, write[9..12].ToArray());
        Assert.Equal((byte)2, pad.CoolingModeId);
        Assert.Equal((byte)54, pad.CoolingModeUnits);
    }

    [Fact]
    public async Task SetFixedMode_ReportsAMismatchAndRestoresTheOriginalPreset()
    {
        var pad = new CoolingPadSimulator { IgnoreCoolingModeWrites = true };
        var service = CreateService(pad);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetFixedModeAsync(RazerCoolingPadFixedMode.High));

        Assert.Contains("readback mismatch", exception.Message, StringComparison.Ordinal);
        var writes = pad.Transport.Requests
            .Where(request => request[7] == 0x0D && request[8] == 0x10)
            .ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(new byte[] { 1, 1, 44 }, writes[1][9..12].ToArray());
        Assert.Equal((byte)1, pad.CoolingModeId);
    }

    [Fact]
    public async Task SetSmartMode_WritesSourceZeroAndReadsBack()
    {
        var pad = new CoolingPadSimulator();
        var service = CreateService(pad);

        var actual = await service.SetSmartModeAsync(RazerCoolingPadSmartMode.Balanced);

        Assert.Equal((byte)4, actual.ModeId);
        var write = Assert.Single(pad.Transport.Requests, request => request[7] == 0x0D && request[8] == 0x10);
        Assert.Equal(new byte[] { 0, 4, 0 }, write[9..12].ToArray());
        Assert.Equal((byte)4, pad.CoolingModeId);
    }

    [Fact]
    public async Task SetFixedMode_OffRestoresThePreviousModeWhenTheReadbackDoesNotMatch()
    {
        var pad = new CoolingPadSimulator { IgnoreCoolingModeWrites = true };
        var service = CreateService(pad);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetFixedModeAsync(RazerCoolingPadFixedMode.Off));

        Assert.Contains("fixed mode Off", exception.Message, StringComparison.OrdinalIgnoreCase);
        var writes = pad.Transport.Requests
            .Where(request => request[7] == 0x0D && request[8] == 0x10)
            .ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(new byte[] { 0, 6, 0 }, writes[0][9..12].ToArray());
        Assert.Equal(new byte[] { 1, 1, 44 }, writes[1][9..12].ToArray());
    }

    [Fact]
    public async Task SetCurveSpeed_WritesThePerformanceCeilingAndReadsBack()
    {
        var pad = new CoolingPadSimulator { SpeedUnits = 24 };
        var service = CreateService(pad);

        var actual = await service.SetCurveSpeedAsync(2700);

        Assert.Equal(2700, actual);
        Assert.Equal((byte)54, pad.SpeedUnits);
        var write = Assert.Single(
            pad.Transport.Requests,
            request => request[7] == 0x0D && request[8] == 0x01);
        Assert.Equal(new byte[] { 1, 5, 54 }, write[9..12].ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(125)]
    [InlineData(155)]
    [InlineData(2650)]
    public async Task SetSpeed_RejectsTargetsOutsideTheAdvertisedSteps(int rpm)
    {
        var pad = new CoolingPadSimulator { SpeedUnits = 24 };
        var service = CreateService(pad);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SetSpeedRpmAsync(rpm));
        Assert.DoesNotContain(pad.Transport.Requests, request => request[7] == 0x0D && request[8] == 0x01);
    }

    private static RazerCoolingPadService CreateService(CoolingPadSimulator pad) =>
        new(pad.Transport, "test", TimeSpan.Zero, pad.Transport.Diagnostic);

    private sealed class CoolingPadSimulator
    {
        public byte SpeedUnits { get; set; } = 24;
        public byte CoolingModeSource { get; set; } = 1;
        public byte CoolingModeId { get; set; } = 1;
        public byte CoolingModeUnits { get; set; } = 44;
        public bool IgnoreWrites { get; init; }
        public bool IgnoreCoolingModeWrites { get; init; }
        public byte LightingRegionId { get; } = 7;
        public byte LightingBrightnessRaw { get; set; } = 127;
        public byte LightingEffectId { get; set; } = 3;
        public byte LightingFlags { get; set; }
        public byte LightingRate { get; set; }
        public byte[] LightingColors { get; set; } = [];
        public bool IgnoreLightingWrites { get; init; }
        public FakeTransport Transport { get; }

        public CoolingPadSimulator()
        {
            Transport = new FakeTransport(Handle);
        }

        private byte[] Handle(byte[] request)
        {
            var commandClass = request[7];
            var command = request[8];
            return (commandClass, command) switch
            {
                (0x0D, 0x81) => Response(request, 3, 1, 5, SpeedUnits),
                (0x0D, 0x01) => WriteSpeed(request),
                (0x0D, 0x90) => Response(request, 3, CoolingModeSource, CoolingModeId, CoolingModeUnits),
                (0x0D, 0x10) => WriteCoolingMode(request),
                (0x0F, 0x80) => Response(request, 10, LightingRegionId, 0, 0, 0, 0, 0, 1, 0, 0, 0),
                (0x0F, 0x81) => Response(request, 4, 1, 2, 3, 4),
                (0x0F, 0x84) => Response(request, 3, 0, LightingRegionId, LightingBrightnessRaw),
                (0x0F, 0x82) => Response(request, (byte)(6 + LightingColors.Length),
                    [0, LightingRegionId, LightingEffectId, LightingFlags, LightingRate,
                        (byte)(LightingColors.Length / 3), .. LightingColors]),
                (0x0F, 0x04) => WriteLightingBrightness(request),
                (0x0F, 0x02) => WriteLightingEffect(request),
                _ => throw new NotSupportedException(
                    $"Unexpected command 0x{commandClass:X2}:0x{command:X2}."),
            };
        }

        private byte[] WriteSpeed(byte[] request)
        {
            if (!IgnoreWrites)
            {
                SpeedUnits = request[11];
            }

            return Response(request, 3, 1, 5, request[11]);
        }

        private byte[] WriteCoolingMode(byte[] request)
        {
            if (!IgnoreCoolingModeWrites)
            {
                CoolingModeSource = request[9];
                CoolingModeId = request[10];
                CoolingModeUnits = request[11];
            }

            return Response(request, 3, CoolingModeSource, CoolingModeId, CoolingModeUnits);
        }

        private byte[] WriteLightingBrightness(byte[] request)
        {
            if (!IgnoreLightingWrites)
            {
                LightingBrightnessRaw = request[11];
            }

            return Response(request, 3, request[9], request[10], LightingBrightnessRaw);
        }

        private byte[] WriteLightingEffect(byte[] request)
        {
            if (!IgnoreLightingWrites)
            {
                LightingEffectId = request[11];
                LightingFlags = request[12];
                LightingRate = request[13];
                var count = request[14];
                LightingColors = request.AsSpan(15, count * 3).ToArray();
            }

            return Response(request, (byte)(6 + LightingColors.Length),
                [request[9], request[10], LightingEffectId, LightingFlags, LightingRate,
                    (byte)(LightingColors.Length / 3), .. LightingColors]);
        }

        private static byte[] Response(byte[] request, byte dataSize, params byte[] arguments)
        {
            var response = new byte[RazerFeatureReport.Length];
            response[0] = 0;
            response[1] = 0x02;
            response[2] = request[2];
            response[6] = dataSize;
            response[7] = request[7];
            response[8] = request[8];
            arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }
    }

    private sealed class FakeTransport(Func<byte[], byte[]> handler) : IRazerFeatureTransport
    {
        public List<byte[]> Requests { get; } = [];
        public Action<string> Diagnostic { get; } = _ => { };

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
            return Task.FromResult(handler(copy));
        }
    }
}
