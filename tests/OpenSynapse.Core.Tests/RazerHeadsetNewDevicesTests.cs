using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerHeadsetNewDevicesTests
{
    // The Xbox/PlayStation BlackShark V3 identities select AudioValkyrie, whose EQ,
    // EQ-band, mic-mute, battery and charging triplets are the MXIC commands already
    // implemented for 0x0555. See docs/HEADSET_AUDIO_ALIGNMENT.md.
    [Theory]
    [InlineData(0x0A4E)]
    [InlineData(0x0A51)]
    [InlineData(0x101E)]
    [InlineData(0x101F)]
    public void BlackSharkV3ConsoleIdentities_UseTheMxicEnvelope(ushort productId)
    {
        Assert.True(RazerHeadsetAudioService.IsSupported(productId));
        Assert.True(RazerMxicHeadsetProtocol.IsSupported(productId));
        Assert.Equal(DeviceCategory.Headset, RazerHeadsetAudioService.GetCategory(productId));
        Assert.False(string.IsNullOrWhiteSpace(RazerHeadsetAudioService.Products[productId]));
    }

    // The Valkyrie T3 identities (0A54, 1022) move EQ, mic mute and sidetone to report id 7
    // frames that nothing in this repository builds yet.
    [Theory]
    // 0x101C is the hardware-observed BlackShark V3 Pro USB identity owned by the
    // proprietary THX panel, so it stays out of the MXIC audio table.
    [InlineData(0x101C)]
    [InlineData(0x0A54)]
    [InlineData(0x1022)]
    public void UnsupportedHeadsetCandidates_AreNotAdmitted(ushort productId)
    {
        Assert.False(RazerHeadsetAudioService.IsSupported(productId));
        Assert.False(RazerHeadsetAudioService.Products.ContainsKey(productId));
        Assert.Equal(DeviceCategory.Headset, RazerHeadsetAudioService.GetCategory(productId));
    }

    // The Hammerhead V3 dongles use the rzDevice40 class 0x18 triplet layout on the same
    // 64-byte MXIC envelope. See docs/HEADSET_AUDIO_ALIGNMENT.md.
    [Theory]
    [InlineData(0x0A61)]
    [InlineData(0x0A64)]
    [InlineData(0x0A68)]
    [InlineData(0x0A6C)]
    [InlineData(0x0A81)]
    public void HammerheadV3_UseTheClass18Dialect(ushort productId)
    {
        Assert.True(RazerHeadsetAudioService.IsSupported(productId));
        Assert.True(RazerMxicHeadsetProtocol.IsSupported(productId));
        Assert.True(RazerMxicHeadsetProtocol.UsesClass18Commands(productId));
        Assert.Equal(DeviceCategory.Headset, RazerHeadsetAudioService.GetCategory(productId));
        Assert.False(string.IsNullOrWhiteSpace(RazerHeadsetAudioService.Products[productId]));
    }

    [Theory]
    [InlineData(0x0576)]
    [InlineData(0x0579)]
    public void ExistingBlackSharkV3Entries_RemainAdmitted(ushort productId)
    {
        Assert.True(RazerHeadsetAudioService.IsSupported(productId));
        Assert.False(string.IsNullOrWhiteSpace(RazerHeadsetAudioService.Products[productId]));
        Assert.Equal(DeviceCategory.Headset, RazerHeadsetAudioService.GetCategory(productId));
    }
}
