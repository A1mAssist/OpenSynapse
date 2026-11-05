using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioPlaybackClockTests
{
    [Fact]
    public void SwitchingFromAFinishedRippleToSolidRestartsPlayback()
    {
        var clock = new StudioPlaybackClock();
        var ripple = StudioEffectCatalog.CreateDefault(StudioEffectKind.Ripple);
        clock.Trigger(0);
        Assert.True(clock.Sample(0.1, ripple, 5, 16).Running);
        Assert.False(clock.Sample(10, ripple, 5, 16).Running);

        var solid = StudioEffectCatalog.CreateDefault(StudioEffectKind.Solid);
        Assert.True(clock.Sample(10.1, solid, 5, 16).Running);
    }

    [Fact]
    public void SwitchingToRadiateWaitsForAKeyTrigger()
    {
        var clock = new StudioPlaybackClock();
        var ripple = StudioEffectCatalog.CreateDefault(StudioEffectKind.Ripple);
        clock.Trigger(0);
        Assert.False(clock.Sample(10, ripple, 5, 16).Running);

        var radiate = StudioEffectCatalog.CreateDefault(StudioEffectKind.Radiate);
        Assert.False(clock.Sample(10.1, radiate, 5, 16).Running);

        clock.Trigger(10.1);
        Assert.True(clock.Sample(10.2, radiate, 5, 16).Running);
    }
}
