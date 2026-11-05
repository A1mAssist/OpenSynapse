using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class Blade695Tests
{
    [Fact]
    public void ManifestUsesTheBlade710FamilyAndAdmitsWrites()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02B7);

        Assert.NotNull(manifest);
        Assert.Equal("blade-710-chroma", manifest!.ProtocolFamily);
        Assert.Equal("Razer Blade 16 (2024)", manifest.DisplayName);
        Assert.Equal(DeviceCategory.Laptop, manifest.Category);
        Assert.Equal(["02B7"], manifest.ProductIds.Select(id => $"{id:X4}"));
        Assert.Contains("thermal-state.get", manifest.Capabilities.Keys);
        Assert.Contains("thermal-state.set", manifest.Capabilities.Keys);
        Assert.Contains("fan-target.get", manifest.Capabilities.Keys);
        Assert.Contains("keyboard-brightness.set", manifest.Capabilities.Keys);
        Assert.Contains("display-mux.set", manifest.Capabilities.Keys);
        Assert.Contains("native-display-mode.set", manifest.Capabilities.Keys);
        Assert.True(BladeModelCapabilities.AllowsControlWrites(0x02B7));
        Assert.True(BladeModelCapabilities.SupportsFanCurve(0x02B7));
        Assert.True(BladeModelCapabilities.SupportsFixedFan(0x02B7));
        Assert.False(BladeModelCapabilities.SupportsMaxFan(0x02B7));
        Assert.Equal(
            [BladeCpuBoostMode.Low, BladeCpuBoostMode.Medium, BladeCpuBoostMode.High],
            BladeModelCapabilities.CpuBoostModesFor(0x02B7));
        Assert.True(DeviceProtocolFamilies.IsBlade(manifest.ProtocolFamily));
        Assert.True(DeviceProtocolFamilies.UsesBladeMatrix(manifest.ProtocolFamily));
        Assert.Equal(6, manifest.MatrixRows);
        Assert.Equal(17, manifest.MatrixColumns);
    }
}
