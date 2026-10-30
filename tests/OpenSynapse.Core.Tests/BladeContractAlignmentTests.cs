using OpenSynapse.Core.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeContractAlignmentTests
{
    /// <summary>
    /// 0239's bundle publishes a 2200..5500 battery-saver range and lists
    /// BatterySaver as a real mode; both the mode and its range are exposed.
    /// </summary>
    [Fact]
    public void BladeStealth2019PublishesTheBatterySaverModeAndRange()
    {
        const ushort productId = 0x0239;

        Assert.Equal(
            new BladeFanTargetRange(2200, 5500, 3800),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.BatterySaver));
        Assert.Contains(
            BladePerformanceMode.BatterySaver,
            BladeModelCapabilities.PerformanceModesFor(productId));
        Assert.Equal(5500, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
        Assert.Equal(5500, BladeModelCapabilities.MaximumWritableFanTargetRpmFor(productId));
        Assert.Equal(2200, BladeModelCapabilities.MinimumFanTargetRpmFor(productId));
    }

    /// <summary>
    /// 02B8's Gaming/Turbo range is 3300..5400, below the former 3700 floor and
    /// above the former 5300 ceiling. The mode stays admitted as Gaming; Turbo is
    /// the same wire byte and is not exposed as a separate mode.
    /// </summary>
    [Fact]
    public void Blade182024UsesTheGamingRangeAsItsEffectiveFanBounds()
    {
        const ushort productId = 0x02B8;

        Assert.Equal(
            new BladeFanTargetRange(3300, 5400, 4300),
            BladeModelCapabilities.FanTargetRangeFor(productId, BladePerformanceMode.Gaming));
        Assert.Equal(3300, BladeModelCapabilities.MinimumFanTargetRpmFor(productId));
        Assert.Equal(5400, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
        Assert.Equal(5400, BladeModelCapabilities.MaximumWritableFanTargetRpmFor(productId));
        // Turbo is the same wire value as Gaming; the bundle exposes no separate mode.
        Assert.Contains(
            BladePerformanceMode.Gaming,
            BladeModelCapabilities.PerformanceModesFor(productId));
    }

    [Theory]
    [InlineData(0x0234)]
    [InlineData(0x0245)]
    [InlineData(0x024B)]
    [InlineData(0x024C)]
    [InlineData(0x024D)]
    [InlineData(0x0253)]
    [InlineData(0x0256)]
    [InlineData(0x026D)]
    [InlineData(0x026E)]
    [InlineData(0x0270)]
    [InlineData(0x0276)]
    [InlineData(0x0279)]
    [InlineData(0x028A)]
    [InlineData(0x028B)]
    [InlineData(0x029E)]
    [InlineData(0x02B7)]
    [InlineData(0x02B8)]
    [InlineData(0x02C5)]
    [InlineData(0x02C7)]
    [InlineData(0x02E0)]
    [InlineData(0x02E1)]
    public void ModelsWithBundleBoostReportsAdmitTheLowMediumHighBaseline(ushort productId)
    {
        Assert.Equal(
            [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High],
            BladeModelCapabilities.CpuBoostModesFor(productId));
        Assert.False(BladeModelCapabilities.AllowsCpuBoost(productId, BladeCpuBoostMode.Boost));
        Assert.False(BladeModelCapabilities.AllowsCpuBoost(productId, BladeCpuBoostMode.Undervolt));
    }

    [Theory]
    [InlineData(0x029D)]
    [InlineData(0x02B6)]
    [InlineData(0x02B7)]
    [InlineData(0x02B8)]
    [InlineData(0x02C5)]
    [InlineData(0x02C6)]
    [InlineData(0x02C7)]
    [InlineData(0x029F)]
    [InlineData(0x02E0)]
    [InlineData(0x02E1)]
    public void FanCurveModelsReuseTheFixedTargetWritePath(ushort productId)
    {
        // The curve is user-defined temperature-to-RPM data; it writes the same
        // 0D:01 fan-target report as fixed speed, so the fixed-target admission is
        // the protocol preconditions, not an OEM curve table in the bundle.
        Assert.True(BladeModelCapabilities.SupportsFanCurve(productId));
        Assert.True(BladeModelCapabilities.SupportsFixedFan(productId));
    }
}
