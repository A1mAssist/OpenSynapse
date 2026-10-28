using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerWirelessPairingTests
{
    [Fact]
    public async Task PairingWritesUseTheProtocol25Frames()
    {
        var transport = new RecordingTransport(request => Reply(request, 0x02));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        await service.SetDevicePairingModeAsync(connection, 0x02, 0x1234);
        await service.UnpairWirelessDeviceAsync(connection, 0xBEEF);
        await service.SetActiveWirelessConnectionStateAsync(connection, 0x03, 0x01);

        Assert.Collection(transport.Requests,
            pair => AssertFrame(pair, 0x03, 0x00, 0x41, 0x02, 0x12, 0x34),
            unpair => AssertFrame(unpair, 0x02, 0x00, 0x42, 0xBE, 0xEF),
            active => AssertFrame(active, 0x02, 0x00, 0x47, 0x03, 0x01));
    }

    [Fact]
    public async Task ActiveStateReadParsesThePayloadAndRejectsAnUnsuccessfulReport()
    {
        var transport = new RecordingTransport(request => Reply(request, 0x02, 0x03, 0x01));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);

        var state = await service.GetActiveWirelessConnectionStateAsync(Connection());

        Assert.Equal(new RazerWirelessConnectionState(0x03, 0x01), state);

        var rejectedTransport = new RecordingTransport(request => Reply(request, 0x05, 0x03, 0x01));
        var rejectedService = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, rejectedTransport);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rejectedService.GetActiveWirelessConnectionStateAsync(Connection()));
    }

    [Fact]
    public async Task ActiveStateReadReturnsUnknownPayloadValuesUnchanged()
    {
        var transport = new RecordingTransport(request => Reply(request, 0x02, 0x7F, 0x02));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);

        var state = await service.GetActiveWirelessConnectionStateAsync(Connection());

        Assert.Equal(new RazerWirelessConnectionState(0x7F, 0x02), state);
    }

    [Fact]
    public async Task MultipleWirelessStatusV2ReadParsesTheFullPayload()
    {
        var arguments = new byte[80];
        arguments[0] = 0x02;
        arguments[1] = 0x01;
        arguments[2] = 0x12;
        arguments[3] = 0x34;
        arguments[4] = 0x00;
        arguments[5] = 0xAB;
        arguments[6] = 0xCD;
        var transport = new RecordingTransport(request => Reply(request, 0x02, arguments));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);

        var statuses = await service.GetMultipleDeviceWirelessConnectionStatusV2Async(Connection());

        Assert.Equal(80, transport.Requests[0][6]);
        Assert.Equal(2, statuses.Count);
        Assert.Equal(new RazerWirelessDeviceConnectionStatus(0x01, 0x1234), statuses[0]);
        Assert.Equal(new RazerWirelessDeviceConnectionStatus(0x00, 0xABCD), statuses[1]);
    }

    [Fact]
    public async Task WirelessProbeAddsBothCapabilitiesOnlyWhenTheReadParses()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        var supportedTransport = new RecordingTransport(request => Reply(request, 0x02, 0x03, 0x01));
        var supportedService = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, supportedTransport);
        var supported = new HashSet<OpenRazerBackendCapability>();

        await supportedService.ProbeWirelessPairingAsync(definition, "test", supported, default);

        Assert.Contains(OpenRazerBackendCapability.WirelessConnectionStateRead, supported);
        Assert.Contains(OpenRazerBackendCapability.WirelessPairingWrite, supported);

        var rejectedTransport = new RecordingTransport(request => Reply(request, 0x05));
        var rejectedService = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, rejectedTransport);
        var rejected = new HashSet<OpenRazerBackendCapability>();

        await rejectedService.ProbeWirelessPairingAsync(definition, "test", rejected, default);

        Assert.Empty(rejected);
    }

    [Fact]
    public async Task WirelessProbeSkipsOtherCategories()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x054A)!;
        var transport = new RecordingTransport(_ => throw new InvalidOperationException("must not probe"));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeWirelessPairingAsync(definition, "test", result, default);

        Assert.Empty(result);
        Assert.Equal(0, transport.Count);
    }

    private static OpenRazerDeviceConnection Connection(ushort productId = 0x00E5)
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        return new OpenRazerDeviceConnection(
            definition,
            "test",
            $"test-{productId:X4}",
            OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(),
            null);
    }

    private static void AssertFrame(
        byte[] request,
        byte dataSize,
        byte commandClass,
        byte commandId,
        params byte[] arguments)
    {
        Assert.Equal(0x1F, request[2]);
        Assert.Equal(dataSize, request[6]);
        Assert.Equal(commandClass, request[7]);
        Assert.Equal(commandId, request[8]);
        Assert.Equal(arguments, request.AsSpan(RazerFeatureReport.ArgumentsOffset, arguments.Length).ToArray());
        Assert.Equal(request[89], RazerFeatureReport.CalculateCrc(request));
    }

    private static byte[] Reply(byte[] request, byte status, params byte[] arguments)
    {
        var response = new byte[RazerFeatureReport.Length];
        response[1] = status;
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

    private sealed class RecordingTransport(Func<byte[], byte[]> handler) : IRazerFeatureTransport
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
            var bytes = request.ToArray();
            Requests.Add(bytes);
            return Task.FromResult(handler(bytes));
        }
    }
}
