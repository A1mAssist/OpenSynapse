using OpenSynapse.Core.Devices;
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
        Assert.Equal(16, transport.Requests.Count);

        router.Update(_ => [Solid(new StudioRgb(255, 0, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(23, transport.Requests.Count);
    }

    [Fact]
    public async Task MatrixWithoutFrameOrStaticOutputIsNotATarget()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            new(
                definition,
                "test-no-output",
                "no-output",
                RazerHidEndpointState.Resolved,
                new HashSet<RazerHidBackendCapability>(),
                new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(),
                null),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);

        Assert.Empty(router.GetTargets());
        Assert.False(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task ReportsWriteFailureWithDeviceName()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        RazerHidDeviceConnection[] connections = [CreateConnection(keyboard, "keyboard")];
        var transport = new RecordingTransport { FailingDevicePath = "test-keyboard" };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        var failures = new List<string>();
        router.WriteFailed += (name, _) => failures.Add(name);

        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        Assert.Equal(keyboard.DisplayName, Assert.Single(failures));
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
    public async Task ReconnectRebuildsWithTheBoundLayout()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        RazerHidDeviceConnection[] active =
        [
            CreateConnection(keyboard, "shared", devicePath: "test-layout"),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => active);
        router.SetLayout("shared", CreateLayout(deviceRow: 0, deviceColumn: 0));
        router.Update(_ => [Layer(new StudioRgb(255, 0, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        var rows = ReadRows(transport, "test-layout");
        Assert.Equal(new RazerRgb(255, 0, 0), rows[0][0]);
        Assert.Equal(default, rows[0][1]);

        // Same instance id, new endpoint and a moved manifest mapping.
        active = [CreateConnection(keyboard, "shared", devicePath: "test-layout")];
        router.SetLayout("shared", CreateLayout(deviceRow: 2, deviceColumn: 0));
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        rows = ReadRows(transport, "test-layout");
        Assert.Equal(default, rows[0][0]);
        Assert.Equal(new RazerRgb(255, 0, 0), rows[2][0]);
    }

    [Fact]
    public async Task ReconnectWithDifferentDimensionsRebuildsTheFrameSource()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        var mouse = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0053)!;
        RazerHidDeviceConnection[] active =
        [
            CreateConnection(keyboard, "shared", devicePath: "test-dimensions"),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => active);
        router.SetLayout("shared", CreateLayout(deviceRow: 0, deviceColumn: 0));
        router.Update(_ => [Layer(new StudioRgb(0, 0, 255))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(6, ReadRows(transport, "test-dimensions").Count);

        active = [CreateConnection(mouse, "shared", devicePath: "test-dimensions-mouse")];
        router.SetLayout("shared", CreateLayout(deviceRow: 0, deviceColumn: 2));
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        var row = Assert.Single(ReadRows(transport, "test-dimensions-mouse")).Value;
        Assert.Equal(3, row.Length);
        Assert.Equal(default, row[0]);
        Assert.Equal(new RazerRgb(0, 0, 255), row[2]);
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

    [Fact]
    public async Task InputOnlyReachesTheNamedDevice()
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
        router.Update(_ => [Layer(new StudioRgb(0, 255, 0), start: StudioPlaybackStart.OnPress)]);

        Assert.Equal(0, router.HandleInput("keyboard", new StudioTriggerInput(0, 0), 0));
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.All(
            ReadRows(transport, "test-keyboard").Values.SelectMany(row => row),
            color => Assert.Equal(default, color));
        Assert.All(
            ReadRows(transport, "test-mouse").Values.SelectMany(row => row),
            color => Assert.Equal(default, color));

        Assert.Equal(0, router.HandleInput("missing", new StudioTriggerInput(0, 0), 0));
        Assert.Equal(1, router.HandleInput("keyboard", new StudioTriggerInput(0, 0), 0));

        Assert.True(await router.ApplyAsync(TimeSpan.FromSeconds(1)));
        Assert.All(
            ReadRows(transport, "test-keyboard").Values.SelectMany(row => row),
            color => Assert.Equal(new RazerRgb(0, 255, 0), color));
        Assert.All(
            ReadRows(transport, "test-mouse").Values.SelectMany(row => row),
            color => Assert.Equal(default, color));
    }

    [Fact]
    public async Task CapturesBaselineBeforeFirstFrameAndRestoresOnStop()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            CreateBaselineConnection(definition, "baseline"),
        ];
        var transport = new RecordingTransport
        {
            LedEffect = (byte)RazerHidClassicLedEffect.Static,
            LedColor = new RazerHidColor(12, 34, 56),
        };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        var effectRead = Assert.Single(transport.Requests, request => IsEffectRead(request.Report));
        var colorRead = Assert.Single(transport.Requests, request => IsColorRead(request.Report));
        var firstFrame = transport.Requests.FindIndex(request => IsFrameWrite(request.Report));
        Assert.True(firstFrame >= 0);
        Assert.True(transport.Requests.IndexOf(effectRead) < firstFrame);
        Assert.True(transport.Requests.IndexOf(colorRead) < firstFrame);

        router.Update(_ => [Solid(new StudioRgb(0, 0, 255))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Single(transport.Requests, request => IsEffectRead(request.Report));
        Assert.Single(transport.Requests, request => IsColorRead(request.Report));

        await router.StopAsync();

        var restore = Assert.Single(transport.Requests, request => IsStaticEffectWrite(request.Report));
        Assert.Equal((byte)0x06, restore.Report[9]);
        Assert.Equal((byte)12, restore.Report[10]);
        Assert.Equal((byte)34, restore.Report[11]);
        Assert.Equal((byte)56, restore.Report[12]);
    }

    [Fact]
    public void CatalogDevicesExposeNoRestorableReadZone()
    {
        foreach (var definition in RazerHidDeviceCatalog.BuiltIn.Devices)
        {
            var zones = RazerHidDeviceService.GetLightingZoneCapabilities(definition);
            Assert.DoesNotContain(zones.Values, zone =>
                zone.CanReadEffect && zone.LightingEffects.Count > 0 ||
                zone.CanReadColor && zone.CanWriteColor);
        }
    }

    [Fact]
    public async Task CatalogDeviceWithoutReadbackUsesTheDocumentedExitEffect()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        RazerHidDeviceConnection[] connections =
        [
            new(keyboard, "test-real-keyboard", "real-keyboard", RazerHidEndpointState.Resolved,
                RazerHidDeviceService.GetCapabilities(keyboard),
                RazerHidDeviceService.GetLightingZoneCapabilities(keyboard), null),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        var failures = new List<string>();
        router.RestoreFailed += (name, _) => failures.Add(name);
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Contains(transport.Requests, request => IsFrameWrite(request.Report));

        await router.StopAsync();

        Assert.Equal(keyboard.DisplayName, Assert.Single(failures));
        Assert.Contains(transport.Requests, request => IsSpectrumEffectWrite(request.Report));
        Assert.DoesNotContain(transport.Requests, request =>
            IsEffectRead(request.Report) || IsColorRead(request.Report) ||
            IsStaticEffectWrite(request.Report) || IsColorWrite(request.Report));
    }

    [Fact]
    public async Task StopWithoutFallbackLeavesRestoreToTheConfiguredLighting()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        RazerHidDeviceConnection[] connections =
        [
            new(keyboard, "test-real-keyboard", "real-keyboard", RazerHidEndpointState.Resolved,
                RazerHidDeviceService.GetCapabilities(keyboard),
                RazerHidDeviceService.GetLightingZoneCapabilities(keyboard), null),
        ];
        var transport = new RecordingTransport();
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        var failures = new List<string>();
        router.RestoreFailed += (name, _) => failures.Add(name);
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        await router.StopAsync(applyFallback: false);

        Assert.Empty(failures);
        Assert.DoesNotContain(transport.Requests, request => IsSpectrumEffectWrite(request.Report));
        Assert.False(await router.ApplyAsync(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0x00A3, RazerHidLedZone.Logo)]
    [InlineData(0x0C00, RazerHidLedZone.Backlight)]
    public async Task WritesOneStaticZoneColorForDevicesWithoutFrameWrite(
        ushort productId,
        RazerHidLedZone expectedZone)
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, productId)!;
        var connection = new RazerHidDeviceConnection(
            definition,
            $"test-zone-{productId:X4}",
            $"zone-{productId:X4}",
            RazerHidEndpointState.Resolved,
            RazerHidDeviceService.GetCapabilities(definition),
            RazerHidDeviceService.GetLightingZoneCapabilities(definition),
            null);
        var transport = new RecordingTransport();
        var restoreFailures = 0;
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => [connection]);
        router.RestoreFailed += (_, _) => restoreFailures++;

        Assert.Equal(expectedZone, StudioHidOutputRouter.FindStaticZone(connection));
        Assert.Same(connection, Assert.Single(router.GetTargets()));

        router.Update(_ => [Solid(new StudioRgb(10, 20, 30))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        var expected = RazerHidLightingProtocol.CreateEffect(definition,
            new RazerHidLightingSettings(
                RazerHidLightingEffect.Static,
                Primary: new RazerHidColor(10, 20, 30),
                Storage: definition.DefaultStorage,
                Zone: expectedZone));
        var request = Assert.Single(transport.Requests);
        Assert.Equal(expected.Report, request.Report);
        Assert.DoesNotContain(transport.Requests, item => IsFrameWrite(item.Report));

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Single(transport.Requests);

        await router.StopAsync();
        Assert.Equal(1, restoreFailures);
        Assert.Contains(transport.Requests, item => IsSpectrumEffectWrite(item.Report));
    }

    [Fact]
    public async Task OffOnlyZoneDeviceWithoutFrameWriteStaysOutOfTargets()
    {
        var definition = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x010F)!;
        var connection = new RazerHidDeviceConnection(
            definition,
            "test-off-only",
            "off-only",
            RazerHidEndpointState.Resolved,
            RazerHidDeviceService.GetCapabilities(definition),
            RazerHidDeviceService.GetLightingZoneCapabilities(definition),
            null);

        Assert.Null(StudioHidOutputRouter.FindStaticZone(connection));
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, new RecordingTransport()),
            () => [connection]);
        Assert.Empty(router.GetTargets());
    }

    [Fact]
    public async Task ReportsRestoreFailureWithDeviceNameAndError()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            CreateBaselineConnection(definition, "restore-failure"),
        ];
        var transport = new RecordingTransport
        {
            LedEffect = (byte)RazerHidClassicLedEffect.Static,
            LedColor = new RazerHidColor(12, 34, 56),
            FailEffectWrite = true,
        };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        var failures = new List<(string Name, string Message)>();
        router.RestoreFailed += (name, exception) => failures.Add((name, exception.Message));
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.StopAsync());

        var failure = Assert.Single(failures);
        Assert.Equal(definition.DisplayName, failure.Name);
        Assert.Contains("Simulated restore failure", failure.Message);
        Assert.Contains(transport.Requests, request => IsColorWrite(request.Report));

        transport.FailEffectWrite = false;
        await router.StopAsync();
        Assert.Contains(transport.Requests, request => IsStaticEffectWrite(request.Report));
    }

    [Fact]
    public async Task BaselineReadFailureDoesNotBlockFramesOrStop()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            CreateBaselineConnection(definition, "read-failure"),
        ];
        var transport = new RecordingTransport { FailBaselineReads = true };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        var restoreFailures = 0;
        router.RestoreFailed += (_, _) => restoreFailures++;
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Contains(transport.Requests, request => IsFrameWrite(request.Report));

        await router.StopAsync();

        Assert.Equal(1, restoreFailures);
        Assert.Contains(transport.Requests, request => IsSpectrumEffectWrite(request.Report));
        Assert.DoesNotContain(transport.Requests, request =>
            IsStaticEffectWrite(request.Report) || IsColorWrite(request.Report));
    }

    [Fact]
    public async Task StopSkipsRestoreWhenDeviceIsGone()
    {
        var definition = CreateBaselineDefinition();
        var connections = new List<RazerHidDeviceConnection>
        {
            CreateBaselineConnection(definition, "gone"),
        };
        var transport = new RecordingTransport
        {
            LedEffect = (byte)RazerHidClassicLedEffect.Static,
            LedColor = new RazerHidColor(12, 34, 56),
        };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        connections.Clear();
        await router.StopAsync();

        Assert.DoesNotContain(transport.Requests, request =>
            IsStaticEffectWrite(request.Report) || IsColorWrite(request.Report));
    }

    [Fact]
    public async Task RepeatedStopRestoresOnlyOnce()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            CreateBaselineConnection(definition, "repeat-stop"),
        ];
        var transport = new RecordingTransport
        {
            LedEffect = (byte)RazerHidClassicLedEffect.Static,
            LedColor = new RazerHidColor(12, 34, 56),
        };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        await router.StopAsync();
        await router.StopAsync();

        Assert.Single(transport.Requests, request => IsStaticEffectWrite(request.Report));
    }

    [Fact]
    public async Task DeviceWithoutEffectWriteRestoresOnlyColor()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            CreateBaselineConnection(definition, "color-only", effectWrite: false),
        ];
        var transport = new RecordingTransport
        {
            LedEffect = (byte)RazerHidClassicLedEffect.Static,
            LedColor = new RazerHidColor(7, 8, 9),
        };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        Assert.DoesNotContain(transport.Requests, request => IsEffectRead(request.Report));
        Assert.Single(transport.Requests, request => IsColorRead(request.Report));

        await router.StopAsync();

        var restore = Assert.Single(transport.Requests, request => IsColorWrite(request.Report));
        Assert.Equal((byte)7, restore.Report[11]);
        Assert.Equal((byte)8, restore.Report[12]);
        Assert.Equal((byte)9, restore.Report[13]);
        Assert.DoesNotContain(transport.Requests, request => IsStaticEffectWrite(request.Report));
    }

    [Fact]
    public async Task StaticBaselineWithoutColorReportsAnUnrestorableZone()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            CreateBaselineConnection(definition, "static-no-color"),
        ];
        var transport = new RecordingTransport
        {
            LedEffect = (byte)RazerHidClassicLedEffect.Static,
            FailColorRead = true,
        };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        var restoreFailures = 0;
        router.RestoreFailed += (_, _) => restoreFailures++;
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.StopAsync());

        Assert.Equal(1, restoreFailures);
        Assert.DoesNotContain(transport.Requests, request =>
            IsStaticEffectWrite(request.Report) || IsColorWrite(request.Report));
    }

    [Fact]
    public async Task OfficialExitEffectFailureIsReportedAndCanBeRetried()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            CreateBaselineConnection(definition, "exit-failure", effectWrite: false),
        ];
        var transport = new RecordingTransport
        {
            FailBaselineReads = true,
            FailSpectrumEffectWrite = true,
        };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        var restoreFailures = 0;
        router.RestoreFailed += (_, _) => restoreFailures++;
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.StopAsync());
        Assert.Equal(1, restoreFailures);

        transport.FailSpectrumEffectWrite = false;
        await router.StopAsync();
        Assert.Contains(transport.Requests, request => IsSpectrumEffectWrite(request.Report));
    }

    [Fact]
    public async Task InputAfterFailedStopIsIgnored()
    {
        var definition = CreateBaselineDefinition();
        RazerHidDeviceConnection[] connections =
        [
            CreateBaselineConnection(definition, "restore-failure"),
        ];
        var transport = new RecordingTransport
        {
            LedEffect = (byte)RazerHidClassicLedEffect.Static,
            LedColor = new RazerHidColor(12, 34, 56),
            FailEffectWrite = true,
        };
        await using var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, transport),
            () => connections);
        router.Update(_ => [Layer(new StudioRgb(0, 255, 0), start: StudioPlaybackStart.OnPress)]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(1, router.HandleInput("restore-failure", new StudioTriggerInput(0, 0), 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.StopAsync());

        // The failed restore keeps the output for a retry; a stopped session must
        // still ignore key input until it is re-applied.
        Assert.Equal(0, router.HandleInput("restore-failure", new StudioTriggerInput(0, 0), 1));
    }

    private static StudioLedLayout CreateLayout(int deviceRow, int deviceColumn) =>
        StudioLedLayout.Parse($$"""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 1,
                "MatrixMaxCol": 1,
                "DeviceMaxRow": 6,
                "DeviceMaxCol": 22,
                "MaxRegion": 1,
                "LedMatrix": [
                  { "MatrixPos": [0, 0], "DevicePos": [{{deviceRow}}, {{deviceColumn}}] }
                ]
              }
            }
            """);

    private static StudioLayerRender Solid(StudioRgb color) => Layer(color);

    private static StudioLayerRender Layer(
        StudioRgb color,
        byte[]? mask = null,
        int durationMs = 0,
        StudioPlaybackStart start = StudioPlaybackStart.Random) =>
        new(new StudioEffectParams
            {
                Effect = StudioEffectKind.Solid,
                Primary = color,
                DurationMs = durationMs,
                Cycles = 1,
                PlaybackStart = start,
                PlaybackEnd = durationMs > 0 ? StudioPlaybackEnd.After : StudioPlaybackEnd.Never,
            },
            mask, 100, StudioBlendMode.Normal, true, LayerId);

    private static Dictionary<byte, RazerRgb[]> ReadRows(
        RecordingTransport transport,
        string devicePath)
    {
        var rows = new Dictionary<byte, RazerRgb[]>();
        foreach (var request in transport.Requests.Where(request =>
            string.Equals(request.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase) &&
            IsFrameWrite(request.Report)))
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

    private static RazerHidDeviceDefinition CreateBaselineDefinition()
    {
        var transactions = new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            ["razer_chroma_standard_matrix_set_custom_frame"] = 0x3F,
            ["razer_chroma_standard_get_led_effect"] = 0xFF,
            ["razer_chroma_standard_get_led_rgb"] = 0xFF,
            ["razer_chroma_standard_set_led_rgb"] = 0xFF,
            ["razer_chroma_standard_matrix_effect_static"] = 0x3F,
            ["razer_chroma_standard_matrix_effect_spectrum"] = 0x3F,
            ["razer_chroma_standard_matrix_effect_none"] = 0x3F,
        };
        var prefixes = new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(StringComparer.Ordinal)
        {
            ["razer_chroma_standard_get_led_effect"] = [new byte[] { 0x01, 0x05 }],
            ["razer_chroma_standard_get_led_rgb"] = [new byte[] { 0x01, 0x05 }],
            ["razer_chroma_standard_set_led_rgb"] = [new byte[] { 0x01, 0x05 }],
            ["razer_chroma_standard_matrix_effect_static"] = [new byte[] { 0x01 }],
            ["razer_chroma_standard_matrix_effect_spectrum"] = [new byte[] { 0x01 }],
            ["razer_chroma_standard_matrix_effect_none"] = [new byte[] { 0x01 }],
        };
        return new RazerHidDeviceDefinition(
            0x0203,
            "Baseline test keyboard",
            DeviceCategory.Keyboard,
            new HashSet<string>(StringComparer.Ordinal) { "set_key_row" },
            16000,
            [],
            new RazerHidMatrixDimensions(1, 2),
            TimeSpan.FromMilliseconds(1),
            transactions,
            prefixes,
            RazerHidStorage.Persistent,
            RazerHidLedZone.Backlight,
            new Dictionary<string, IReadOnlyList<RazerHidCommandStep>>(StringComparer.Ordinal));
    }

    private static RazerHidDeviceConnection CreateBaselineConnection(
        RazerHidDeviceDefinition device,
        string id,
        bool effectWrite = true)
    {
        var capabilities = new HashSet<RazerHidBackendCapability>
        {
            RazerHidBackendCapability.MatrixFrameWrite,
            RazerHidBackendCapability.LedColorWrite,
        };
        if (effectWrite)
        {
            capabilities.Add(RazerHidBackendCapability.LightingEffectWrite);
        }

        var zone = new RazerHidLightingZoneCapabilities(
            device.DefaultLedZone,
            CanReadBrightness: false,
            CanWriteBrightness: false,
            CanReadState: false,
            CanWriteState: false,
            CanReadEffect: true,
            CanWriteEffect: effectWrite,
            CanReadColor: true,
            CanWriteColor: true,
            CanWriteBlinking: false,
            LightingEffects: new HashSet<RazerHidLightingEffect>
            {
                RazerHidLightingEffect.Static,
                RazerHidLightingEffect.Spectrum,
            });
        return new RazerHidDeviceConnection(
            device,
            $"test-{id}",
            id,
            RazerHidEndpointState.Resolved,
            capabilities,
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>
            {
                [device.DefaultLedZone] = zone,
            },
            null);
    }

    private static bool IsEffectRead(byte[] report) =>
        report[7] == 0x03 && report[8] == 0x82;

    private static bool IsColorRead(byte[] report) =>
        report[7] == 0x03 && report[8] == 0x81;

    private static bool IsFrameWrite(byte[] report) =>
        report[7] == 0x03 && report[8] == 0x0B ||
        report[7] == 0x0F && report[8] == 0x03;

    private static bool IsStaticEffectWrite(byte[] report) =>
        report[7] == 0x03 && report[8] == 0x0A && report[6] == 4 && report[9] == 0x06;

    private static bool IsSpectrumEffectWrite(byte[] report) =>
        report[7] == 0x03 && report[8] == 0x0A &&
        report[6] >= 1 && report[9] == 0x04 ||
        report[7] == 0x0F && report[8] == 0x02 &&
        report[6] >= 6 && report[11] == 0x03;

    private static bool IsColorWrite(byte[] report) =>
        report[7] == 0x03 && report[8] == 0x01;

    private static RazerHidDeviceConnection CreateConnection(
        RazerHidDeviceDefinition device,
        string id,
        RazerHidEndpointState state = RazerHidEndpointState.Resolved,
        string? devicePath = null) =>
        new(device, devicePath ?? $"test-{id}", id, state,
            RazerHidDeviceService.GetCapabilities(device),
            RazerHidDeviceService.GetLightingZoneCapabilities(device), null);

    private static RazerHidDeviceConnection CreateConnectionWithoutKey(
        RazerHidDeviceDefinition device,
        string devicePath) =>
        new(device, devicePath, null, RazerHidEndpointState.Resolved,
            RazerHidDeviceService.GetCapabilities(device),
            RazerHidDeviceService.GetLightingZoneCapabilities(device), null);

    private sealed record RecordedRequest(string DevicePath, byte[] Report);

    private sealed class RecordingTransport : IRazerFeatureTransport
    {
        internal List<RecordedRequest> Requests { get; } = [];
        internal string? FailingDevicePath { get; init; }
        internal int? FailAtRequestCount { get; set; }
        internal byte? LedEffect { get; init; }
        internal RazerHidColor? LedColor { get; init; }
        internal bool FailBaselineReads { get; init; }
        internal bool FailColorRead { get; init; }
        internal bool FailEffectWrite { get; set; }
        internal bool FailSpectrumEffectWrite { get; set; }

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

            if ((FailBaselineReads && (IsEffectRead(response) || IsColorRead(response))) ||
                (FailColorRead && IsColorRead(response)))
            {
                throw new IOException("Simulated baseline read failure.");
            }

            if (FailEffectWrite && IsStaticEffectWrite(response))
            {
                throw new IOException("Simulated restore failure.");
            }

            if (FailSpectrumEffectWrite && IsSpectrumEffectWrite(response))
            {
                throw new IOException("Simulated exit effect failure.");
            }

            response[1] = 0x02;
            if (IsEffectRead(response) && LedEffect is { } effect)
            {
                response[6] = 3;
                response[11] = effect;
                response[89] = RazerFeatureReport.CalculateCrc(response);
            }
            else if (IsColorRead(response) && LedColor is { } color)
            {
                response[6] = 5;
                response[11] = color.Red;
                response[12] = color.Green;
                response[13] = color.Blue;
                response[89] = RazerFeatureReport.CalculateCrc(response);
            }

            return Task.FromResult(response);
        }
    }
}
