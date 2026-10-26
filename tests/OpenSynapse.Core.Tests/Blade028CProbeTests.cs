using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class Blade028CProbeTests
{
    [Fact]
    public void TheBlade14_2022ManifestLoadsAndCanReadEveryBladeControl()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x028C);

        Assert.NotNull(manifest);
        Assert.Equal("blade-671", manifest!.ProtocolFamily);
        Assert.Equal("Razer Blade 14 (2022)", manifest.DisplayName);
        Assert.DoesNotContain("probe", manifest.Id, StringComparison.OrdinalIgnoreCase);

        // Every report the Blade channel uses must be reachable on this model.
        foreach (var capability in new[]
                 {
                     "thermal-state.get",
                     "fan-target.get",
                     "current-fan-rpm.get",
                     "boost.get",
                     "charge-limit.get",
                     "power-mode.get",
                     "keyboard-brightness.get",
                     "keyboard-brightness.set",
                     "keyboard-effect.get",
                     "keyboard-effect.set",
                 })
        {
            Assert.Contains(capability, manifest.Capabilities.Keys);
        }
    }

    [Fact]
    public void TheOwnerReadingsAdmitTheBaselineAndFixedFanContracts()
    {
        // 028C answered every Blade read on the owner's hardware (thermal state, both fan
        // tachometers, charge limit, power mode, boost), which admits the baseline CPU levels.
        // A fixed-speed write was confirmed by both tachometers, so it is admitted.
        // Maximum fan and the fan curve were never exercised, so they stay closed.
        const ushort productId = 0x028C;
        Assert.Equal(
            [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High, BladeCpuBoostMode.Boost],
            BladeModelCapabilities.CpuBoostModesFor(productId));
        Assert.True(BladeModelCapabilities.AllowsCpuBoost(productId, BladeCpuBoostMode.Boost));
        Assert.True(BladeModelCapabilities.SupportsFixedFan(productId));
        Assert.True(BladeModelCapabilities.FixedFanRequiresCustomMode(productId));
        Assert.Equal(3500, BladeModelCapabilities.MinimumFanTargetRpmFor(productId));
        Assert.Equal(5000, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
        Assert.False(BladeModelCapabilities.SupportsMaxFan(productId));
        Assert.False(BladeModelCapabilities.SupportsFanCurve(productId));
    }

    [Fact]
    public void TheBlade14_2022ExposesTheOfficialModesPlusTheSupportedExtraMode()
    {
        const ushort productId = 0x028C;
        Assert.Equal(
            [
                BladePerformanceMode.Balanced,
                BladePerformanceMode.Silent,
                BladePerformanceMode.Custom,
                BladePerformanceMode.Performance,
            ],
            BladeModelCapabilities.PerformanceModesFor(productId));

        // Other Blade models keep the generic protocol list.
        Assert.Contains(BladePerformanceMode.Performance, BladeModelCapabilities.PerformanceModesFor(0x02C6));
    }
}