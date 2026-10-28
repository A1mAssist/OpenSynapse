using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerOnboardProfileTests
{
    [Fact]
    public async Task PublicOnboardProfileCallsUseTheMiddlewareCommandLayout()
    {
        var transport = new ProbeTransport(request => request[8] switch
        {
            0x8C => Echo(request, 0x02, 0x11, 0x22, 0x33),
            _ => Echo(request, 0x04),
        });
        var service = CreateService(transport);
        var connection = ReadyConnection(Mouse());

        Assert.Equal(4, await service.GetOnboardProfileCountAsync(connection));
        Assert.Equal(4, await service.GetMaxOnboardProfilesAsync(connection));
        Assert.Equal(4, await service.GetActiveOnboardProfileIdAsync(connection));
        Assert.Equal(new RazerProfileColor(0x02, 0x11, 0x22, 0x33),
            await service.GetOnboardProfileColorAsync(connection, 0x02));
        await service.CreateOnboardProfileAsync(connection, 0x03);
        await service.DeleteOnboardProfileAsync(connection, 0x04);
        await service.ResetOnboardProfileAsync(connection, 0x05);
        await service.SetActiveOnboardProfileIdAsync(connection, 0x06);
        await service.SetOnboardProfileColorAsync(connection, 0x07, 0x11, 0x22, 0x33);

        Assert.Equal(9, transport.Requests.Count);
        AssertFrame(transport.Requests[0], 0x80, [], 1);
        AssertFrame(transport.Requests[1], 0x8A, [], 1);
        AssertFrame(transport.Requests[2], 0x84, [], 1);
        AssertFrame(transport.Requests[3], 0x8C, [0x02], 4);
        AssertFrame(transport.Requests[4], 0x02, [0x03], 1);
        AssertFrame(transport.Requests[5], 0x03, [0x04], 1);
        AssertFrame(transport.Requests[6], 0x0B, [0x05], 1);
        AssertFrame(transport.Requests[7], 0x04, [0x06], 1);
        AssertFrame(transport.Requests[8], 0x0C, [0x07, 0x11, 0x22, 0x33], 4);
    }

    [Fact]
    public async Task ProfileReadsReturnPayloadAndRejectTruncatedReports()
    {
        var device = Mouse();
        var transport = new ProbeTransport(request => request[8] switch
        {
            0x80 => Echo(request, 0x03),
            0x8A => Echo(request, 0x05),
            0x84 => Echo(request, 0x02),
            0x8C => Echo(request, 0x04, 0x10, 0x20, 0x30),
            _ => throw new InvalidOperationException(),
        });
        var service = CreateService(transport);
        var connection = ReadyConnection(device);

        Assert.Equal(3, await service.GetOnboardProfileCountAsync(connection));
        Assert.Equal(5, await service.GetMaxOnboardProfilesAsync(connection));
        Assert.Equal(2, await service.GetActiveOnboardProfileIdAsync(connection));
        Assert.Equal(new RazerProfileColor(0x04, 0x10, 0x20, 0x30),
            await service.GetOnboardProfileColorAsync(connection, 0x04));

        var brokenService = CreateService(new ProbeTransport(_ => new byte[8]));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => brokenService.GetOnboardProfileCountAsync(ReadyConnection(device)));
    }

    [Fact]
    public async Task ProfileProbeAddsReadAndWriteOnlyWhenMaxProfilesIsPositive()
    {
        var device = Mouse();
        var positive = new ProbeTransport(request => Echo(request, 0x04));
        var supported = new HashSet<OpenRazerBackendCapability>();

        await CreateService(positive).ProbeOnboardProfileAsync(
            device, "test-path", supported, CancellationToken.None);

        Assert.Contains(OpenRazerBackendCapability.OnboardProfileRead, supported);
        Assert.Contains(OpenRazerBackendCapability.OnboardProfileWrite, supported);
        AssertFrame(Assert.Single(positive.Requests), 0x8A, [], 1);

        var zero = new ProbeTransport(request => Echo(request, 0x00));
        var unsupported = new HashSet<OpenRazerBackendCapability>();
        await CreateService(zero).ProbeOnboardProfileAsync(
            device, "test-path", unsupported, CancellationToken.None);

        Assert.Empty(unsupported);
        Assert.Single(zero.Requests);
    }

    [Fact]
    public async Task ProfileProbeSkipsDevicesOutsideMouseAndKeyboard()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x054A)!;
        var transport = new ProbeTransport(_ => throw new InvalidOperationException("must not probe"));
        var result = new HashSet<OpenRazerBackendCapability>();

        await CreateService(transport).ProbeOnboardProfileAsync(
            device, "test-path", result, CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(transport.Requests);
    }

    private static OpenRazerDeviceDefinition Mouse() =>
        OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x00E5)!;

    private static OpenRazerDeviceConnection ReadyConnection(OpenRazerDeviceDefinition device) =>
        new(device, "test-path", "test-device", OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability>(),
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

    private static OpenRazerDeviceService CreateService(ProbeTransport transport) =>
        new(OpenRazerDeviceCatalog.BuiltIn, transport);

    private static void AssertFrame(byte[] request, byte commandId, byte[] arguments, byte dataSize)
    {
        Assert.Equal(RazerFeatureReport.Length, request.Length);
        Assert.Equal(0x1F, request[2]);
        Assert.Equal(dataSize, request[6]);
        Assert.Equal(0x05, request[7]);
        Assert.Equal(commandId, request[8]);
        Assert.Equal(arguments,
            request[RazerFeatureReport.ArgumentsOffset..(RazerFeatureReport.ArgumentsOffset + arguments.Length)]);
        Assert.Equal(RazerFeatureReport.CalculateCrc(request), request[89]);
    }

    /// <summary>Reply that echoes the request header so <c>IsSuccessfulResponse</c> accepts it.</summary>
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
