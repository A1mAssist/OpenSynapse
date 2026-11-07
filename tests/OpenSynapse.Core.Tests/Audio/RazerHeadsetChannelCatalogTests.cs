using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHeadsetChannelCatalogTests
{
    private static readonly HashSet<ushort> KnownUnsupported =
    [
        0x0309, 0x052D, 0x0530, 0x0534, 0x053C, 0x0562,
    ];

    [Fact]
    public void EveryProductHasADeclaredChannel()
    {
        foreach (var productId in RazerHeadsetAudioService.Products.Keys)
        {
            var channel = RazerHeadsetChannelCatalog.Resolve(productId);
            Assert.True(
                KnownUnsupported.Contains(productId)
                    ? channel == RazerHeadsetChannel.Unsupported
                    : channel != RazerHeadsetChannel.Unsupported,
                RazerHeadsetChannelCatalog.Describe(productId));
        }
    }

    [Theory]
    [InlineData(0x0567, RazerHeadsetChannel.Billie64)]
    [InlineData(0x056B, RazerHeadsetChannel.Billie64)]
    [InlineData(0x0A4E, RazerHeadsetChannel.Valkyrie64)]
    [InlineData(0x0A51, RazerHeadsetChannel.Valkyrie64)]
    [InlineData(0x101E, RazerHeadsetChannel.Valkyrie64)]
    [InlineData(0x101F, RazerHeadsetChannel.Valkyrie64)]
    [InlineData(0x0587, RazerHeadsetChannel.Billie64)]
    [InlineData(0x0593, RazerHeadsetChannel.Mxic64)]
    [InlineData(0x0555, RazerHeadsetChannel.Mxic64)]
    // AudioKrakenBase and all of its subclasses use the 37-byte HID channel.
    [InlineData(0x0520, RazerHeadsetChannel.KrakenBase37)]
    [InlineData(0x0521, RazerHeadsetChannel.KrakenBase37)]
    [InlineData(0x0526, RazerHeadsetChannel.KrakenBase37)]
    [InlineData(0x0527, RazerHeadsetChannel.KrakenBase37)]
    [InlineData(0x0529, RazerHeadsetChannel.KrakenBase37)]
    [InlineData(0x0554, RazerHeadsetChannel.KrakenBase37)]
    [InlineData(0x0560, RazerHeadsetChannel.KrakenBase37)]
    // AudioOdessaT2Wireless uses the Billie-style 64-byte report-id-2 frame.
    [InlineData(0x0565, RazerHeadsetChannel.Billie64)]
    [InlineData(0x056E, RazerHeadsetChannel.Billie64)]
    public void BundleDifferencesSelectTheirDeclaredChannel(ushort productId, RazerHeadsetChannel expected) =>
        Assert.Equal(expected, RazerHeadsetChannelCatalog.Resolve(productId));

    [Theory]
    [InlineData(0x0539)]
    [InlineData(0x053B)]
    [InlineData(0x0583)]
    [InlineData(0x0584)]
    public void BarracudaAudioMxicDevicesUseThe64ByteChannel(ushort productId) =>
        Assert.Equal(RazerHeadsetChannel.Mxic64, RazerHeadsetChannelCatalog.Resolve(productId));

    [Fact]
    public void BlackSharkV3ProStaysOnItsDedicatedPanel()
    {
        Assert.Equal(RazerHeadsetChannel.DedicatedBlackShark, RazerHeadsetChannelCatalog.Resolve(0x0576));
        Assert.Equal(RazerHeadsetChannel.DedicatedBlackShark, RazerHeadsetChannelCatalog.Resolve(0x0577));
        Assert.Equal(RazerHeadsetChannel.DedicatedBlackShark, RazerHeadsetChannelCatalog.Resolve(0x101B));
        Assert.Equal(RazerHeadsetChannel.DedicatedBlackShark, RazerHeadsetChannelCatalog.Resolve(0x101C));
        Assert.Equal(RazerHeadsetChannel.Valkyrie64, RazerHeadsetChannelCatalog.Resolve(0x0579));
    }

    [Fact]
    public void WiredNariEssentialUsesTheSameFeatureChannelAsTheWirelessModel()
    {
        Assert.True(RazerHeadsetAudioService.IsSupported(0x051F));
        Assert.Equal("Razer Nari Essential", RazerHeadsetAudioService.Products[0x051F]);
        Assert.Equal(RazerHeadsetChannel.Feature91, RazerHeadsetChannelCatalog.Resolve(0x051F));
        Assert.Equal("Razer Nari Essential", RazerHeadsetAudioService.Products[0x051E]);
    }

    [Fact]
    public void UnknownProductsAreUnsupported()
    {
        Assert.Equal(RazerHeadsetChannel.Unsupported, RazerHeadsetChannelCatalog.Resolve(0xFFFF));
        Assert.False(RazerHeadsetChannelCatalog.IsSupported(0xFFFF));
    }

    [Fact]
    public void DescribeNamesTheProductTypeAndChannel()
    {
        var description = RazerHeadsetChannelCatalog.Describe(0x0555);
        Assert.Contains("0555", description);
        Assert.Contains("AudioMxicDevice", description);
        Assert.Contains("Mxic64", description);
    }
}
