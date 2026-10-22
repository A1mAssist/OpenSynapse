using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class FirmwareTests
{
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
}
