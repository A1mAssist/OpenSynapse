using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioHidInputBridgeTests
{
    private const string FeaturePath = @"\\?\HID#VID_1532&PID_0203&MI_00&Col03#bridge";
    private const string KeyboardPath = @"\\?\HID#VID_1532&PID_0203&MI_00&Col01#bridge";
    private const string OtherDevicePath = @"\\?\HID#VID_1532&PID_0203&MI_01&Col01#bridge";
    private static readonly Guid LayerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task MappedScanCodeTriggersTheRouter()
    {
        await using var router = await CreateRouterAsync(selectedCell: 0);
        using var bridge = new StudioHidInputBridge(router);

        Assert.Equal(1, bridge.ProcessFrame(Key(30, down: true)));
    }

    [Fact]
    public async Task KeyUpIsIgnoredAndDoesNotBlockTheNextPress()
    {
        await using var router = await CreateRouterAsync(selectedCell: 0);
        using var bridge = new StudioHidInputBridge(router);

        Assert.Equal(1, bridge.ProcessFrame(Key(30, down: true)));
        Assert.Equal(0, bridge.ProcessFrame(Key(30, down: false)));
        Assert.Equal(1, bridge.ProcessFrame(Key(30, down: true)));
    }

    [Fact]
    public async Task UnknownScanCodeIsIgnored()
    {
        await using var router = await CreateRouterAsync(selectedCell: 0);
        using var bridge = new StudioHidInputBridge(router);

        Assert.Equal(0, bridge.ProcessFrame(Key(0x39, down: true)));
        Assert.Equal(1, bridge.ProcessFrame(Key(30, down: true)));
    }

    [Fact]
    public async Task FrameFromAnotherDeviceIsIgnored()
    {
        await using var router = await CreateRouterAsync(selectedCell: 0);
        using var bridge = new StudioHidInputBridge(router);

        Assert.Equal(0, bridge.ProcessFrame(Key(30, down: true, devicePath: OtherDevicePath)));
        Assert.Equal(1, bridge.ProcessFrame(Key(30, down: true)));
    }

    [Fact]
    public async Task HidReportIsIgnored()
    {
        await using var router = await CreateRouterAsync(selectedCell: 0);
        using var bridge = new StudioHidInputBridge(router);

        // Razer report 4 does not carry a proven manifest code space.
        Assert.Equal(0, bridge.ProcessFrame(
            new RawInputFrame(KeyboardPath, true, 0, 0, 0, [0x04, 0x0A])));
    }

    [Fact]
    public async Task ExtendedFlagSelectsTheMatchingManifestEntry()
    {
        // Scan code 29 exists twice: plain control is matrix [0, 1], extended
        // control is [1, 0]. A modifier-blind lookup would trigger the wrong LED.
        await using var router = await CreateRouterAsync(selectedCell: 1);
        using var bridge = new StudioHidInputBridge(router);

        Assert.Equal(1, bridge.ProcessFrame(Key(29, down: true, flags: 0)));
        Assert.Equal(0, bridge.ProcessFrame(Key(29, down: true, flags: 2)));
    }

    private static async Task<StudioHidOutputRouter> CreateRouterAsync(int selectedCell)
    {
        RazerHidDeviceConnection[] connections = [CreateConnection()];
        var router = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, new StubTransport()),
            () => connections);
        router.SetLayout("bridge", CreateLayout());
        router.Update(_ => [Layer(selectedCell)]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        return router;
    }

    private static RazerHidDeviceConnection CreateConnection()
    {
        var definition = new RazerHidDeviceDefinition(
            0x0203,
            "Studio input bridge test keyboard",
            DeviceCategory.Keyboard,
            new HashSet<string>(StringComparer.Ordinal) { "set_key_row" },
            16000,
            [],
            new RazerHidMatrixDimensions(2, 2),
            TimeSpan.FromMilliseconds(1),
            new Dictionary<string, byte>(StringComparer.Ordinal)
            {
                ["razer_chroma_standard_matrix_set_custom_frame"] = 0x46,
            },
            new Dictionary<string, IReadOnlyList<ReadOnlyMemory<byte>>>(StringComparer.Ordinal),
            RazerHidStorage.Persistent,
            RazerHidLedZone.Backlight,
            new Dictionary<string, IReadOnlyList<RazerHidCommandStep>>(StringComparer.Ordinal));
        return new RazerHidDeviceConnection(
            definition,
            FeaturePath,
            "bridge",
            RazerHidEndpointState.Resolved,
            new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MatrixFrameWrite },
            new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(),
            null);
    }

    private static StudioLedLayout CreateLayout() =>
        StudioLedLayout.Parse("""
            {
              "ledConfig_0_1": {
                "MatrixMaxRow": 2,
                "MatrixMaxCol": 2,
                "DeviceMaxRow": 2,
                "DeviceMaxCol": 2,
                "MaxRegion": 1,
                "LedMatrix": [
                  { "MatrixPos": [0, 0], "DevicePos": [0, 0] },
                  { "MatrixPos": [0, 1], "DevicePos": [0, 1] },
                  { "MatrixPos": [1, 0], "DevicePos": [1, 0] },
                  { "MatrixPos": [1, 1], "DevicePos": [1, 1] }
                ],
                "LedInputMap": [
                  { "InputType": "kbd", "InputData": [30, 0], "MatrixPos": [0, 0] },
                  { "InputType": "kbd", "InputData": [29, 0], "MatrixPos": [0, 1] },
                  { "InputType": "kbd", "InputData": [29, 2], "MatrixPos": [1, 0] }
                ]
              }
            }
            """);

    private static StudioLayerRender Layer(int selectedCell)
    {
        var mask = new byte[4];
        mask[selectedCell] = StudioMaskBuilder.Full;
        return new StudioLayerRender(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Solid,
                Primary = new StudioRgb(255, 0, 0),
                DurationMs = 1000,
                Cycles = 1,
                PlaybackStart = StudioPlaybackStart.OnSelectedKeys,
                PlaybackEnd = StudioPlaybackEnd.Never,
            },
            mask,
            100,
            StudioBlendMode.Normal,
            true,
            LayerId);
    }

    private static RawInputFrame Key(
        ushort scanCode,
        bool down,
        ushort flags = 0,
        string? devicePath = null) =>
        new(
            devicePath ?? KeyboardPath,
            false,
            scanCode,
            (ushort)(flags | (down ? 0 : 0x01)),
            0,
            null);

    private sealed class StubTransport : IRazerFeatureTransport
    {
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
            var response = request.ToArray();
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
