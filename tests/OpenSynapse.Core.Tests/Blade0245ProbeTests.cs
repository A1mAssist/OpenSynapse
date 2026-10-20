using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class Blade0245ProbeTests
{
    [Fact]
    public void TheBlade15_Mid2019ProbeLoadsAndCanReadEveryBladeControl()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x0245);

        Assert.NotNull(manifest);
        Assert.Equal("blade-671", manifest!.ProtocolFamily);

        // Upstream OpenRazer only implements lighting for this product id, so the fan and power
        // reads below are unverified on the owner's hardware. They must still be reachable, or the
        // probe cannot report the evidence this device needs.
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
        // stays blocked until the 0245 power-mode mask and fan behaviour are confirmed on the
        // owner's hardware. BladeModelCapabilities is the only gate that can unlock them.
        foreach (var productId in new ushort[] { 0x0245 })
        {
            Assert.False(BladeModelCapabilities.SupportsMaxFan(productId));
            Assert.False(BladeModelCapabilities.SupportsFanCurve(productId));
            Assert.False(BladeModelCapabilities.SupportsFixedFan(productId));
            Assert.Equal(5300, BladeModelCapabilities.MaximumFanTargetRpmFor(productId));
            Assert.Empty(BladeModelCapabilities.CpuBoostModesFor(productId));
            Assert.False(BladeModelCapabilities.AllowsCpuBoost(productId, BladeCpuBoostMode.Low));
            Assert.False(BladeModelCapabilities.AllowsCpuBoost(productId, BladeCpuBoostMode.Medium));
            Assert.False(BladeModelCapabilities.AllowsCpuBoost(productId, BladeCpuBoostMode.High));
        }
    }

    [Fact]
    public void TheBlade15AcceptsTheCaptured5300RpmStoredTarget()
    {
        var response = BladeFanProtocol.CreateGetTargetRequest(BladeFanProtocol.ZoneCpu);
        response[1] = 0x02;
        response[RazerFeatureReport.ArgumentsOffset + 2] = 0x35;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal(
            5300,
            BladeFanProtocol.ParseTarget(
                response,
                BladeFanProtocol.ZoneCpu,
                BladeModelCapabilities.MaximumFanTargetRpmFor(0x0245)));
        Assert.Throws<InvalidOperationException>(() =>
            BladeFanProtocol.ParseTarget(response, BladeFanProtocol.ZoneCpu));
    }
}