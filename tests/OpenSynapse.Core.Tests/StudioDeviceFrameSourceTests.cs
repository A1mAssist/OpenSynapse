using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioDeviceFrameSourceTests
{
    [Theory]
    [InlineData(2, 5)]
    [InlineData(6, 1)]
    [InlineData(1, 22)]
    public async Task RendersTheDeviceMatrixSize(int rows, int columns)
    {
        var source = new StudioDeviceFrameSource(rows, columns);
        source.Update([Solid(Guid.NewGuid(), new StudioRgb(0, 255, 0))]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(rows * columns, frame.Count);
        Assert.All(frame, color => Assert.Equal(new RazerRgb(0, 255, 0), color));
    }

    [Fact]
    public async Task LayerPhaseIsStableAcrossSources()
    {
        var id = Guid.Parse("8d2b387f-5f5b-4f3f-9d2f-6d6c3d0f9a11");
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Wave,
            Primary = new StudioRgb(0, 255, 0),
            Secondary = new StudioRgb(0, 0, 255),
        };
        var first = new StudioDeviceFrameSource(2, 6);
        var second = new StudioDeviceFrameSource(2, 6);
        first.Update([new StudioLayerRender(parameters, null, 100, StudioBlendMode.Normal, true, id)]);
        second.Update([new StudioLayerRender(parameters, null, 100, StudioBlendMode.Normal, true, id)]);

        var firstFrame = await first.RenderAsync(TimeSpan.FromSeconds(1.25), CancellationToken.None);
        var secondFrame = await second.RenderAsync(TimeSpan.FromSeconds(1.25), CancellationToken.None);

        Assert.Equal(firstFrame, secondFrame);
    }

    [Fact]
    public async Task FinishedLayerRendersTransparentBlack()
    {
        var source = new StudioDeviceFrameSource(2, 3);
        var parameters = new StudioEffectParams
        {
            Effect = StudioEffectKind.Solid,
            Primary = new StudioRgb(0, 255, 0),
            DurationMs = 100,
            Cycles = 1,
            PlaybackEnd = StudioPlaybackEnd.After,
        };
        source.Update([new StudioLayerRender(parameters, null, 100, StudioBlendMode.Normal, true, Guid.NewGuid())]);

        var running = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);
        var finished = await source.RenderAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(new RazerRgb(0, 255, 0), running[0]);
        Assert.All(finished, color => Assert.Equal(default, color));
    }

    [Fact]
    public async Task SourcesKeepIndependentMasksAndClocks()
    {
        var id = Guid.Parse("5f3c7a42-8b21-4d9e-a1f0-2c6d4e8b9a13");
        var shortSource = new StudioDeviceFrameSource(2, 2);
        var longSource = new StudioDeviceFrameSource(1, 3);
        var shortMask = new byte[2 * 2];
        shortMask[0] = StudioMaskBuilder.Full;
        var longMask = new byte[1 * 3];
        longMask[2] = StudioMaskBuilder.Full;
        shortSource.Update([Solid(id, new StudioRgb(255, 0, 0), shortMask, durationMs: 100)]);
        longSource.Update([Solid(id, new StudioRgb(0, 0, 255), longMask, durationMs: 10_000)]);

        var shortFirst = await shortSource.RenderAsync(TimeSpan.Zero, CancellationToken.None);
        var longFirst = await longSource.RenderAsync(TimeSpan.Zero, CancellationToken.None);
        var shortLater = await shortSource.RenderAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        var longLater = await longSource.RenderAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(new RazerRgb(255, 0, 0), shortFirst[0]);
        Assert.All(shortFirst.Skip(1), color => Assert.Equal(default, color));
        Assert.Equal(default, longFirst[0]);
        Assert.Equal(new RazerRgb(0, 0, 255), longFirst[2]);
        Assert.All(shortLater, color => Assert.Equal(default, color));
        Assert.Equal(default, longLater[0]);
        Assert.Equal(new RazerRgb(0, 0, 255), longLater[2]);
    }

    [Fact]
    public async Task RendersMoreThanOneThousandPixels()
    {
        var source = new StudioDeviceFrameSource(64, 16);
        var mask = new byte[64 * 16];
        mask[0] = StudioMaskBuilder.Full;
        mask[^1] = StudioMaskBuilder.Full;
        source.Update([Solid(Guid.NewGuid(), new StudioRgb(0, 255, 0), mask)]);

        var frame = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        // Functional 1024-LED composite only; wall-clock timing is not canvas FPS evidence.
        Assert.Equal(1024, frame.Count);
        Assert.Equal(new RazerRgb(0, 255, 0), frame[0]);
        Assert.Equal(default, frame[1]);
        Assert.Equal(new RazerRgb(0, 255, 0), frame[^1]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(65, 65)]
    public void RejectsMatricesOutsideTheCompositorRange(int rows, int columns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StudioDeviceFrameSource(rows, columns));
    }

    private static StudioLayerRender Solid(
        Guid id,
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
            mask, 100, StudioBlendMode.Normal, true, id);
}
