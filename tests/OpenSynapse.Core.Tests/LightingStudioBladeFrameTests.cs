using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioBladeFrameTests
{
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
}
