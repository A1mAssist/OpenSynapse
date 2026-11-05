using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidDiscoveryCacheRegressionTests
{
    [Fact]
    public async Task MiddlewareProbeCacheIsPerEndpointAndTransientFailuresRetry()
    {
        var definition = Find(0x00E5);
        var transport = new EndpointTransport
        {
            Handler = (path, request) => path == "awake" && request[8] == 0x84
                ? Echo(request, 0x03, 0x01, 0x02, 0x04)
                : throw new NotSupportedException(),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var awake = await service.ProbeMiddlewareCapabilitiesAsync(definition, "awake");
        var awakeMappingCount = transport.CountFor("awake", 0x84);
        var sleeping = await service.ProbeMiddlewareCapabilitiesAsync(definition, "sleeping");

        Assert.Contains(RazerHidBackendCapability.MappingRead, awake);
        Assert.Empty(sleeping);
        Assert.Equal(1, awakeMappingCount);

        transport.Handler = (path, request) => path == "sleeping" && request[8] == 0x84
            ? Echo(request, 0x03, 0x01, 0x02, 0x04)
            : throw new NotSupportedException();
        var awakened = await service.ProbeMiddlewareCapabilitiesAsync(definition, "sleeping");
        Assert.Contains(RazerHidBackendCapability.MappingWrite, awakened);

        var beforeCachedAwake = transport.CountFor("awake", 0x84);
        await service.ProbeMiddlewareCapabilitiesAsync(definition, "awake");
        Assert.Equal(beforeCachedAwake, transport.CountFor("awake", 0x84));
    }

    [Fact]
    public async Task CachedEndpointMustAnswerAgainBeforeItResolves()
    {
        var definition = Find(0x00E5);
        var transport = new EndpointTransport
        {
            Handler = (_, request) => Echo(request),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        Assert.Equal("cached", (await service.ResolveEndpointPathAsync(definition, ["cached"])).Path);

        transport.Handler = (_, _) => throw new IOException("receiver asleep");
        var sleeping = await service.ResolveEndpointPathAsync(definition, ["cached"]);
        Assert.Null(sleeping.Path);
        Assert.Equal("receiver asleep", sleeping.Error);

        transport.Handler = (_, request) => Echo(request);
        Assert.Equal("cached", (await service.ResolveEndpointPathAsync(definition, ["cached"])).Path);
    }

    [Fact]
    public async Task MismatchedOriginalIdentityCannotPromoteAnAnsweringReadToWrite()
    {
        var definition = Find(0x00A2);
        var transport = new EndpointTransport
        {
            Handler = (_, request) => request[8] == 0x84
                ? Echo(request, 0x03, 0x01, 0x02, 0x04)
                : throw new NotSupportedException(),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var capabilities = await service.ProbeMiddlewareCapabilitiesAsync(definition, "cobra");
        Assert.Contains(RazerHidBackendCapability.MappingRead, capabilities);
        Assert.DoesNotContain(capabilities, capability => capability.ToString().EndsWith("Write", StringComparison.Ordinal));
        Assert.DoesNotContain(RazerHidDeviceService.GetCapabilities(definition),
            capability => capability.ToString().EndsWith("Write", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MappingOnlyEndpointResolvesThroughTheReadOnlyFallback()
    {
        var definition = Find(0x00E5);
        var transport = new EndpointTransport
        {
            Handler = (_, request) => request[7] == 0x02 && request[8] == 0x84
                ? Echo(request, 0x03, 0x01, 0x02, 0x04)
                : throw new IOException("identity probes unavailable"),
        };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);

        var resolved = await service.ResolveEndpointPathAsync(definition, ["mapping-only"]);

        Assert.Equal("mapping-only", resolved.Path);
        Assert.Contains(transport.Requests, request =>
            request[7] == 0x02 && request[8] == 0x84);
    }

    [Fact]
    public async Task OrochiV2DpiWriteUsesOneAxisValue()
    {
        var definition = Find(0x00E0);
        var transport = new EndpointTransport { Handler = (_, request) => Echo(request) };
        var service = new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport);
        var connection = new RazerHidDeviceConnection(definition, "orochi", "orochi",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.DpiWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

        await service.SetDpiAsync(connection, 800, 1600);

        var report = Assert.Single(transport.Requests);
        Assert.Equal(report[10], report[12]);
        Assert.Equal(report[11], report[13]);
    }

    private static RazerHidDeviceDefinition Find(ushort productId) =>
        RazerHidDeviceCatalog.BuiltIn.Find(RazerHidDeviceDefinition.VendorId, productId)!;

    private static byte[] Echo(byte[] request, params byte[] arguments)
    {
        var response = request.ToArray();
        response[1] = 0x02;
        if (arguments.Length > 0)
        {
            response[6] = (byte)arguments.Length;
            arguments.CopyTo(response, RazerFeatureReport.ArgumentsOffset);
        }
        response[89] = RazerFeatureReport.CalculateCrc(response);
        return response;
    }

    private sealed class EndpointTransport : IRazerFeatureTransport
    {
        public required Func<string, byte[], byte[]> Handler { get; set; }
        public int Count { get; private set; }
        public List<byte[]> Requests { get; } = [];
        private readonly Dictionary<(string Path, byte Command), int> _counts = [];

        public int CountFor(string path, byte command) => _counts.GetValueOrDefault((path, command));

        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments, TimeSpan deviceWait,
            CancellationToken cancellationToken, bool allowRemainingPacketsMismatch = false) =>
            throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            Count++;
            var bytes = request.ToArray();
            Requests.Add(bytes);
            _counts[(devicePath, bytes[8])] = CountFor(devicePath, bytes[8]) + 1;
            return Task.FromResult(Handler(devicePath, bytes));
        }
    }
}
