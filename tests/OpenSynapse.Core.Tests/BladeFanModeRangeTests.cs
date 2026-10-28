using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeFanModeRangeTests
{
    [Fact]
    public void TheBlade16_2023PublishesASeparateRangePerMode()
    {
        // 029F bundle: Balanced/Silent/Hyperboost 3400..5200, Custom 4000..5300.
        const ushort productId = 0x029F;
        Assert.Equal(
            new BladeFanTargetRange(4000, 5300, 4600),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Custom));
        Assert.Equal(
            new BladeFanTargetRange(3400, 5200, 4100),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Balanced));
        Assert.Equal(
            new BladeFanTargetRange(3400, 5200, 4100),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Hyperboost));
        // The read-back ceiling must still cover the Custom maximum, or a stored 5300 target
        // would fail to parse while the device is in Custom mode.
        Assert.Equal(5300, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
    }

    [Fact]
    public void AnUnlistedModeFallsBackToTheModelRange()
    {
        // 02B6 publishes only its Silent range; Balanced keeps the model-wide fallback.
        const ushort productId = 0x02B6;
        Assert.Equal(
            new BladeFanTargetRange(2200, 3800, 3000),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Silent));
        var balanced = BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Balanced);
        Assert.Equal(2200, balanced.MinimumRpm);
        Assert.Equal(5400, balanced.MaximumRpm);
    }

    [Fact]
    public void TheModeListsMatchTheMiddleware()
    {
        // 029F: four modes, no Performance. 02C6: six, with BatterySaver.
        Assert.Equal(
            [
                BladePerformanceMode.Balanced,
                BladePerformanceMode.Silent,
                BladePerformanceMode.Custom,
                BladePerformanceMode.Hyperboost,
            ],
            BladeModelCapabilities.PerformanceModesFor(0x029F));
        Assert.Equal(
            [
                BladePerformanceMode.Balanced,
                BladePerformanceMode.Silent,
                BladePerformanceMode.Performance,
                BladePerformanceMode.Custom,
                BladePerformanceMode.Hyperboost,
                BladePerformanceMode.BatterySaver,
            ],
            BladeModelCapabilities.PerformanceModesFor(0x02C6));
        Assert.Contains(
            BladePerformanceMode.BatterySaver,
            BladeModelCapabilities.PerformanceModesFor(0x02C5));
        Assert.DoesNotContain(
            BladePerformanceMode.Performance,
            BladeModelCapabilities.PerformanceModesFor(0x029F));
    }

    [Fact]
    public void The02C6HyperboostCeilingMatchesTheBundle()
    {
        // 02C6 bundle: Hyperboost 3700..5300, Custom 4000..5300.
        const ushort productId = 0x02C6;
        Assert.Equal(
            new BladeFanTargetRange(3700, 5300, 4500),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Hyperboost));
        Assert.Equal(
            new BladeFanTargetRange(3300, 5400, 4300),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Performance));
    }
}
