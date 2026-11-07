using OpenSynapse.Core.Devices;
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

    [Fact]
    public async Task SetPollingRate_UsesTheActiveProfile()
    {
        var simulator = new KeyboardSimulator
        {
            SupportsProfile = true,
            ProfileId = 3,
            PollingCode = 0x01,
        };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerKeyboardService(transport, "test", TimeSpan.Zero);

        await service.SetPollingRateAsync(1_000);

        Assert.Contains(transport.Requests, request =>
            request[7] == 0x00 && request[8] == 0xC0 && request[9] == 0x03);
        Assert.Contains(transport.Requests, request =>
            request[7] == 0x00 && request[8] == 0x40 &&
            request[9] == 0x03 && request[10] == 0x08);
    }

    [Theory]
    [InlineData(0x02C9, 0x02CC)]
    [InlineData(0x02CA, 0x02CC)]
    [InlineData(0x02D2, 0x02D4)]
    [InlineData(0x02D3, 0x02D4)]
    [InlineData(0x02B4, 0x02B3)]
    [InlineData(0x02B5, 0x02B3)]
    [InlineData(0x02C2, 0x02C4)]
    [InlineData(0x02C3, 0x02C4)]
    [InlineData(0x02F5, 0x02F4)]
    [InlineData(0x02F6, 0x02F4)]
    public void WirelessPidsAdmitTheWiredModel(ushort wireless, ushort wired)
    {
        Assert.True(RazerProductAliases.IsWirelessAlias(wireless));
        Assert.Equal(wired, RazerProductAliases.Canonical(wireless));
        Assert.True(RazerKeyboardService.IsSupported(wireless));
        Assert.Equal(
            RazerKeyboardService.UsesLegacyChroma(wired),
            RazerKeyboardService.UsesLegacyChroma(wireless));
    }

    [Fact]
    public void TheDongleInterfaceIsAdmittedForTheBlackWidowV4LowProfile()
    {
        var dongle = new WindowsHidDiscovery.HidInterfaceDescriptor(
            @"\\?\hid#vid_1532&pid_02c9&mi_02", "container", 0x1532, 0x02C9,
            InputReportByteLength: 64, OutputReportByteLength: 64,
            FeatureReportByteLength: 91, UsagePage: 0xFF00, Usage: 0x0001,
            DeviceAccessState.Available,
            "Razer BlackWidow V4 Low-profile HyperSpeed");

        var candidates = RazerKeyboardService.SelectCandidates([dongle]);

        Assert.Equal(0x02C9, Assert.Single(candidates).ProductId);
    }

    [Theory]
    [InlineData(0x02CA)]
    [InlineData(0x02D3)]
    [InlineData(0x02B5)]
    [InlineData(0x02C3)]
    [InlineData(0x02F6)]
    public void BleSiblingsAreNotAdmittedToTheProtocol25KeyboardPage(ushort productId)
    {
        var descriptor = Descriptor("ble", "keyboard-ble", productId);

        Assert.Empty(RazerKeyboardService.SelectCandidates([descriptor]));
    }

    [Fact]
    public void SelectConnectionsKeepsEveryPhysicalKeyboardAndCollapsesItsInterfaces()
    {
        var interfaces = new[]
        {
            Descriptor("wireless", "keyboard-wireless", 0x02C9),
            Descriptor("wired", "keyboard-wireless", 0x02CC),
            Descriptor("protype", "keyboard-protype", 0x0277),
            Descriptor("huntsman-a", "keyboard-huntsman-a", 0x02D0),
            Descriptor("huntsman-b", "keyboard-huntsman-b", 0x02D0),
            Descriptor("wireless-only", "keyboard-wireless-only", 0x02F5),
            Descriptor("offline", "keyboard-offline", 0x0249, DeviceAccessState.BusyOrUnavailable),
            Descriptor("not-admitted", "keyboard-other", 0x1234),
        };

        var connections = RazerKeyboardService.SelectConnections(interfaces);

        Assert.Equal(
            ["protype", "wired", "huntsman-a", "huntsman-b", "wireless-only", "offline"],
            connections.Select(item => item.DevicePath));
        Assert.Equal((ushort)0x02CC, connections[1].ProductId);
        Assert.Equal((ushort)0x02CC, connections[1].DisplayProductId);
        Assert.Equal("Razer BlackWidow V4 Low-profile HyperSpeed", connections[1].Name);
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
            Assert.Null(RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId));
        }
    }

    [Fact]
    public async Task MappingReadsMiddleware02ButtonListAndRoundTripsAssignment()
    {
        byte function = RazerHidMappingFunctions.ButtonCode;
        var data = new[] { (byte)0x05 };
        var transport = new FakeTransport(request =>
        {
            var commandClass = request[7];
            var command = request[8];
            return (commandClass, command) switch
            {
                (0x02, 0x84) => Response([0x03, 0x01, 0x02, 0x03]),
                (0x02, 0x8C) => Response(
                    [0x01, request[10], request[11], function, (byte)data.Length, .. data]),
                (0x02, 0x0C) => WriteMiddlewareAssignment(request, ref function, ref data),
                _ => throw new NotSupportedException(
                    $"Unexpected mapping command 0x{commandClass:X2}:0x{command:X2}."),
            };
        });
        var service = new RazerKeyboardService(transport, "test", TimeSpan.Zero);

        Assert.Equal([0x01, 0x02, 0x03], await service.GetMappingButtonIdsAsync());
        var before = await service.GetMappingAssignmentAsync(0x02, 0);
        Assert.Equal(function, before.FunctionId);
        Assert.Equal(data, before.Data);

        await service.SetMappingAssignmentAsync(0x02, 0, RazerHidMappingFunctions.KeyCode, new byte[] { 0x04 });

        var after = await service.GetMappingAssignmentAsync(0x02, 0);
        Assert.Equal(RazerHidMappingFunctions.KeyCode, after.FunctionId);
        Assert.Equal([0x04], after.Data);
    }

    [Fact]
    public async Task MappingFallsBackToButtonAssignment08AndRoundTripsAssignment()
    {
        byte function = (byte)RazerButtonFunctionType.ButtonCode;
        var data = new[] { (byte)0x04 };
        var transport = new FakeTransport(request =>
        {
            var commandClass = request[7];
            var command = request[8];
            return (commandClass, command) switch
            {
                (0x02, 0x84) => throw new NotSupportedException(),
                (0x08, 0x80) => Response([0x01, 0x02]),
                (0x08, 0x84) => ButtonAssignment08Response(request[10], function, data),
                (0x08, 0x04) => WriteButtonAssignment08(request, ref function, ref data),
                _ => throw new NotSupportedException(
                    $"Unexpected mapping command 0x{commandClass:X2}:0x{command:X2}."),
            };
        });
        var service = new RazerKeyboardService(transport, "test", TimeSpan.Zero);

        Assert.Equal([0x01, 0x02], await service.GetMappingButtonIdsAsync());
        var before = await service.GetMappingAssignmentAsync(0x01, 0);
        Assert.Equal(function, before.FunctionId);
        Assert.Equal(data, before.Data);

        await service.SetMappingAssignmentAsync(0x01, 0, (byte)RazerButtonFunctionType.KeyCode, new byte[] { 0x05 });

        var after = await service.GetMappingAssignmentAsync(0x01, 0);
        Assert.Equal((byte)RazerButtonFunctionType.KeyCode, after.FunctionId);
        Assert.Equal([0x05], after.Data);
    }

    private static byte[] WriteMiddlewareAssignment(byte[] request, ref byte function, ref byte[] data)
    {
        function = request[12];
        var size = request[13];
        data = request[14..(14 + size)].ToArray();
        return Response([]);
    }

    private static byte[] ButtonAssignment08Response(byte button, byte function, byte[] data)
    {
        var arguments = new byte[15];
        arguments[0] = button;
        arguments[2] = function;
        arguments[4] = (byte)data.Length;
        for (var index = 0; index < data.Length; index++)
        {
            arguments[6 + (index * 2)] = data[index];
        }
        return Response(arguments);
    }

    private static byte[] WriteButtonAssignment08(byte[] request, ref byte function, ref byte[] data)
    {
        function = request[14];
        var size = request[15];
        data = request[16..(16 + size)].ToArray();
        return Response([]);
    }

    private static byte[] Response(params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[6] = (byte)arguments.Length;
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private static WindowsHidDiscovery.HidInterfaceDescriptor Descriptor(
        string devicePath,
        string physicalDeviceKey,
        ushort productId,
        DeviceAccessState access = DeviceAccessState.Available) =>
        new(devicePath, physicalDeviceKey, 0x1532, productId, 64, 64, 91, 0xFF00, 0x0001,
            access, "test");

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
        public bool SupportsProfile { get; init; }
        public bool IgnoreWrites { get; init; }
        public byte GameMode { get; set; } = 1;
        public byte KeyCover { get; set; }
        public byte Lifted { get; set; }
        public byte PollingCode { get; set; } = 0x01;
        public byte ProfileId { get; set; } = 0;

        public byte[] Handle(byte[] request)
        {
            var command = request[8];
            var commandClass = request[7];
            return (commandClass, command) switch
            {
                (0x05, 0x84) when SupportsProfile => Response(request, 1, ProfileId),
                (0x00, 0x87) => Response(request, 4, 2, 7, 0),
                (0x00, 0x82) => Response(request, 22, [.. "PM1234"u8, .. new byte[16]]),
                (0x00, 0x88) when SupportsGameMode => Response(request, 4, GameMode, KeyCover, Lifted),
                (0x00, 0x08) when SupportsGameMode => WriteGameMode(request),
                (0x00, 0xC0) when SupportsPolling => Response(request, 2, 0x00, PollingCode),
                (0x00, 0x40) when SupportsPolling => WritePolling(request),
                (0x07, 0x80) when SupportsBattery => Response(request, 2, 0x00, 0xFF),
                (0x07, 0x84) when SupportsBattery => Response(request, 2, 0x00, 0x01),
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
