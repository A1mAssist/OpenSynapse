using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioRegionFactoryTests
{
    [Fact]
    public void BuildsOneDeviceWithDeduplicatedLeds()
    {
        var parameters = new StudioEffectParams { Effect = StudioEffectKind.Wave, Speed = 15 };
        var region = StudioRegionFactory.FromLeds(parameters, 736, "abc",
        [
            new StudioLedRef(0, 0),
            new StudioLedRef(0, 0),
            new StudioLedRef(1, 2, InputType: "1", InputData: "W"),
        ]);

        var device = Assert.Single(region.Devices);
        Assert.Equal((ushort)736, device.ProductId);
        Assert.Equal("abc", device.InstanceId);
        Assert.Equal(2, device.Leds.Count);
        Assert.Equal(StudioEffectKind.Wave, region.Params.Effect);
        Assert.Equal(15, region.Params.Speed);
    }

    [Fact]
    public void ClonesParameters()
    {
        var parameters = new StudioEffectParams { Effect = StudioEffectKind.Fire, Primary = new StudioRgb(1, 2, 3) };
        var region = StudioRegionFactory.FromLeds(parameters, 736, null, [new StudioLedRef(0, 0)]);

        parameters.Primary = new StudioRgb(9, 9, 9);

        Assert.Equal(new StudioRgb(1, 2, 3), region.Params.Primary);
    }

    [Fact]
    public void EmptySelectionCreatesNoDevice()
    {
        var region = StudioRegionFactory.FromLeds(new StudioEffectParams(), 736, null, []);

        Assert.Empty(region.Devices);
    }
}
