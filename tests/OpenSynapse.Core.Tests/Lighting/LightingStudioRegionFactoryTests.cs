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

    [Fact]
    public void GroupsLedsByInstanceIntoOneRegion()
    {
        var parameters = new StudioEffectParams { Effect = StudioEffectKind.Wave, Speed = 15 };
        var region = StudioRegionFactory.FromLedsByInstance(
            parameters,
            [
                new StudioLedRef(0, 0, InstanceId: "kbd-a"),
                new StudioLedRef(0, 0, InstanceId: "kbd-b"),
                new StudioLedRef(0, 0, InstanceId: "kbd-a"),
                new StudioLedRef(1, 1, InstanceId: "kbd-b"),
            ],
            instanceId => instanceId == "kbd-a" ? (ushort)1 : (ushort)2);

        Assert.Equal(2, region.Devices.Count);
        var first = Assert.Single(region.Devices, device => device.InstanceId == "kbd-a");
        Assert.Equal((ushort)1, first.ProductId);
        Assert.Equal("kbd-a", Assert.Single(first.Leds).InstanceId);
        var second = Assert.Single(region.Devices, device => device.InstanceId == "kbd-b");
        Assert.Equal((ushort)2, second.ProductId);
        Assert.Equal(2, second.Leds.Count);
        Assert.Equal(StudioEffectKind.Wave, region.Params.Effect);
        Assert.Equal(15, region.Params.Speed);
        Assert.NotSame(parameters, region.Params);
    }

    [Fact]
    public void LegacySelectionFallsBackToCurrentDevice()
    {
        var region = StudioRegionFactory.FromLedsByInstance(
            new StudioEffectParams(),
            [new StudioLedRef(2, 3)],
            instanceId => StudioLedSelection.SameInstance(instanceId, "kbd-a") ? (ushort)736 : null,
            "kbd-a");

        var device = Assert.Single(region.Devices);
        Assert.Equal("kbd-a", device.InstanceId);
        Assert.Equal((ushort)736, device.ProductId);
        Assert.Equal("kbd-a", Assert.Single(device.Leds).InstanceId);
    }
}
