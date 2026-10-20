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
    public void TheProbeStaysReadOnlyBecauseNoModelContractAdmitsIt()
    {
        // The manifest declares the family contract so reads can be attempted, but every write
        // stays blocked until the 028C power-mode mask and fan behaviour are confirmed on the
        // owner's hardware. BladeModelCapabilities is the only gate that can unlock them.
        foreach (var productId in new ushort[] { 0x028C })
        {
            Assert.False(BladeModelCapabilities.SupportsMaxFan(productId));
            Assert.False(BladeModelCapabilities.SupportsFanCurve(productId));
            Assert.Empty(BladeModelCapabilities.CpuBoostModesFor(productId));
        }

    }
}