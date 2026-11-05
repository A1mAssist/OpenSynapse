using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class LightingStudioTests
{
    [Fact]
    public void SolidLayerFillsEveryPixel()
    {
        var layer = new StudioLayerSnapshot(
            StudioEffectKind.Solid, 100, StudioBlendMode.Normal,
            new StudioRgb(0x44, 0xD6, 0x2C), StudioRgb.Black, 1);

        var frame = StudioCompositor.Render([layer], 7, 16, TimeSpan.Zero);

        Assert.Equal(7 * 16, frame.Length);
        Assert.All(frame, color => Assert.Equal(new StudioRgb(0x44, 0xD6, 0x2C), color));
    }

    [Fact]
    public void OpacityBlendsAgainstBlack()
    {
        var layer = new StudioLayerSnapshot(
            StudioEffectKind.Solid, 50, StudioBlendMode.Normal,
            new StudioRgb(0x44, 0xD6, 0x2C), StudioRgb.Black, 1);

        var frame = StudioCompositor.Render([layer], 1, 1, TimeSpan.Zero);

        Assert.Equal(new StudioRgb(0x22, 0x6B, 0x16), frame[0]);
    }

    [Fact]
    public void AddBlendClampsToWhite()
    {
        var layers = new[]
        {
            new StudioLayerSnapshot(StudioEffectKind.Solid, 100, StudioBlendMode.Add,
                new StudioRgb(0x80, 0x80, 0x80), StudioRgb.Black, 1),
            new StudioLayerSnapshot(StudioEffectKind.Solid, 100, StudioBlendMode.Normal,
                new StudioRgb(0x80, 0x80, 0x80), StudioRgb.Black, 1),
        };

        var frame = StudioCompositor.Render(layers, 1, 1, TimeSpan.Zero);

        Assert.Equal(StudioRgb.White, frame[0]);
    }

    [Fact]
    public void TopLayerCoversTheLayerBelow()
    {
        var layers = new[]
        {
            new StudioLayerSnapshot(StudioEffectKind.Solid, 100, StudioBlendMode.Normal,
                new StudioRgb(0xFF, 0x00, 0x00), StudioRgb.Black, 1),
            new StudioLayerSnapshot(StudioEffectKind.Solid, 100, StudioBlendMode.Normal,
                new StudioRgb(0x00, 0xFF, 0x00), StudioRgb.Black, 1),
        };

        var frame = StudioCompositor.Render(layers, 1, 1, TimeSpan.Zero);

        Assert.Equal(new StudioRgb(0xFF, 0x00, 0x00), frame[0]);
    }

    [Fact]
    public void GradientUsesSecondaryAtTheRightEdge()
    {
        var layer = new StudioLayerSnapshot(
            StudioEffectKind.Gradient, 100, StudioBlendMode.Normal,
            StudioRgb.Black, StudioRgb.White, 1);

        var frame = StudioCompositor.Render([layer], 1, 16, TimeSpan.Zero);

        Assert.Equal(StudioRgb.Black, frame[0]);
        Assert.Equal(StudioRgb.White, frame[^1]);
    }

    [Fact]
    public void SpectrumChangesOverTime()
    {
        var layer = new StudioLayerSnapshot(
            StudioEffectKind.Spectrum, 100, StudioBlendMode.Normal,
            StudioRgb.Black, StudioRgb.Black, 1);

        var first = StudioCompositor.Render([layer], 1, 1, TimeSpan.Zero)[0];
        var later = StudioCompositor.Render([layer], 1, 1, TimeSpan.FromSeconds(5))[0];

        Assert.NotEqual(first, later);
    }

    [Fact]
    public void FireIsDeterministicForTheSameTime()
    {
        var layer = new StudioLayerSnapshot(
            StudioEffectKind.Fire, 100, StudioBlendMode.Normal,
            new StudioRgb(0xFF, 0x60, 0x00), StudioRgb.Black, 1);

        var first = StudioCompositor.Render([layer], 7, 16, TimeSpan.FromSeconds(1.25));
        var second = StudioCompositor.Render([layer], 7, 16, TimeSpan.FromSeconds(1.25));

        Assert.Equal(first, second);
    }

    [Fact]
    public void SnapshotSkipsDisabledLayersAndIsImmutable()
    {
        var document = new StudioDocument();
        var layer = new StudioLayer("底色") { Effect = StudioEffectKind.Solid };
        document.Layers.Add(layer);
        document.Layers.Add(new StudioLayer("关闭的图层") { Enabled = false });

        var snapshot = document.Snapshot();
        layer.Primary = StudioRgb.White;

        Assert.Single(snapshot);
        Assert.NotEqual(StudioRgb.White, snapshot[0].Primary);
    }

    [Fact]
    public void DocumentJsonRoundTrips()
    {
        var document = new StudioDocument { Name = "测试工作室" };
        document.Layers.Add(new StudioLayer("火焰")
        {
            Effect = StudioEffectKind.Fire,
            Opacity = 42,
            Blend = StudioBlendMode.Screen,
            Primary = new StudioRgb(0x11, 0x22, 0x33),
            Secondary = new StudioRgb(0x44, 0x55, 0x66),
            Speed = 2.5,
        });

        var json = StudioDocumentCodec.Serialize(document);
        var restored = StudioDocumentCodec.Deserialize(json);

        Assert.Equal("测试工作室", restored.Name);
        var layer = Assert.Single(restored.Layers);
        Assert.Equal(StudioEffectKind.Fire, layer.Effect);
        Assert.Equal(42, layer.Opacity);
        Assert.Equal(StudioBlendMode.Screen, layer.Blend);
        Assert.Equal(new StudioRgb(0x11, 0x22, 0x33), layer.Primary);
        Assert.Equal(new StudioRgb(0x44, 0x55, 0x66), layer.Secondary);
        Assert.Equal(2.5, layer.Speed);
        Assert.NotEqual(Guid.Empty, layer.Id);
    }

    [Fact]
    public void NewerSchemaIsRejected()
    {
        var json = """{"schemaVersion":99,"name":"future","layers":[]}""";

        Assert.Throws<InvalidOperationException>(() => StudioDocumentCodec.Deserialize(json));
    }
}
