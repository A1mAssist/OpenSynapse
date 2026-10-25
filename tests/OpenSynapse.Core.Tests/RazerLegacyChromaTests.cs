using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerLegacyChromaTests
{
    [Fact]
    public void GetEffectDeclaresTheMiddlewarePacketSize()
    {
        var request = RazerLegacyChromaProtocol.GetEffect(0x05);

        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x82, request[8]);
        Assert.Equal((byte)80, request[6]);
        Assert.Equal([RazerLegacyChromaProtocol.ReadProfileId, RazerLegacyChromaProtocol.ReadRegionId], request[9..11]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void SetEffectMatchesTheMiddlewareLayout()
    {
        var request = RazerLegacyChromaProtocol.SetEffect(
            0x05, RazerChromaEffect.Static, [0x11, 0x22, 0x33]);

        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x02, request[8]);
        Assert.Equal((byte)9, request[6]);
        Assert.Equal([0x01, 0x00, 0x01, 0x00, 0x00, 0x01, 0x11, 0x22, 0x33], request[9..18]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void SetEffectRejectsTheClass10OnlyEffects()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerLegacyChromaProtocol.SetEffect(
            0, RazerChromaEffect.AudioMeter, []));
    }

    [Fact]
    public void EffectResponseParsesTheColourCount()
    {
        var state = RazerLegacyChromaProtocol.ReadEffect(
            Response(0x01, 0x00, 0x00, 0x02, 0x00, 0x28, 0x02, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60));

        Assert.Equal(0x02, state.EffectId);
        Assert.Equal(0x28, state.Rate);
        Assert.Equal([0x10, 0x20, 0x30, 0x40, 0x50, 0x60], state.Colors);
    }

    [Fact]
    public async Task ReadStateReportsTheLegacyEffectWithoutBrightness()
    {
        var simulator = new LegacySimulator { EffectId = 2 };
        var service = new RazerChromaStandardService(
            new FakeTransport(simulator.Handle), "test", TimeSpan.Zero, null, RazerChromaChannel.Legacy);

        var state = await service.ReadStateAsync();

        Assert.Null(state.BrightnessPercent);
        Assert.Equal(RazerChromaEffect.Breathing, state.Effect);
        Assert.Empty(state.Errors);
    }

    [Fact]
    public async Task SetEffectWritesAndVerifiesOverClass0F()
    {
        var simulator = new LegacySimulator();
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerChromaStandardService(
            transport, "test", TimeSpan.Zero, null, RazerChromaChannel.Legacy);

        await service.SetEffectAsync(RazerChromaEffect.Spectrum, []);

        Assert.Equal(3, simulator.EffectId);
        Assert.Contains(transport.Requests, request => request[8] == 0x02 && request[11] == 0x03);
    }

    [Fact]
    public async Task SetEffectStillWritesWhenTheFirmwareIgnoresTheRead()
    {
        var simulator = new LegacySimulator { AnswerReads = false };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerChromaStandardService(
            transport, "test", TimeSpan.Zero, null, RazerChromaChannel.Legacy);

        await service.SetEffectAsync(RazerChromaEffect.Static, [0x11, 0x22, 0x33]);

        Assert.Equal(1, simulator.EffectId);
        Assert.Equal([0x11, 0x22, 0x33], simulator.Colors);
    }

    [Fact]
    public async Task BrightnessIsUnsupportedOnTheLegacyChannel()
    {
        var service = new RazerChromaStandardService(
            new FakeTransport(new LegacySimulator().Handle), "test", TimeSpan.Zero, null,
            RazerChromaChannel.Legacy);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.SetBrightnessAsync(50));
    }

    [Fact]
    public void KeyboardCatalogSplitsTheTwoLightingChannels()
    {
        Assert.True(RazerKeyboardService.UsesLegacyChroma(0x023E));
        Assert.True(RazerKeyboardService.UsesLegacyChroma(0x02D0));
        Assert.False(RazerKeyboardService.UsesLegacyChroma(0x0249));
        Assert.False(RazerKeyboardService.UsesLegacyChroma(0x02E6));
        foreach (var productId in RazerKeyboardService.LegacyChromaProducts)
        {
            Assert.True(RazerKeyboardService.IsSupported(productId), $"0x{productId:X4}");
        }
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

    private sealed class LegacySimulator
    {
        public byte EffectId { get; set; }
        public byte[] Colors { get; private set; } = [];
        public bool AnswerReads { get; init; } = true;

        public byte[] Handle(byte[] request)
        {
            if (request[8] == 0x82)
            {
                if (!AnswerReads)
                {
                    throw new NotSupportedException("The device does not support this command.");
                }

                return Response(request, request[2], EffectPayload());
            }

            if (request[8] != 0x02)
            {
                throw new NotSupportedException($"Unexpected command 0x{request[8]:X2}.");
            }

            EffectId = request[11];
            var count = request[14];
            Colors = request[15..(15 + (count * 3))];
            return Response(request, request[2], EffectPayload());
        }

        private byte[] EffectPayload() =>
            [1, 0, EffectId, 0, 0, (byte)(Colors.Length / 3), .. Colors];

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
