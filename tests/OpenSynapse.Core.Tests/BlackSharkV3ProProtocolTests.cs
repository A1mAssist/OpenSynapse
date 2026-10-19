using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BlackSharkV3ProProtocolTests
{
    [Fact]
    public void GetBattery_UsesWirelessEnvelope()
    {
        var report = BlackSharkV3ProProtocol.CreateGetBattery();

        Assert.Equal(BlackSharkV3ProProtocol.ReportLength, report.Length);
        Assert.Equal(BlackSharkV3ProProtocol.ReportId, report[0]);
        Assert.Equal(BlackSharkV3ProProtocol.TransactionId, report[2]);
        Assert.Equal(0x05, report[6]);
        Assert.Equal(0x80, report[9]);
        Assert.Equal(0x21, report[10]);
        Assert.Equal(0x00, report[11]);
        Assert.Equal(0x00, report[12]);
        Assert.Equal(0x00, report[13]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void GetBattery_WiredUsesZeroTransportByte()
    {
        var report = BlackSharkV3ProProtocol.CreateGetBattery(wireless: false);

        Assert.Equal(0x00, report[9]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void SetSidetone_EncodesLevel()
    {
        var report = BlackSharkV3ProProtocol.CreateSetSidetone(7);

        Assert.Equal(0x06, report[6]);
        Assert.Equal(0x80, report[9]);
        Assert.Equal(0x99, report[10]);
        Assert.Equal(0x01, report[12]);
        Assert.Equal(0x07, report[13]);
        Assert.Equal(0x00, report[14]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void SetAnc_EncodesModeAndLevel()
    {
        var report = BlackSharkV3ProProtocol.CreateSetAnc(BlackSharkAncMode.Anc, 3);

        Assert.Equal(0x06, report[6]);
        Assert.Equal(0x92, report[10]);
        Assert.Equal(0x02, report[12]);
        Assert.Equal(0x01, report[13]);
        Assert.Equal(0x03, report[14]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void SetAnc_OffForcesZeroLevel()
    {
        var report = BlackSharkV3ProProtocol.CreateSetAnc(BlackSharkAncMode.Off, 4);

        Assert.Equal(0x00, report[13]);
        Assert.Equal(0x00, report[14]);
    }

    [Fact]
    public void SetGameChat_EncodesBalance()
    {
        var report = BlackSharkV3ProProtocol.CreateSetGameChat(10);

        Assert.Equal(0x06, report[6]);
        Assert.Equal(0xDC, report[10]);
        Assert.Equal(0x01, report[12]);
        Assert.Equal(0x0A, report[13]);
        Assert.Equal(0x00, report[14]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Fact]
    public void SetThx_EncodesToggle()
    {
        var report = BlackSharkV3ProProtocol.CreateSetThx(true);

        Assert.Equal(0x06, report[6]);
        Assert.Equal(0x9E, report[10]);
        Assert.Equal(0x01, report[12]);
        Assert.Equal(0x01, report[13]);
        Assert.Equal(0x00, report[14]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }

    [Theory]
    [InlineData(0x0576)]
    [InlineData(0x0577)]
    [InlineData(0x101B)]
    public void SupportedProductIds_IncludeTheControlAndAudioPids(int productId)
    {
        Assert.True(BlackSharkV3ProProtocol.IsSupportedProductId((ushort)productId));
    }

    [Fact]
    public void WakeSequence_UsesTheThreeSynapsePrimers()
    {
        var reports = BlackSharkV3ProProtocol.CreateWakeSequence();

        Assert.Collection(
            reports,
            report => AssertPrimer(report, 0x02, 0x00),
            report => AssertPrimer(report, 0x2A, 0x00),
            report => AssertPrimer(report, 0x2A, 0x80));
    }

    [Fact]
    public void ParseResponse_ReadsArguments()
    {
        var report = new byte[BlackSharkV3ProProtocol.ReportLength];
        report[0] = BlackSharkV3ProProtocol.ReportId;
        report[1] = 0x02;
        report[10] = 0x21;
        report[11] = 0x01;
        report[12] = 2;
        report[13] = 87;
        report[14] = 1;

        Assert.True(BlackSharkV3ProProtocol.TryParseResponse(report, out var response));
        Assert.Equal(0x02, response.Status);
        Assert.Equal(0x21, response.CommandClass);
        Assert.Equal(0x01, response.Subtype);
        Assert.Equal(new byte[] { 87, 1 }, response.Arguments);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void SetSidetone_RejectsOutOfRangeLevel(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlackSharkV3ProProtocol.CreateSetSidetone(level));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void SetGameChat_RejectsOutOfRangeBalance(int balance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlackSharkV3ProProtocol.CreateSetGameChat(balance));
    }

    private static void AssertPrimer(byte[] report, byte commandClass, byte direction)
    {
        Assert.Equal(0x04, report[6]);
        Assert.Equal(direction, report[9]);
        Assert.Equal(commandClass, report[10]);
        Assert.True(BlackSharkV3ProProtocol.HasValidCrc(report));
    }
}
