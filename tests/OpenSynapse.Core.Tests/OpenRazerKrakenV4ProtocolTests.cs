using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerKrakenV4ProtocolTests
{
    [Fact]
    public void StaticColorSendsBrightnessThenTheDirectFrame()
    {
        var reports = OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(0x11, 0x22, 0x33));
        Assert.Equal(2, reports.Count);

        var brightness = reports[0];
        Assert.Equal(OpenRazerKrakenV4Protocol.ReportLength, brightness.Length);
        Assert.Equal(0x02, brightness[0]);
        Assert.Equal(0x60, brightness[2]);
        Assert.Equal(0x05, brightness[6]);
        Assert.Equal([0xC1, 0x00, 0x01, 0xFF], brightness[10..14]);
        Assert.Equal(Checksum(brightness), brightness[OpenRazerKrakenV4Protocol.ReportLength - 2]);

        var frame = reports[1];
        Assert.Equal(OpenRazerKrakenV4Protocol.ReportLength, frame.Length);
        Assert.Equal(0x02, frame[0]);
        Assert.Equal(0x60, frame[2]);
        Assert.Equal(0x20, frame[6]);
        Assert.Equal(0x0F, frame[7]);
        Assert.Equal(0x03, frame[8]);
        Assert.Equal(0x00, frame[9]);
        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x08], frame[10..15]);
        for (var index = 0; index < 9; index++)
        {
            Assert.Equal([0x11, 0x22, 0x33], frame[(15 + index * 3)..(18 + index * 3)]);
        }

        Assert.Equal(0x00, frame[OpenRazerKrakenV4Protocol.ReportLength - 2]);
    }

    [Fact]
    public void WaveSendsBrightnessThenTheWaveEffect()
    {
        var reports = OpenRazerKrakenV4Protocol.CreateWave();
        Assert.Equal(2, reports.Count);

        Assert.Equal([0xC1, 0x00, 0x01, 0xFF], reports[0][10..14]);
        Assert.Equal([0xC0, 0x00, 0x01, 0x04], reports[1][10..14]);
        Assert.Equal(Checksum(reports[1]), reports[1][OpenRazerKrakenV4Protocol.ReportLength - 2]);
    }

    [Fact]
    public void OffSendsTheOffEffect()
    {
        var report = Assert.Single(OpenRazerKrakenV4Protocol.CreateOff());
        Assert.Equal([0xC0, 0x00, 0x01, 0x00], report[10..14]);
        Assert.Equal(Checksum(report), report[OpenRazerKrakenV4Protocol.ReportLength - 2]);
    }

    [Fact]
    public void GeneralCommandsCarryTheChecksum()
    {
        foreach (var report in new[]
                 {
                     OpenRazerKrakenV4Protocol.CreateOff()[0],
                     OpenRazerKrakenV4Protocol.CreateWave()[0],
                     OpenRazerKrakenV4Protocol.CreateWave()[1],
                 })
        {
            Assert.Equal(Checksum(report), report[OpenRazerKrakenV4Protocol.ReportLength - 2]);
        }
    }

    [Fact]
    public void IntensityScalesTheDirectFrame()
    {
        var reports = OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(200, 100, 50), 128);
        var frame = reports[1];
        for (var index = 0; index < 9; index++)
        {
            Assert.Equal([100, 50, 25], frame[(15 + index * 3)..(18 + index * 3)]);
        }
    }

    [Fact]
    public void TheStaticFrameMatchesOpenRgbByteForByte()
    {
        // RazerKrakenV4Controller::SetDirect: 0F 03, data size 32, then
        // 0,0,0,0,8 and nine RGB triples. Custom frames carry no checksum.
        Assert.Equal(
            "020060000000200F0300000000000811223311223311223311223311223311223311223311223311223300000000000000000000000000000000000000000000",
            Convert.ToHexString(OpenRazerKrakenV4Protocol.CreateStatic(
                new OpenRazerColor(0x11, 0x22, 0x33))[1]));
    }

    [Fact]
    public void DongleFramesUseTheWirelessRoutingFlag()
    {
        var reports = OpenRazerKrakenV4Protocol.CreateStatic(
            new OpenRazerColor(0x11, 0x22, 0x33), isDongle: true);

        Assert.Equal(0x80, reports[0][9]);
        Assert.Equal(0x80, reports[1][9]);
        Assert.Equal(0xC1, reports[0][10]);
        Assert.Equal(0x0F, reports[1][7]);
        Assert.Equal(0x03, reports[1][8]);
    }

    private static byte Checksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0;
        for (var index = 0; index < OpenRazerKrakenV4Protocol.ReportLength - 2; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }
}
