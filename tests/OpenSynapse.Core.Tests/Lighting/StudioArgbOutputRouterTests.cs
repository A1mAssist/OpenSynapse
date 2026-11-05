using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioArgbOutputRouterTests
{
    private static readonly Guid LayerId = Guid.Parse("7c9b1f62-4d8e-4a3a-9b7a-2e1d0c5f8a44");

    [Fact]
    public async Task WritesEveryConfiguredPortAtItsChannelAndLength()
    {
        var transport = new RecordingArgbTransport();
        var prepareCalls = 0;
        await using var router = CreateRouter(
            transport,
            [3, 2, 1],
            () => prepareCalls++);

        Assert.True(await router.RefreshAsync());
        Assert.Equal(1, prepareCalls);
        Assert.Equal(6, router.GetTargets().Count);
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        Assert.Equal(6, transport.Frames.Count);
        Assert.Equal(Enumerable.Range(0, 6).Select(value => (byte)value), transport.Frames.Select(frame => frame.Channel));
        Assert.Equal([3, 2, 1, 80, 80, 80], transport.Frames.Select(frame => frame.Colors.Length));
        Assert.All(transport.Frames.SelectMany(frame => frame.Colors), color =>
            Assert.Equal(new RazerHidColor(0, 255, 0), color));
    }

    [Fact]
    public async Task DeduplicatesFramesAndWritesAgainAfterStopAndUpdate()
    {
        var transport = new RecordingArgbTransport();
        await using var router = CreateRouter(transport, [2, 2, 2]);
        await router.RefreshAsync();
        router.Update(_ => [Solid(new StudioRgb(255, 0, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(6, transport.Frames.Count);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(6, transport.Frames.Count);

        router.Update(_ => [Solid(new StudioRgb(0, 0, 255))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(12, transport.Frames.Count);
        Assert.All(transport.Frames.Skip(6).SelectMany(frame => frame.Colors), color =>
            Assert.Equal(new RazerHidColor(0, 0, 255), color));

        await router.StopAsync();
        Assert.False(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(12, transport.Frames.Count);

        router.Update(_ => [Solid(new StudioRgb(255, 255, 255))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(18, transport.Frames.Count);
    }

    [Fact]
    public async Task ReportsOnePortFailureAndRetriesOnlyThatPort()
    {
        var transport = new RecordingArgbTransport { FailingChannel = 2 };
        await using var router = CreateRouter(transport, [1, 1, 1, 1, 1, 1]);
        var failures = new List<(string Name, string Message)>();
        router.WriteFailed += (name, exception) => failures.Add((name, exception.Message));
        await router.RefreshAsync();
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(5, transport.Frames.Count);
        Assert.DoesNotContain(transport.Frames, frame => frame.Channel == 2);
        var failure = Assert.Single(failures);
        Assert.Contains("#3", failure.Name);
        Assert.Contains("Simulated ARGB write failure", failure.Message);
        transport.FailingChannel = null;

        Assert.True(await router.ApplyAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(6, transport.Frames.Count);
        Assert.Equal((byte)2, transport.Frames.Single(frame => frame.Channel == 2).Channel);
    }

    [Fact]
    public async Task RefreshesCustomFrameModeOnlyOncePerEndpoint()
    {
        var transport = new RecordingArgbTransport();
        var prepareCalls = 0;
        await using var router = CreateRouter(
            transport,
            [1, 1, 1],
            () => prepareCalls++);

        Assert.True(await router.RefreshAsync());
        Assert.True(await router.RefreshAsync());
        Assert.Equal(1, prepareCalls);

        await router.StopAsync();
        Assert.True(await router.RefreshAsync());
        Assert.Equal(2, prepareCalls);
    }

    [Fact]
    public async Task ReconnectRebindsEveryPortAndRewritesTheFrame()
    {
        var transport = new RecordingArgbTransport();
        var connection = CreateConnection();
        var prepareCalls = 0;
        await using var router = new StudioArgbOutputRouter(
            _ => Task.FromResult<RazerHidSpecialLightingConnection?>(connection),
            transport.WriteAsync,
            (_, _) =>
            {
                prepareCalls++;
                return Task.CompletedTask;
            },
            () => [2, 2, 2]);
        await router.RefreshAsync();
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(6, transport.Frames.Count);

        connection = CreateConnection(
            "argb-two", @"\\?\control-two", @"\\?\feature-two");
        Assert.True(await router.RefreshAsync());
        Assert.Equal(2, prepareCalls);
        Assert.All(router.GetTargets(), port => Assert.Equal("argb-two", port.InstanceId));
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Equal(12, transport.Frames.Count);
        Assert.All(transport.Frames.Skip(6), frame => Assert.Equal("argb-two", frame.InstanceId));
    }

    [Fact]
    public async Task RegionOnOneChannelOnlyLightsThatChannel()
    {
        var transport = new RecordingArgbTransport();
        await using var router = CreateRouter(transport, [3, 3, 3, 3, 3, 3]);
        await router.RefreshAsync();

        var device = new StudioRegionDevice { ProductId = 0x0F1F, InstanceId = "argb-test" };
        device.Leds.Add(new StudioLedRef(2, 1));
        var region = new StudioRegion();
        region.Devices.Add(device);
        var mask = StudioMaskBuilder.BuildRegion(region, 0x0F1F, "argb-test", 6, 3);
        var layers = new[]
        {
            new StudioLayerRender(
                new StudioEffectParams
                {
                    Effect = StudioEffectKind.Solid,
                    Primary = new StudioRgb(0, 255, 0),
                },
                mask,
                100,
                StudioBlendMode.Normal,
                true,
                LayerId),
        };
        router.Update(port => StudioArgbDevice.SliceChannel(
            layers, 6, 3, port.Channel, port.LedCount));

        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        var channelTwo = transport.Frames.Single(frame => frame.Channel == 2).Colors;
        Assert.Equal(new RazerHidColor(0, 255, 0), channelTwo[1]);
        Assert.Equal(new RazerHidColor(0, 0, 0), channelTwo[0]);
        Assert.All(transport.Frames.Where(frame => frame.Channel != 2)
            .SelectMany(frame => frame.Colors),
            color => Assert.Equal(new RazerHidColor(0, 0, 0), color));
    }

    [Fact]
    public async Task MissingControlEndpointReportsTheDeviceError()
    {
        var transport = new RecordingArgbTransport();
        var connection = CreateConnection(
            controlPath: null,
            error: "ARGB needs Razer's 0F1F control driver.");
        await using var router = new StudioArgbOutputRouter(
            _ => Task.FromResult<RazerHidSpecialLightingConnection?>(connection),
            transport.WriteAsync,
            null,
            () => [1, 1, 1]);
        var failures = new List<(string Name, string Message)>();
        router.WriteFailed += (name, exception) => failures.Add((name, exception.Message));

        Assert.False(await router.RefreshAsync());
        Assert.Empty(router.GetTargets());
        var failure = Assert.Single(failures);
        Assert.Equal(connection.DisplayName, failure.Name);
        Assert.Contains("control driver", failure.Message);
    }

    [Fact]
    public async Task NoReadyControllerAppliesNothing()
    {
        var transport = new RecordingArgbTransport();
        await using var router = new StudioArgbOutputRouter(
            _ => Task.FromResult<RazerHidSpecialLightingConnection?>(null),
            transport.WriteAsync,
            null,
            () => [1, 1, 1]);

        Assert.False(await router.RefreshAsync());
        Assert.Empty(router.GetTargets());
        Assert.False(await router.ApplyAsync(TimeSpan.Zero));
        Assert.Empty(transport.Frames);
    }

    [Fact]
    public async Task StopAppliesTheDocumentedExitEffectOnceAfterFrames()
    {
        var transport = new RecordingArgbTransport();
        var restoreCalls = 0;
        await using var router = CreateRouter(
            transport,
            [2, 2, 2],
            restore: (_, _) =>
            {
                restoreCalls++;
                return Task.CompletedTask;
            });
        await router.RefreshAsync();
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        await router.StopAsync();
        await router.StopAsync();

        Assert.Equal(1, restoreCalls);
    }

    [Fact]
    public async Task StopWithoutWrittenFramesDoesNotApplyTheExitEffect()
    {
        var transport = new RecordingArgbTransport();
        var restoreCalls = 0;
        await using var router = CreateRouter(
            transport,
            [2, 2, 2],
            restore: (_, _) =>
            {
                restoreCalls++;
                return Task.CompletedTask;
            });
        await router.RefreshAsync();

        await router.StopAsync();

        Assert.Equal(0, restoreCalls);
    }

    [Fact]
    public async Task StopReportsExitEffectFailureAndRetries()
    {
        var transport = new RecordingArgbTransport();
        var fail = true;
        var restoreCalls = 0;
        await using var router = CreateRouter(
            transport,
            [2, 2, 2],
            restore: (_, _) =>
            {
                restoreCalls++;
                return fail ? throw new IOException("Simulated ARGB restore failure.") : Task.CompletedTask;
            });
        await router.RefreshAsync();
        router.Update(_ => [Solid(new StudioRgb(0, 255, 0))]);
        Assert.True(await router.ApplyAsync(TimeSpan.Zero));

        await Assert.ThrowsAsync<IOException>(() => router.StopAsync());
        fail = false;
        await router.StopAsync();

        Assert.Equal(2, restoreCalls);
    }

    private static StudioArgbOutputRouter CreateRouter(
        RecordingArgbTransport transport,
        IReadOnlyList<int> counts,
        Action? onPrepare = null,
        Func<RazerHidSpecialLightingConnection, CancellationToken, Task>? restore = null) =>
        new(
            _ => Task.FromResult<RazerHidSpecialLightingConnection?>(CreateConnection()),
            transport.WriteAsync,
            (_, _) =>
            {
                onPrepare?.Invoke();
                return Task.CompletedTask;
            },
            () => counts,
            restore: restore);

    private static RazerHidSpecialLightingConnection CreateConnection(
        string instanceId = "argb-test",
        string? controlPath = @"\\?\control-argb",
        string featurePath = @"\\?\feature-argb",
        string? error = null) =>
        new(
            0x0F1F,
            "ARGB",
            RazerHidSpecialLightingKind.Argb320,
            instanceId,
            null,
            controlPath,
            featurePath,
            new HashSet<RazerHidLightingEffect> { RazerHidLightingEffect.Custom },
            false,
            false,
            error);

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

    private sealed record RecordedFrame(string InstanceId, byte Channel, RazerHidColor[] Colors);

    private sealed class RecordingArgbTransport
    {
        internal List<RecordedFrame> Frames { get; } = [];
        internal byte? FailingChannel { get; set; }

        internal Task WriteAsync(
            RazerHidSpecialLightingConnection connection,
            byte channel,
            IReadOnlyList<RazerHidColor> colors,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (channel == FailingChannel)
            {
                throw new IOException("Simulated ARGB write failure.");
            }

            Frames.Add(new RecordedFrame(connection.InstanceId, channel, colors.ToArray()));
            return Task.CompletedTask;
        }
    }
}
