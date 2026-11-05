using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioFrameSourceInputTests
{
    [Fact]
    public async Task DeviceSourceOnPressTurnsTheFrameOn()
    {
        var source = new StudioDeviceFrameSource(1, 3);
        source.Update([Layer(StudioPlaybackStart.OnPress)]);

        Assert.All(await RenderAsync(source, 0), color => Assert.Equal(default, color));

        Assert.Equal(1, source.HandleInput(new StudioTriggerInput(0, 1), 0));

        var frame = await RenderAsync(source, 0.01);
        Assert.NotEqual(default, frame[1]);
    }

    [Fact]
    public async Task DeviceSourceSelectedKeysOnlyTriggerTheMatchingMask()
    {
        var source = new StudioDeviceFrameSource(1, 3);
        source.Update(
        [
            Layer(StudioPlaybackStart.OnSelectedKeys, mask: Mask(3, 0)),
            Layer(StudioPlaybackStart.OnSelectedKeys, mask: Mask(3, 2)),
        ]);

        Assert.Equal(0, source.HandleInput(new StudioTriggerInput(0, 1), 0));
        Assert.All(await RenderAsync(source, 0.01), color => Assert.Equal(default, color));

        Assert.Equal(1, source.HandleInput(new StudioTriggerInput(0, 0), 1));

        var frame = await RenderAsync(source, 1.01);
        Assert.NotEqual(default, frame[0]);
        Assert.Equal(default, frame[2]);
    }

    [Fact]
    public async Task DeviceSourceToggleStartsAndStopsTheLayer()
    {
        var source = new StudioDeviceFrameSource(1, 3);
        source.Update([Layer(StudioPlaybackStart.OnPress, StudioPlaybackEnd.Toggle)]);

        Assert.Equal(1, source.HandleInput(new StudioTriggerInput(0, 0), 0));
        Assert.NotEqual(default, (await RenderAsync(source, 0.01))[0]);

        Assert.Equal(1, source.HandleInput(new StudioTriggerInput(0, 0), 1));
        Assert.Equal(default, (await RenderAsync(source, 1.01))[0]);

        Assert.Equal(1, source.HandleInput(new StudioTriggerInput(0, 0), 2));
        Assert.NotEqual(default, (await RenderAsync(source, 2.01))[0]);
    }

    [Fact]
    public async Task BladeSourceOnPressTurnsTheFrameOn()
    {
        var source = new StudioBladeFrameSourceV2();
        source.Update([Layer(StudioPlaybackStart.OnPress)]);

        Assert.All(await RenderAsync(source, 0), color => Assert.Equal(default, color));

        Assert.Equal(1, source.HandleInput(
            new StudioTriggerInput(0, 0), 0));

        Assert.Contains(await RenderAsync(source, 0.01), color => color != default);
    }

    [Fact]
    public async Task DirectBladeSourceTriggerTurnsTheSixBySixteenFrameOn()
    {
        var source = new StudioBladeFrameSourceV2(StudioBladeFrameMode.Direct, 6, 16);
        source.Update([Layer(StudioPlaybackStart.OnPress)]);

        var dark = await RenderAsync(source, 0);
        Assert.Equal(6 * 16, dark.Count);
        Assert.All(dark, color => Assert.Equal(default, color));

        Assert.Equal(1, source.HandleInput(new StudioTriggerInput(0, 0), 0));

        var lit = await RenderAsync(source, 0.01);
        Assert.Equal(6 * 16, lit.Count);
        Assert.Contains(lit, color => color != default);
    }

    [Fact]
    public async Task FinishedRippleDoesNotFreezeTheNextEffect()
    {
        var source = new StudioDeviceFrameSource(5, 16);
        var id = Guid.Parse("8f5e4b3c-2d1a-4c6b-9e7f-0123456789ab");
        source.Update(
        [
            new StudioLayerRender(
                StudioEffectCatalog.CreateDefault(StudioEffectKind.Ripple),
                null,
                100,
                StudioBlendMode.Normal,
                true,
                id),
        ]);

        Assert.Equal(1, source.HandleInput(new StudioTriggerInput(2, 7), 0));
        Assert.Contains(await RenderAsync(source, 0.1), color => color != default);
        Assert.All(await RenderAsync(source, 10), color => Assert.Equal(default, color));

        source.Update(
        [
            new StudioLayerRender(
                new StudioEffectParams
                {
                    Effect = StudioEffectKind.Solid,
                    Primary = new StudioRgb(0, 255, 0),
                },
                null,
                100,
                StudioBlendMode.Normal,
                true,
                id),
        ]);

        Assert.Contains(await RenderAsync(source, 10.1), color => color != default);
    }

    private static StudioLayerRender Layer(
        StudioPlaybackStart start,
        StudioPlaybackEnd end = StudioPlaybackEnd.Never,
        byte[]? mask = null) =>
        new(
            new StudioEffectParams
            {
                Effect = StudioEffectKind.Solid,
                Primary = new StudioRgb(0, 255, 0),
                PlaybackStart = start,
                PlaybackEnd = end,
            },
            mask,
            100,
            StudioBlendMode.Normal,
            true,
            Guid.NewGuid());

    private static byte[] Mask(int length, params int[] selected)
    {
        var mask = new byte[length];
        foreach (var index in selected) mask[index] = StudioMaskBuilder.Full;
        return mask;
    }

    private static async Task<IReadOnlyList<OpenSynapse.Windows.Protocols.RazerRgb>> RenderAsync(
        ISoftwareLightingFrameSource source,
        double seconds) =>
        await source.RenderAsync(TimeSpan.FromSeconds(seconds), CancellationToken.None);
}
