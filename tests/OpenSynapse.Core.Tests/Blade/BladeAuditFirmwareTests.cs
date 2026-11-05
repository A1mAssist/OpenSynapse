using System.Text.Json.Nodes;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAuditFirmwareTests
{
    [Theory]
    [InlineData(null, null, "--")]
    [InlineData(1, null, "--")]
    [InlineData(null, 9, "--")]
    [InlineData(1, 9, "1.09")]
    [InlineData(0, 0, "0.00")]
    public void MissingEcRegistryValuesRemainUnknown(object? major, object? minor, string expected) =>
        Assert.Equal(expected, WindowsFirmwareVersionReader.FormatEc(major, minor));

    [Fact]
    public void Real02C6AdditionalComponentsPreventTheOriginalFalseNegative()
    {
        // Source: artifacts/razer-mw-full-cdn/bundles/02C6/manifest.json:31-82.
        // These built-in fields are copied from that original; tests are not the protocol source.
        var device = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6)!.Device!;
        var result = RazerFirmwareUpdateClient.TryCreateManifestResult(
            new("2.01", "1.08", "1.8.0.0", "--"), device)!;
        Assert.Equal("2.02", result.PublishedBios);
        Assert.Equal("1.09", result.PublishedEc);
        Assert.Equal(["BIOS", "EC"], result.NewerComponents);
        Assert.False(result.IsComplete);
        Assert.Contains("MCU", result.UncheckedComponents!);
        Assert.Contains("vBIOS", result.UncheckedComponents!);
    }

    [Theory]
    [InlineData("RTX 5080", "98.03.5C.40.15", true)]
    [InlineData("NVIDIA GeForce RTX 5090 Laptop GPU", "98.03.5C.40.15", false)]
    [InlineData("RTX 5070", "98.03.5C.40.10", false)]
    [InlineData(null, "98.03.5C.40.10", false)]
    public void VbiosComparisonUsesTheMatchingGpuOnly(string? model, string version, bool update)
    {
        var result = RazerFirmwareUpdateClient.TryCreateManifestResult(
            new("2.02", "1.09", "1.8.0.0", version, model),
            RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6)!.Device)!;
        Assert.Equal(update, result.NewerComponents.Contains("vBIOS"));
        Assert.Equal(model is null or "RTX 5070", result.UncheckedComponents!.Contains("vBIOS"));
        Assert.False(result.IsComplete); // Package and MCU are still not independently identified.
    }

    [Theory]
    [InlineData("98.03.5C.40.0F", "98.3.5c.40.10", true, 1)]
    [InlineData("98.03.5C.40.16", "98.3.5c.40.16", true, 0)]
    [InlineData("2.02", "2.2.0", false, 0)]
    public void VersionComparisonSupportsHexAndNumericZeroPadding(string installed, string published, bool hex, int expected) =>
        Assert.Equal(expected, RazerFirmwareUpdateClient.CompareVersions(installed, published, hex));

    [Theory]
    [InlineData("--", "2.02", false)]
    [InlineData("2.02", "bad", false)]
    [InlineData("98.03.5C", "98.03.5C.40.16", true)]
    public void MissingOrUnparseableVersionIsUnknownNotCurrent(string installed, string published, bool hex) =>
        Assert.Null(RazerFirmwareUpdateClient.CompareVersions(installed, published, hex));

    [Theory]
    [InlineData(0x02E0, "1.04", "1.03")]
    [InlineData(0x02C5, "1.06", "1.03")]
    [InlineData(0x02C7, "4.01", "1.06")]
    public void OtherLocalOriginalsRetainTheirOwnComponentVersions(ushort pid, string bios, string ec)
    {
        var device = RazerDeviceRegistry.BuiltIn.Find(0x1532, pid)!.Device!;
        var result = RazerFirmwareUpdateClient.TryCreateManifestResult(FirmwareVersions.Empty, device)!;
        Assert.Equal(bios, result.PublishedBios); Assert.Equal(ec, result.PublishedEc);
        Assert.False(result.IsComplete);
        Assert.Contains("BIOS", result.UncheckedComponents!);
        Assert.Contains("EC", result.UncheckedComponents!);
    }

    [Fact]
    public void SupportPageAlsoChecksVbiosAndReportsUnknownComponents()
    {
        const string page = "<tr><td>vBIOS Firmware Updater RTX 5080: v98.03.5C.40.16 RTX 5090: v98.03.5C.40.15</td></tr>";
        var result = RazerFirmwareUpdateClient.Parse(page,
            new("--", "--", "--", "98.03.5C.40.10", "RTX 5080"));
        Assert.Equal(["vBIOS"], result.NewerComponents);
        Assert.False(result.IsComplete);
        Assert.Equal(["BIOS", "EC", "MCU"], result.UncheckedComponents);
    }

    [Fact]
    public void MissingAdditionalDataCannotProveAllFirmwareCurrent()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoLayout.Root,
            "src/OpenSynapse.Windows/Devices/Manifests/blade-710.json")))!.AsObject();
        manifest["device"]!["firmware"]!["latest"]!.AsObject().Remove("additional");
        var device = RazerDeviceRegistry.LoadJson([manifest.ToJsonString()]).Find(0x1532, 0x02C6)!.Device;
        var result = RazerFirmwareUpdateClient.TryCreateManifestResult(new("2.02", "1.09", "1.8.0.0", "--"), device)!;
        Assert.False(result.HasUpdate); Assert.False(result.IsComplete);
        Assert.Equal(["Firmware package", "BIOS", "EC", "MCU", "vBIOS"], result.UncheckedComponents);
        Assert.Equal("https://rzr.to/k1kflh", result.DownloadUrl);
    }

    [Theory]
    [InlineData(0x02C5, "1.06", "1.03")]
    [InlineData(0x02C7, "4.01", "1.06")]
    public void ConfirmedComponentUpdateUsesItsOriginalUrlNotTheDifferentPackageUrl(ushort pid, string bios, string ec)
    {
        var result = RazerFirmwareUpdateClient.TryCreateManifestResult(new("1.00", "1.00", "--", "--"),
            RazerDeviceRegistry.BuiltIn.Find(0x1532, pid)!.Device)!;
        Assert.Equal(bios, result.PublishedBios); Assert.Equal(ec, result.PublishedEc);
        Assert.Equal(["BIOS", "EC"], result.NewerComponents);
        Assert.Equal("https://rzr.to/k1kflh", result.DownloadUrl);
    }

    [Fact]
    public void WorkspaceUsesPartialStatusRatherThanGlobalCurrentForUncheckedComponents()
    {
        var source = File.ReadAllText(Path.Combine(RepoLayout.Root,
            "src/OpenSynapse.App/ViewModels/FirmwareWorkspace.UpdateStatus.cs"));
        Assert.Contains("result.IsComplete", source);
        Assert.Contains("FirmwareUpdatePartial", source);
        Assert.Contains("FirmwareUpdateAvailablePartial", source);
        Assert.DoesNotContain("\"FirmwareUpdateCurrent\"", source);
    }
}
