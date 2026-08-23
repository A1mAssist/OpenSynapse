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
    public async Task ExternalFrameSourceSkipsDuplicateFramesAndTracksDiagnostics()
    {
        var source = new ChromaExternalFrameSource();
        var frame = Enumerable.Repeat(
            new RazerRgb(1, 2, 3),
            QuickLightingEngine.PixelCount).ToArray();

        Assert.True(source.Publish(frame));
        Assert.False(source.Publish(frame));

        Assert.Equal(1, source.PublishedFrames);
        Assert.Equal(1, source.DuplicateFrames);
        Assert.Equal(1, source.Version);
        Assert.NotNull(source.LastPublishedAt);
        Assert.Equal(frame, await source.RenderAsync(TimeSpan.Zero, CancellationToken.None));
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
        keys[0][1] = 0x01000000u | (~0x000000FFu);

        var frame = ChromaKeyboardFrameMapper.CustomKey(colors, keys);
        Assert.Equal(new RazerRgb(0xFF, 0x00, 0x00), frame[1]);
    }

    [Fact]
    public void ChromaCoordinatesCoverTheBladePhysicalKeyboardLayout()
    {
        AssertMapping(0, 1, 1, "Esc");
        for (var index = 0; index < 12; index++)
        {
            AssertMapping(0, 3 + index, 2 + index, $"F{index + 1}");
        }
        AssertMapping(0, 17, 16, "Power");

        AssertMapping(1, 1, 18, "Grave");
        for (var index = 0; index < 12; index++)
        {
            AssertMapping(1, 2 + index, 19 + index, $"Number row {index}");
        }
        AssertMapping(1, 14, 32, "Backspace");

        AssertMapping(2, 1, 35, "Tab");
        for (var index = 0; index < 12; index++)
        {
            AssertMapping(2, 2 + index, 36 + index, $"Q row {index}");
        }
        AssertMapping(2, 14, 49, "Backslash");
        AssertMapping(2, 15, 14, "Insert");
        AssertMapping(2, 17, 33, "Page Up/M1");

        AssertMapping(3, 1, 52, "Caps Lock");
        for (var index = 0; index < 11; index++)
        {
            AssertMapping(3, 2 + index, 53 + index, $"Home row {index}");
        }
        AssertMapping(3, 14, 66, "Enter");
        AssertMapping(3, 15, 15, "Delete");
        AssertMapping(3, 17, 50, "Page Down/M2");
        AssertMapping(3, 21, 67, "M3");

        AssertMapping(4, 1, 69, "Left Shift");
        for (var index = 0; index < 10; index++)
        {
            AssertMapping(4, 3 + index, 71 + index, $"Z row {index}");
        }
        AssertMapping(4, 14, 83, "Right Shift");
        AssertMapping(4, 16, 98, "Up");
        AssertMapping(4, 21, 84, "M4");

        AssertMapping(5, 1, 86, "Left Ctrl");
        AssertMapping(5, 2, 88, "Windows");
        AssertMapping(5, 3, 90, "Left Alt");
        AssertMapping(5, 11, 94, "Right Alt");
        AssertMapping(5, 12, 87, "Fn");
        AssertMapping(5, 13, 95, "Copilot");
        AssertMapping(5, 14, 96, "Right Ctrl");
        AssertMapping(5, 15, 97, "Left");
        AssertMapping(5, 16, 100, "Down");
        AssertMapping(5, 17, 99, "Right");
        AssertMapping(5, 21, 101, "M5");

        static void AssertMapping(int row, int column, int deviceIndex, string name)
        {
            var matrix = Enumerable.Range(0, 6)
                .Select(_ => Enumerable.Repeat(0u, 22).ToList())
                .ToList();
            matrix[row][column] = 0x000000FFu;

            var frame = ChromaKeyboardFrameMapper.Custom(matrix);

            Assert.Equal(new RazerRgb(0xFF, 0, 0), frame[deviceIndex]);
            Assert.True(frame.Count(color => color != default) == 1, name);
        }
    }

    [Fact]
    public void SixRowChromaCanvasDoesNotShiftRowsIntoTheSeventhLogicalRow()
    {
        var matrix = Enumerable.Range(0, 6)
            .Select(row => Enumerable.Repeat(row == 5 ? 0x000000FFu : 0u, 22).ToList())
            .ToList();

        var frame = ChromaKeyboardFrameMapper.Custom(matrix);

        Assert.Equal(new RazerRgb(0xFF, 0, 0), frame[86]);
        Assert.Equal(new RazerRgb(0xFF, 0, 0), frame[100]);
        Assert.Equal(default, frame[91]);
    }

    [Fact]
    public void SpaceCoordinateRemainsDarkBecauseTheBladeSpacebarHasNoLed()
    {
        var matrix = Enumerable.Range(0, 6)
            .Select(_ => Enumerable.Repeat(0u, 22).ToList())
            .ToList();
        matrix[5][7] = 0x000000FFu;

        var frame = ChromaKeyboardFrameMapper.Custom(matrix);

        Assert.All(frame, color => Assert.Equal(default, color));
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
    public void Custom2UsesTheSamePhysicalKeyMapWithItsOneCellBorder()
    {
        var colors = Enumerable.Range(0, 8)
            .Select(_ => Enumerable.Repeat(0u, 24).ToList())
            .ToList();
        colors[1][4] = 0x000000FFu;
        colors[6][17] = 0x0000FF00u;

        var frame = ChromaKeyboardFrameMapper.Custom2(colors);

        Assert.Equal(new RazerRgb(0xFF, 0, 0), frame[2]);
        Assert.Equal(new RazerRgb(0, 0xFF, 0), frame[100]);
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
