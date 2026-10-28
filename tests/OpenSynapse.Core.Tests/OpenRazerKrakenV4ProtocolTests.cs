using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerKrakenV4ProtocolTests
{
    [Fact]
    public void StaticColorSetsTheEffectAndThenWritesTheFrame()
    {
        var reports = OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(0x11, 0x22, 0x33));
        Assert.Equal(2, reports.Count);

        var effect = reports[0];
        Assert.Equal(OpenRazerKrakenV4Protocol.ReportLength, effect.Length);
        Assert.Equal(0x02, effect[0]);
        Assert.Equal(0x60, effect[2]);
        Assert.Equal(0x05, effect[6]);
        Assert.Equal(0x00, effect[7]);
        Assert.Equal(0x00, effect[8]);
        Assert.Equal(0x00, effect[9]);
        Assert.Equal([0xC0, 0x00, 0x01, 0x01], effect[10..14]);

        var frame = reports[1];
        Assert.Equal(0x0F, frame[7]);
        Assert.Equal(0x03, frame[8]);
        Assert.Equal(0x00, frame[9]);
        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x08], frame[10..15]);

        for (var led = 0; led < OpenRazerKrakenV4Protocol.LedCount; led++)
        {
            Assert.Equal([0x11, 0x22, 0x33], frame[(15 + (led * 3))..(18 + (led * 3))]);
        }
    }

    [Fact]
    public void EveryReportCarriesTheChecksumTheFirmwareVerifies()
    {
        foreach (var report in OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(0x11, 0x22, 0x33))
                     .Concat(OpenRazerKrakenV4Protocol.CreateOff())
                     .Concat(OpenRazerKrakenV4Protocol.CreateWave()))
        {
            byte expected = 0;
            for (var index = 0; index < OpenRazerKrakenV4Protocol.ReportLength - 2; index++)
            {
                expected ^= report[index];
            }

            Assert.Equal(expected, report[OpenRazerKrakenV4Protocol.ReportLength - 2]);
        }
    }

    [Fact]
    public void OffTurnsTheEffectOffAndClearsTheFrame()
    {
        var reports = OpenRazerKrakenV4Protocol.CreateOff();
        Assert.Equal(2, reports.Count);

        var effect = reports[0];
        Assert.Equal([0xC0, 0x00, 0x01, 0x00], effect[10..14]);

        var frame = reports[1];
        Assert.Equal(0x0F, frame[7]);
        Assert.Equal(0x03, frame[8]);
        Assert.All(frame[15..42], value => Assert.Equal(0, value));
    }

    [Fact]
    public void WaveSetsTheWaveEffect()
    {
        var report = Assert.Single(OpenRazerKrakenV4Protocol.CreateWave());
        Assert.Equal(0x00, report[7]);
        Assert.Equal(0x00, report[8]);
        Assert.Equal([0xC0, 0x00, 0x01, 0x04], report[10..14]);
    }

    [Fact]
    public void IntensityScalesTheColor()
    {
        var dimmed = OpenRazerKrakenV4Protocol.CreateStatic(
            new OpenRazerColor(200, 100, 50), 128);
        var frame = dimmed[1];
        Assert.Equal(100, frame[15]);
        Assert.Equal(50, frame[16]);
        Assert.Equal(25, frame[17]);
    }

    [Fact]
    public void TheFramesMatchTheRazerBundleByteForByte()
    {
        // Reference frames built from the "Billie" headset bundle: Set Chroma Effect is
        // C0 00 01, Set Chroma Frame is 0F:03 with a [0,0,row-1,0,col-1] header, and the
        // checksum is the XOR of bytes 0..61.
        var staticReports = OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(0x11, 0x22, 0x33));
        Assert.Equal(
            "02006000000005000000C0000101000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A700",
            Convert.ToHexString(staticReports[0]));
        Assert.Equal(
            "020060000000210F0300000000000811223311223311223311223311223311223311223311223311223300000000000000000000000000000000000000004700",
            Convert.ToHexString(staticReports[1]));

        Assert.Equal(
            "02006000000005000000C0000104000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A200",
            Convert.ToHexString(Assert.Single(OpenRazerKrakenV4Protocol.CreateWave())));
    }
}
