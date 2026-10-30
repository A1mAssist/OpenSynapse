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
    public void TheBlade14_2024PublishesBalancedSilentAndCustomRanges()
    {
        // 02B6 bundle: Balanced 2000..5400, Silent 2200..3800, Custom 3300..5400.
        const ushort productId = 0x02B6;
        Assert.Equal(
            new BladeFanTargetRange(2000, 5400, 3800),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Balanced));
        Assert.Equal(
            new BladeFanTargetRange(2200, 3800, 3000),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Silent));
        Assert.Equal(
            new BladeFanTargetRange(3300, 5400, 4300),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Custom));
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

    [Theory]
    [InlineData(0x0233, "Balanced,Performance")]
    [InlineData(0x0234, "Balanced,Custom")]
    [InlineData(0x0239, "Balanced,Performance,BatterySaver")]
    [InlineData(0x023A, "Balanced,Performance,Gaming")]
    [InlineData(0x023B, "Balanced,Gaming")]
    [InlineData(0x0240, "Balanced,Performance")]
    [InlineData(0x0245, "Balanced,Custom")]
    [InlineData(0x0246, "Balanced,Performance,Gaming")]
    [InlineData(0x024A, "Balanced,BatterySaver,Gaming")]
    [InlineData(0x024B, "Balanced,Custom")]
    [InlineData(0x024C, "Balanced,Custom")]
    [InlineData(0x024D, "Balanced,Custom")]
    [InlineData(0x0252, "Balanced,BatterySaver,Gaming")]
    [InlineData(0x0253, "Balanced,Custom")]
    [InlineData(0x0256, "Balanced,Custom")]
    [InlineData(0x0259, "Balanced,BatterySaver,Gaming")]
    [InlineData(0x0268, "Balanced,Performance,Gaming")]
    [InlineData(0x026D, "Balanced,Custom")]
    [InlineData(0x026E, "Balanced,Custom")]
    [InlineData(0x026F, "Balanced,Performance,Gaming")]
    [InlineData(0x0270, "Balanced,Custom")]
    [InlineData(0x0276, "Balanced,Custom")]
    [InlineData(0x0279, "Balanced,Custom")]
    [InlineData(0x027A, "Balanced,Performance,Gaming")]
    [InlineData(0x028A, "Balanced,Silent,Custom")]
    [InlineData(0x028B, "Balanced,Silent,Custom")]
    [InlineData(0x029D, "Balanced,Silent,Custom,Hyperboost")]
    [InlineData(0x029E, "Balanced,Silent,Custom")]
    [InlineData(0x02B6, "Balanced,Silent,Gaming,Custom")]
    [InlineData(0x02B8, "Balanced,Silent,Gaming,Custom")]
    [InlineData(0x02E0, "Balanced,Silent,Performance,Custom,Hyperboost,BatterySaver")]
    [InlineData(0x028C, "Balanced,Silent,Custom,Performance")]
    [InlineData(0x029F, "Balanced,Silent,Custom,Hyperboost")]
    [InlineData(0x02A0, "Balanced,Silent,Custom")]
    [InlineData(0x02B7, "Balanced,Silent,Gaming,Custom,Hyperboost")]
    [InlineData(0x02C5, "Balanced,Silent,Performance,Custom,BatterySaver")]
    [InlineData(0x02C6, "Balanced,Silent,Performance,Custom,Hyperboost,BatterySaver")]
    [InlineData(0x02C7, "Balanced,Silent,Performance,Gaming,Custom,BatterySaver")]
    [InlineData(0x02E1, "Balanced,Silent,Performance,Gaming,Custom,BatterySaver")]
    [InlineData(0x0255, "Balanced,Performance,Gaming")]
    public void EveryBundleModelPublishesItsModeList(ushort productId, string expected)
    {
        Assert.Equal(
            expected,
            string.Join(",", BladeModelCapabilities.PerformanceModesFor(productId)));
    }

    [Theory]
    [InlineData(0x0233, false)]
    [InlineData(0x0234, false)]
    [InlineData(0x0239, false)]
    [InlineData(0x023A, false)]
    [InlineData(0x023B, false)]
    [InlineData(0x0240, false)]
    [InlineData(0x0245, false)]
    [InlineData(0x0246, false)]
    [InlineData(0x024A, false)]
    [InlineData(0x024B, false)]
    [InlineData(0x024C, false)]
    [InlineData(0x024D, false)]
    [InlineData(0x0252, false)]
    [InlineData(0x0253, false)]
    [InlineData(0x0255, false)]
    [InlineData(0x0256, false)]
    [InlineData(0x0259, false)]
    [InlineData(0x0268, false)]
    [InlineData(0x026D, false)]
    [InlineData(0x026E, false)]
    [InlineData(0x026F, false)]
    [InlineData(0x0270, false)]
    [InlineData(0x0276, false)]
    [InlineData(0x0279, false)]
    [InlineData(0x027A, false)]
    [InlineData(0x028A, false)]
    [InlineData(0x028B, false)]
    [InlineData(0x028C, false)]
    [InlineData(0x029D, true)]
    [InlineData(0x029E, true)]
    [InlineData(0x029F, true)]
    [InlineData(0x02A0, true)]
    [InlineData(0x02B6, true)]
    [InlineData(0x02B7, false)]
    [InlineData(0x02B8, true)]
    [InlineData(0x02C5, false)]
    [InlineData(0x02C6, false)]
    [InlineData(0x02C7, false)]
    [InlineData(0x02E0, false)]
    [InlineData(0x02E1, false)]
    public void EveryBundleModelPublishesItsMaxFanAdmission(ushort productId, bool expected)
    {
        Assert.Equal(expected, BladeModelCapabilities.SupportsMaxFan(productId));
    }

    [Theory]
    [InlineData(0x0233, BladePerformanceMode.Balanced, 3500, 5700, 4500)]
    [InlineData(0x0233, BladePerformanceMode.Performance, 4500, 5700, 5000)]
    [InlineData(0x023A, BladePerformanceMode.Gaming, 4000, 5300, 4600)]
    [InlineData(0x023B, BladePerformanceMode.Gaming, 4000, 5300, 4600)]
    [InlineData(0x0246, BladePerformanceMode.Gaming, 3900, 5500, 4500)]
    [InlineData(0x024A, BladePerformanceMode.Gaming, 2500, 5500, 4000)]
    [InlineData(0x024A, BladePerformanceMode.BatterySaver, 2500, 5500, 4000)]
    [InlineData(0x0252, BladePerformanceMode.BatterySaver, 2500, 5500, 4000)]
    [InlineData(0x028A, BladePerformanceMode.Balanced, 3400, 5200, 4100)]
    [InlineData(0x029E, BladePerformanceMode.Custom, 4000, 5300, 4600)]
    [InlineData(0x029D, BladePerformanceMode.Custom, 2000, 5400, 3800)]
    [InlineData(0x02B6, BladePerformanceMode.Gaming, 2000, 5400, 3800)]
    [InlineData(0x02B7, BladePerformanceMode.Gaming, 3300, 5400, 4300)]
    [InlineData(0x02B8, BladePerformanceMode.Balanced, 3700, 5300, 4500)]
    [InlineData(0x02B8, BladePerformanceMode.Gaming, 3300, 5400, 4300)]
    [InlineData(0x02C7, BladePerformanceMode.Gaming, 3300, 5400, 4300)]
    [InlineData(0x02E0, BladePerformanceMode.Hyperboost, 3700, 5300, 4500)]
    [InlineData(0x02E1, BladePerformanceMode.Gaming, 3300, 5400, 4300)]
    [InlineData(0x028C, BladePerformanceMode.Balanced, 3300, 5400, 4300)]
    [InlineData(0x028C, BladePerformanceMode.Silent, 3300, 5400, 4300)]
    [InlineData(0x028C, BladePerformanceMode.Custom, 3300, 5400, 4300)]
    [InlineData(0x02A0, BladePerformanceMode.Balanced, 2200, 3800, 3000)]
    [InlineData(0x02A0, BladePerformanceMode.Silent, 3700, 5300, 4500)]
    [InlineData(0x02A0, BladePerformanceMode.Custom, 1700, 4300, 3000)]
    [InlineData(0x0255, BladePerformanceMode.Balanced, 4200, 5500, 4900)]
    [InlineData(0x0255, BladePerformanceMode.Performance, 4900, 5500, 5200)]
    [InlineData(0x0255, BladePerformanceMode.Gaming, 4900, 5500, 5200)]
    public void EveryBladeDeviceTypePublishesItsModeRanges(
        ushort productId,
        BladePerformanceMode mode,
        int minimum,
        int maximum,
        int defaultRpm)
    {
        Assert.Equal(
            new BladeFanTargetRange(minimum, maximum, defaultRpm),
            BladeModelCapabilities.FanTargetRangeFor(productId, mode));
    }
}
