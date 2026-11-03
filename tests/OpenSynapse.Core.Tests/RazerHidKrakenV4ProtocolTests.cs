using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidKrakenV4ProtocolTests
{
    [Fact]
    public void StaticColorSetsTheEffectAndThenWritesTheFrame()
    {
        var reports = RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(0x11, 0x22, 0x33));
        Assert.Equal(2, reports.Count);

        var effect = reports[0];
        Assert.Equal(RazerHidKrakenV4Protocol.ReportLength, effect.Length);
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

        for (var led = 0; led < RazerHidKrakenV4Protocol.LedCount; led++)
        {
            Assert.Equal([0x11, 0x22, 0x33], frame[(15 + (led * 3))..(18 + (led * 3))]);
        }
    }

    [Fact]
    public void GeneralCommandsCarryTheChecksumAndCustomFramesLeaveItZero()
    {
        foreach (var report in new[]
                 {
                     RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(0x11, 0x22, 0x33))[0],
                     RazerHidKrakenV4Protocol.CreateOff()[0],
                     RazerHidKrakenV4Protocol.CreateWave()[0],
                 })
        {
            byte expected = 0;
            for (var index = 0; index < RazerHidKrakenV4Protocol.ReportLength - 2; index++)
            {
                expected ^= report[index];
            }

            Assert.Equal(expected, report[RazerHidKrakenV4Protocol.ReportLength - 2]);
        }

        Assert.All(
            RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(0x11, 0x22, 0x33))
                .Skip(1)
                .Concat(RazerHidKrakenV4Protocol.CreateOff().Skip(1)),
            report => Assert.Equal(0, report[RazerHidKrakenV4Protocol.ReportLength - 2]));
    }

    [Fact]
    public void OffTurnsTheEffectOffAndClearsTheFrame()
    {
        var reports = RazerHidKrakenV4Protocol.CreateOff();
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
        var report = Assert.Single(RazerHidKrakenV4Protocol.CreateWave());
        Assert.Equal(0x00, report[7]);
        Assert.Equal(0x00, report[8]);
        Assert.Equal([0xC0, 0x00, 0x01, 0x04], report[10..14]);
    }

    [Fact]
    public void ConnectUsesTheFirmwareVersionCommand()
    {
        var report = RazerHidKrakenV4Protocol.CreateConnect();
        Assert.Equal(0x04, report[6]);
        Assert.Equal([0x02, 0x00, 0x00], report[10..13]);

        byte expected = 0;
        for (var index = 0; index < RazerHidKrakenV4Protocol.ReportLength - 2; index++)
        {
            expected ^= report[index];
        }

        Assert.Equal(expected, report[RazerHidKrakenV4Protocol.ReportLength - 2]);
    }

    [Fact]
    public void IntensityScalesTheColor()
    {
        var dimmed = RazerHidKrakenV4Protocol.CreateStatic(
            new RazerHidColor(200, 100, 50), 128);
        var frame = dimmed[1];
        Assert.Equal(100, frame[15]);
        Assert.Equal(50, frame[16]);
        Assert.Equal(25, frame[17]);
    }

    [Fact]
    public void TheFramesMatchTheRazerBundleByteForByte()
    {
        // Reference frames built from the "Billie" headset bundle: Set Chroma Effect is
        // C0 00 <length> with an XOR checksum; C0 01 01 is the response header. Set
        // Chroma Frame is 0F:03 with a
        // [0,0,row-1,0,col-1] header and a zero checksum slot.
        var staticReports = RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(0x11, 0x22, 0x33));
        Assert.Equal(
            "02006000000005000000C0000101000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A700",
            Convert.ToHexString(staticReports[0]));
        Assert.Equal(
            "020060000000210F0300000000000811223311223311223311223311223311223311223311223311223300000000000000000000000000000000000000000000",
            Convert.ToHexString(staticReports[1]));

        Assert.Equal(
            "02006000000005000000C0000104000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A200",
            Convert.ToHexString(Assert.Single(RazerHidKrakenV4Protocol.CreateWave())));
    }

    [Fact]
    public void DongleFramesUseTheWirelessRoutingFlag()
    {
        var reports = RazerHidKrakenV4Protocol.CreateStatic(
            new RazerHidColor(0x11, 0x22, 0x33), isDongle: true);
        Assert.Equal(0x80, reports[0][9]);
        Assert.Equal(0x80, reports[1][9]);
        Assert.Equal([0xC0, 0x00, 0x01, 0x01], reports[0][10..14]);

        var connect = RazerHidKrakenV4Protocol.CreateConnect(isDongle: true);
        Assert.Equal(0x80, connect[9]);

        byte expected = 0;
        for (var index = 0; index < RazerHidKrakenV4Protocol.ReportLength - 2; index++)
        {
            expected ^= connect[index];
        }
        Assert.Equal(expected, connect[RazerHidKrakenV4Protocol.ReportLength - 2]);
    }
}
