using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerDriverCatalogTests
{
    [Fact]
    public void ResolvesWiredProductAndOfficialPackage()
    {
        Assert.True(RazerDriverPackageCatalog.TryResolve(0x0098, out var package));

        Assert.Equal("0098", package.ProductId);
        Assert.Equal("Razer DeathAdder Essential", package.Name);
        Assert.Contains("PID0098Drv", package.Url, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://app-assets.razer.com/", package.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void WirelessDongleResolvesToBaseProductPackage()
    {
        Assert.True(RazerDriverPackageCatalog.TryResolve(0x00B7, out var package));

        Assert.Equal("00B6", package.ProductId);
        Assert.Contains("PID00b6Drv", package.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Razer DeathAdder V3 Pro", package.Name);
    }

    [Theory]
    [InlineData(0x0061, "PID0061Drv")]
    [InlineData(0x028E, "PID028eDrv")]
    [InlineData(0x00B8, "PID00b8Drv")]
    [InlineData(0x00E2, "PID00e2Drv")]
    [InlineData(0x00E5, "PID00e5Drv")]
    [InlineData(0x00EF, "PID00efDrv")]
    [InlineData(0x02B3, "PID02b3Drv")]
    [InlineData(0x0271, "PID0271Drv")]
    [InlineData(0x02CD, "PID02cdDrv")]
    [InlineData(0x0A74, "PID0a75Drv")]
    [InlineData(0x0A7C, "PID0a7dDrv")]
    [InlineData(0x0F1B, "PID0f1bDrv")]
    [InlineData(0x0F59, "PID0f59Drv")]
    [InlineData(0x0F5A, "PID0f59Drv")]
    [InlineData(0xB005, "PIDb005Drv")]
    public void ResolvesFilterDriverPackagesBeyondTheInitialCatalog(
        int productId,
        string expectedFile)
    {
        Assert.True(RazerDriverPackageCatalog.TryResolve((ushort)productId, out var package));

        Assert.Contains(expectedFile, package.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductsSharingAnInventoryEntryKeepTheirOwnPackages()
    {
        Assert.True(RazerDriverPackageCatalog.TryResolve(0x00E5, out var viper));
        Assert.Equal("Razer Viper V4 Pro", viper.Name);
        Assert.Contains("PID00e5Drv", viper.Url, StringComparison.OrdinalIgnoreCase);

        Assert.True(RazerDriverPackageCatalog.TryResolve(0x00EF, out var deathAdder));
        Assert.Equal("Razer DeathAdder V4 Pro Carbon Fiber Edition", deathAdder.Name);
        Assert.Contains("PID00efDrv", deathAdder.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0x00C8, "00C7")]
    [InlineData(0x00D1, "00D0")]
    [InlineData(0x00DB, "00DA")]
    [InlineData(0x00DF, "00DE")]
    [InlineData(0x00E3, "00E2")]
    [InlineData(0x00E6, "00E5")]
    [InlineData(0x00E8, "00E7")]
    [InlineData(0x00EA, "00EB")]
    [InlineData(0x00F0, "00EF")]
    [InlineData(0x02DA, "02CD")]
    [InlineData(0xB00B, "B00A")]
    public void WirelessSiblingResolvesToWiredPackage(
        int productId,
        string expectedProductId)
    {
        Assert.True(RazerDriverPackageCatalog.TryResolve((ushort)productId, out var package));

        Assert.Equal(expectedProductId, package.ProductId);
    }

    [Fact]
    public void UnknownProductHasNoPackage()
    {
        Assert.False(RazerDriverPackageCatalog.TryResolve(0xFFFF, out _));
    }

    [Fact]
    public void InstallerRejectsNonRazerPublisher()
    {
        var path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var package = new RazerDriverPackage(
            "TEST",
            "Not a Razer package",
            "0",
            0,
            "https://app-assets.razer.com/test.exe");

        var exception = Assert.Throws<InvalidDataException>(
            () => RazerDriverInstaller.VerifyPackage(path, package));
        Assert.Contains("publisher", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
