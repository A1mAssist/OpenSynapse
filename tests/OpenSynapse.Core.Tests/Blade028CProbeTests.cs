using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class Blade028CProbeTests
{
    [Fact]
    public void TheBlade14_2022ProbeLoadsAndCanReadEveryBladeControl()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x028C);

        Assert.NotNull(manifest);
        Assert.Equal("blade-671", manifest!.ProtocolFamily);

        // Every read the user asked about must be reachable so the probe can report evidence.
        foreach (var capability in new[]
                 {
                     "thermal-state.get",
                     "fan-target.get",
                     "current-fan-rpm.get",
                     "boost.get",
                     "charge-limit.get",
                     "power-mode.get",
                     "keyboard-brightness.get",
                     "keyboard-effect.get",
                 })
        {
            Assert.Contains(capability, manifest.Capabilities.Keys);
        }
    }

    [Fact]
    public void TheOwnerReadingsAdmitTheBaselineContractAndKeepTheUnprovenControlsClosed()
    {
        // 028C answered every Blade read on the owner's hardware (thermal state, both fan
        // tachometers, charge limit, power mode, boost), which admits the baseline CPU levels.
        // Maximum fan and the fan curve were never exercised, so they stay closed.
        const ushort productId = 0x028C;
        Assert.Equal(
            [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High],
            BladeModelCapabilities.CpuBoostModesFor(productId));
        Assert.True(BladeModelCapabilities.AllowsCpuBoost(productId, BladeCpuBoostMode.High));
        Assert.False(BladeModelCapabilities.SupportsMaxFan(productId));
        Assert.False(BladeModelCapabilities.SupportsFanCurve(productId));
    }
}