using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioInputSamplerTests
{
    [Fact]
    public void BilinearResizeAveragesTwoByTwoIntoOnePixel()
    {
        var frame = new AmbientCaptureFrame(
            new[]
            {
                new RazerRgb(0, 0, 0),
                new RazerRgb(255, 0, 0),
                new RazerRgb(0, 255, 0),
                new RazerRgb(0, 0, 255),
            },
            2,
            2);

        var pixels = StudioInputSampler.CreateSnapshot(frame, 1, 1);

        Assert.Equal(new StudioRgb(64, 64, 64), Assert.Single(pixels));
    }

    [Fact]
    public void BilinearResizeKeepsSameSizeOrientation()
    {
        var frame = new AmbientCaptureFrame(
            new[]
            {
                new RazerRgb(1, 2, 3),
                new RazerRgb(4, 5, 6),
                new RazerRgb(7, 8, 9),
                new RazerRgb(10, 11, 12),
            },
            2,
            2);

        var pixels = StudioInputSampler.CreateSnapshot(frame, 2, 2);

        Assert.Equal(new StudioRgb(1, 2, 3), pixels[0]);
        Assert.Equal(new StudioRgb(4, 5, 6), pixels[1]);
        Assert.Equal(new StudioRgb(7, 8, 9), pixels[2]);
        Assert.Equal(new StudioRgb(10, 11, 12), pixels[3]);
    }

    [Fact]
    public void BilinearResizeExpandsOnePixelWithoutBlending()
    {
        var frame = new AmbientCaptureFrame(new[] { new RazerRgb(10, 20, 30) }, 1, 1);

        var pixels = StudioInputSampler.CreateSnapshot(frame, 2, 2);

        Assert.All(pixels, pixel => Assert.Equal(new StudioRgb(10, 20, 30), pixel));
    }

    [Fact]
    public void UvSamplerMapsCellCentersAndClampsEdges()
    {
        var pixels = new[]
        {
            new StudioRgb(1, 2, 3),
            new StudioRgb(4, 5, 6),
            new StudioRgb(7, 8, 9),
            new StudioRgb(10, 11, 12),
        };

        Assert.Equal(pixels[0], StudioInputSampler.SampleUv(pixels, 2, 2, 0.25, 0.25));
        Assert.Equal(pixels[1], StudioInputSampler.SampleUv(pixels, 2, 2, 0.75, 0.25));
        Assert.Equal(pixels[2], StudioInputSampler.SampleUv(pixels, 2, 2, 0.25, 0.75));
        Assert.Equal(pixels[3], StudioInputSampler.SampleUv(pixels, 2, 2, 0.75, 0.75));
        Assert.Equal(pixels[0], StudioInputSampler.SampleUv(pixels, 2, 2, -1, -1));
        Assert.Equal(pixels[3], StudioInputSampler.SampleUv(pixels, 2, 2, 2, 2));
    }

    [Fact]
    public void MissingFrameProducesBlackSnapshot()
    {
        var pixels = StudioInputSampler.CreateSnapshot(null, 3, 2);

        Assert.Equal(6, pixels.Length);
        Assert.All(pixels, pixel => Assert.Equal(StudioRgb.Black, pixel));
    }

    [Fact]
    public void SilentAudioNormalizesToZero()
    {
        Assert.Equal(0, StudioInputSampler.SmoothLevel(0, 0, TimeSpan.FromMilliseconds(16)));
        Assert.Equal(0, StudioInputSampler.SmoothLevel(0, double.NaN, TimeSpan.FromMilliseconds(16)));
        Assert.Equal(0, StudioInputSampler.SmoothLevel(0, -1, TimeSpan.FromMilliseconds(16)));
    }

    [Fact]
    public async Task SampleBeforeStartReturnsSilenceAndBlack()
    {
        await using var sampler = new StudioInputSampler();

        var input = sampler.Sample(2, 3);

        Assert.Equal(0, input.AudioLevel);
        Assert.Equal(0, sampler.AudioLevel);
        Assert.NotNull(input.ScreenSampler);
        Assert.NotNull(input.ScreenSamplerUv);
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                Assert.Equal(StudioRgb.Black, input.ScreenSampler!(column, row));
            }
        }

        Assert.Equal(StudioRgb.Black, input.ScreenSamplerUv!(0.5, 0.5));
    }
}