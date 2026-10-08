using OpenSynapse.App.Runtime;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerSoftwareLightingRuntimeTests
{
    [Fact]
    public async Task AppliesOneProjectedFrameAndSkipsDuplicate()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var connection = CreateConnection(device, "mamba");
        var transport = new RecordingTransport();
        var runtime = new OpenRazerSoftwareLightingRuntime(
            new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport),
            () => [connection]);

        var frame = new OpenRazerSoftwareLightingFrame(1, 2,
            [new RazerRgb(255, 0, 0), new RazerRgb(0, 0, 255)]);

        Assert.True(await runtime.ApplyFrameAsync(frame));
        Assert.Single(transport.Requests);
        Assert.True(await runtime.ApplyFrameAsync(frame));
        Assert.Single(transport.Requests);
        Assert.Contains((byte)255, transport.Requests[0]);

        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task FrameProviderRunsOncePerTickAndStopsOnCancellation()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var connection = CreateConnection(device, "mamba");
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        await using var runtime = new OpenRazerSoftwareLightingRuntime(
            new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, new RecordingTransport()),
            () => [connection]);

        await runtime.StartAsync(_ =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult<OpenRazerSoftwareLightingFrame?>(
                new OpenRazerSoftwareLightingFrame(1, 1, [new RazerRgb(1, 2, 3)]));
        }, TimeSpan.FromMilliseconds(10), cancellation.Token);

        await Task.Delay(45);
        cancellation.Cancel();
        await runtime.StopAsync();

        Assert.False(runtime.IsRunning);
        Assert.InRange(calls, 2, 8);
    }

    [Fact]
    public void RejectsFramesWhosePixelCountDoesNotMatchDimensions()
    {
        Assert.Throws<ArgumentException>(() =>
            new OpenRazerSoftwareLightingFrame(2, 2, [new RazerRgb(1, 2, 3)]));
    }

    [Fact]
    public async Task DeviceRemovalClearsDedupStateBeforeReconnect()
    {
        var device = OpenRazerDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var connection = CreateConnection(device, "mamba");
        var active = new List<OpenRazerDeviceConnection> { connection };
        var transport = new RecordingTransport();
        await using var runtime = new OpenRazerSoftwareLightingRuntime(
            new OpenRazerDeviceService(OpenRazerDeviceCatalog.BuiltIn, transport),
            () => active);
        var frame = new OpenRazerSoftwareLightingFrame(1, 1, [new RazerRgb(10, 20, 30)]);

        Assert.True(await runtime.ApplyFrameAsync(frame));
        active.Clear();
        Assert.False(await runtime.ApplyFrameAsync(frame));
        active.Add(connection);
        Assert.True(await runtime.ApplyFrameAsync(frame));
        Assert.Equal(2, transport.Requests.Count);
    }

    private static OpenRazerDeviceConnection CreateConnection(OpenRazerDeviceDefinition device, string id) =>
        new(device, $"test-{id}", id, OpenRazerEndpointState.Resolved,
            new HashSet<OpenRazerBackendCapability> { OpenRazerBackendCapability.MatrixFrameWrite },
            new Dictionary<OpenRazerLedZone, OpenRazerLightingZoneCapabilities>(), null);

    private sealed class RecordingTransport : IRazerFeatureTransport
    {
        internal List<byte[]> Requests { get; } = [];

        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            Requests.Add(response.ToArray());
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
