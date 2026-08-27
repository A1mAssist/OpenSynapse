using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerKrakenProtocolTests
{
    [Fact]
    public void RequestMatchesTheUpstream37ByteMemoryLayout()
    {
        var report = OpenRazerKrakenProtocol.CreateRequest(0x40, 0x172D, [0x05, 0xAA]);

        Assert.Equal(37, report.Length);
        Assert.Equal(new byte[] { 0x04, 0x40, 0x02, 0x17, 0x2D, 0x05, 0xAA }, report[..7]);
        Assert.All(report[7..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void RequestRejectsMoreThan32ArgumentBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenRazerKrakenProtocol.CreateRequest(0x40, 0x172D, new byte[33]));
    }

    [Fact]
    public void StaticWritesKylieColorThenEnablesStaticEffect()
    {
        var reports = OpenRazerKrakenProtocol.CreateStatic(
            OpenRazerKrakenLayout.Kylie,
            new OpenRazerColor(0x11, 0x22, 0x33));

        AssertReport(reports[0], 0x1741, [0x11, 0x22, 0x33]);
        AssertReport(reports[1], 0x172D, [0x01]);
    }

    [Fact]
    public void ClassicStaticAndOffWriteOnlyTheRainieEffectByte()
    {
        var staticReports = OpenRazerKrakenProtocol.CreateStatic(
            OpenRazerKrakenLayout.Rainie, new OpenRazerColor(1, 2, 3), writeColor: false);
        AssertReport(Assert.Single(staticReports), 0x1008, [0x01]);
        AssertReport(Assert.Single(OpenRazerKrakenProtocol.CreateOff(OpenRazerKrakenLayout.Rainie)),
            0x1008, [0x00]);
    }

    [Fact]
    public void CustomPreservesOptionalIntensity()
    {
        var reports = OpenRazerKrakenProtocol.CreateCustom(
            OpenRazerKrakenLayout.Kylie, new OpenRazerColor(1, 2, 3), 0x44);
        AssertReport(reports[0], 0x1189, [1, 2, 3, 0x44]);
        AssertReport(reports[1], 0x172D, [0x01]);
    }

    [Fact]
    public void SpectrumUsesTheRainieEffectAddressAndBitfield()
    {
        var reports = OpenRazerKrakenProtocol.CreateSpectrum(OpenRazerKrakenLayout.Rainie);

        AssertReport(Assert.Single(reports), 0x1008, [0x05]);
    }

    [Theory]
    [InlineData(1, 0x1741, 0x0B)]
    [InlineData(2, 0x1745, 0x19)]
    [InlineData(3, 0x174D, 0x29)]
    public void BreathingUsesKylieColorBanksAndEffectBitfield(
        int colorCount,
        ushort baseAddress,
        byte effectByte)
    {
        OpenRazerColor[] colors =
        [
            new(0x11, 0x12, 0x13),
            new(0x21, 0x22, 0x23),
            new(0x31, 0x32, 0x33),
        ];

        var reports = OpenRazerKrakenProtocol.CreateBreathing(
            OpenRazerKrakenLayout.Kylie,
            colors[..colorCount]);

        Assert.Equal(colorCount + 1, reports.Count);
        for (var index = 0; index < colorCount; index++)
        {
            var color = colors[index];
            AssertReport(
                reports[index],
                checked((ushort)(baseAddress + (index * 4))),
                [color.Red, color.Green, color.Blue]);
        }
        AssertReport(reports[^1], 0x172D, [effectByte]);
    }

    [Fact]
    public void RainieBreathingUsesItsSingleColorAddress()
    {
        var reports = OpenRazerKrakenProtocol.CreateBreathing(
            OpenRazerKrakenLayout.Rainie,
            [new OpenRazerColor(1, 2, 3)]);

        AssertReport(reports[0], 0x15DE, [1, 2, 3]);
        AssertReport(reports[1], 0x1008, [0x0B]);
    }

    [Fact]
    public void RainieRejectsMultiColorBreathing()
    {
        Assert.Throws<NotSupportedException>(() => OpenRazerKrakenProtocol.CreateBreathing(
            OpenRazerKrakenLayout.Rainie,
            [new OpenRazerColor(1, 2, 3), new OpenRazerColor(4, 5, 6)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void BreathingRejectsAnInvalidColorCount(int count)
    {
        var colors = Enumerable.Repeat(new OpenRazerColor(1, 2, 3), count).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenRazerKrakenProtocol.CreateBreathing(OpenRazerKrakenLayout.Kylie, colors));
    }

    private static void AssertReport(byte[] report, ushort address, byte[] arguments)
    {
        Assert.Equal(37, report.Length);
        Assert.Equal(0x04, report[0]);
        Assert.Equal(0x40, report[1]);
        Assert.Equal(arguments.Length, report[2]);
        Assert.Equal((byte)(address >> 8), report[3]);
        Assert.Equal((byte)address, report[4]);
        Assert.Equal(arguments, report[5..(5 + arguments.Length)]);
        Assert.All(report[(5 + arguments.Length)..], value => Assert.Equal(0, value));
    }
}
