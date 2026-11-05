using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerOnboardMacroNameTests
{
    [Fact]
    public void NameBlockRoundTripsWithDotNetGuidByteOrder()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var name = new RazerOnboardMacroName(
            guid,
            42,
            "Boss key",
            [1, 2, 3],
            "20261105120000",
            "0123456789abcdef0123456789abcdef");

        var encoded = name.Encode();
        var decoded = RazerOnboardMacroName.Decode(encoded);

        Assert.Equal(250, encoded.Length);
        Assert.Equal(new byte[] { 0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66 },
            encoded[..8]);
        Assert.Equal(new byte[] { 42, 0, 0, 0 }, encoded[16..20]);
        Assert.Equal(guid, decoded.Guid);
        Assert.Equal(42, decoded.NumItems);
        Assert.Equal("Boss key", decoded.Name);
        Assert.Equal([1, 2, 3], decoded.ExtraInfo[..3]);
        Assert.Equal("20261105120000", decoded.Timestamp);
        Assert.Equal("0123456789abcdef0123456789abcdef", decoded.Hash);
    }

    [Fact]
    public void NameBlockRoundTripsUtf8WithoutChangingTheGuid()
    {
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var name = new RazerOnboardMacroName(guid, 1, "Macro \u00e9", [], "", "");

        var decoded = RazerOnboardMacroName.Decode(name.Encode());

        Assert.Equal("Macro \u00e9", decoded.Name);
        Assert.Equal(guid, decoded.Guid);
    }

    [Fact]
    public void ShortNameBlockIsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            RazerOnboardMacroName.Decode(new byte[249]));
    }

    [Fact]
    public void NameCommandsUseTheBundleEightyByteCommandBody()
    {
        var read = RazerOnboardMacroProtocol.GetMacroName.CreateRequest(
            0x1F, RazerOnboardMacroProtocol.CreateReadNameArguments(7, 0));
        var write = RazerOnboardMacroProtocol.SetMacroName.CreateRequest(
            0x1F,
            RazerOnboardMacroProtocol.CreateWriteNameArguments(7, 0, new byte[64]));

        Assert.Equal(80, read[RazerFeatureReport.DataSizeOffset]);
        Assert.Equal(80, write[RazerFeatureReport.DataSizeOffset]);
    }

    [Fact]
    public void NameWriteArgumentsCarrySlotOffsetSizeAndChunk()
    {
        var arguments = RazerOnboardMacroProtocol.CreateWriteNameArguments(
            7, 64, new byte[] { 0xAA, 0xBB });

        Assert.Equal(new byte[] { 0, 7, 0, 64, 0, 250, 0xAA, 0xBB }, arguments);
    }

    [Fact]
    public void FreeSlotSelectionNeverReturnsUsedSlots()
    {
        var free = RazerOnboardMacroSlots.FindFreeSlots(8, [1, 3, 8]);

        Assert.Equal([2, 4, 5, 6, 7], free);
    }

    [Theory]
    [InlineData(RazerHidMacroPlayback.Once, RazerHidMappingFunctions.MacroTypeI, new byte[] { 0x00, 0x07, 0x01 })]
    [InlineData(RazerHidMacroPlayback.Repeat, RazerHidMappingFunctions.MacroTypeI, new byte[] { 0x00, 0x07, 0x04 })]
    [InlineData(RazerHidMacroPlayback.Hold, RazerHidMappingFunctions.MacroTypeII, new byte[] { 0x00, 0x07 })]
    [InlineData(RazerHidMacroPlayback.Toggle, RazerHidMappingFunctions.MacroTypeIII, new byte[] { 0x00, 0x07 })]
    [InlineData(RazerHidMacroPlayback.Sequence, RazerHidMappingFunctions.MacroTypeIV, new byte[] { 0x00, 0x07 })]
    public void MacroSlotUsesTheMiddlewareFunctionAndPlaybackData(
        RazerHidMacroPlayback playback,
        byte functionId,
        byte[] data)
    {
        var action = RazerHidMappingActions.CreateMacroAction(7, playback, 4);

        Assert.Equal(functionId, action.FunctionId);
        Assert.Equal(data, action.Data);
    }
}
