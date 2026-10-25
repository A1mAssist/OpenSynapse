using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerKrakenV4ProtocolTests
{
    [Fact]
    public void StaticColorUsesTheWiredDirectReport()
    {
        var report = Assert.Single(OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(0x11, 0x22, 0x33)));

        Assert.Equal(OpenRazerKrakenV4Protocol.ReportLength, report.Length);
        Assert.Equal(0x02, report[0]);
        Assert.Equal(0x60, report[2]);
        Assert.Equal(32, report[6]);
        Assert.Equal(0x0F, report[7]);
        Assert.Equal(0x03, report[8]);
        Assert.Equal(0x00, report[9]);
        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x08], report[10..15]);

        for (var led = 0; led < OpenRazerKrakenV4Protocol.LedCount; led++)
        {
            Assert.Equal([0x11, 0x22, 0x33], report[(15 + (led * 3))..(18 + (led * 3))]);
        }
    }

    [Fact]
    public void WaveAndBrightnessUseTheEncapsulatedCommandBytes()
    {
        var reports = OpenRazerKrakenV4Protocol.CreateWave(0xCC);
        Assert.Equal(2, reports.Count);

        var wave = reports[0];
        Assert.Equal(5, wave[6]);
        Assert.Equal(0x00, wave[7]);
        Assert.Equal(0x00, wave[8]);
        Assert.Equal(0x00, wave[9]);
        Assert.Equal(0xC0, wave[10]);
        Assert.Equal(0x01, wave[12]);
        Assert.Equal(0x04, wave[13]);

        var brightness = reports[1];
        Assert.Equal(0xC1, brightness[10]);
        Assert.Equal(0xCC, brightness[13]);
    }

    [Fact]
    public void OffIsAnAllBlackDirectFrameAndIntensityScalesTheColor()
    {
        var off = Assert.Single(OpenRazerKrakenV4Protocol.CreateOff());
        Assert.All(off[15..42], value => Assert.Equal(0, value));

        var dimmed = Assert.Single(OpenRazerKrakenV4Protocol.CreateStatic(
            new OpenRazerColor(200, 100, 50), 128));
        Assert.Equal(100, dimmed[15]);
        Assert.Equal(50, dimmed[16]);
        Assert.Equal(25, dimmed[17]);
    }
}
