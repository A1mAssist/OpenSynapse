using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidKrakenV4ProtocolTests
{
    [Fact]
    public void StaticColorSetsTheEffectThenWritesADirectFrame()
    {
        var reports = RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(0x11, 0x22, 0x33));
        Assert.Equal(2, reports.Count);
        Assert.Equal([0xC0, 0x00, 0x01, 0x01], reports[0][10..14]);

        var frame = reports[1];
        Assert.Equal(RazerHidKrakenV4Protocol.ReportLength, frame.Length);
        Assert.Equal(0x02, frame[0]);
        Assert.Equal(0x60, frame[2]);
        Assert.Equal(0x21, frame[6]);
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
        var staticReports = RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(0x11, 0x22, 0x33));
        foreach (var report in new[]
                 {
                     RazerHidKrakenV4Protocol.CreateOff()[0],
                     RazerHidKrakenV4Protocol.CreateWave()[0],
                     staticReports[0],
                 })
        {
            byte expected = 0;
            for (var index = 0; index < RazerHidKrakenV4Protocol.ReportLength - 2; index++)
            {
                expected ^= report[index];
            }

            Assert.Equal(expected, report[RazerHidKrakenV4Protocol.ReportLength - 2]);
        }

        Assert.Equal(0, staticReports[1][RazerHidKrakenV4Protocol.ReportLength - 2]);
    }

    [Fact]
    public void OffTurnsTheEffectOff()
    {
        var effect = Assert.Single(RazerHidKrakenV4Protocol.CreateOff());
        Assert.Equal([0xC0, 0x00, 0x01, 0x00], effect[10..14]);
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
        var frame = RazerHidKrakenV4Protocol.CreateStatic(
            new RazerHidColor(200, 100, 50), 128)[1];
        Assert.Equal(100, frame[15]);
        Assert.Equal(50, frame[16]);
        Assert.Equal(25, frame[17]);
    }

    [Fact]
    public void TheFramesMatchTheRazerBundleByteForByte()
    {
        // Reference frames built from the "Billie" headset bundle. The static
        // path first sends C0 00 01 01, then the 0F:03 frame with a
        // [0,0,row-1,0,col-1] header. The bundle counts the trailing checksum
        // slot in data_size, so the frame is 5 + 27 + 1 = 0x21 bytes.
        var staticReports = RazerHidKrakenV4Protocol.CreateStatic(
            new RazerHidColor(0x11, 0x22, 0x33));
        Assert.Equal(
            "02006000000005000000C0000101000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A700",
            Convert.ToHexString(staticReports[0]));
        Assert.Equal(
            "020060000000210F0300000000000811223311223311223311223311223311223311223311223311223300000000000000000000000000000000000000000000",
            Convert.ToHexString(staticReports[1]));

        Assert.Equal(
            "02006000000004000000020000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000006400",
            Convert.ToHexString(RazerHidKrakenV4Protocol.CreateConnect()));
        Assert.Equal(
            "02006000000005000000C0000100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A600",
            Convert.ToHexString(Assert.Single(RazerHidKrakenV4Protocol.CreateOff())));
        Assert.Equal(
            "02006000000005000000C0000104000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A200",
            Convert.ToHexString(Assert.Single(RazerHidKrakenV4Protocol.CreateWave())));
    }

    [Fact]
    public void TheStaticEffectExpectsAReplyAndTheFrameDoesNot()
    {
        var reports = RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(1, 2, 3));
        Assert.True(RazerHidKrakenV4Protocol.ExpectsResponse(reports[0]));
        Assert.False(RazerHidKrakenV4Protocol.ExpectsResponse(reports[1]));
    }

    [Fact]
    public void TheEffectReplyUsesTheC0ResponseHeader()
    {
        var reply = new byte[RazerHidKrakenV4Protocol.ReportLength];
        reply[0] = 0x02;
        reply[2] = 0x60;
        reply[10] = 0xC0;
        reply[11] = 0x01;
        Assert.True(RazerHidKrakenV4Protocol.IsEffectResponse(reply));

        reply[11] = 0x00;
        Assert.False(RazerHidKrakenV4Protocol.IsEffectResponse(reply));
    }

    [Fact]
    public void DongleFramesUseTheWirelessRoutingFlag()
    {
        var report = RazerHidKrakenV4Protocol.CreateStatic(
            new RazerHidColor(0x11, 0x22, 0x33), isDongle: true)[1];
        Assert.Equal(0x80, report[9]);
        Assert.Equal([0x00, 0x00, 0x00, 0x00, 0x08], report[10..15]);

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
