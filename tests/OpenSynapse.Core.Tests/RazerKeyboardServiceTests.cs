using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerKeyboardServiceTests
{
    [Fact]
    public async Task ReadState_ReportsSettingsAndIdentity()
    {
        var simulator = new KeyboardSimulator();
        var service = new RazerKeyboardService(new FakeTransport(simulator.Handle), "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Equal(new Version(2, 7), state.Firmware);
        Assert.Equal("PM1234", state.Serial);
        Assert.Equal((byte)100, state.BatteryPercent);
        Assert.True(state.Charging);
        Assert.True(state.GameModeEnabled);
        Assert.Equal(8_000, state.PollingRate);
    }

    [Fact]
    public async Task ReadState_HidesUnsupportedSettingsInsteadOfFailing()
    {
        var simulator = new KeyboardSimulator { SupportsGameMode = false, SupportsBattery = false, SupportsPolling = false };
        var service = new RazerKeyboardService(new FakeTransport(simulator.Handle), "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Null(state.GameModeEnabled);
        Assert.Null(state.BatteryPercent);
        Assert.Null(state.PollingRate);
        Assert.NotEmpty(state.Errors);
        Assert.Equal("PM1234", state.Serial);
    }

    [Fact]
    public async Task SetGameMode_WritesOnlyTheModeByteAndRestoresOnFailure()
    {
        var simulator = new KeyboardSimulator { GameMode = 0, KeyCover = 2, Lifted = 3, IgnoreWrites = true };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerKeyboardService(transport, "test", TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetGameModeAsync(true));

        var writes = transport.Requests.Where(request => request[8] == 0x08).ToArray();
        Assert.Equal(2, writes.Length);
        // The middleware writes only the mode byte; the firmware fills the rest.
        Assert.Equal([1, 0, 0], writes[0][9..12]);
        Assert.Equal([0, 0, 0], writes[^1][9..12]);
    }

    [Fact]
    public async Task SetPollingRate_WritesTheMaskCodeAndVerifiesIt()
    {
        var simulator = new KeyboardSimulator { PollingCode = 0x01 };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerKeyboardService(transport, "test", TimeSpan.Zero);

        await service.SetPollingRateAsync(1_000);

        Assert.Equal(0x08, simulator.PollingCode);
        Assert.Contains(transport.Requests, request =>
            request[8] == 0x40 && request[9] == 0x00 && request[10] == 0x08);
    }

    [Theory]
    [InlineData(0x023E)]
    [InlineData(0x024F)]
    [InlineData(0x0250)]
    [InlineData(0x02B1)]
    [InlineData(0x02B3)]
    [InlineData(0x02C4)]
    [InlineData(0x02CC)]
    [InlineData(0x02D4)]
    [InlineData(0x02E3)]
    [InlineData(0x02E6)]
    [InlineData(0x02EB)]
    [InlineData(0x02F4)]
    public void KeyboardPage_CoversTheRemainingKeyboardModels(ushort productId) =>
        Assert.True(RazerKeyboardService.IsSupported(productId));

    [Fact]
    public void KeyboardPage_DoesNotClaimWhatAnotherPageOwns()
    {
        foreach (var productId in RazerKeyboardService.Products.Keys)
        {
            Assert.False(RazerHeadsetAudioService.IsSupported(productId), $"audio 0x{productId:X4}");
            Assert.Null(OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId));
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

    private sealed class KeyboardSimulator
    {
        public bool SupportsGameMode { get; init; } = true;
        public bool SupportsBattery { get; init; } = true;
        public bool SupportsPolling { get; init; } = true;
        public bool IgnoreWrites { get; init; }
        public byte GameMode { get; set; } = 1;
        public byte KeyCover { get; set; }
        public byte Lifted { get; set; }
        public byte PollingCode { get; set; } = 0x01;

        public byte[] Handle(byte[] request)
        {
            var command = request[8];
            var commandClass = request[7];
            return (commandClass, command) switch
            {
                (0x00, 0x87) => Response(request, 4, 2, 7, 0),
                (0x00, 0x82) => Response(request, 22, [.. "PM1234"u8, .. new byte[16]]),
                (0x00, 0x88) when SupportsGameMode => Response(request, 4, GameMode, KeyCover, Lifted),
                (0x00, 0x08) when SupportsGameMode => WriteGameMode(request),
                (0x00, 0xC0) when SupportsPolling => Response(request, 2, 0x00, PollingCode),
                (0x00, 0x40) when SupportsPolling => WritePolling(request),
                (0x07, 0x80) when SupportsBattery => Response(request, 1, 0xFF),
                (0x07, 0x84) when SupportsBattery => Response(request, 1, 1),
                _ => throw new NotSupportedException($"Unexpected command 0x{commandClass:X2}:0x{command:X2}."),
            };
        }

        private byte[] WriteGameMode(byte[] request)
        {
            if (!IgnoreWrites)
            {
                GameMode = request[9];
            }

            return Response(request, 4, GameMode, KeyCover, Lifted);
        }

        private byte[] WritePolling(byte[] request)
        {
            if (!IgnoreWrites)
            {
                PollingCode = request[10];
            }

            return Response(request, 2, 0x00, PollingCode);
        }

        private static byte[] Response(byte[] request, byte dataSize, params byte[] arguments)
        {
            var response = new byte[RazerFeatureReport.Length];
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
}
