using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHidSpecialLightingTests
{
    [Theory]
    [InlineData(0x0520)]
    [InlineData(0x0527)]
    [InlineData(0x0560)]
    public void KrakenLightingMergesIntoTheAudioPage(ushort productId) =>
        Assert.True(
            RazerHidSpecialLightingService.IsMergedIntoAudioPage(productId, hasAudioChannelInterface: true),
            $"0x{productId:X4}");

    [Theory]
    [InlineData(0x0520)]
    [InlineData(0x0521)]
    [InlineData(0x0526)]
    [InlineData(0x0527)]
    [InlineData(0x0529)]
    [InlineData(0x0554)]
    [InlineData(0x0560)]
    public void KrakenBaseModelsExposeThe37ByteAudioChannel(ushort productId) =>
        Assert.True(
            RazerHidSpecialLightingService.HasAudioChannelInterface(
                productId,
                new[] { Descriptor(productId, input: 37, output: 37) }),
            $"0x{productId:X4}");

    [Fact]
    public void KrakenV4DoesNotMoveToTheAudioPageOnItsLightingCollection() =>
        Assert.False(
            RazerHidSpecialLightingService.HasAudioChannelInterface(
                0x056B,
                new[] { Descriptor(0x056B, input: 64, output: 64) }));

    [Theory]
    [InlineData(0x0501)]
    [InlineData(0x0504)]
    [InlineData(0x0506)]
    [InlineData(0x0510)]
    public void OlderKrakenModelsKeepTheirLightingRow(ushort productId) =>
        Assert.False(
            RazerHidSpecialLightingService.IsMergedIntoAudioPage(productId, hasAudioChannelInterface: false),
            $"0x{productId:X4}");

    [Fact]
    public void KrakenV4KeepsItsLightingRowBecauseItHasNoAudioFeatureInterface()
    {
        // The Kraken V4 exposes only a 64-byte FF14 collection, so the audio page
        // never selects it. Moving its lighting card to the audio page would hide
        // the headset from the app entirely.
        Assert.False(
            RazerHidSpecialLightingService.IsMergedIntoAudioPage(0x056B, hasAudioChannelInterface: false),
            "0x056B");
    }

    [Fact]
    public void KrakenV4DongleAlsoKeepsItsLightingRow()
    {
        Assert.False(
            RazerHidSpecialLightingService.IsMergedIntoAudioPage(0x056C, hasAudioChannelInterface: false),
            "0x056C");
    }

    private static WindowsHidDiscovery.HidInterfaceDescriptor Descriptor(
        ushort productId,
        ushort input,
        ushort output) =>
        new("path", "key", 0x1532, productId, input, output, 0, 0xFF00, 0x0001,
            OpenSynapse.Core.Devices.DeviceAccessState.Available, "test");

    [Fact]
    public void ArgbFrameCarriesChannelAndColorBytes()
    {
        var frame = RazerHidArgbFrame.Create(2,
        [
            new RazerHidColor(0x11, 0x22, 0x33),
            new RazerHidColor(0x44, 0x55, 0x66),
        ]);

        Assert.Equal(RazerHidArgbFrame.ReportLength, frame.Length);
        Assert.Equal(new byte[] { 0x04, 2, 2, 0, 1, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 },
            frame[..11]);
        Assert.All(frame[11..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void ArgbFrameSwitchesReportIdFromChannelFive()
    {
        Assert.Equal(0x04, RazerHidArgbFrame.Create(4, [new RazerHidColor(1, 2, 3)])[0]);
        Assert.Equal(0x84, RazerHidArgbFrame.Create(5, [new RazerHidColor(1, 2, 3)])[0]);
    }

    [Fact]
    public void ArgbFrameRejectsOutOfRangeInput()
    {
        var colors = Enumerable.Repeat(new RazerHidColor(1, 2, 3), RazerHidArgbFrame.MaxLeds).ToArray();
        Assert.Equal(RazerHidArgbFrame.ReportLength, RazerHidArgbFrame.Create(0, colors).Length);
        Assert.Equal(
            RazerHidArgbFrame.MaxLeds - 1,
            RazerHidArgbFrame.Create(0, colors)[4]);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RazerHidArgbFrame.Create(0, [.. colors, new RazerHidColor(1, 2, 3)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerHidArgbFrame.Create(0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RazerHidArgbFrame.Create(6, [new RazerHidColor(1, 2, 3)]));
    }
}
