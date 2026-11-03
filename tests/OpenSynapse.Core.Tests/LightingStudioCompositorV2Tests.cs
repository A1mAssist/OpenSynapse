using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioCompositorV2Tests
{
    private static StudioLayerRender Layer(
        StudioRgb color,
        byte[]? mask = null,
        double opacity = 100,
        StudioBlendMode blend = StudioBlendMode.Normal) =>
        new(new StudioEffectParams { Effect = StudioEffectKind.Solid, Primary = color }, mask, opacity, blend);

    [Fact]
    public void MaskZeroKeepsTheLayerBelow()
    {
        var mask = new byte[4];
        var frame = StudioCompositor.RenderLayers(
            [Layer(StudioRgb.White, mask), Layer(new StudioRgb(255, 0, 0))],
            2,
            2,
            0);

        Assert.All(frame, pixel => Assert.Equal(new StudioRgb(255, 0, 0), pixel));
    }

    [Fact]
    public void MaskFullAppliesTheLayer()
    {
        var mask = Enumerable.Repeat((byte)100, 4).ToArray();
        var frame = StudioCompositor.RenderLayers(
            [Layer(new StudioRgb(0, 255, 0), mask), Layer(new StudioRgb(255, 0, 0))],
            2,
            2,
            0);

        Assert.All(frame, pixel => Assert.Equal(new StudioRgb(0, 255, 0), pixel));
    }

    [Fact]
    public void TopLayerWins()
    {
        var frame = StudioCompositor.RenderLayers(
            [Layer(new StudioRgb(0, 0, 255)), Layer(new StudioRgb(255, 0, 0))],
            1,
            1,
            0);

        Assert.Equal(new StudioRgb(0, 0, 255), frame[0]);
    }

    [Fact]
    public void OpacityBlendsChannels()
    {
        var frame = StudioCompositor.RenderLayers(
            [Layer(StudioRgb.White, opacity: 50), Layer(StudioRgb.Black)],
            1,
            1,
            0);

        Assert.InRange(frame[0].R, 126, 129);
        Assert.InRange(frame[0].G, 126, 129);
        Assert.InRange(frame[0].B, 126, 129);
    }

    [Fact]
    public void MismatchedMaskThrows()
    {
        var mask = new byte[3];

        Assert.Throws<ArgumentException>(() => StudioCompositor.RenderLayers(
            [Layer(StudioRgb.White, mask)],
            2,
            2,
            0));
    }
}
