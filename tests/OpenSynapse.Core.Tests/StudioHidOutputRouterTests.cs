using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioHidOutputRouterTests
{
    private static readonly Guid LayerId = Guid.Parse("a4f0e2c8-1f6b-4c2e-9d0d-8b3f4c5a6d77");

    [Fact]
    public async Task WritesEachMatrixDeviceOnceAndHonorsStop()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var unavailable = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0204)!;
        RazerHidDeviceConnection[] connections =
        [
            CreateConnection(keyboard, "keyboard"),
            CreateConnection(mouse, "mouse"),
            CreateConnection(unavailable, "unavailable", RazerHidEndpointState.BusyOrUnavailable),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);

        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(7, transport.Requests.Count);
        Assert.DoesNotContain(
            transport.Requests,
            request => request.DevicePath == "test-unavailable");

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(7, transport.Requests.Count);

        router.Update(_ => [Solid(new StudioRgb(0, 0, 255))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(14, transport.Requests.Count);

        await router.StopAsync();
        Assert.False(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(14, transport.Requests.Count);

        router.Update(_ => [Solid(new StudioRgb(255, 0, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(21, transport.Requests.Count);
    }

    [Fact]
    public async Task WritesIndependentFramesAndMasksPerDevice()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        RazerHidDeviceConnection[] connections =
        [
            CreateConnection(keyboard, "keyboard"),
            CreateConnection(mouse, "mouse"),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);

        router.Update(connection =>
        {
            if (connection.InstanceId == "keyboard")
            {
                var mask = new byte[6 * 22];
                mask[0] = StudioMaskBuilder.Full;
                return [Layer(new StudioRgb(255, 0, 0), mask)];
            }

            var mouseMask = new byte[1 * 3];
            mouseMask[2] = StudioMaskBuilder.Full;
            return [Layer(new StudioRgb(0, 0, 255), mouseMask)];
        });

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        var keyboardRows = ReadRows(transport, "test-keyboard");
        Assert.Equal(6, keyboardRows.Count);
        Assert.Equal(22, keyboardRows[0].Length);
        Assert.Equal(new RazerRgb(255, 0, 0), keyboardRows[0][0]);
        Assert.All(keyboardRows[0].Skip(1), color => Assert.Equal(default, color));
        for (byte row = 1; row < 6; row++)
        {
            Assert.All(keyboardRows[row], color => Assert.Equal(default, color));
        }

        var mouseRow = Assert.Single(ReadRows(transport, "test-mouse")).Value;
        Assert.Equal(3, mouseRow.Length);
        Assert.Equal(default, mouseRow[0]);
        Assert.Equal(default, mouseRow[1]);
        Assert.Equal(new RazerRgb(0, 0, 255), mouseRow[2]);
    }

    [Fact]
    public async Task SameSizeDevicesKeepSeparateFrames()
    {
        var first = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var second = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0209)!;
        RazerHidDeviceConnection[] connections =
        [
            CreateConnection(first, "keyboard-a", devicePath: "test-a"),
            CreateConnection(second, "keyboard-b", devicePath: "test-b"),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(connection => connection.InstanceId == "keyboard-a"
            ? [Layer(new StudioRgb(255, 0, 0))]
            : [Layer(new StudioRgb(0, 0, 255))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        var firstRows = ReadRows(transport, "test-a");
        var secondRows = ReadRows(transport, "test-b");
        Assert.Equal(6, firstRows.Count);
        Assert.Equal(6, secondRows.Count);
        Assert.All(
            firstRows.Values.SelectMany(row => row),
            color => Assert.Equal(new RazerRgb(255, 0, 0), color));
        Assert.All(
            secondRows.Values.SelectMany(row => row),
            color => Assert.Equal(new RazerRgb(0, 0, 255), color));
    }

    [Fact]
    public async Task SameModelDevicesWithoutAPhysicalKeyKeepSeparateOutputs()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        RazerHidDeviceConnection[] connections =
        [
            CreateConnectionWithoutKey(keyboard, "test-first"),
            CreateConnectionWithoutKey(keyboard, "test-second"),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Layer(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(12, transport.Requests.Count);

        // Distinct instance ids keep both dedup states; a shared PID fallback would
        // make the two outputs rewrite themselves on every unchanged tick.
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(12, transport.Requests.Count);
        Assert.Equal(6, transport.Requests.Count(request => request.DevicePath == "test-first"));
        Assert.Equal(6, transport.Requests.Count(request => request.DevicePath == "test-second"));
    }

    [Fact]
    public async Task PerDevicePlaybackClocksStopIndependently()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        RazerHidDeviceConnection[] connections =
        [
            CreateConnection(keyboard, "keyboard"),
            CreateConnection(mouse, "mouse"),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(connection => connection.InstanceId == "keyboard"
            ? [Layer(new StudioRgb(255, 0, 0), durationMs: 100)]
            : [Layer(new StudioRgb(0, 0, 255), durationMs: 10_000)]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        var before = transport.Requests.Count;
        Assert.True(await router.ApplyAsync(TimeSpan.FromSeconds(1)));

        Assert.Equal(before + 6, transport.Requests.Count);
        var keyboardRows = ReadRows(transport, "test-keyboard");
        Assert.Equal(6, keyboardRows.Count);
        Assert.All(
            keyboardRows.Values.SelectMany(row => row),
            color => Assert.Equal(default, color));
        var mouseRow = Assert.Single(ReadRows(transport, "test-mouse")).Value;
        Assert.All(mouseRow, color => Assert.Equal(new RazerRgb(0, 0, 255), color));
    }

    [Fact]
    public async Task RemovingOneDeviceKeepsOtherDeviceDedupState()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        var keyboardConnection = CreateConnection(keyboard, "keyboard");
        var mouseConnection = CreateConnection(mouse, "mouse");
        var active = new List<RazerHidDeviceConnection> { keyboardConnection, mouseConnection };
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => active);
        router.Update(_ => [Layer(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(7, transport.Requests.Count);

        active.Remove(mouseConnection);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(7, transport.Requests.Count);

        active.Add(mouseConnection);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(8, transport.Requests.Count);
    }

    [Fact]
    public async Task ReconnectWithDifferentProductRebindsDeviceLayers()
    {
        var first = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var second = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0209)!;
        RazerHidDeviceConnection[] active =
        [
            CreateConnection(first, "shared", devicePath: "test-first"),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => active);
        router.Update(connection =>
        {
            var mask = new byte[6 * 22];
            if (connection.Definition.ProductId == 0x0203)
            {
                mask[0] = StudioMaskBuilder.Full;
                return [Layer(new StudioRgb(255, 0, 0), mask)];
            }

            mask[^1] = StudioMaskBuilder.Full;
            return [Layer(new StudioRgb(0, 0, 255), mask)];
        });

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(new RazerRgb(255, 0, 0), ReadRows(transport, "test-first")[0][0]);

        active = [CreateConnection(second, "shared", devicePath: "test-second")];
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        var rows = ReadRows(transport, "test-second");
        Assert.Equal(6, rows.Count);
        Assert.Equal(default, rows[0][0]);
        Assert.Equal(new RazerRgb(0, 0, 255), rows[5][21]);
    }

    [Fact]
    public async Task PartialWriteFailureForcesFullRewriteOnRetry()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        RazerHidDeviceConnection[] connections = [CreateConnection(keyboard, "keyboard")];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Layer(new StudioRgb(255, 0, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(6, transport.Requests.Count);

        transport.FailAtRequestCount = transport.Requests.Count + 4;
        router.Update(_ => [Layer(new StudioRgb(0, 0, 255))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(10, transport.Requests.Count);

        router.Update(_ => [Layer(new StudioRgb(255, 0, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(16, transport.Requests.Count);

        Assert.All(
            ReadRows(transport, "test-keyboard").Values.SelectMany(row => row),
            color => Assert.Equal(new RazerRgb(255, 0, 0), color));
    }

    [Fact]
    public async Task OneDeviceWriteFailureDoesNotBlockOtherDevices()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        RazerHidDeviceConnection[] connections =
        [
            CreateConnection(mouse, "mouse"),
            CreateConnection(keyboard, "keyboard"),
        ];
        var transport = new RecordingTransport { FailingDevicePath = "test-mouse" };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Layer(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(1, transport.Requests.Count(request => request.DevicePath == "test-mouse"));
        Assert.Equal(6, transport.Requests.Count(request => request.DevicePath == "test-keyboard"));

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(2, transport.Requests.Count(request => request.DevicePath == "test-mouse"));
        Assert.Equal(6, transport.Requests.Count(request => request.DevicePath == "test-keyboard"));
    }

    private static StudioLayerRender Solid(StudioRgb color) => Layer(color);

    private static StudioLayerRender Layer(
        StudioRgb color,
        byte[]? mask = null,
        int durationMs = 0) =>
        new(new StudioEffectParams
            {
                Effect = StudioEffectKind.Solid,
                Primary = color,
                DurationMs = durationMs,
                Cycles = 1,
                PlaybackEnd = durationMs > 0 ? StudioPlaybackEnd.After : StudioPlaybackEnd.Never,
            },
            mask, 100, StudioBlendMode.Normal, true, LayerId);

    private static Dictionary<byte, RazerRgb[]> ReadRows(
        RecordingTransport transport,
        string devicePath)
    {
        var rows = new Dictionary<byte, RazerRgb[]>();
        foreach (var request in transport.Requests.Where(request =>
            string.Equals(request.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)))
        {
            var report = request.Report;
            var standard = report[RazerFeatureReport.CommandClassOffset] == 0x03 &&
                report[RazerFeatureReport.CommandIdOffset] == 0x0B;
            var row = standard ? report[10] : report[11];
            var start = standard ? report[11] : report[12];
            var stop = standard ? report[12] : report[13];
            var offset = standard ? 13 : 14;
            var colors = new RazerRgb[stop - start + 1];
            for (var index = 0; index < colors.Length; index++)
            {
                var position = offset + index * 3;
                colors[index] = new RazerRgb(
                    report[position], report[position + 1], report[position + 2]);
            }

            rows[row] = colors;
        }

        return rows;
    }

    private static RazerHidDeviceConnection CreateConnection(
        RazerHidDeviceDefinition device,
        string id,
        RazerHidEndpointState state = RazerHidEndpointState.Resolved,
        string? devicePath = null) =>
        new(device, devicePath ?? $"test-{id}", id, state,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MatrixFrameWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

    private static RazerHidDeviceConnection CreateConnectionWithoutKey(
        RazerHidDeviceDefinition device,
        string devicePath) =>
        new(device, devicePath, null, RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MatrixFrameWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null);

    private sealed record RecordedRequest(string DevicePath, byte[] Report);

    private sealed class RecordingTransport : IRazerFeatureTransport
    {
        internal List<RecordedRequest> Requests { get; } = [];
        internal string? FailingDevicePath { get; init; }
        internal int? FailAtRequestCount { get; set; }

        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            Requests.Add(new RecordedRequest(devicePath, response.ToArray()));
            if (string.Equals(devicePath, FailingDevicePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Simulated HID write failure.");
            }

            if (FailAtRequestCount is { } failAt && Requests.Count == failAt)
            {
                FailAtRequestCount = null;
                throw new IOException("Simulated partial HID write failure.");
            }

            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
