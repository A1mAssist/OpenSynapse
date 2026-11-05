using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioCombinedOutputRouterTests
{
    private static readonly Guid LayerId = Guid.Parse("e3f6c6d1-0c4d-4f2c-b4aa-1f1f6a2f7b61");

    [Fact]
    public async Task CombinedPumpKeepsHidAndArgbFramesSeparate()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        RazerHidDeviceConnection[] connections =
        [
            new(keyboard, "test-keyboard", "keyboard", RazerHidEndpointState.Resolved,
                new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MatrixFrameWrite },
                new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null),
        ];
        var hidTransport = new RecordingHidTransport();
        var argbTransport = new RecordingArgbTransport();
        await using var hid = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, hidTransport),
            () => connections);
        await using var argb = new StudioArgbOutputRouter(
            _ => Task.FromResult<RazerHidSpecialLightingConnection?>(ArgbConnection()),
            argbTransport.WriteAsync,
            null,
            () => [2, 2, 2, 2, 2, 2]);
        hid.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await argb.RefreshAsync());
        argb.Update(_ => [Solid(new StudioRgb(0, 0, 255))]);

        Assert.True(await hid.ApplyAsync(TimeSpan.Zero));
        Assert.True(await argb.ApplyAsync(TimeSpan.Zero));

        Assert.NotEmpty(hidTransport.Reports);
        Assert.All(hidTransport.Reports, report => Assert.Equal(91, report.Length));
        Assert.Equal(6, argbTransport.Frames.Count);
        Assert.All(argbTransport.Frames.SelectMany(frame => frame.Colors),
            color => Assert.Equal(new RazerHidColor(0, 0, 255), color));
        Assert.DoesNotContain(hidTransport.Reports, report =>
            report.Length == RazerHidArgbFrame.ReportLength);
        Assert.DoesNotContain(argbTransport.Frames.SelectMany(frame => frame.Colors),
            color => color == new RazerHidColor(0, 255, 0));
    }

    [Fact]
    public async Task ArgbFailureDoesNotStopTheHidRouter()
    {
        var keyboard = RazerHidDeviceCatalog.BuiltIn.Find(0x1532, 0x0203)!;
        RazerHidDeviceConnection[] connections =
        [
            new(keyboard, "test-keyboard", "keyboard", RazerHidEndpointState.Resolved,
                new HashSet<RazerHidBackendCapability> { RazerHidBackendCapability.MatrixFrameWrite },
                new Dictionary<RazerHidLedZone, RazerHidLightingZoneCapabilities>(), null),
        ];
        var hidTransport = new RecordingHidTransport();
        var argbTransport = new RecordingArgbTransport { FailAll = true };
        await using var hid = new StudioHidOutputRouter(
            new RazerHidDeviceService(RazerHidDeviceCatalog.BuiltIn, hidTransport),
            () => connections);
        await using var argb = new StudioArgbOutputRouter(
            _ => Task.FromResult<RazerHidSpecialLightingConnection?>(ArgbConnection()),
            argbTransport.WriteAsync,
            null,
            () => [1, 1, 1, 1, 1, 1]);
        var failures = new List<string>();
        argb.WriteFailed += (name, _) => failures.Add(name);
        hid.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await argb.RefreshAsync());
        argb.Update(_ => [Solid(new StudioRgb(0, 0, 255))]);

        Assert.True(await hid.ApplyAsync(TimeSpan.Zero));
        Assert.True(await argb.ApplyAsync(TimeSpan.Zero));

        Assert.NotEmpty(hidTransport.Reports);
        Assert.Equal(6, failures.Count);
    }

    private static StudioLayerRender Solid(StudioRgb color) =>
        new(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Solid,
                Primary = color,
            },
            null,
            100,
            StudioBlendMode.Normal,
            true,
            LayerId);

    private static RazerHidSpecialLightingConnection ArgbConnection() =>
        new(
            0x0F1F,
            "ARGB",
            RazerHidSpecialLightingKind.Argb320,
            "argb-test",
            null,
            @"\?\control-argb",
            @"\?\feature-argb",
            new HashSet<RazerHidLightingEffect> { RazerHidLightingEffect.Custom },
            false,
            false,
            null);

    private sealed record RecordedFrame(byte Channel, RazerHidColor[] Colors);

    private sealed class RecordingArgbTransport
    {
        internal List<RecordedFrame> Frames { get; } = [];
        internal bool FailAll { get; init; }

        internal Task WriteAsync(
            RazerHidSpecialLightingConnection connection,
            byte channel,
            IReadOnlyList<RazerHidColor> colors,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailAll) throw new IOException("Simulated ARGB failure.");
            Frames.Add(new RecordedFrame(channel, colors.ToArray()));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHidTransport : IRazerFeatureTransport
    {
        internal List<byte[]> Reports { get; } = [];

        public Task<byte[]> QueryAsync(string devicePath, byte transactionId, byte dataSize,
            byte commandClass, byte commandId, ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<byte[]> QueryPreparedAsync(string devicePath, ReadOnlyMemory<byte> request,
            TimeSpan deviceWait, CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var response = request.ToArray();
            Reports.Add(response);
            response[1] = 0x02;
            return Task.FromResult(response);
        }
    }
}
