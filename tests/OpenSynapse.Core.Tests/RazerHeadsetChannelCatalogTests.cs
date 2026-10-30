using OpenSynapse.Windows.Devices;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHeadsetChannelCatalogTests
{
    private static readonly HashSet<ushort> KnownUnsupported =
    [
        0x0309, 0x052D, 0x0530, 0x0534, 0x053C, 0x0562, 0x0565,
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
    // AudioKrakenKittyV2Pro only appends a 37-byte Chroma frame to AudioKittyV2Pro;
    // the audio base class still uses the 91-byte feature channel.
    [InlineData(0x0554, RazerHeadsetChannel.Feature91)]
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
