using OpenSynapse.ChromaSdk.Protocol;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ChromaSdkProtocolTests
{
    [Fact]
    public void SetEffectRoundTripsColorsAndKeyMasks()
    {
        var colors = Enumerable.Range(0, 6 * 22).Select(index => (uint)(index * 7919)).ToArray();
        var keys = Enumerable.Range(0, 6 * 22).Select(index => (uint)(index % 3 == 0 ? 0x01000000 | index : 0)).ToArray();
        var effect = new ChromaSdkEffect(
            ChromaSdkDeviceType.Keyboard,
            ChromaSdkEffectKind.CustomKey,
            22,
            6,
            colors,
            keys);
        using var stream = new MemoryStream();

        ChromaSdkPipe.Write(stream, new ChromaSdkSetEffectMessage(effect));
        stream.Position = 0;
        var message = Assert.IsType<ChromaSdkSetEffectMessage>(ChromaSdkPipe.Read(stream));

        Assert.Equal(ChromaSdkDeviceType.Keyboard, message.Value.DeviceType);
        Assert.Equal(ChromaSdkEffectKind.CustomKey, message.Value.Kind);
        Assert.Equal(22, message.Value.Width);
        Assert.Equal(6, message.Value.Height);
        Assert.Equal(colors, message.Value.Colors);
        Assert.Equal(keys, message.Value.Keys);
    }

    [Fact]
    public void EightByTwentyFourCustomEffectRoundTrips()
    {
        var colors = Enumerable.Range(0, 8 * 24).Select(index => (uint)(index * 3571)).ToArray();
        var effect = new ChromaSdkEffect(
            ChromaSdkDeviceType.Keyboard,
            ChromaSdkEffectKind.Custom2,
            24,
            8,
            colors);
        using var stream = new MemoryStream();

        ChromaSdkPipe.Write(stream, new ChromaSdkSetEffectMessage(effect));
        stream.Position = 0;
        var message = Assert.IsType<ChromaSdkSetEffectMessage>(ChromaSdkPipe.Read(stream));

        Assert.Equal(ChromaSdkEffectKind.Custom2, message.Value.Kind);
        Assert.Equal(24, message.Value.Width);
        Assert.Equal(8, message.Value.Height);
        Assert.Equal(colors, message.Value.Colors);
    }

    [Fact]
    public void HelloRoundTripsUnicodeTitle()
    {
        using var stream = new MemoryStream();
        ChromaSdkPipe.Write(stream, new ChromaSdkHelloMessage(
            new ChromaSdkHello(1, 42, "灯光工作室", ChromaSdkDeviceMask.Keyboard)));
        stream.Position = 0;

        var message = Assert.IsType<ChromaSdkHelloMessage>(ChromaSdkPipe.Read(stream));
        Assert.Equal(1, message.Value.ProtocolVersion);
        Assert.Equal(42, message.Value.ProcessId);
        Assert.Equal("灯光工作室", message.Value.Title);
        Assert.Equal(ChromaSdkDeviceMask.Keyboard, message.Value.SupportedDevices);
    }

    [Fact]
    public void EffectRejectsMismatchedDimensions()
    {
        var effect = new ChromaSdkEffect(
            ChromaSdkDeviceType.Keyboard,
            ChromaSdkEffectKind.Custom,
            22,
            6,
            new uint[6 * 21]);
        Assert.Throws<InvalidDataException>(effect.Validate);
    }

    [Fact]
    public void ReadRejectsOversizedFrame()
    {
        using var stream = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, ChromaSdkPipe.MaxPayload + 1);
        stream.Write(length);
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => ChromaSdkPipe.Read(stream));
    }
}