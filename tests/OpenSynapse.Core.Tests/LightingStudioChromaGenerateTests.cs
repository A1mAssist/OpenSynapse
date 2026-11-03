using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Lighting;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioChromaGenerateTests
{
    private const string SampleConfig = """
        {"colorSource":"procedural","offsetX":0,"offsetY":0,"rotation":0,"scale":1,"scaleX":1,"scaleY":1,"mirrorX":false,"mirrorY":false,"flipX":false,"flipY":false,"patternSpread":1,"patternSpeed":0,"blend":"Sample","pattern":"1"}
        """;

    [Fact]
    public void SameMediaBuildsTheSamePalette()
    {
        var parameters = Create(SampleConfig, new Sampler((u, v) => Quadrants(u, v)));

        var first = StudioMediaPalette.BuildPalette(parameters, parameters.MediaSampler!);
        var second = StudioMediaPalette.BuildPalette(parameters, new Sampler((u, v) => Quadrants(u, v)));

        Assert.Equal(StudioMediaPalette.PaletteSize, first.Length);
        Assert.Equal(first, second);
        Assert.Contains(first, color => color != StudioRgb.Black);
    }

    [Fact]
    public void SameMediaRendersTheSameFrame()
    {
        var first = StudioEffectRenderer.Render(
            Create(SampleConfig, new Sampler((u, v) => Quadrants(u, v))), 6, 17, 1.5);
        var second = StudioEffectRenderer.Render(
            Create(SampleConfig, new Sampler((u, v) => Quadrants(u, v))), 6, 17, 1.5);

        Assert.Equal(first, second);
        Assert.Contains(first, color => color != StudioRgb.Black);
    }

    [Fact]
    public void OffsetMovesTheSamplePoint()
    {
        var config = SampleConfig.Replace("\"offsetX\":0", "\"offsetX\":0.25", StringComparison.Ordinal);
        var frame = StudioEffectRenderer.Render(Create(config, new Sampler((u, v) => Gradient(u, v))), 1, 5, 0);

        for (var column = 0; column < 5; column++)
        {
            var expected = Gradient(Wrap(column / 4d - 0.25), 0);
            Assert.Equal(expected, frame[column]);
        }
    }

    [Fact]
    public void RotationTurnsClockwiseAroundTheCentre()
    {
        var config = SampleConfig.Replace("\"rotation\":0", "\"rotation\":90", StringComparison.Ordinal);
        var frame = StudioEffectRenderer.Render(Create(config, new Sampler((u, v) => Gradient(u, v))), 3, 3, 0);

        // Right middle (u = 1, v = 0.5) rotates to bottom middle (v = 1, wrapped to 0).
        Assert.Equal(Gradient(0.5, 0), frame[1 * 3 + 2]);
    }

    [Fact]
    public void MirrorXFoldsAroundTheCentre()
    {
        var config = SampleConfig.Replace("\"mirrorX\":false", "\"mirrorX\":true", StringComparison.Ordinal);
        var frame = StudioEffectRenderer.Render(Create(config, new Sampler((u, v) => Gradient(u, v))), 1, 7, 0);

        for (var column = 0; column < 3; column++)
        {
            Assert.Equal(frame[column], frame[6 - column]);
        }
    }

    [Fact]
    public void MirrorYFoldsAroundTheCentre()
    {
        var config = SampleConfig.Replace("\"mirrorY\":false", "\"mirrorY\":true", StringComparison.Ordinal);
        var frame = StudioEffectRenderer.Render(Create(config, new Sampler((u, v) => Gradient(u, v))), 7, 1, 0);

        for (var row = 0; row < 3; row++)
        {
            Assert.Equal(frame[row], frame[6 - row]);
        }
    }

    [Fact]
    public void MissingSamplerKeepsTheProceduralFallback()
    {
        var parameters = Create(SampleConfig, sampler: null);

        var frame = StudioEffectRenderer.Render(parameters, 6, 17, 0.8);
        var again = StudioEffectRenderer.Render(Create(SampleConfig, sampler: null), 6, 17, 0.8);

        Assert.Contains(frame, color => color != StudioRgb.Black);
        Assert.Equal(frame, again);
    }

    [Fact]
    public void FailingSamplerKeepsTheProceduralFallback()
    {
        var frame = StudioEffectRenderer.Render(
            Create(SampleConfig, new Sampler((_, _) => null)), 6, 17, 0.8);

        Assert.Contains(frame, color => color != StudioRgb.Black);
    }

    [Fact]
    public void PartialSamplerFailureKeepsRendering()
    {
        var frame = StudioEffectRenderer.Render(
            Create(SampleConfig, new Sampler((u, v) => Wrap(u) < 0.5 ? Gradient(u, v) : null)),
            4,
            8,
            0.4);

        Assert.Contains(frame, color => color != StudioRgb.Black);
    }

    [Fact]
    public void DarkenLightenBlendMovesTwentyPercentBothWays()
    {
        StudioRgb[] palette = [StudioRgb.Black];

        var bright = new StudioRgb(200, 200, 200);
        var lightened = StudioMediaPalette.ApplyBlend(
            StudioGenerateBlend.DarkenLighten20P, bright, bright, palette, StudioMediaPalette.Lightness(bright));
        Assert.Equal(new StudioRgb(240, 240, 240), lightened);

        var dark = new StudioRgb(60, 60, 60);
        var darkened = StudioMediaPalette.ApplyBlend(
            StudioGenerateBlend.DarkenLighten20P, dark, dark, palette, StudioMediaPalette.Lightness(dark));
        Assert.Equal(new StudioRgb(48, 48, 48), darkened);
    }

    [Fact]
    public void GenerateConfigReadsObservedFields()
    {
        var config = StudioGenerateConfig.Parse("""
            {"colorSource":"AI","offsetX":2.4,"offsetY":1,"rotation":-35.5,"scale":2,"scaleX":3.2,"scaleY":1.4,"mirrorX":true,"mirrorY":false,"flipX":false,"flipY":true,"patternSpread":22,"patternSpeed":4.5,"blend":"patternlerp","pattern":7}
            """);

        Assert.True(config.IsAi);
        Assert.Equal(2.4, config.OffsetX, 3);
        Assert.Equal(-35.5, config.Rotation, 3);
        Assert.Equal(6.4, config.ScaleX, 3);
        Assert.Equal(2.8, config.ScaleY, 3);
        Assert.True(config.MirrorX);
        Assert.False(config.MirrorY);
        Assert.True(config.FlipY);
        Assert.Equal(22, config.PatternSpread, 3);
        Assert.Equal(4.5, config.PatternSpeed, 3);
        Assert.Equal(StudioGenerateBlend.PatternLerp, config.Blend);
        Assert.Equal(7, config.Pattern);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    public void GenerateConfigFallsBackToDefaults(string? json)
    {
        Assert.Equal(StudioGenerateConfig.Default, StudioGenerateConfig.Parse(json));
    }

    [Fact]
    public async Task MissingMediaFileReturnsNoSampler()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");

        Assert.Null(await StudioMediaSource.TryLoadAsync(path));
    }

    [Fact]
    public async Task CorruptMediaFileReturnsNoSampler()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        try
        {
            Assert.Null(await StudioMediaSource.TryLoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImageSourceSamplesBgraPixels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await WriteQuadrantPngAsync(path);
        try
        {
            var source = await StudioMediaSource.TryLoadAsync(path);
            Assert.NotNull(source);

            Assert.True(source.TrySample(0.25, 0.25, out var topLeft));
            Assert.Equal(new StudioRgb(255, 0, 0), topLeft);

            Assert.True(source.TrySample(0.75, 0.25, out var topRight));
            Assert.Equal(new StudioRgb(0, 255, 0), topRight);

            Assert.True(source.TrySample(0.25, 0.75, out var bottomLeft));
            Assert.Equal(new StudioRgb(0, 0, 255), bottomLeft);

            Assert.True(source.TrySample(0.75, 0.75, out var bottomRight));
            Assert.Equal(new StudioRgb(255, 255, 0), bottomRight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VideoPlaybackSelectsTheNearestCachedFrame()
    {
        var playback = StudioVideoPlayback.FromFrames(
            TimeSpan.FromMilliseconds(250),
            [SolidFrame(255, 0, 0), SolidFrame(0, 0, 255)]);

        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.1, out var first));
        Assert.Equal(new StudioRgb(255, 0, 0), first);

        Assert.True(playback.TrySampleAt(0.5, 0.5, 0.2, out var second));
        Assert.Equal(new StudioRgb(0, 0, 255), second);
    }

    [Fact]
    public void VideoPlaybackClampsTimeOutsideTheCache()
    {
        var playback = StudioVideoPlayback.FromFrames(
            TimeSpan.FromMilliseconds(250),
            [SolidFrame(255, 0, 0), SolidFrame(0, 0, 255)]);

        Assert.True(playback.TrySampleAt(0.5, 0.5, -12, out var before));
        Assert.Equal(new StudioRgb(255, 0, 0), before);

        Assert.True(playback.TrySampleAt(0.5, 0.5, 90, out var after));
        Assert.Equal(new StudioRgb(0, 0, 255), after);

        Assert.True(playback.TrySampleAt(0.5, 0.5, double.PositiveInfinity, out var infinite));
        Assert.Equal(new StudioRgb(0, 0, 255), infinite);

        Assert.True(playback.TrySampleAt(0.5, 0.5, double.NaN, out var nan));
        Assert.Equal(new StudioRgb(255, 0, 0), nan);
    }

    [Fact]
    public void DefaultSamplerMethodIgnoresTime()
    {
        IStudioMediaSampler sampler = new Sampler((u, v) => Quadrants(u, v));

        Assert.True(sampler.TrySampleAt(0.75, 0.25, 0, out var first));
        Assert.True(sampler.TrySampleAt(0.75, 0.25, 240, out var later));

        Assert.Equal(new StudioRgb(0, 255, 0), first);
        Assert.Equal(first, later);
    }

    [Fact]
    public async Task ImageSourceIgnoresSamplingTime()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await WriteQuadrantPngAsync(path);
        try
        {
            var source = await StudioMediaSource.TryLoadAsync(path);
            Assert.NotNull(source);

            Assert.True(source.TrySampleAt(0.75, 0.75, 0, out var start));
            Assert.True(source.TrySampleAt(0.75, 0.75, 45, out var later));

            Assert.Equal(new StudioRgb(255, 255, 0), start);
            Assert.Equal(start, later);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static StudioEffectParams Create(string config, IStudioMediaSampler? sampler) => new()
    {
        Effect = StudioEffectKind.ChromaGenerate,
        MediaSrc = @"C:\media\clip.png",
        CurrentConfigJson = config,
        MediaSampler = sampler,
    };

    private static StudioRgb Gradient(double u, double v) =>
        new((byte)Math.Round(Wrap(u) * 255), (byte)Math.Round(Wrap(v) * 255), 0);

    private static StudioRgb Quadrants(double u, double v) => (Wrap(u) < 0.5, Wrap(v) < 0.5) switch
    {
        (true, true) => new StudioRgb(255, 0, 0),
        (false, true) => new StudioRgb(0, 255, 0),
        (true, false) => new StudioRgb(0, 0, 255),
        _ => new StudioRgb(255, 255, 0),
    };

    private static double Wrap(double value) => value - Math.Floor(value);

    private static byte[] SolidFrame(byte red, byte green, byte blue)
    {
        var pixels = new byte[StudioMediaPalette.GridSize * StudioMediaPalette.GridSize * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = 255;
        }

        return pixels;
    }

    private static async Task WriteQuadrantPngAsync(string path)
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                var color = (column < size / 2, row < size / 2) switch
                {
                    (true, true) => (255, 0, 0),
                    (false, true) => (0, 255, 0),
                    (true, false) => (0, 0, 255),
                    _ => (255, 255, 0),
                };
                var index = (row * size + column) * 4;
                pixels[index] = (byte)color.Item1;
                pixels[index + 1] = (byte)color.Item2;
                pixels[index + 2] = (byte)color.Item3;
                pixels[index + 3] = 255;
            }
        }

        using var memory = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, size, size, 96, 96, pixels);
        await encoder.FlushAsync();

        var bytes = new byte[memory.Size];
        using var reader = new DataReader(memory.GetInputStreamAt(0));
        await reader.LoadAsync((uint)memory.Size);
        reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
    }

    private sealed class Sampler(Func<double, double, StudioRgb?> sample) : IStudioMediaSampler
    {
        public bool TrySample(double u, double v, out StudioRgb color)
        {
            var value = sample(u, v);
            color = value ?? default;
            return value.HasValue;
        }
    }
}

