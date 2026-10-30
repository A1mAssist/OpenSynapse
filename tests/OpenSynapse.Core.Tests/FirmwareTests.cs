using System.Text.Json.Nodes;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class FirmwareTests
{
    [Fact]
    public void SupportSearchUsesTheMostSpecificModelNameAndRemovesDuplicates()
    {
        Assert.Equal(
            ["RZ09-0301", "Razer Blade 15 (2019) Advanced", "Blade"],
            RazerFirmwareUpdateClient.BuildSearchQueries(
                " RZ09-0301 ",
                "Razer Blade 15 (2019) Advanced",
                " Blade "));
        Assert.Equal(
            ["Blade"],
            RazerFirmwareUpdateClient.BuildSearchQueries(null, "Blade", "Blade"));
    }

    [Fact]
    public void McuVersionQueryUsesTheBladeCommandAndDecodesFourBytes()
    {
        var request = BladeMcuVersionProtocol.CreateGetRequest();
        Assert.Equal(0x04, request[6]);
        Assert.Equal(0x00, request[7]);
        Assert.Equal(0x87, request[8]);

        var response = (byte[])request.Clone();
        response[1] = 0x02;
        response[9] = 1;
        response[10] = 2;
        response[11] = 3;
        response[12] = 4;
        response[89] = RazerFeatureReport.CalculateCrc(response);

        Assert.Equal("1.2.3.4", BladeMcuVersionProtocol.Parse(response, request));
    }

    [Fact]
    public void SupportPageParserReadsPublishedVersionsAndFindsNewerComponents()
    {
        const string html = """
            <table>
              <tr><td>MCU Firmware Updater v1.09 July 1, 2026</td></tr>
              <tr><td>BIOS Firmware Updater v2.02 June 30, 2026</td></tr>
              <tr><td>EC Firmware Updater v1.09 June 30, 2026</td></tr>
              <tr><td>vBIOS Firmware Updater RTX 5080: v98.03.5C.40.16 RTX 5090: v98.03.5C.40.15</td></tr>
            </table>
            """;

        var result = RazerFirmwareUpdateClient.Parse(
            html,
            new FirmwareVersions("2.01", "1.08", "1.09", "98.03.5C.40.10"));

        Assert.Equal("2.02", result.PublishedBios);
        Assert.Equal("1.09", result.PublishedEc);
        Assert.Equal("1.09", result.PublishedMcu);
        Assert.Contains("RTX 5080", result.PublishedVbios);
        Assert.Equal(["BIOS", "EC"], result.NewerComponents);
    }

    [Fact]
    public void SupportSearchFindsTheModelDriversPageAndUpdaterLink()
    {
        const string search = """
            <a href="/app/answers/detail/a_id/19866/kw/RZ09-0529">Razer Blade 18" (2025) | RZ09-0529 Drivers &amp; Downloads</a>
            """;
        Assert.Equal(
            "/app/answers/detail/a_id/19866/kw/RZ09-0529",
            RazerFirmwareUpdateClient.FindDriversPageHref(search));

        const string support = """
            <a href="/app/answers/detail/a_id/9668/kw/RZ09-0483">Razer Blade 16" (2023) | RZ09-0483x Support &amp; FAQs</a>
            """;
        Assert.Equal(
            "/app/answers/detail/a_id/9668/kw/RZ09-0483",
            RazerFirmwareUpdateClient.FindDriversPageHref(support));

        const string drivers = """
            <a href="//mysupport.razer.com/app/answers/detail/a_id/5350">How to complete the BIOS Firmware Updater installation successfully</a>
            <a href="//mysupport.razer.com/app/answers/detail/a_id/15235">Razer Blade 16 BIOS Updater</a>
            """;
        Assert.Equal(
            "//mysupport.razer.com/app/answers/detail/a_id/15235",
            RazerFirmwareUpdateClient.FindUpdaterAnswerHref(drivers, "BIOS"));

        const string answer = """
            <a href="https://rzr.to/2vxhvx">Download</a>
            <a href="http://rzr.to/supporte">Support</a>
            """;
        Assert.Equal("https://rzr.to/2vxhvx", RazerFirmwareUpdateClient.ExtractDownloadUrl(answer));
    }

    [Fact]
    public void ManifestDeviceBlockReadsMetadataAndDriverFirmware()
    {
        var manifest = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x023A);
        var device = Assert.IsType<RazerDeviceMetadata>(manifest!.Device);

        Assert.Equal("Razer Blade 15 Advanced Model", device.Name);
        Assert.Equal("Charlotte2_MW", device.ShortName);
        Assert.Equal([0, 129], device.EditionIds);
        Assert.Equal("rzDevice25LedMatrixBlade", device.RzDeviceType);
        Assert.Null(device.Firmware!.Latest);
        Assert.Equal("0.1.2.0", device.Firmware.Driver!.Version);
        Assert.Equal(
            "https://app-assets.razer.com/files/synapse/devices/570/1.0.0.1/PID023aDrv_v0.1.2.0.exe",
            device.Firmware.Driver.Url);
    }

    [Fact]
    public void Blade023AUsesTheManifestDriverInsteadOfTheSupportPage()
    {
        var device = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x023A)!.Device!;
        var result = RazerFirmwareUpdateClient.TryCreateManifestResult(FirmwareVersions.Empty, device);

        Assert.NotNull(result);
        Assert.True(result.HasUpdate);
        Assert.Equal("Razer Blade 15 Advanced Model", result.DeviceName);
        Assert.Equal(["Driver 0.1.2.0"], result.NewerComponents);
        Assert.Equal(
            "https://app-assets.razer.com/files/synapse/devices/570/1.0.0.1/PID023aDrv_v0.1.2.0.exe",
            result.DownloadUrl);
    }

    [Fact]
    public async Task Blade023ACheckUsesTheManifestWithoutSearchingTheSupportPage()
    {
        var client = new RazerFirmwareUpdateClient(RazerDeviceRegistry.BuiltIn);
        var device = new DeviceDescriptor(
            "023A",
            "Razer Blade 15 Advanced Model",
            0x1532,
            0x023A,
            DeviceAccessState.Available,
            DeviceCapabilityState.PendingValidation,
            91,
            1,
            2,
            "blade-671");

        var result = await client.CheckAsync(FirmwareVersions.Empty, [device]);

        Assert.Equal(["Driver 0.1.2.0"], result.NewerComponents);
        Assert.Equal(
            "https://app-assets.razer.com/files/synapse/devices/570/1.0.0.1/PID023aDrv_v0.1.2.0.exe",
            result.DownloadUrl);
    }

    [Fact]
    public void Blade02C6UsesTheManifestLatestFirmwareAndComparesVersions()
    {
        var device = RazerDeviceRegistry.BuiltIn.Find(0x1532, 0x02C6)!.Device!;
        var older = RazerFirmwareUpdateClient.TryCreateManifestResult(
            new FirmwareVersions("2.01", "1.08", "1.7.0.0", "--"),
            device);

        Assert.NotNull(older);
        Assert.Equal("Razer Blade 16", older.DeviceName);
        Assert.Equal("1.8.0.0", older.PublishedMcu);
        Assert.Equal(["Firmware 1.8.0.0"], older.NewerComponents);
        Assert.Equal("https://rzr.to/k1kflh", older.DownloadUrl);

        var current = RazerFirmwareUpdateClient.TryCreateManifestResult(
            new FirmwareVersions("2.01", "1.08", "1.8.0.0", "--"),
            device);
        Assert.NotNull(current);
        Assert.False(current.HasUpdate);
        Assert.Null(current.DownloadUrl);
    }

    [Fact]
    public void ManifestFirmwarePrefersLatestThenDriverThenTheSupportPage()
    {
        var latest = new RazerFirmwareRelease("9.9.9.9", "https://example.com/latest");
        var driver = new RazerFirmwareRelease("0.1.2.0", "https://example.com/driver");

        var latestResult = RazerFirmwareUpdateClient.TryCreateManifestResult(
            FirmwareVersions.Empty,
            DeviceWithFirmware(new RazerFirmwareInfo(latest, driver)))!;
        Assert.Null(latestResult.DeviceName);
        Assert.Equal("9.9.9.9", latestResult.PublishedMcu);
        Assert.Equal("https://example.com/latest", latestResult.DownloadUrl);

        var driverResult = RazerFirmwareUpdateClient.TryCreateManifestResult(
            FirmwareVersions.Empty,
            DeviceWithFirmware(new RazerFirmwareInfo(null, driver)))!;
        Assert.Equal(["Driver 0.1.2.0"], driverResult.NewerComponents);
        Assert.Equal("https://example.com/driver", driverResult.DownloadUrl);

        Assert.Null(RazerFirmwareUpdateClient.TryCreateManifestResult(
            FirmwareVersions.Empty,
            DeviceWithFirmware(new RazerFirmwareInfo(null, null))));
    }

    [Fact]
    public void ManifestDeviceNameOverridesTheWmiModelName()
    {
        Assert.Equal(
            "Blade",
            RazerFirmwareUpdateClient.ResolveModelName("Blade", "Razer Blade 15 Advanced Model"));
        Assert.Equal(
            "Razer Blade 15 Advanced Model",
            RazerFirmwareUpdateClient.ResolveModelName(null, "Razer Blade 15 Advanced Model"));
        Assert.Equal(
            ["Blade", "Razer Blade 15 Advanced Model"],
            RazerFirmwareUpdateClient.BuildSearchQueries(
                null,
                RazerFirmwareUpdateClient.ResolveModelName("Blade", "ignored"),
                "Razer Blade 15 Advanced Model"));
    }

    [Fact]
    public void ManifestWithoutDeviceBlockStillLoadsAsALegacyManifest()
    {
        var root = ReadManifest("blade-023a.json");
        root.Remove("device");

        var registry = RazerDeviceRegistry.LoadJson([root.ToJsonString()]);
        var manifest = registry.Find(0x1532, 0x023A)!;
        Assert.Null(manifest.Device);
        Assert.Null(RazerFirmwareUpdateClient.TryCreateManifestResult(
            FirmwareVersions.Empty,
            manifest.Device));
    }

    [Fact]
    public void NullDeviceBlockStillLoadsAsALegacyManifest()
    {
        var root = ReadManifest("blade-023a.json");
        root["device"] = null;

        var manifest = RazerDeviceRegistry.LoadJson([root.ToJsonString()]).Find(0x1532, 0x023A)!;
        Assert.Null(manifest.Device);
    }

    [Fact]
    public void ManifestDeviceBlockRejectsUnknownFields()
    {
        var root = ReadManifest("blade-023a.json");
        root["device"]!.AsObject()["unexpected"] = true;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RazerDeviceRegistry.LoadJson([root.ToJsonString()]));
        Assert.Contains("unexpected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RazerDeviceMetadata DeviceWithFirmware(RazerFirmwareInfo firmware) =>
        new(null, null, [], [], null, firmware);

    private static JsonObject ReadManifest(string fileName) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepoLayout.Root,
            "src",
            "OpenSynapse.Windows",
            "Devices",
            "Manifests",
            fileName)))!.AsObject();
}
