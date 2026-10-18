using System.Text;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ChromaFileTests
{
    private const uint Red = 0x000000FF;
    private const uint Green = 0x0000FF00;

    [Fact]
    public async Task ParserReadsVersionOneKeyboardFramesAndPlaybackLoops()
    {
        var content = BuildChroma(
            device: 0,
            (0.5f, Fill(6 * 22, 0)),
            (0.25f, Fill(6 * 22, Red)));

        var animation = ChromaFileAnimationParser.Parse(content, "test.chroma", DateTime.UnixEpoch);

        Assert.False(animation.IsUniform);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(750), animation.Duration);
        Assert.All(animation.Frames[0].Colors, color => Assert.Equal(default, color));
        Assert.Contains(animation.Frames[1].Colors, color => color == new RazerRgb(255, 0, 0));

        var source = new ChromaFileFrameSource(animation);
        Assert.All(await source.RenderAsync(TimeSpan.Zero, CancellationToken.None),
            color => Assert.Equal(default, color));
        Assert.All(await source.RenderAsync(TimeSpan.FromMilliseconds(499), CancellationToken.None),
            color => Assert.Equal(default, color));
        Assert.Contains(
            await source.RenderAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None),
            color => color == new RazerRgb(255, 0, 0));
        Assert.Contains(
            await source.RenderAsync(TimeSpan.FromMilliseconds(749), CancellationToken.None),
            color => color == new RazerRgb(255, 0, 0));
        Assert.All(await source.RenderAsync(TimeSpan.FromMilliseconds(750), CancellationToken.None),
            color => Assert.Equal(default, color));
    }

    [Fact]
    public void UniformAnimationCollapsesToStaticColor()
    {
        var animation = ChromaFileAnimationParser.Parse(
            BuildChroma(device: 0, (0.1f, Fill(6 * 22, Green))),
            "uniform.chroma",
            DateTime.UnixEpoch);

        Assert.True(animation.IsUniform);
        Assert.Equal(new RazerRgb(0, 255, 0), animation.UniformColor);
        Assert.Single(animation.Frames);
        Assert.All(animation.Frames[0].Colors, color => Assert.Equal(new RazerRgb(0, 255, 0), color));
    }

    [Fact]
    public void ExtendedKeyboardCanvasMapsThroughTheSamePhysicalLayout()
    {
        var colors = Fill(8 * 24, 0);
        colors[(1 * 24) + 4] = Red;

        var animation = ChromaFileAnimationParser.Parse(
            BuildChroma(device: 3, (0.1f, colors)),
            "extended.chroma",
            DateTime.UnixEpoch);

        Assert.Equal(new RazerRgb(255, 0, 0), animation.Frames[0].Colors[2]);
    }

    [Theory]
    [InlineData(2, 1, 0u)]
    [InlineData(1, 2, 0u)]
    [InlineData(1, 1, 1u)]
    [InlineData(1, 1, 1801u)]
    public void InvalidHeadersAreRejected(int version, byte deviceType, uint frameCount)
    {
        var content = BuildRaw(version, deviceType, 0, frameCount, []);
        Assert.Throws<InvalidDataException>(() =>
            ChromaFileAnimationParser.Parse(content, "invalid.chroma", DateTime.UnixEpoch));
    }

    [Fact]
    public void InvalidDurationsAndColorsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => ParseSingle(0f, Fill(6 * 22, 0)));
        Assert.Throws<InvalidDataException>(() => ParseSingle(-1f, Fill(6 * 22, 0)));
        Assert.Throws<InvalidDataException>(() => ParseSingle(0.1f, Fill(6 * 22, 0x01000000)));

        var truncated = BuildChroma(device: 0, (0.1f, Fill(6 * 22, 0)));
        Assert.Throws<InvalidDataException>(() =>
            ChromaFileAnimationParser.Parse(
                truncated.AsSpan(0, truncated.Length - 1),
                "truncated.chroma",
                DateTime.UnixEpoch));
    }

    [Fact]
    public void ProfileRoundTripKeepsTheImportedAnimation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opensynapse-chroma-{Guid.NewGuid():N}");
        var previousRoot = ChromaFileAnimationStore.RootDirectory;
        try
        {
            ChromaFileAnimationStore.RootDirectory = root;
            var animation = ChromaFileAnimationStore.Import(
                BuildChroma(device: 0, (0.2f, Fill(6 * 22, Red))));
            var profile = BladeLightingProfileCodec.Create(
                new BladeLightingEffect(BladeLightingMode.ChromaFile, ChromaAnimation: animation));

            Assert.Equal("chromafile", profile.Effect);
            Assert.Equal(animation.FileName, profile.Parameters["file"]);

            var parsed = BladeLightingProfileCodec.Parse(profile);
            Assert.Equal(BladeLightingMode.ChromaFile, parsed.Mode);
            Assert.NotNull(parsed.ChromaAnimation);
            Assert.Equal(Red, (uint)(
                parsed.ChromaAnimation!.UniformColor.Red |
                (parsed.ChromaAnimation.UniformColor.Green << 8) |
                (parsed.ChromaAnimation.UniformColor.Blue << 16)));
        }
        finally
        {
            ChromaFileAnimationStore.RootDirectory = previousRoot;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ParseSingle(float duration, uint[] colors) =>
        ChromaFileAnimationParser.Parse(
            BuildChroma(device: 0, (duration, colors)),
            "invalid.chroma",
            DateTime.UnixEpoch);

    private static byte[] BuildChroma(byte device, params (float Duration, uint[] Colors)[] frames)
    {
        var pixels = device == 3 ? 8 * 24 : 6 * 22;
        return BuildRaw(1, 1, device, (uint)frames.Length, frames, pixels);
    }

    private static byte[] BuildRaw(
        int version,
        byte deviceType,
        byte device,
        uint frameCount,
        (float Duration, uint[] Colors)[] frames,
        int pixels = 6 * 22)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(version);
        writer.Write(deviceType);
        writer.Write(device);
        writer.Write(frameCount);
        for (var index = 0; index < frames.Length; index++)
        {
            writer.Write(frames[index].Duration);
            var colors = frames[index].Colors;
            for (var pixel = 0; pixel < pixels; pixel++)
            {
                writer.Write(pixel < colors.Length ? colors[pixel] : 0u);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static uint[] Fill(int count, uint color)
    {
        var result = new uint[count];
        Array.Fill(result, color);
        return result;
    }
}
