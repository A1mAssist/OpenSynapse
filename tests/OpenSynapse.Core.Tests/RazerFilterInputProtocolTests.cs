using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerFilterInputProtocolTests
{
    [Fact]
    public void BuildersAndCapturedFramesMatchVerifiedDriverLayout()
    {
        Assert.Equal(
            "000000000A000000700000000000000000000000AAAAAAAAAAAAAAAAAAAAAAAA",
            Convert.ToHexString(RazerFilterInputProtocol.CreateConsumerInput(0x70)));

        var hook = RazerFilterInputProtocol.CreateKeyboardHook(0x3B, 1);
        Assert.Equal(RazerFilterInputProtocol.InputHookLength, hook.Length);
        Assert.Equal(1, BitConverter.ToInt32(hook, 4));
        Assert.Equal(0x3B, BitConverter.ToUInt16(hook, 10));
        Assert.Equal(0, BitConverter.ToUInt16(hook, 12));
        Assert.Equal(0u, BitConverter.ToUInt32(hook, 16));

        var extendedHook = RazerFilterInputProtocol.CreateKeyboardHook(0x50, 3);
        Assert.Equal(0x50, BitConverter.ToUInt16(extendedHook, 10));
        Assert.Equal(2, BitConverter.ToUInt16(extendedHook, 12));
        Assert.Equal(0u, BitConverter.ToUInt32(extendedHook, 16));

        var clearKey = RazerFilterInputProtocol.CreateKeyboardClearKey(0x50, 3);
        Assert.Equal(0x20, clearKey.Length);
        Assert.Equal(extendedHook[..clearKey.Length], clearKey);

        Assert.Equal([1, 0, 0, 0, 1], RazerFilterInputProtocol.CreateKeyboardRedirect(true));

        var keyboard = CapturedFrame("000000000000000002000000000000000100000000003B0000000000100000000000000001000000F6FFFFFF");
        Assert.True(RazerFilterInputProtocol.TryParseInputFrame(keyboard, out var keyboardInput));
        Assert.Equal(new BladeMappingInputEvent(BladeMappingInputKind.Keyboard, 0x3B, true), keyboardInput);

        var razerKey = CapturedFrame("00000000000000000400000000000000030000000000D3000100000000000000");
        Assert.True(RazerFilterInputProtocol.TryParseInputFrame(razerKey, out var razerKeyInput));
        Assert.Equal(new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0xD3, false), razerKeyInput);

        var extended = CapturedFrame("0000000000000000020000000000000001000000000050000200000000000000");
        Assert.True(RazerFilterInputProtocol.TryParseInputFrame(extended, out var extendedInput));
        Assert.Equal(
            new BladeMappingInputEvent(BladeMappingInputKind.Keyboard, 0x50, true, true),
            extendedInput);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RazerFilterInputProtocol.CreateKeyboardHook(0x50, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RazerFilterInputProtocol.CreateKeyboardClearKey(0x50, 4));

        var dirtyCapturedRazerKey = Convert.FromHexString(
            "4101000000000000040000000F87FFFF0300000000000A000000000000000000" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "0000000000000000AEFB2BDFFBF0FFFF00000000000000000000000000000000" +
            "309AA68F0F87FFFF0900000000000000509F9D590F87FFFFB09BD9590F87FFFF" +
            "0300000000000000A9E852530E86FFFF01000000000000002B01C1C806F8FFFF" +
            "0000000000000000000000000000000000000000000000000000000000000000" +
            "00000000000000000000000000000000040000000000000090E752530E86FFFF" +
            "6EFC2BDFFBF0FFFF37DBF36506F8FFFF0000000000000000509AA68F0F87FFFF" +
            "7CBFAF440F87FFFF5E2DF25A06F8FFFF0400000006F8FFFF90E752530E86FFFF" +
            "E0F4AB4D0F87FFFFD7D9F36506F8FFFF");
        Assert.True(RazerFilterInputProtocol.TryParseInputFrame(
            dirtyCapturedRazerKey,
            out var dirtyInput));
        Assert.Equal(
            new BladeMappingInputEvent(BladeMappingInputKind.RazerKey, 0x0A, true),
            dirtyInput);
    }

    [Fact]
    public void RejectsTruncatedWrongChannelAndUnsupportedFlags()
    {
        var frame = CapturedFrame(
            "000000000000000002000000000000000100000000003B0000000000000000");
        Assert.False(RazerFilterInputProtocol.TryParseInputFrame(frame[..^1], out _));
        Assert.False(RazerFilterInputProtocol.TryParseInputFrame(
            frame.Concat(new byte[1]).ToArray(),
            out _));

        frame[8] = 4;
        Assert.False(RazerFilterInputProtocol.TryParseInputFrame(frame, out _));

        frame[8] = 2;
        frame[24] = 4;
        Assert.False(RazerFilterInputProtocol.TryParseInputFrame(frame, out _));

        frame[8] = 4;
        frame[16] = 3;
        frame[24] = 2;
        Assert.False(RazerFilterInputProtocol.TryParseInputFrame(frame, out _));
    }

    private static byte[] CapturedFrame(string prefix)
    {
        var frame = new byte[RazerFilterInputProtocol.InputFrameLength];
        Convert.FromHexString(prefix).CopyTo(frame, 0);
        return frame;
    }
}
