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
