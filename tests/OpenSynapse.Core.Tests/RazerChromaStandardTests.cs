using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerChromaStandardTests
{
    [Fact]
    public void StaticEffectPayloadMatchesTheMiddlewareLayout()
    {
        var request = RazerChromaStandardProtocol.SetEffect(
            0x02, RazerChromaEffect.Static, [0x11, 0x22, 0x33]);

        Assert.Equal(0x10, request[7]);
        Assert.Equal(0x03, request[8]);
        Assert.Equal(0x07, request[6]);
        Assert.Equal([0x01, 0x00, 0x00, 0x01, 0x11, 0x22, 0x33], request[9..16]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void BreathingTakesTwoColorsAndRejectsThree()
    {
        var request = RazerChromaStandardProtocol.SetEffect(
            0x00, RazerChromaEffect.Breathing, [1, 2, 3, 4, 5, 6]);

        Assert.Equal(0x0A, request[6]);
        Assert.Equal(0x02, request[9 + 3]);

        Assert.Throws<ArgumentException>(() => RazerChromaStandardProtocol.SetEffect(
            0x00, RazerChromaEffect.Static, [1, 2, 3, 4, 5, 6, 7, 8, 9]));
    }

    [Fact]
    public void BrightnessConvertsBetweenPercentAndRawBytes()
    {
        Assert.Equal((byte)255, RazerChromaStandardProtocol.ToBrightnessLevel(100));
        Assert.Equal((byte)0, RazerChromaStandardProtocol.ToBrightnessLevel(0));
        Assert.Equal((byte)100, RazerChromaStandardProtocol.ReadBrightnessPercent(Response(1, 0xFF)));
        Assert.Equal((byte)51, RazerChromaStandardProtocol.ReadBrightnessPercent(Response(1, 0x80)));
        Assert.Equal((byte)0xFF, RazerChromaStandardProtocol.SetBrightness(0, 0xFF)[9]);
        Assert.Equal((byte)0x01, RazerChromaStandardProtocol.SetBrightness(0, 0xFF)[6]);
        Assert.Equal((0x00, 0x85), (RazerChromaStandardProtocol.GetBrightness(0)[6], RazerChromaStandardProtocol.GetBrightness(0)[8]));
    }

    [Fact]
    public void EffectResponseParsesColors()
    {
        var state = RazerChromaStandardProtocol.ReadEffect(
            Response(0x03, 0x01, 0x00, 0x28, 0x02, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60));

        Assert.Equal(RazerChromaEffect.Static, state.Effect);
        Assert.Equal((byte)0x00, state.Flags);
        Assert.Equal(0x28, state.Rate);
        Assert.Equal([0x10, 0x20, 0x30, 0x40, 0x50, 0x60], state.Colors);
    }

    [Fact]
    public async Task SetBrightnessRestoresThePreviousLevelWhenTheDeviceIgnoresTheWrite()
    {
        var simulator = new ChromaSimulator { Brightness = 255, IgnoreWrites = true };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerChromaStandardService(transport, "test", TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetBrightnessAsync(50));

        var writes = transport.Requests.Where(request => request[8] == 0x05).ToArray();
        Assert.Equal(2, writes.Length);
        Assert.Equal(0xFF, writes[^1][9]);
    }

    [Fact]
    public async Task SetEffectWritesAndVerifiesTheEffectId()
    {
        var simulator = new ChromaSimulator();
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerChromaStandardService(transport, "test", TimeSpan.Zero);

        await service.SetEffectAsync(RazerChromaEffect.Spectrum, []);

        Assert.Equal(RazerChromaEffect.Spectrum, simulator.Effect);
        Assert.Contains(transport.Requests, request => request[8] == 0x03 && request[9] == 0x03);
    }

    [Fact]
    public void CatalogCoversTheAetherArgbAndMouseFamilies()
    {
        foreach (var productId in new ushort[]
                 {
                     0x030C, 0x030D, 0x030E, 0x030F, 0x0310, 0x0317,
                     0x030A, 0x0F2C, 0x0F35, 0x0F52, 0x0F59, 0x0566,
                     0x00E5, 0x00EF, 0x0093, 0x00E2,
                 })
        {
            Assert.True(RazerChromaStandardService.IsSupported(productId), $"0x{productId:X4}");
        }

        Assert.Equal(OpenSynapse.Core.Devices.DeviceCategory.Mouse,
            RazerChromaStandardService.Products[0x00E5].Category);
        Assert.Equal(OpenSynapse.Core.Devices.DeviceCategory.Monitor,
            RazerChromaStandardService.Products[0x0F28].Category);
    }

    [Fact]
    public void CatalogDoesNotOverlapAPageThatAlreadyOwnsTheDevice()
    {
        foreach (var productId in RazerChromaStandardService.Products.Keys)
        {
            Assert.False(RazerHeadsetAudioService.IsSupported(productId), $"audio 0x{productId:X4}");
            Assert.False(RazerKeyboardService.IsSupported(productId), $"keyboard 0x{productId:X4}");
            Assert.Null(OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId));
        }
    }

    [Fact]
    public async Task ReadStateReportsBothValues()
    {
        var service = new RazerChromaStandardService(
            new FakeTransport(new ChromaSimulator { Brightness = 0x80 }.Handle), "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Equal((byte)51, state.BrightnessPercent);
        Assert.Equal(RazerChromaEffect.Off, state.Effect);
        Assert.Empty(state.Errors);
    }

    private sealed class FakeTransport(Func<byte[], byte[]> handler) : IRazerFeatureTransport
    {
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
            return Task.FromResult(handler(copy));
        }
    }

    private sealed class ChromaSimulator
    {
        public byte Brightness { get; set; } = 0xFF;
        public RazerChromaEffect Effect { get; set; } = RazerChromaEffect.Off;
        public byte[] Colors { get; set; } = [];
        public bool IgnoreWrites { get; init; }

        public byte[] Handle(byte[] request)
        {
            switch (request[8])
            {
                case 0x85:
                    return Response(request, request[2], Brightness);
                case 0x05:
                    if (!IgnoreWrites)
                    {
                        Brightness = request[9];
                    }

                    return Response(request, request[2], Brightness);
                case 0x83:
                    return Response(request, request[2], EffectPayload());
                case 0x03:
                    if (!IgnoreWrites)
                    {
                        Effect = (RazerChromaEffect)request[9];
                        var count = request[11];
                        Colors = request[12..(12 + (count * 3))];
                    }

                    return Response(request, request[2], EffectPayload());
                default:
                    throw new NotSupportedException($"Unexpected command 0x{request[8]:X2}.");
            }
        }

        private byte[] EffectPayload() =>
            [(byte)Effect, 0, 0, (byte)(Colors.Length / 3), .. Colors];

        private static byte[] Response(byte[] request, byte transaction, params byte[] arguments)
        {
            var response = new byte[RazerFeatureReport.Length];
            response[1] = 0x02;
            response[2] = transaction;
            response[6] = (byte)Math.Min(arguments.Length, 80);
            response[7] = request[7];
            response[8] = request[8];
            arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
            response[89] = RazerFeatureReport.CalculateCrc(response);
            return response;
        }
    }

    private static byte[] Response(byte transaction, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = transaction;
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
