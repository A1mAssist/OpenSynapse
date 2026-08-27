using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerArgbProtocolTests
{
    [Fact]
    public void FrameMatchesTheUpstream320ByteLayout()
    {
        var report = OpenRazerArgbProtocol.CreateFrame(
            3,
            [new(0x11, 0x22, 0x33), new(0x44, 0x55, 0x66)]);

        Assert.Equal(320, report.Length);
        Assert.Equal(new byte[] { 0x04, 0x03, 0x03, 0x00, 0x01 }, report[..5]);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 }, report[5..11]);
        Assert.All(report[11..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void ChannelFiveUsesTheAlternateReportIdAndAccepts105Leds()
    {
        var colors = Enumerable.Repeat(new OpenRazerColor(1, 2, 3), 105).ToArray();

        var report = OpenRazerArgbProtocol.CreateFrame(5, colors);

        Assert.Equal(0x84, report[0]);
        Assert.Equal(5, report[1]);
        Assert.Equal(5, report[2]);
        Assert.Equal(104, report[4]);
        Assert.Equal(new byte[] { 1, 2, 3 }, report[^3..]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(106)]
    public void FrameRejectsAnInvalidLedCount(int count)
    {
        var colors = Enumerable.Repeat(new OpenRazerColor(0, 0, 0), count).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenRazerArgbProtocol.CreateFrame(0, colors));
    }
}
