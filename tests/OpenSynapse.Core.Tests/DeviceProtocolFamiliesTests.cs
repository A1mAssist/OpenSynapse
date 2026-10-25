using OpenSynapse.App.ViewModels;
using OpenSynapse.Windows.Devices;
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
    [InlineData(DeviceProtocolFamilies.Blade2023)]
    [InlineData(DeviceProtocolFamilies.Blade2020)]
    public void IsBladeAdmitsEveryBladeFamily(string protocolFamily) =>
        Assert.True(DeviceProtocolFamilies.IsBlade(protocolFamily));

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
}
