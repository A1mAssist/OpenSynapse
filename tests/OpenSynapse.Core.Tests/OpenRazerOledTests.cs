using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerOledTests
{
    [Fact]
    public async Task BrightnessReadAndWriteUseTheProtocol25OledLayout()
    {
        var transport = new ProbeTransport(request =>
            request[8] == 0x83 ? Echo(request, 77) : Echo(request, request[9]));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        Assert.Equal((byte)77, await service.GetOledBrightnessAsync(connection));
        await service.SetOledBrightnessAsync(connection, 120);

        AssertFrame(transport.Requests[0], 1, 0x83);
        AssertFrame(transport.Requests[1], 1, 0x03, 100);
    }

    [Fact]
    public async Task ScreensaverReadParsesBothResponseBytes()
    {
        var transport = new ProbeTransport(request => Echo(request, 3, 4));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        var option = await service.GetOledScreensaverOptionAsync(connection);

        Assert.Equal(new RazerOledScreensaverOption(3, 4), option);
        AssertFrame(transport.Requests[0], 2, 0x96);
    }

    [Fact]
    public async Task ImageAndAnimationCommandsUseTheirDeclaredPayloads()
    {
        byte[] imageStates = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        byte[] animationState = [1, 2, 3, 4, 5, 6];
        var transport = new ProbeTransport(request => request[8] == 0x15
            ? Echo(request, imageStates)
            : Echo(request, animationState));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        await service.SetOledHomeScreenImageStateAsync(connection, imageStates);
        var actualAnimationState = await service.GetOledHomeScreenAnimationStateAsync(connection);

        Assert.Equal(animationState, actualAnimationState);
        AssertFrame(transport.Requests[0], 10, 0x15, imageStates);
        AssertFrame(transport.Requests[1], 6, 0x94);
    }

    [Fact]
    public async Task LanguageAndChromaBrightnessStatusReadsRoundTrip()
    {
        var transport = new ProbeTransport(request => request[8] switch
        {
            0x85 => Echo(request, 2),
            0x98 => Echo(request, 2, 3, 4),
            _ => throw new InvalidOperationException("unexpected command"),
        });
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        Assert.Equal((byte)2, await service.GetOledLanguageAsync(connection));
        Assert.Equal(
            new RazerChromaBrightnessStatus(2, 3, 4),
            await service.GetOledChromaBrightnessStatusAsync(connection, 2));

        AssertFrame(transport.Requests[0], 1, 0x85);
        AssertFrame(transport.Requests[1], 3, 0x98, 2);
    }

    [Fact]
    public async Task RemainingWriteCommandsUseTheMiddlewareTuples()
    {
        var transport = new ProbeTransport(request => Echo(request));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var connection = Connection();

        await service.SetOledLanguageAsync(connection, 2);
        await service.SetOledHomeScreenActiveItemAsync(connection, 7);
        await service.SetOledTimeToDimAsync(connection, 30);
        await service.SetOledLowBatteryWarningPercentageAsync(connection, 15);
        await service.SetOledUpdateVolumeAsync(connection, 64, muted: true);
        await service.SetOledChromaBrightnessStatusAsync(connection, 2, 3, 4);

        AssertFrame(transport.Requests[0], 1, 0x05, 2);
        AssertFrame(transport.Requests[1], 2, 0x0D, 7, 7);
        AssertFrame(transport.Requests[2], 1, 0x06, 30);
        AssertFrame(transport.Requests[3], 1, 0x12, 15);
        AssertFrame(transport.Requests[4], 2, 0x0B, 64, 1);
        AssertFrame(transport.Requests[5], 3, 0x18, 2, 3, 4);
    }

    [Fact]
    public async Task OledProbeAddsCapabilitiesForKeyboards()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x028D)!;
        var transport = new ProbeTransport(request => Echo(request, 55));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeOledAsync(definition, "test", result, default);

        Assert.Contains(OpenRazerBackendCapability.OledRead, result);
        Assert.Contains(OpenRazerBackendCapability.OledWrite, result);
        Assert.Equal(1, transport.Count);
        AssertFrame(transport.Requests[0], 1, 0x83);
    }

    [Fact]
    public async Task OledProbeLeavesCapabilitiesEmptyWhenBrightnessReadFails()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x028D)!;
        var transport = new ProbeTransport(_ => throw new NotSupportedException("no OLED"));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeOledAsync(definition, "test", result, default);
        await service.ProbeOledAsync(definition, "test", result, default);

        Assert.Empty(result);
        Assert.Equal(1, transport.Count);
    }

    [Fact]
    public async Task OledProbeSkipsNonKeyboardDevices()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;
        var transport = new ProbeTransport(_ => throw new InvalidOperationException("must not probe"));
        var service = new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport);
        var result = new HashSet<OpenRazerBackendCapability>();

        await service.ProbeOledAsync(definition, "test", result, default);

        Assert.Empty(result);
        Assert.Equal(0, transport.Count);
    }

    private static OpenRazerDeviceConnection Connection()
    {
        var definition = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x028D)!;
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
        Assert.Equal(RazerOledProtocol.CommandClass, request[7]);
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
