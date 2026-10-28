using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerKeyboardControlsTests
{
    private const byte KnobLedClass = 0x03;
    private const byte KnobLedId = 0x24;

    [Fact]
    public async Task KnobRequestsUseTheMiddlewareTuplesAndStageRows()
    {
        var transport = new ProbeTransport(request => Echo(request, new byte[request[6]]));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection(0x028D);
        var stages = Enumerable.Range(0, RazerKnobProtocol.StageCount)
            .Select(index => new RazerKnobStage(
                (byte)(index % 2),
                (byte)index,
                (RazerKnobMode)(index % 8),
                (byte)(0x10 + index),
                (byte)(0x20 + index),
                (byte)(0x30 + index)))
            .ToArray();

        _ = await service.GetKnobConfigurationAsync(connection, KnobLedClass, KnobLedId);
        await service.SetKnobConfigurationAsync(connection, KnobLedClass, KnobLedId, stages);
        _ = await service.GetKnobModeAsync(connection, KnobLedClass, KnobLedId);
        await service.SetKnobModeAsync(connection, KnobLedClass, KnobLedId,
            new RazerKnobStage(1, 2, RazerKnobMode.SwitchApplication, 0x33, 0x44, 0x55));

        Assert.Equal(4, transport.Requests.Count);
        Assert.Equal((0x32, 0x03, 0x92), Command(transport.Requests[0]));
        Assert.Equal((0x32, 0x03, 0x12), Command(transport.Requests[1]));
        Assert.Equal((0x08, 0x03, 0x93), Command(transport.Requests[2]));
        Assert.Equal((0x08, 0x03, 0x13), Command(transport.Requests[3]));

        Assert.Equal(new byte[] { KnobLedClass, KnobLedId }, transport.Requests[0][9..11]);
        Assert.Equal(new byte[48], transport.Requests[0][11..59]);
        Assert.Equal(
            new byte[]
            {
                0x03, 0x24,
                0x00, 0x00, 0x00, 0x10, 0x20, 0x30,
                0x01, 0x01, 0x01, 0x11, 0x21, 0x31,
                0x00, 0x02, 0x02, 0x12, 0x22, 0x32,
                0x01, 0x03, 0x03, 0x13, 0x23, 0x33,
                0x00, 0x04, 0x04, 0x14, 0x24, 0x34,
                0x01, 0x05, 0x05, 0x15, 0x25, 0x35,
                0x00, 0x06, 0x06, 0x16, 0x26, 0x36,
                0x01, 0x07, 0x07, 0x17, 0x27, 0x37,
            },
            transport.Requests[1][9..59]);
        Assert.Equal(new byte[] { 0x03, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
            transport.Requests[2][9..17]);
        Assert.Equal(new byte[] { 0x03, 0x24, 0x01, 0x02, 0x07, 0x33, 0x44, 0x55 },
            transport.Requests[3][9..17]);
    }

    [Fact]
    public async Task KnobConfigurationParsesAllEightStages()
    {
        var transport = new ProbeTransport(request => Echo(request, KnobConfigurationArguments()));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);

        var configuration = await service.GetKnobConfigurationAsync(
            Connection(0x028D), KnobLedClass, KnobLedId);

        Assert.Equal(KnobLedClass, configuration.LedClass);
        Assert.Equal(KnobLedId, configuration.LedId);
        Assert.Equal(RazerKnobProtocol.StageCount, configuration.Stages.Count);
        for (var index = 0; index < configuration.Stages.Count; index++)
        {
            var stage = configuration.Stages[index];
            Assert.Equal((byte)(index % 2), stage.Disabled);
            Assert.Equal((byte)index, stage.Stage);
            Assert.Equal((RazerKnobMode)(index % 8), stage.Mode);
            Assert.Equal((byte)(0x10 + index), stage.Red);
            Assert.Equal((byte)(0x20 + index), stage.Green);
            Assert.Equal((byte)(0x30 + index), stage.Blue);
        }
    }

    [Fact]
    public async Task AnalogActuationUsesTheFiveByteRecord()
    {
        var transport = new ProbeTransport(request => request[8] switch
        {
            0x99 => Echo(request, 0x00, 0x03,
                0x01, 0x12, 0x34, 0x56, 0x78,
                0x02, 0x01, 0x02, 0x03, 0x04),
            0x19 => Echo(request, request[9..(9 + request[6])]),
            _ => throw new InvalidOperationException($"Unexpected command 0x{request[8]:X2}."),
        });
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection(0x02A6);

        var table = await service.GetAnalogKeyActuationPointsAsync(
            connection, 0x00, 0x03, [0x01, 0x02]);
        await service.SetAnalogKeyActuationPointsAsync(connection, 0x00, 0x03,
        [
            new RazerAnalogActuationPoint(0x01, 0x1234, 0x5678),
            new RazerAnalogActuationPoint(0x02, 0x0102, 0x0304),
        ]);

        Assert.Equal((0x0C, 0x02, 0x99), Command(transport.Requests[0]));
        Assert.Equal(new byte[] { 0x00, 0x03, 0x01, 0, 0, 0, 0, 0x02, 0, 0, 0, 0 },
            transport.Requests[0][9..21]);
        Assert.Equal((0x0C, 0x02, 0x19), Command(transport.Requests[1]));
        Assert.Equal(
            new byte[]
            {
                0x00, 0x03,
                0x01, 0x12, 0x34, 0x56, 0x78,
                0x02, 0x01, 0x02, 0x03, 0x04,
            },
            transport.Requests[1][9..21]);

        Assert.Equal(0x00, table.DataType);
        Assert.Equal(0x03, table.ClassId);
        Assert.Equal(
            [
                new RazerAnalogActuationPoint(0x01, 0x1234, 0x5678),
                new RazerAnalogActuationPoint(0x02, 0x0102, 0x0304),
            ],
            table.Points);
    }

    [Fact]
    public async Task AnalogShortCommandsUseTheirMiddlewareTuples()
    {
        var transport = new ProbeTransport(request => request[8] switch
        {
            0xA0 => Echo(request, 0x01, 0x01),
            0x20 => Echo(request, request[9..(9 + request[6])]),
            0xA8 => Echo(request, 0x01, 0x40),
            0x28 => Echo(request, request[9..(9 + request[6])]),
            0xAA => Echo(request, 0x02),
            0x2A => Echo(request, request[9..(9 + request[6])]),
            0xA1 => Echo(request, 0x01, 0x01, 0x02, 0x03),
            0x21 => Echo(request, request[9..(9 + request[6])]),
            _ => throw new InvalidOperationException($"Unexpected command 0x{request[8]:X2}."),
        });
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection(0x02A6);

        var continuous = await service.GetAnalogKeyContinuousRapidTriggerAsync(connection, 0x01);
        await service.SetAnalogKeyContinuousRapidTriggerAsync(connection, 0x01, true);
        var deadZone = await service.GetAnalogKeyBottomDeadZoneAsync(connection, 0x01);
        await service.SetAnalogKeyBottomDeadZoneAsync(connection, 0x01, 0x40);
        var reportMode = await service.GetAnalogReportModeAsync(connection);
        await service.SetAnalogReportModeAsync(connection, 0x02);
        var killSwitch = await service.GetKeyboardKillSwitchAsync(connection, 0x01);
        await service.SetKeyboardKillSwitchAsync(connection, 0x01, true, 0x02, 0x03);

        Assert.Equal(new RazerAnalogKeyContinuousRapidTrigger(0x01, true), continuous);
        Assert.Equal((0x02, 0x02, 0xA0), Command(transport.Requests[0]));
        Assert.Equal(new byte[] { 0x01, 0x00 }, transport.Requests[0][9..11]);
        Assert.Equal((0x02, 0x02, 0x20), Command(transport.Requests[1]));
        Assert.Equal(new byte[] { 0x01, 0x01 }, transport.Requests[1][9..11]);

        Assert.Equal(new RazerAnalogKeyBottomDeadZone(0x01, 0x40), deadZone);
        Assert.Equal((0x02, 0x02, 0xA8), Command(transport.Requests[2]));
        Assert.Equal(new byte[] { 0x01, 0x00 }, transport.Requests[2][9..11]);
        Assert.Equal((0x02, 0x02, 0x28), Command(transport.Requests[3]));
        Assert.Equal(new byte[] { 0x01, 0x40 }, transport.Requests[3][9..11]);

        Assert.Equal(0x02, reportMode);
        Assert.Equal((0x01, 0x02, 0xAA), Command(transport.Requests[4]));
        Assert.Equal(new byte[] { 0x00 }, transport.Requests[4][9..10]);
        Assert.Equal((0x01, 0x02, 0x2A), Command(transport.Requests[5]));
        Assert.Equal(new byte[] { 0x02 }, transport.Requests[5][9..10]);

        Assert.Equal(new RazerAnalogKeyKillSwitch(0x01, true, 0x02, 0x03), killSwitch);
        Assert.Equal((0x04, 0x02, 0xA1), Command(transport.Requests[6]));
        Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x00 }, transport.Requests[6][9..13]);
        Assert.Equal((0x04, 0x02, 0x21), Command(transport.Requests[7]));
        Assert.Equal(new byte[] { 0x01, 0x01, 0x02, 0x03 }, transport.Requests[7][9..13]);
    }

    [Fact]
    public async Task AnalogTableCommandsUseTheirMiddlewareRecords()
    {
        var transport = new ProbeTransport(request => request[8] switch
        {
            0x9A => Echo(request, 0x00, 0x03, 0x01, 0x01, 0x12, 0x34, 0x56, 0x78),
            0x1A => Echo(request, request[9..(9 + request[6])]),
            0x9B => Echo(request, 0x03, 0x01, 0x01, 0x04, 0x05),
            0x1B => Echo(request, request[9..(9 + request[6])]),
            0x9E => Echo(request, 0x03, 0x01, 0x00, 0x02, 0x02, 0xAA, 0xBB, 0x00, 0x00, 0x00),
            0x1E => Echo(request, request[9..(9 + request[6])]),
            _ => throw new InvalidOperationException($"Unexpected command 0x{request[8]:X2}."),
        });
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection(0x02A6);

        var rapidTriggers = await service.GetAnalogKeyRapidTriggersAsync(connection, 0x00, 0x03, [0x01]);
        await service.SetAnalogKeyRapidTriggersAsync(connection, 0x00, 0x03,
            [new RazerAnalogRapidTrigger(0x01, true, 0x1234, 0x5678)]);
        var modTap = await service.GetAnalogKeyModTapAssignmentsAsync(connection, 0x03, [0x01]);
        await service.SetAnalogKeyModTapAssignmentsAsync(connection, 0x03,
            [new RazerAnalogModTapAssignment(0x01, true, 0x04, 0x05)]);
        var assignments = await service.GetAnalogKeyAssignmentsAsync(connection, 0x03,
            [new RazerAnalogKeyAssignmentSelector(0x01, RazerButtonAssignmentMode.NormalMode)]);
        await service.SetAnalogKeyAssignmentsAsync(connection, 0x03,
        [
            new RazerAnalogKeyAssignment(
                0x01,
                RazerButtonAssignmentMode.NormalMode,
                RazerButtonFunctionType.KeyCode,
                2,
                [0xAA, 0xBB]),
        ]);

        Assert.Equal((0x08, 0x02, 0x9A), Command(transport.Requests[0]));
        Assert.Equal(new byte[] { 0x00, 0x03, 0x01, 0, 0, 0, 0, 0 },
            transport.Requests[0][9..17]);
        Assert.Equal(new RazerAnalogRapidTrigger(0x01, true, 0x1234, 0x5678),
            Assert.Single(rapidTriggers.Entries));
        Assert.Equal((0x08, 0x02, 0x1A), Command(transport.Requests[1]));
        Assert.Equal(new byte[] { 0x00, 0x03, 0x01, 0x01, 0x12, 0x34, 0x56, 0x78 },
            transport.Requests[1][9..17]);

        Assert.Equal((0x05, 0x02, 0x9B), Command(transport.Requests[2]));
        Assert.Equal(new byte[] { 0x03, 0x01, 0, 0, 0 }, transport.Requests[2][9..14]);
        Assert.Equal(new RazerAnalogModTapAssignment(0x01, true, 0x04, 0x05),
            Assert.Single(modTap.Entries));
        Assert.Equal((0x05, 0x02, 0x1B), Command(transport.Requests[3]));
        Assert.Equal(new byte[] { 0x03, 0x01, 0x01, 0x04, 0x05 }, transport.Requests[3][9..14]);

        Assert.Equal((0x0A, 0x02, 0x9E), Command(transport.Requests[4]));
        Assert.Equal(new byte[] { 0x03, 0x01, 0x00, 0, 0, 0, 0, 0, 0, 0 },
            transport.Requests[4][9..19]);
        var assignment = Assert.Single(assignments.Entries);
        Assert.Equal(0x01, assignment.KeyId);
        Assert.Equal(RazerButtonAssignmentMode.NormalMode, assignment.Mode);
        Assert.Equal(RazerButtonFunctionType.KeyCode, assignment.FunctionId);
        Assert.Equal(2, assignment.DataSize);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0x00, 0x00, 0x00 }, assignment.Data);
        Assert.Equal((0x0A, 0x02, 0x1E), Command(transport.Requests[5]));
        Assert.Equal(new byte[] { 0x03, 0x01, 0x00, 0x02, 0x02, 0xAA, 0xBB, 0x00, 0x00, 0x00 },
            transport.Requests[5][9..19]);
    }

    [Fact]
    public async Task KeyboardControlProbesAddReadAndWriteCapabilities()
    {
        var knobTransport = new ProbeTransport(request =>
        {
            Assert.Equal((0x08, 0x03, 0x93), Command(request));
            Assert.Equal(new byte[] { KnobLedClass, KnobLedId }, request[9..11]);
            return Echo(request, KnobLedClass, KnobLedId, 0x00, 0x01, 0x05, 0x10, 0x20, 0x30);
        });
        var knobService = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, knobTransport);
        var knobResult = new HashSet<OpenRazerBackendCapability>();

        await knobService.ProbeKnobAsync(Definition(0x028D), "test", knobResult, default);

        Assert.Contains(OpenRazerBackendCapability.KnobRead, knobResult);
        Assert.Contains(OpenRazerBackendCapability.KnobWrite, knobResult);
        Assert.Equal(1, knobTransport.Count);

        var analogTransport = new ProbeTransport(request =>
        {
            Assert.Equal((0x01, 0x02, 0xAA), Command(request));
            return Echo(request, 0x02);
        });
        var analogService = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, analogTransport);
        var analogResult = new HashSet<OpenRazerBackendCapability>();

        await analogService.ProbeAnalogKeyAsync(Definition(0x02A6), "test", analogResult, default);

        Assert.Contains(OpenRazerBackendCapability.AnalogKeyRead, analogResult);
        Assert.Contains(OpenRazerBackendCapability.AnalogKeyWrite, analogResult);
        Assert.Equal(1, analogTransport.Count);
    }

    [Fact]
    public async Task KeyboardControlProbesSkipNonKeyboards()
    {
        var transport = new ProbeTransport(_ => throw new InvalidOperationException("must not probe"));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeKnobAsync(Definition(0x054A), "test", result, default);
        await service.ProbeAnalogKeyAsync(Definition(0x054A), "test", result, default);

        Assert.Empty(result);
        Assert.Equal(0, transport.Count);
    }

    [Fact]
    public async Task KeyboardControlProbesRejectFailedGets()
    {
        var transport = new ProbeTransport(_ => throw new NotSupportedException());
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeKnobAsync(Definition(0x028D), "test", result, default);
        await service.ProbeAnalogKeyAsync(Definition(0x028D), "test", result, default);

        Assert.Empty(result);
        Assert.Equal(2, transport.Count);
    }

    private static OpenRazerDeviceDefinition Definition(ushort productId) =>
        OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;

    private static OpenRazerDeviceConnection Connection(ushort productId) =>
        new(Definition(productId), "test-path", "test-device", OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

    private static byte[] KnobConfigurationArguments()
    {
        var arguments = new byte[50];
        arguments[0] = KnobLedClass;
        arguments[1] = KnobLedId;
        for (var index = 0; index < RazerKnobProtocol.StageCount; index++)
        {
            var offset = 2 + (index * RazerKnobProtocol.StageSize);
            arguments[offset] = (byte)(index % 2);
            arguments[offset + 1] = (byte)index;
            arguments[offset + 2] = (byte)(index % 8);
            arguments[offset + 3] = (byte)(0x10 + index);
            arguments[offset + 4] = (byte)(0x20 + index);
            arguments[offset + 5] = (byte)(0x30 + index);
        }

        return arguments;
    }

    private static (byte Size, byte Class, byte Command) Command(byte[] request) =>
        (request[6], request[7], request[8]);

    /// <summary>Reply that echoes the request header so the protocol parsers accept it.</summary>
    private static byte[] Echo(byte[] request, params byte[] arguments)
    {
        Assert.Equal(request[6], (byte)arguments.Length);
        var response = new byte[RazerFeatureReport.Length];
        response[1] = 0x02;
        response[2] = request[2];
        response[3] = request[3];
        response[4] = request[4];
        response[6] = request[6];
        response[7] = request[7];
        response[8] = request[8];
        arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private sealed class ProbeTransport(Func<byte[], byte[]> handler) : IRazerFeatureTransport
    {
        public List<byte[]> Requests { get; } = [];
        public int Count => Requests.Count;

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
