using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioBladeFrameTests
{
    [Fact]
    public void ResolveRejectsDowngradedDialectAndMismatchedCollection()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x023A)!;
        var descriptor = new DeviceDescriptor(
            "blade",
            manifest.DisplayName,
            0x1532,
            0x023A,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            manifest.Collection.FeatureReportLength,
            manifest.Collection.UsagePage,
            manifest.Collection.Usage,
            "blade");

        Assert.Equal(StudioBladeFrameMode.Direct, StudioBladeDevices.Resolve(descriptor)?.Mode);

        Assert.Null(StudioBladeDevices.Resolve(descriptor with
        {
            Usage = (ushort)(descriptor.Usage + 1),
        }));

        BladeLightingRuntimeDialect.Reset();
        try
        {
            BladeLightingRuntimeDialect.Downgrade(0x023A, "firmware rejected native effects");
            Assert.Null(StudioBladeDevices.Resolve(descriptor));
        }
        finally
        {
            BladeLightingRuntimeDialect.Reset();
        }
    }

    [Fact]
    public void ResolveKeepsThe710BladeAndRejectsFirmwareEffects()
    {
        var blade710 = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6)!;
        var eligible = DescriptorFor(blade710, 0x02C6);
        Assert.Equal(StudioBladeFrameMode.Sparse710, StudioBladeDevices.Resolve(eligible)?.Mode);

        var firmware = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x029F)!;
        Assert.Null(StudioBladeDevices.Resolve(DescriptorFor(firmware, 0x029F)));
    }

    private static DeviceDescriptor DescriptorFor(RazerDeviceManifest manifest, ushort productId) =>
        new(
            "blade",
            manifest.DisplayName,
            0x1532,
            productId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            manifest.Collection.FeatureReportLength,
            manifest.Collection.UsagePage,
            manifest.Collection.Usage,
            "blade");

    [Fact]
    public async Task RandomLayerRendersOntoTheDevice()
    {
        var source = new StudioBladeFrameSourceV2();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Solid,
            Primary = new StudioRgb(0, 255, 0),
            PlaybackStart = StudioPlaybackStart.Random,
        };
        source.Update([new StudioLayerRender(parameters, null, 100, StudioBlendMode.Normal, true, Guid.NewGuid())]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(new RazerRgb(0, 255, 0), frame[1]);
    }

    [Fact]
    public async Task OnPressLayerStaysDarkUntilTriggered()
    {
        var source = new StudioBladeFrameSourceV2();
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Solid,
            Primary = new StudioRgb(0, 255, 0),
            PlaybackStart = StudioPlaybackStart.OnPress,
            PlaybackEnd = StudioPlaybackEnd.Never,
        };
        source.Update([new StudioLayerRender(parameters, null, 100, StudioBlendMode.Normal, true, Guid.NewGuid())]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(default, frame[1]);
    }

    [Fact]
    public async Task SparseModeKeepsThe710SparseDeviceMap()
    {
        var source = new StudioBladeFrameSourceV2();
        source.Update([Green()]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(BladeLightingLayout.DevicePixelCount, frame.Count);
        Assert.Equal(new RazerRgb(0, 255, 0), frame[1]);
        Assert.Equal(default, frame[0]);
        Assert.Equal(default, frame[17]);
    }

    [Fact]
    public async Task DirectModeRendersSixBySixteenWithoutThe710Map()
    {
        var source = new StudioBladeFrameSourceV2(StudioBladeFrameMode.Direct, 6, 16);
        source.Update([Green()]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(6 * 16, frame.Count);
        Assert.All(frame, color => Assert.Equal(new RazerRgb(0, 255, 0), color));
    }

    [Fact]
    public async Task LedMatrixBladePumpWritesTheDirectSixBySixteenFrame()
    {
        var session = new RecordingSession();
        byte transactionId = 0;
        await using var pump = new BladeMatrixFramePump(
            new RecordingTransport(session),
            "blade",
            _ => Task.CompletedTask,
            6,
            16,
            (frame, rows, columns) => BladeLightingLedMatrixBladeProtocol.CreateMatrixFrameRequests(
                frame, rows, columns, () => transactionId++));

        var frame = Enumerable.Range(0, 6 * 16)
            .Select(index => new RazerRgb((byte)index, 0, 0))
            .ToArray();
        Assert.True(pump.TryPublish(frame));
        await pump.FirstFrameApplied;
        await pump.StopAsync();

        byte expectedTransactionId = 0;
        var expected = BladeLightingLedMatrixBladeProtocol.CreateMatrixFrameRequests(
            frame, 6, 16, () => expectedTransactionId++);
        Assert.Equal(expected, session.Sent);
    }

    private static StudioLayerRender Green() =>
        new(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Solid,
                Primary = new StudioRgb(0, 255, 0),
            },
            null,
            100,
            StudioBlendMode.Normal,
            true,
            Guid.NewGuid());

    private sealed class RecordingTransport(RecordingSession session) : IRazerFeatureTransport
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
            bool allowRemainingPacketsMismatch = false) => throw new NotSupportedException();

        public Task<IRazerFeatureSession> OpenSessionAsync(
            string devicePath,
            CancellationToken cancellationToken) => Task.FromResult<IRazerFeatureSession>(session);
    }

    private sealed class RecordingSession : IRazerFeatureSession
    {
        internal List<byte[]> Sent { get; } = [];

        public byte NextTransactionId() => 0;

        public Task SendAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken)
        {
            Sent.Add(request.ToArray());
            return Task.CompletedTask;
        }

        public Task<byte[]> QueryAsync(
            byte transactionId,
            byte dataSize,
            byte commandClass,
            byte commandId,
            ReadOnlyMemory<byte> arguments,
            TimeSpan deviceWait,
            byte responseReportId,
            CancellationToken cancellationToken,
            bool allowRemainingPacketsMismatch = false)
        {
            var request = RazerFeatureReport.CreateRequest(
                transactionId, dataSize, commandClass, commandId, arguments.Span);
            request[0] = responseReportId;
            Sent.Add(request);
            return Task.FromResult(request);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
