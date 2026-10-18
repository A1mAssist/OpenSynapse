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
}
