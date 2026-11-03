using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidSoftwareLightingRuntimeTests
{
    [Fact]
    public async Task AppliesOneProjectedFrameAndSkipsDuplicate()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var connection = CreateConnection(device, "mamba");
        var transport = new RecordingTransport();
        var runtime = new RazerHidSoftwareLightingRuntime(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => [connection]);

        var frame = new RazerHidSoftwareLightingFrame(1, 2,
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
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var connection = CreateConnection(device, "mamba");
        var calls = 0;
        var secondCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await using var runtime = new RazerHidSoftwareLightingRuntime(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, new RecordingTransport()),
            () => [connection]);

        await runtime.StartAsync(_ =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                secondCall.TrySetResult();
                cancellation.Cancel();
            }

            return ValueTask.FromResult<RazerHidSoftwareLightingFrame?>(
                new RazerHidSoftwareLightingFrame(1, 1, [new RazerRgb(1, 2, 3)]));
        }, TimeSpan.FromMilliseconds(10), cancellation.Token);

        await secondCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runtime.StopAsync();

        Assert.False(runtime.IsRunning);
        var callsAtStop = Volatile.Read(ref calls);
        await Task.Delay(40);
        Assert.Equal(callsAtStop, Volatile.Read(ref calls));
        Assert.Equal(2, callsAtStop);
    }

    [Fact]
    public void RejectsFramesWhosePixelCountDoesNotMatchDimensions()
    {
        Assert.Throws<ArgumentException>(() =>
            new RazerHidSoftwareLightingFrame(2, 2, [new RazerRgb(1, 2, 3)]));
    }

    [Fact]
    public async Task DeviceRemovalClearsDedupStateBeforeReconnect()
    {
        var device = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var connection = CreateConnection(device, "mamba");
        var active = new List<RazerHidDeviceConnection> { connection };
        var transport = new RecordingTransport();
        await using var runtime = new RazerHidSoftwareLightingRuntime(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => active);
        var frame = new RazerHidSoftwareLightingFrame(1, 1, [new RazerRgb(10, 20, 30)]);

        Assert.True(await runtime.ApplyFrameAsync(frame));
        active.Clear();
        Assert.False(await runtime.ApplyFrameAsync(frame));
        active.Add(connection);
        Assert.True(await runtime.ApplyFrameAsync(frame));
        Assert.Equal(2, transport.Requests.Count);
    }

    private static RazerHidDeviceConnection CreateConnection(RazerHidDeviceDefinition device, string id) =>
        new(device, $"test-{id}", id, RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MatrixFrameWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

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
