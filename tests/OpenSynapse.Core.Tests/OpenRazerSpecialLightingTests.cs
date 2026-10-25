using OpenSynapse.Windows.Devices;
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
}
