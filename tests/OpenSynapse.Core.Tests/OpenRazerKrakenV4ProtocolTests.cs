using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerKrakenV4ProtocolTests
{
    [Fact]
    public void StaticColorCarriesTheColorInSetChromaEffect()
    {
        var report = Assert.Single(OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(0x11, 0x22, 0x33)));
        Assert.Equal(OpenRazerKrakenV4Protocol.ReportLength, report.Length);
        Assert.Equal(0x02, report[0]);
        Assert.Equal(0x60, report[2]);
        Assert.Equal(0x08, report[6]);
        Assert.Equal(0x00, report[7]);
        Assert.Equal(0x00, report[8]);
        Assert.Equal(0x00, report[9]);
        Assert.Equal([0xC0, 0x00, 0x04, 0x01, 0x11, 0x22, 0x33], report[10..17]);
        Assert.Equal(Checksum(report), report[OpenRazerKrakenV4Protocol.ReportLength - 2]);
    }

    [Fact]
    public void GeneralCommandsCarryTheChecksum()
    {
        foreach (var report in new[]
                 {
                     Assert.Single(OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(0x11, 0x22, 0x33))),
                     Assert.Single(OpenRazerKrakenV4Protocol.CreateOff()),
                     Assert.Single(OpenRazerKrakenV4Protocol.CreateWave()),
                 })
        {
            Assert.Equal(Checksum(report), report[OpenRazerKrakenV4Protocol.ReportLength - 2]);
        }
    }

    [Fact]
    public void OffTurnsTheEffectOff()
    {
        var report = Assert.Single(OpenRazerKrakenV4Protocol.CreateOff());
        Assert.Equal([0xC0, 0x00, 0x01, 0x00], report[10..14]);
    }

    [Fact]
    public void WaveSetsTheWaveEffect()
    {
        var report = Assert.Single(OpenRazerKrakenV4Protocol.CreateWave());
        Assert.Equal([0xC0, 0x00, 0x01, 0x04], report[10..14]);
    }

    [Fact]
    public void ConnectUsesTheFirmwareVersionCommand()
    {
        var report = OpenRazerKrakenV4Protocol.CreateConnect();
        Assert.Equal(0x04, report[6]);
        Assert.Equal([0x02, 0x00, 0x00], report[10..13]);

        Assert.Equal(Checksum(report), report[OpenRazerKrakenV4Protocol.ReportLength - 2]);
    }

    [Fact]
    public void IntensityScalesTheColor()
    {
        var report = Assert.Single(OpenRazerKrakenV4Protocol.CreateStatic(
            new OpenRazerColor(200, 100, 50), 128));
        Assert.Equal([0xC0, 0x00, 0x04, 0x01, 100, 50, 25], report[10..17]);
    }

    [Fact]
    public void TheFramesMatchTheRazerBundleByteForByte()
    {
        // Reference frames built from the "Billie" headset bundle: Set Chroma Effect
        // is C0 00 <length>, with the effect id and, for static, the RGB colour.
        Assert.Equal(
            "02006000000008000000C0000401112233000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000AF00",
            Convert.ToHexString(Assert.Single(
                OpenRazerKrakenV4Protocol.CreateStatic(new OpenRazerColor(0x11, 0x22, 0x33)))));
        Assert.Equal(
            "02006000000005000000C0000100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A600",
            Convert.ToHexString(Assert.Single(OpenRazerKrakenV4Protocol.CreateOff())));
        Assert.Equal(
            "02006000000005000000C0000104000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000A200",
            Convert.ToHexString(Assert.Single(OpenRazerKrakenV4Protocol.CreateWave())));
        Assert.Equal(
            "02006000000004000000020000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000006400",
            Convert.ToHexString(OpenRazerKrakenV4Protocol.CreateConnect()));
    }

    [Fact]
    public void DongleFramesUseTheWirelessRoutingFlag()
    {
        var report = Assert.Single(OpenRazerKrakenV4Protocol.CreateStatic(
            new OpenRazerColor(0x11, 0x22, 0x33), isDongle: true));
        Assert.Equal(0x80, report[9]);
        Assert.Equal([0xC0, 0x00, 0x04, 0x01, 0x11, 0x22, 0x33], report[10..17]);

        var connect = OpenRazerKrakenV4Protocol.CreateConnect(isDongle: true);
        Assert.Equal(0x80, connect[9]);
        Assert.Equal(Checksum(connect), connect[OpenRazerKrakenV4Protocol.ReportLength - 2]);
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