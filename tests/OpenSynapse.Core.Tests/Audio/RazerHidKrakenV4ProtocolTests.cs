using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidKrakenV4ProtocolTests
{
    [Fact]
    public void StaticColorCarriesTheColorInTheEffectCommand()
    {
        var report = Assert.Single(RazerHidKrakenV4Protocol.CreateStatic(
            new RazerHidColor(0x11, 0x22, 0x33)));
        Assert.Equal(RazerHidKrakenV4Protocol.ReportLength, report.Length);
        Assert.Equal(0x02, report[0]);
        Assert.Equal(0x60, report[2]);
        Assert.Equal(0x08, report[6]);
        Assert.Equal(0x00, report[7]);
        Assert.Equal(0x00, report[8]);
        Assert.Equal(0x00, report[9]);
        Assert.Equal([0xC0, 0x00, 0x04, 0x01, 0x11, 0x22, 0x33], report[10..17]);
    }

    [Fact]
    public void GeneralCommandsCarryTheChecksum()
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
        var report = Assert.Single(RazerHidKrakenV4Protocol.CreateStatic(
            new RazerHidColor(200, 100, 50), 128));
        Assert.Equal(100, report[14]);
        Assert.Equal(50, report[15]);
        Assert.Equal(25, report[16]);
    }

    [Fact]
    public void TheCommandsMatchTheRazerBundleByteForByte()
    {
        // Reference commands built from the "Billie" headset bundle. Static
        // carries the colour in C0 00 04; off and wave only carry the effect id.
        Assert.Equal(
            "02006000000008000000C0000401112233000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000AF00",
            Convert.ToHexString(Assert.Single(RazerHidKrakenV4Protocol.CreateStatic(
                new RazerHidColor(0x11, 0x22, 0x33)))));

        Assert.Equal(
            "02006000000004000000020000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000006400",
            Convert.ToHexString(RazerHidKrakenV4Protocol.CreateConnect()));
        Assert.Equal(
            "02006000000005000000C0000100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A600",
            Convert.ToHexString(Assert.Single(RazerHidKrakenV4Protocol.CreateOff())));
        Assert.Equal(
            "02006000000005000000E80001010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000008F00",
            Convert.ToHexString(RazerHidKrakenV4Protocol.CreateForceChroma(enabled: true)));
        Assert.Equal(
            "02006000000005000000C10001FF0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000005800",
            Convert.ToHexString(RazerHidKrakenV4Protocol.CreateBrightness(100)));
        Assert.Equal(
            "02006000000005000000C0000104000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A200",
            Convert.ToHexString(Assert.Single(RazerHidKrakenV4Protocol.CreateWave())));
    }

    [Fact]
    public void TheStaticEffectExpectsAReply()
    {
        var reports = RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(1, 2, 3));
        Assert.True(RazerHidKrakenV4Protocol.ExpectsResponse(reports[0]));
    }

    [Fact]
    public void AGeneralReplyEchoesTheRequestCommand()
    {
        var request = RazerHidKrakenV4Protocol.CreateStatic(new RazerHidColor(1, 2, 3))[0];
        var reply = new byte[RazerHidKrakenV4Protocol.ReportLength];
        reply[0] = 0x02;
        reply[2] = 0x60;
        reply[10] = 0xC0;
        reply[11] = 0x01;
        Assert.True(RazerHidKrakenV4Protocol.IsCommandResponse(request, reply));

        reply[11] = 0x00;
        Assert.False(RazerHidKrakenV4Protocol.IsCommandResponse(request, reply));
    }

    [Fact]
    public void DongleCommandsUseTheWirelessRoutingFlag()
    {
        var report = Assert.Single(RazerHidKrakenV4Protocol.CreateStatic(
            new RazerHidColor(0x11, 0x22, 0x33), isDongle: true));
        Assert.Equal(0x80, report[9]);
        Assert.Equal([0xC0, 0x00, 0x04, 0x01, 0x11, 0x22, 0x33], report[10..17]);

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
