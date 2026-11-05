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
    public void MatrixStaticMatchesTheRzDevice25Payload()
    {
        var request = RazerLegacyChromaProtocol.SetMatrixStatic(0x05, [0x11, 0x22, 0x33]);

        Assert.Equal((byte)4, request[6]);
        Assert.Equal(0x03, request[7]);
        Assert.Equal(0x0A, request[8]);
        Assert.Equal([0x06, 0x11, 0x22, 0x33], request[9..13]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    [Fact]
    public void CustomFrameModeUsesTheArgbEffectSelector()
    {
        var request = RazerLegacyChromaProtocol.SetCustomFrameMode(0x3F);

        Assert.Equal(0x3F, request[2]);
        Assert.Equal((byte)12, request[6]);
        Assert.Equal(0x0F, request[7]);
        Assert.Equal(0x02, request[8]);
        Assert.Equal([0x00, 0x00, 0x08], request[9..12]);
        Assert.All(request[12..89], value => Assert.Equal(0, value));
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
    public void EffectResponseRejectsColorsOutsideTheDeclaredPayload()
    {
        Assert.Throws<InvalidDataException>(() => RazerLegacyChromaProtocol.ReadEffect(
            ResponseWithDeclaredLength(6, 0x01, 0x00, 0x01, 0x00, 0x00, 0x01, 0x10, 0x20, 0x30)));
        Assert.Throws<InvalidDataException>(() => RazerLegacyChromaProtocol.ReadEffect(
            Response(0x01, 0x01, 0x00, 0x01, 0x00, 0x00, 0x03)));
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
    public async Task ColorlessLegacyUpdateAcceptsResidualFirmwareRgb()
    {
        var simulator = new LegacySimulator { EffectId = 1, Colors = [1, 2, 3] };
        var transport = new FakeTransport(request =>
        {
            var previousColors = simulator.Colors;
            var response = simulator.Handle(request);
            if (request[8] == 0x02 && request[14] == 0) simulator.Colors = previousColors;
            return response;
        });
        var service = new RazerChromaStandardService(
            transport, "test", TimeSpan.Zero, null, RazerChromaChannel.Legacy);

        await service.SetEffectAsync(RazerChromaEffect.Spectrum, []);

        Assert.Equal((byte)3, simulator.EffectId);
        Assert.Equal(new byte[] { 1, 2, 3 }, simulator.Colors);
        Assert.Single(transport.Requests, request => request[8] == 0x02);
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
    public async Task UnknownPreviousLegacyEffectIsRejectedBeforeWriting()
    {
        var simulator = new LegacySimulator { EffectId = 0x7F };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerChromaStandardService(
            transport, "test", TimeSpan.Zero, null, RazerChromaChannel.Legacy);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SetEffectAsync(RazerChromaEffect.Static, [0x11, 0x22, 0x33]));

        Assert.DoesNotContain(transport.Requests, request => request[8] == 0x02);
    }

    [Fact]
    public async Task ReadbackBecomingUnavailableAfterAReadableGetFailsAndRestores()
    {
        var simulator = new LegacySimulator
        {
            EffectId = (byte)RazerChromaEffect.Static,
            Colors = [0xFF, 0x00, 0x00],
            FailReadNumber = 2,
        };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerChromaStandardService(
            transport, "test", TimeSpan.Zero, null, RazerChromaChannel.Legacy);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetEffectAsync(RazerChromaEffect.Static, [0x00, 0x00, 0xFF]));

        Assert.Equal(2, transport.Requests.Count(request => request[8] == 0x02));
        Assert.Equal(0x82, transport.Requests[^1][8]);
    }

    [Fact]
    public async Task SameLegacyEffectWithWrongColorFailsInsteadOfPassingById()
    {
        var simulator = new LegacySimulator
        {
            EffectId = (byte)RazerChromaEffect.Static,
            Colors = [0xFF, 0x00, 0x00],
            IgnoreWrites = true,
        };
        var service = new RazerChromaStandardService(
            new FakeTransport(simulator.Handle), "test", TimeSpan.Zero, null, RazerChromaChannel.Legacy);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetEffectAsync(RazerChromaEffect.Static, [0x00, 0x00, 0xFF]));
    }

    [Fact]
    public async Task BlackWidowV4Pro75UsesTheMatrixStaticSelector()
    {
        var transport = new FakeTransport(request =>
        {
            if (request[7] != 0x03 || request[8] != 0x0A)
            {
                throw new NotSupportedException($"Unexpected command 0x{request[7]:X2}:0x{request[8]:X2}.");
            }

            return Response(request[2]);
        });
        var service = new RazerChromaStandardService(
            transport, "test", TimeSpan.Zero, null, RazerChromaChannel.Legacy, 0x02B3);

        await service.SetEffectAsync(RazerChromaEffect.Static, [0x11, 0x22, 0x33]);

        var request = Assert.Single(transport.Requests);
        Assert.Equal((byte)4, request[6]);
        Assert.Equal(0x03, request[7]);
        Assert.Equal(0x0A, request[8]);
        Assert.Equal([0x06, 0x11, 0x22, 0x33], request[9..13]);
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
        public byte[] Colors { get; set; } = [];
        public bool AnswerReads { get; init; } = true;
        public bool IgnoreWrites { get; init; }
        public int? FailReadNumber { get; init; }
        private int _readCount;

        public byte[] Handle(byte[] request)
        {
            if (request[8] == 0x82)
            {
                _readCount++;
                if (!AnswerReads)
                {
                    throw new NotSupportedException("The device does not support this command.");
                }
                if (FailReadNumber == _readCount)
                {
                    throw new IOException("Effect readback unavailable.");
                }

                return Response(request, request[2], EffectPayload());
            }

            if (request[8] != 0x02)
            {
                throw new NotSupportedException($"Unexpected command 0x{request[8]:X2}.");
            }

            if (!IgnoreWrites)
            {
                EffectId = request[11];
                var count = request[14];
                Colors = request[15..(15 + (count * 3))];
            }
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
        response[6] = (byte)Math.Min(arguments.Length, 80);
        arguments.CopyTo(response.AsSpan(RazerFeatureReport.ArgumentsOffset));
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private static byte[] ResponseWithDeclaredLength(byte declaredLength, params byte[] arguments)
    {
        var response = Response(0x01, arguments);
        response[6] = declaredLength;
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }
}
