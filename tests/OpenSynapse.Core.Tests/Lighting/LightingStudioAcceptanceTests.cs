using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>End-to-end checks for the plan's acceptance phrases.</summary>
public sealed class LightingStudioAcceptanceTests
{
    [Fact]
    public void ThreeKeyBreathingOnlyLightsTheSelectedKeys()
    {
        var parameters = StudioEffectCatalog.CreateDefault(StudioEffectKind.Breathing);
        parameters.DurationMs = 1_000;
        var mask = new byte[7 * 16];
        foreach (var index in new[] { 0, 1, 16 })
        {
            mask[index] = StudioMaskBuilder.Full;
        }

        var clock = new StudioPlaybackClock(0.5);
        var state = clock.Sample(0, parameters, 7, 16);
        var layer = new StudioLayerRender(
            parameters, mask, 100, StudioBlendMode.Normal, true, Guid.NewGuid());
        var frame = StudioCompositor.RenderTimedLayers(
            [(layer, state.LocalSeconds)], 7, 16);

        Assert.True(state.Running);
        Assert.NotEqual(default, frame[0]);
        Assert.NotEqual(default, frame[1]);
        Assert.NotEqual(default, frame[16]);
        Assert.Equal(default, frame[2]);
        Assert.Equal(default, frame[15]);
        Assert.Equal(default, frame[17]);
    }
}
