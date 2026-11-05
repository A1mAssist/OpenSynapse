using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// The Blade 15 (Mid 2019) probe manifest declares the blade-671 family, so every lookup that
/// compared against blade-710 alone silently skipped it. These tests pin the two failures the
/// owner reported against build r7: fixed fan writes finding no device, and the device page
/// readouts never refreshing.
/// </summary>
public sealed class DeviceProtocolFamiliesTests
{
    [Theory]
    [InlineData(DeviceProtocolFamilies.Blade)]
    [InlineData(DeviceProtocolFamilies.BladeChroma)]
    [InlineData(DeviceProtocolFamilies.Blade2023)]
    [InlineData(DeviceProtocolFamilies.Blade2020)]
    public void IsBladeAdmitsEveryBladeFamily(string protocolFamily) =>
        Assert.True(DeviceProtocolFamilies.IsBlade(protocolFamily));

    [Theory]
    [InlineData(DeviceProtocolFamilies.Blade)]
    [InlineData(DeviceProtocolFamilies.BladeChroma)]
    public void UsesBladeMatrixAdmitsOnlyThe710Families(string protocolFamily) =>
        Assert.True(DeviceProtocolFamilies.UsesBladeMatrix(protocolFamily));

    [Theory]
    [InlineData(DeviceProtocolFamilies.Blade2023)]
    [InlineData(DeviceProtocolFamilies.Blade2020)]
    [InlineData(DeviceProtocolFamilies.Viper)]
    [InlineData("openrazer-standard")]
    [InlineData("")]
    public void UsesBladeMatrixRejectsFamiliesWithDifferentFrameSizes(string protocolFamily) =>
        Assert.False(DeviceProtocolFamilies.UsesBladeMatrix(protocolFamily));

    [Theory]
    [InlineData(DeviceProtocolFamilies.Viper)]
    [InlineData("openrazer-standard")]
    [InlineData("")]
    public void IsBladeRejectsNonBladeFamilies(string protocolFamily) =>
        Assert.False(DeviceProtocolFamilies.IsBlade(protocolFamily));

    [Fact]
    public void TheBlade15ProbeFamilyCountsAsBladeSoFanWritesCanFindTheDevice() =>
        Assert.Equal(
            DeviceProtocolFamilies.Blade2023,
            RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x0245)!.ProtocolFamily);

    [Fact]
    public void StudioBladeProfileUsesTheSparse710CanvasForBlade710()
    {
        var profile = StudioBladeDevices.Resolve(Blade(0x02C6, DeviceProtocolFamilies.Blade));

        Assert.NotNull(profile);
        Assert.Equal(StudioBladeFrameMode.Sparse710, profile.Value.Mode);
        Assert.Equal((7, 16), (profile.Value.Rows, profile.Value.Columns));
    }

    [Fact]
    public void StudioBladeProfileRendersTheManifestMatrixForLedMatrixBlade()
    {
        var profile = StudioBladeDevices.Resolve(Blade(0x023A, DeviceProtocolFamilies.Blade2023));

        Assert.NotNull(profile);
        Assert.Equal(StudioBladeFrameMode.Direct, profile.Value.Mode);
        Assert.Equal((6, 16), (profile.Value.Rows, profile.Value.Columns));
    }

    [Fact]
    public void StudioBladeProfileRendersTheManifestMatrixForLedMatrix2026()
    {
        var profile = StudioBladeDevices.Resolve(Blade(0x02E0, DeviceProtocolFamilies.Blade2023));

        Assert.NotNull(profile);
        Assert.Equal(StudioBladeFrameMode.Direct, profile.Value.Mode);
        Assert.Equal((6, 17), (profile.Value.Rows, profile.Value.Columns));
    }

    [Theory]
    [InlineData(0x029F, DeviceProtocolFamilies.Blade2023)] // FirmwareEffect
    [InlineData(0x0255, DeviceProtocolFamilies.Blade2020)] // Unknown
    public void StudioBladeProfileExcludesDevicesWithoutASoftwareMatrix(
        ushort productId,
        string protocolFamily) =>
        Assert.Null(StudioBladeDevices.Resolve(Blade(productId, protocolFamily)));

    private static DeviceDescriptor Blade(ushort productId, string protocolFamily) =>
        new(
            $"blade-{productId:X4}-test",
            "test blade",
            0x1532,
            productId,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            0x0001,
            0x0002,
            protocolFamily,
            DeviceCategory.Laptop);
}
