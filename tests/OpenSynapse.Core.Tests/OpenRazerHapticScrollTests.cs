using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerHapticScrollTests
{
    [Fact]
    public async Task SetStagesWritesTheActiveStageFirst()
    {
        var transport = new ProbeTransport(request => Echo(request));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        await service.SetHapticScrollStagesAsync(
            connection,
            0x05,
            0x02,
            [
                OpenRazerHapticScrollStageIds.Standard,
                OpenRazerHapticScrollStageIds.SmoothScroll,
                OpenRazerHapticScrollStageIds.Custom,
            ]);

        Assert.Equal(2, transport.Count);
        AssertFrame(transport.Requests[0], 0x02, 0x00, 0x05, 0x02);
    }

    [Fact]
    public async Task SetStagesWritesTheCountAndStageIds()
    {
        var transport = new ProbeTransport(request => Echo(request));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        await service.SetHapticScrollStagesAsync(
            connection,
            0x05,
            0x02,
            [
                OpenRazerHapticScrollStageIds.Standard,
                OpenRazerHapticScrollStageIds.SmoothScroll,
                OpenRazerHapticScrollStageIds.Custom,
            ]);

        AssertFrame(
            transport.Requests[1],
            0x09,
            0x07,
            0x05,
            0x02,
            0x03,
            OpenRazerHapticScrollStageIds.Standard,
            OpenRazerHapticScrollStageIds.SmoothScroll,
            OpenRazerHapticScrollStageIds.Custom,
            0x00,
            0x00,
            0x00);
    }

    [Fact]
    public async Task ReadReturnsTheActiveStageAndConfiguredStages()
    {
        var transport = new ProbeTransport(request => Echo(
            request,
            0x05,
            0x02,
            0x03,
            OpenRazerHapticScrollStageIds.Standard,
            OpenRazerHapticScrollStageIds.Distinct,
            OpenRazerHapticScrollStageIds.UltraFine));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        var stages = await service.GetHapticScrollStagesAsync(connection, 0x05);

        Assert.Equal(0x05, stages.ProfileId);
        Assert.Equal(0x02, stages.ActiveStage);
        Assert.Equal(0x03, stages.MaxStage);
        Assert.Equal(RazerHapticoreScrollProtocol.MaxStage, stages.StageTypes.Count);
        Assert.Equal(
            [
                RazerHapticoreScrollStageType.Standard,
                RazerHapticoreScrollStageType.Distinct,
                RazerHapticoreScrollStageType.UltraFine,
                (RazerHapticoreScrollStageType)0,
                (RazerHapticoreScrollStageType)0,
                (RazerHapticoreScrollStageType)0,
            ],
            stages.StageTypes);
        AssertFrame(transport.Requests[0], 0x09, 0x87, 0x05);
    }

    [Fact]
    public async Task ProbeAddsReadAndWriteCapabilitiesForMice()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        var transport = new ProbeTransport(request => Echo(request, 0x01, 0x01));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeHapticScrollAsync(definition, "test", result, default);

        Assert.Contains(OpenRazerBackendCapability.HapticScrollRead, result);
        Assert.Contains(OpenRazerBackendCapability.HapticScrollWrite, result);
        Assert.Equal(1, transport.Count);
        AssertFrame(transport.Requests[0], 0x02, 0x80, 0x01);
    }

    [Fact]
    public async Task ProbeLeavesCapabilitiesEmptyWhenTheReadFails()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        var transport = new ProbeTransport(_ => throw new NotSupportedException("no haptic scroll"));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeHapticScrollAsync(definition, "test", result, default);
        await service.ProbeHapticScrollAsync(definition, "test", result, default);

        Assert.Empty(result);
        Assert.Equal(1, transport.Count);
    }

    [Fact]
    public async Task ProbeSkipsNonMouseDevices()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x028D)!;
        var transport = new ProbeTransport(_ => throw new InvalidOperationException("must not probe"));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeHapticScrollAsync(definition, "test", result, default);

        Assert.Empty(result);
        Assert.Equal(0, transport.Count);
    }

    [Fact]
    public async Task SetStagesRejectsUnknownStageIdsBeforeWriting()
    {
        var transport = new ProbeTransport(request => Echo(request));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SetHapticScrollStagesAsync(connection, 0x05, 0x02, [0x7F]));

        Assert.Equal(0, transport.Count);
    }

    private static OpenRazerDeviceConnection Connection()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        return new OpenRazerDeviceConnection(
            definition,
            "test",
            "test-device",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(),
            null);
    }

    private static void AssertFrame(
        byte[] request,
        byte dataSize,
        byte commandId,
        params byte[] arguments)
    {
        Assert.Equal(0x1F, request[2]);
        Assert.Equal(dataSize, request[6]);
        Assert.Equal(RazerHapticoreScrollProtocol.CommandClass, request[7]);
        Assert.Equal(commandId, request[8]);
        Assert.Equal(
            arguments,
            request.AsSpan(RazerFeatureReport.ArgumentsOffset, arguments.Length).ToArray());
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    private static byte[] Echo(byte[] request, params byte[] arguments)
    {
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
