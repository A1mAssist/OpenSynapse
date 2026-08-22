using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ChromaLightingTests
{
    [Fact]
    public void PackedChromaColorUsesBgrOrder()
    {
        Assert.Equal(new RazerRgb(0x11, 0x22, 0x33),
            ChromaKeyboardFrameMapper.ToRgb(0x00332211));
    }

    [Fact]
    public async Task ExternalFrameSourceKeepsOnlyTheLatestCompleteFrame()
    {
        var source = new ChromaExternalFrameSource();
        var first = Enumerable.Repeat(new RazerRgb(1, 2, 3), QuickLightingEngine.PixelCount).ToArray();
        var second = Enumerable.Repeat(new RazerRgb(4, 5, 6), QuickLightingEngine.PixelCount).ToArray();

        source.Publish(first);
        source.Publish(second);
        var actual = await source.RenderAsync(TimeSpan.Zero, CancellationToken.None);

        Assert.All(actual, color => Assert.Equal(new RazerRgb(4, 5, 6), color));
    }

    [Fact]
    public void StaticAndCustomFramesMatchBladeMatrixContract()
    {
        var staticFrame = ChromaKeyboardFrameMapper.Static(new RazerRgb(10, 20, 30));
        var custom = ChromaKeyboardFrameMapper.Custom(
            Enumerable.Range(0, 6)
                .Select(_ => Enumerable.Repeat(0x000000FFu, 22).ToList())
                .ToList());

        Assert.Equal(QuickLightingEngine.PixelCount, staticFrame.Length);
        Assert.Equal(QuickLightingEngine.PixelCount, custom.Length);
        Assert.Contains(custom, color => color == new RazerRgb(255, 0, 0));
    }

    [Fact]
    public void CustomKeyUsesTheChromaActivationMaskAndInvertedColor()
    {
        var colors = Enumerable.Range(0, 6)
            .Select(_ => Enumerable.Repeat(0u, 22).ToList())
            .ToList();
        var keys = Enumerable.Range(0, 6)
            .Select(_ => Enumerable.Repeat(0u, 22).ToList())
            .ToList();
        keys[0][0] = 0x01000000u | (~0x000000FFu);

        var frame = ChromaKeyboardFrameMapper.CustomKey(colors, keys);
        Assert.Equal(new RazerRgb(0xFF, 0x00, 0x00), frame[1]);
    }

    [Fact]
    public void CustomKeyAcceptsTheEightByTwentyFourCustom2ColorCanvas()
    {
        var colors = Enumerable.Range(0, 8)
            .Select(_ => Enumerable.Repeat(0x0000FF00u, 24).ToList())
            .ToList();
        var keys = Enumerable.Range(0, 6)
            .Select(_ => Enumerable.Repeat(0u, 22).ToList())
            .ToList();

        var frame = ChromaKeyboardFrameMapper.Custom2Key(colors, keys);
        Assert.Contains(frame, color => color == new RazerRgb(0x00, 0xFF, 0x00));
    }

    [Fact]
    public void ChromaMatricesRejectNonProtocolDimensions()
    {
        var invalid = Enumerable.Range(0, 6)
            .Select(_ => Enumerable.Repeat(0u, 21).ToList())
            .ToList();

        Assert.Throws<ArgumentException>(() => ChromaKeyboardFrameMapper.Custom(invalid));
    }
}
