using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerGamepadServiceTests
{
    [Fact]
    public async Task ReadState_UsesTheSharedGamepadCommandTable()
    {
        var simulator = new GamepadSimulator { ProfileId = 3, SensitivityLeft = 61, SensitivityRight = 44 };
        var service = new RazerGamepadService(
            new FakeTransport(simulator.Handle), "test", TimeSpan.Zero);

        var state = await service.ReadStateAsync();

        Assert.Equal((byte)3, state.ProfileId);
        Assert.Equal(new byte[] { 1, 2 }, state.ControlIds);
        Assert.Equal(new byte[] { 32, 33 }, state.ButtonIds);
        Assert.Equal((byte)61, state.Sticks[0].Sensitivity);
        Assert.Equal((byte)44, state.Sticks[1].Sensitivity);
        Assert.Equal((ushort)7, state.Sticks[0].Zones?.DeadZone);
        Assert.Equal(RazerGamepadCircularity.Standard, state.Sticks[0].Circularity);
        Assert.Equal(RazerGamepadTriggerMode.Analog, state.Triggers[0].Mode);
        Assert.Equal((byte)0, state.Triggers[0].StartRange);
        Assert.Equal((byte)100, state.Triggers[0].EndRange);
        Assert.Equal((byte)4, state.VibrationScale);
        Assert.Equal(false, state.IgnoreDPadDiagonals);
        Assert.Equal(RazerGamepadSocdMode.Neutral, state.SocdMode);
        Assert.Equal((ushort)0x1234, state.AdcMask);
        Assert.Equal(new RazerGamepadRawInput(1, 2, 3, 4, 5, 6), state.RawInput);
    }

    [Fact]
    public async Task ReadButtonAssignments_UsesTheResolvedBaseModuleCommands()
    {
        var simulator = new GamepadSimulator { ProfileId = 3 };
        simulator.ButtonAssignments[32] = new(
            32, RazerGamepadButtonMode.Normal, RazerGamepadFunction.MacroTypeI, 2, [0x0A, 0x0B]);
        simulator.ButtonAssignments[33] = new(
            33, RazerGamepadButtonMode.Normal, RazerGamepadFunction.KeyCode, 1, [0x1E]);
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerGamepadService(transport, "test", TimeSpan.Zero);
        await service.ReadStateAsync();

        var assignments = await service.ReadButtonAssignmentsAsync();

        Assert.Equal(2, assignments.Count);
        Assert.Equal(RazerGamepadFunction.MacroTypeI, assignments[0].Function);
        Assert.Equal(new byte[] { 0x0A, 0x0B }, assignments[0].Data);
        var read = Assert.Single(transport.Requests, request =>
            request[7] == 0x02 && request[8] == 0x8C && request[10] == 32);
        Assert.Equal(new byte[] { 3, 32, 0 }, read[9..12]);
    }

    [Fact]
    public async Task SetSensitivity_UsesTheActiveProfileAndVerifiesReadback()
    {
        var simulator = new GamepadSimulator { ProfileId = 3, SensitivityLeft = 50 };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerGamepadService(transport, "test", TimeSpan.Zero);
        await service.ReadStateAsync();

        await service.SetSensitivityAsync(1, 75);

        var write = Assert.Single(transport.Requests, request =>
            request[7] == 0x0C && request[8] == 0x02);
        Assert.Equal(new byte[] { 3, 1, 75 }, write[9..12]);
        Assert.Equal((byte)75, simulator.SensitivityLeft);
    }

    [Fact]
    public async Task SetZones_PreservesSoftBumperAndDeltaWhenTheCallerCarriesThem()
    {
        var simulator = new GamepadSimulator
        {
            Zones = new RazerGamepadZones(7, 11, 13, false),
        };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerGamepadService(transport, "test", TimeSpan.Zero);
        await service.ReadStateAsync();

        await service.SetZonesAsync(1, new RazerGamepadZones(9, 11, 13, true));

        var write = Assert.Single(transport.Requests, request =>
            request[7] == 0x0C && request[8] == 0x05);
        Assert.Equal(new byte[] { 0, 1, 0, 9, 0, 11, 0, 13, 1 }, write[9..18]);
    }

    [Fact]
    public async Task SetVibration_WritesAllFourMotorsAndReadsMotorFive()
    {
        var simulator = new GamepadSimulator { VibrationScale = 1 };
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerGamepadService(transport, "test", TimeSpan.Zero);
        await service.ReadStateAsync();

        await service.SetVibrationScaleAsync(5);

        var writes = transport.Requests.Where(request => request[7] == 0x0C && request[8] == 0x06).ToArray();
        Assert.Equal(4, writes.Length);
        Assert.Equal([5, 6, 7, 8], writes.Select(request => request[10]).ToArray());
        Assert.All(writes, request => Assert.Equal(5, request[11]));
        Assert.Equal(5, simulator.VibrationScale);
    }

    [Fact]
    public async Task SetIgnoreAndSocd_OnlyUseTheirOwnCommands()
    {
        var simulator = new GamepadSimulator();
        var transport = new FakeTransport(simulator.Handle);
        var service = new RazerGamepadService(transport, "test", TimeSpan.Zero);
        await service.ReadStateAsync();

        await service.SetIgnoreDPadDiagonalsAsync(true);
        await service.SetSocdAsync(RazerGamepadSocdMode.AbsoluteUpPriority);

        Assert.Contains(transport.Requests, request => request[8] == 0x17 && request[10] == 1);
        Assert.Contains(transport.Requests, request => request[8] == 0x1B && request[10] == 4);
    }

    [Theory]
    [InlineData(0x0A45)]
    [InlineData(0x0A4C)]
    [InlineData(0x0A57)]
    [InlineData(0x0A5A)]
    [InlineData(0x0A74)]
    [InlineData(0x0A7C)]
    [InlineData(0x1025)]
    [InlineData(0x1030)]
    [InlineData(0x1013)]
    public void GamepadPage_CoversTheGamepadModels(ushort productId) =>
        Assert.True(RazerGamepadService.IsSupported(productId));

    [Fact]
    public void GamepadPage_DoesNotClaimWhatAnotherPageOwns()
    {
        foreach (var productId in RazerGamepadService.Products.Keys)
        {
            Assert.False(RazerKeyboardService.IsSupported(productId), $"keyboard 0x{productId:X4}");
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

    private sealed class GamepadSimulator
    {
        public byte ProfileId { get; init; }
        public byte SensitivityLeft { get; set; } = 50;
        public byte SensitivityRight { get; set; } = 50;
        public RazerGamepadZones Zones { get; set; } = new(7, 0, 0, false);
        public RazerGamepadCircularity[] Circularities { get; } = [RazerGamepadCircularity.Standard, RazerGamepadCircularity.Standard];
        public byte[] TriggerRanges { get; } = [0, 100, 0, 100];
        public RazerGamepadTriggerMode[] TriggerModes { get; } = [RazerGamepadTriggerMode.Analog, RazerGamepadTriggerMode.Analog];
        public RazerGamepadDigitalTrigger[] DigitalTriggers { get; } = [new(0, false), new(0, false)];
        public byte VibrationScale { get; set; } = 4;
        public bool IgnoreDPad { get; set; }
        public RazerGamepadSocdMode Socd { get; set; }
        public ushort AdcMask { get; set; } = 0x1234;
        public byte[] ButtonIds { get; } = [32, 33];
        public Dictionary<byte, RazerGamepadButtonAssignment> ButtonAssignments { get; } = [];

        public byte[] Handle(byte[] request)
        {
            var commandClass = request[7];
            var command = request[8];
            return (commandClass, command) switch
            {
                (0x05, 0x84) => Response(request, 1, ProfileId),
                (0x0C, 0x80) => Response(request, 3, 2, 1, 2),
                (0x02, 0x80) => Response(request, 3, [(byte)ButtonIds.Length, .. ButtonIds]),
                (0x02, 0x8C) => Response(
                    request,
                    10,
                    [
                        0x02,
                        request[10],
                        request[11],
                        (byte)ButtonAssignments[request[10]].Function,
                        (byte)ButtonAssignments[request[10]].Data.Count,
                        .. ButtonAssignments[request[10]].Data,
                    ]),
                (0x0C, 0x82) => Response(request, 3, 0x0C, request[10], Sensitivity(request[10])),
                (0x0C, 0x02) => WriteSensitivity(request),
                (0x0C, 0x85) => Response(request, 9, ZoneBytes(request[10])),
                (0x0C, 0x05) => WriteZones(request),
                (0x0C, 0x92) => Response(request, 3, 0x0C, request[10], (byte)Circularities[request[10] - 1]),
                (0x0C, 0x12) => WriteCircularity(request),
                (0x0C, 0x93) => Response(request, 4, 0x0C, request[10], TriggerRanges[(request[10] - 1) * 2], TriggerRanges[(request[10] - 1) * 2 + 1]),
                (0x0C, 0x13) => WriteTriggerRange(request),
                (0x0C, 0x95) => Response(request, 3, 0x0C, request[10], (byte)TriggerModes[request[10] - 1]),
                (0x0C, 0x15) => WriteTriggerMode(request),
                (0x0C, 0x96) => Response(request, 4, 0x0C, request[10], DigitalTriggers[request[10] - 1].ActuationPoint, (byte)(DigitalTriggers[request[10] - 1].IsRapidTrigger ? 1 : 0)),
                (0x0C, 0x16) => WriteDigitalTrigger(request),
                (0x0C, 0x86) => Response(request, 3, 0x0C, request[10], VibrationScale),
                (0x0C, 0x06) => WriteVibration(request),
                (0x0C, 0x97) => Response(request, 2, 0x0C, (byte)(IgnoreDPad ? 1 : 0)),
                (0x0C, 0x17) => WriteIgnoreDPad(request),
                (0x0C, 0x9B) => Response(request, 2, 0x0C, (byte)Socd),
                (0x0C, 0x1B) => WriteSocd(request),
                (0x0C, 0x91) => Response(request, 2, (byte)(AdcMask >> 8), (byte)AdcMask),
                (0x0C, 0x11) => WriteAdcMask(request),
                (0x0C, 0x98) => Response(request, 12, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6),
                _ => throw new NotSupportedException($"Unexpected command 0x{commandClass:X2}:0x{command:X2}."),
            };
        }

        private byte Sensitivity(byte controlId) => controlId == 1 ? SensitivityLeft : SensitivityRight;

        private byte[] ZoneBytes(byte controlId)
        {
            _ = controlId;
            return [0x0C, controlId, (byte)(Zones.DeadZone >> 8), (byte)Zones.DeadZone,
                (byte)(Zones.SoftBumper >> 8), (byte)Zones.SoftBumper,
                (byte)(Zones.DeltaThreshold >> 8), (byte)Zones.DeltaThreshold,
                Zones.PreventDoubleDeadZone ? (byte)1 : (byte)0];
        }

        private byte[] WriteSensitivity(byte[] request)
        {
            if (request[10] == 1) SensitivityLeft = request[11];
            else SensitivityRight = request[11];
            return Response(request, 3, 0x0C, request[10], Sensitivity(request[10]));
        }

        private byte[] WriteZones(byte[] request)
        {
            Zones = new RazerGamepadZones(
                (ushort)((request[11] << 8) | request[12]),
                (ushort)((request[13] << 8) | request[14]),
                (ushort)((request[15] << 8) | request[16]),
                request[17] != 0);
            return Response(request, 9, ZoneBytes(request[10]));
        }

        private byte[] WriteCircularity(byte[] request)
        {
            Circularities[request[10] - 1] = (RazerGamepadCircularity)request[11];
            return Response(request, 3, 0x0C, request[10], request[11]);
        }

        private byte[] WriteTriggerRange(byte[] request)
        {
            var offset = (request[10] - 1) * 2;
            TriggerRanges[offset] = request[11];
            TriggerRanges[offset + 1] = request[12];
            return Response(request, 4, 0x0C, request[10], request[11], request[12]);
        }

        private byte[] WriteTriggerMode(byte[] request)
        {
            TriggerModes[request[10] - 1] = (RazerGamepadTriggerMode)request[11];
            return Response(request, 3, 0x0C, request[10], request[11]);
        }

        private byte[] WriteDigitalTrigger(byte[] request)
        {
            DigitalTriggers[request[10] - 1] = new RazerGamepadDigitalTrigger(request[11], request[12] != 0);
            return Response(request, 4, 0x0C, request[10], request[11], request[12]);
        }

        private byte[] WriteVibration(byte[] request)
        {
            VibrationScale = request[11];
            return Response(request, 3, 0x0C, request[10], request[11]);
        }

        private byte[] WriteAdcMask(byte[] request)
        {
            AdcMask = (ushort)((request[9] << 8) | request[10]);
            return Response(request, 2, (byte)(AdcMask >> 8), (byte)AdcMask);
        }

        private byte[] WriteIgnoreDPad(byte[] request)
        {
            IgnoreDPad = request[10] != 0;
            return Response(request, 2, 0x0C, request[10]);
        }

        private byte[] WriteSocd(byte[] request)
        {
            Socd = (RazerGamepadSocdMode)request[10];
            return Response(request, 2, 0x0C, request[10]);
        }

        private static byte[] Response(byte[] request, byte dataSize, params byte[] arguments) =>
            Response(request, dataSize, (ReadOnlySpan<byte>)arguments);

        private static byte[] Response(byte[] request, byte dataSize, ReadOnlySpan<byte> arguments)
        {
            var response = new byte[RazerFeatureReport.Length];
            response[0] = RazerGamepadProtocol.ReportId;
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
