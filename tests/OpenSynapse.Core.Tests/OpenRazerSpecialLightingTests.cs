using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OpenRazerSpecialLightingTests
{
    [Theory]
    [InlineData(0x0520)]
    [InlineData(0x0527)]
    [InlineData(0x0560)]
    public void KrakenLightingMergesIntoTheAudioPage(ushort productId) =>
        Assert.True(OpenRazerSpecialLightingService.IsMergedIntoAudioPage(productId), $"0x{productId:X4}");

    [Theory]
    [InlineData(0x0501)]
    [InlineData(0x0504)]
    [InlineData(0x0506)]
    [InlineData(0x0510)]
    public void OlderKrakenModelsKeepTheirLightingRow(ushort productId) =>
        Assert.False(OpenRazerSpecialLightingService.IsMergedIntoAudioPage(productId), $"0x{productId:X4}");

    [Fact]
    public void ArgbFrameCarriesChannelAndColorBytes()
    {
        var frame = OpenRazerArgbFrame.Create(2,
        [
            new OpenRazerColor(0x11, 0x22, 0x33),
            new OpenRazerColor(0x44, 0x55, 0x66),
        ]);

        Assert.Equal(OpenRazerArgbFrame.ReportLength, frame.Length);
        Assert.Equal(new byte[] { 0x04, 2, 2, 0, 1, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 },
            frame[..11]);
        Assert.All(frame[11..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void ArgbFrameSwitchesReportIdFromChannelFive()
    {
        Assert.Equal(0x04, OpenRazerArgbFrame.Create(4, [new OpenRazerColor(1, 2, 3)])[0]);
        Assert.Equal(0x84, OpenRazerArgbFrame.Create(5, [new OpenRazerColor(1, 2, 3)])[0]);
    }

    [Fact]
    public void ArgbFrameRejectsOutOfRangeInput()
    {
        var colors = Enumerable.Repeat(new OpenRazerColor(1, 2, 3), OpenRazerArgbFrame.MaxLeds).ToArray();
        Assert.Equal(OpenRazerArgbFrame.ReportLength, OpenRazerArgbFrame.Create(0, colors).Length);
        Assert.Equal(
            OpenRazerArgbFrame.MaxLeds - 1,
            OpenRazerArgbFrame.Create(0, colors)[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenRazerArgbFrame.Create(0, [.. colors, new OpenRazerColor(1, 2, 3)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenRazerArgbFrame.Create(0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenRazerArgbFrame.Create(6, [new OpenRazerColor(1, 2, 3)]));
    }
}
