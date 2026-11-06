using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerKrakenBaseProtocolTests
{
    [Fact]
    public void FirmwareRequestUsesTheBundleFrame()
    {
        var request = RazerKrakenBaseProtocol.CreateFirmwareRequest();

        Assert.Equal(37, request.Length);
        Assert.Equal(new byte[] { 4, 32, 2, 0, 48 }, request[..5]);
    }

    [Fact]
    public void FirmwareResponseUsesNibblesFromBytesOneAndTwo()
    {
        Assert.Equal(
            new Version(3, 4, 1, 2),
            RazerKrakenBaseProtocol.ParseFirmware(new byte[] { 0, 0x12, 0x34 }));
    }

    [Fact]
    public void SerialResponseKeepsTheAsciiBody()
    {
        var response = new byte[37];
        "RZ09-0529"u8.CopyTo(response.AsSpan(1));

        Assert.Equal("RZ09-0529", RazerKrakenBaseProtocol.ParseSerialNumber(response));
    }

    [Fact]
    public void AncResponseUsesTheFirstDataByte()
    {
        Assert.True(RazerKrakenBaseProtocol.ParseAncEnabled(new byte[] { 0, 1 }));
        Assert.False(RazerKrakenBaseProtocol.ParseAncEnabled(new byte[] { 0, 2 }));
    }

    [Theory]
    [InlineData(0x0520)]
    [InlineData(0x0521)]
    [InlineData(0x0526)]
    [InlineData(0x0527)]
    [InlineData(0x0529)]
    [InlineData(0x0554)]
    [InlineData(0x0560)]
    public void AudioKrakenBaseModelsUseThe37ByteChannel(ushort productId) =>
        Assert.Equal(
            RazerHeadsetChannel.KrakenBase37,
            RazerHeadsetChannelCatalog.Resolve(productId));
}
