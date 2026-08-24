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
    public void ChromaSourceCoordinatesMapToTheBladePhysicalKeyboardLayout()
    {
        AssertMapping(0, 1, 1, "Esc");
        for (var index = 0; index < 12; index++)
        {
            AssertMapping(0, 3 + index, 2 + index, $"F{index + 1}");
        }
        AssertMapping(2, 15, 14, "Insert on physical row 0");
        AssertMapping(3, 15, 15, "Delete on physical row 0");
        AssertMapping(0, 17, 16, "Power on physical row 0");

        AssertMapping(1, 1, 18, "Grave");
        for (var index = 0; index < 12; index++)
        {
            AssertMapping(1, 2 + index, 19 + index, $"Number row {index}");
        }
        AssertMapping(1, 14, 32, "Backspace on physical row 1");
        AssertMapping(2, 17, 33, "Page Up/M1 on physical row 1");

        AssertMapping(2, 1, 35, "Tab");
        for (var index = 0; index < 12; index++)
        {
            AssertMapping(2, 2 + index, 36 + index, $"Q row {index}");
        }
        AssertMapping(2, 14, 49, "Backslash");
        AssertMapping(3, 17, 50, "Page Down/M2 on physical row 2");

        AssertMapping(3, 1, 52, "Caps Lock");
        for (var index = 0; index < 11; index++)
        {
            AssertMapping(3, 2 + index, 53 + index, $"Home row {index}");
        }
        AssertMapping(3, 14, 66, "Enter");
        AssertMapping(3, 21, 67, "M3 after Enter on physical row 3");

        AssertMapping(4, 1, 69, "Left Shift");
        for (var index = 0; index < 10; index++)
        {
            AssertMapping(4, 3 + index, 71 + index, $"Z row {index}");
        }
        AssertMapping(4, 14, 83, "Right Shift");
        AssertMapping(4, 21, 84, "M4");

        AssertMapping(5, 1, 86, "Left Ctrl on physical row 5");
        AssertMapping(5, 12, 87, "Fn after Left Ctrl on physical row 5");
        AssertMapping(5, 2, 88, "Windows after Fn on physical row 5");
        AssertMapping(5, 3, 90, "Left Alt on physical row 5");
        AssertMapping(5, 11, 94, "Right Alt after the unlit Space on physical row 5");
        AssertMapping(5, 13, 95, "Copilot");
        AssertMapping(5, 14, 96, "Right Ctrl");
        AssertMapping(5, 15, 97, "Left");
        AssertMapping(4, 16, 98, "Up in the stacked physical row 5 arrow slot");
        AssertMapping(5, 16, 100, "Down in the stacked physical row 5 arrow slot");
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
    public async Task RippleDirectionScanCodesUseTheBottomLogicalArrowPositions()
    {
        await using var adapter = new WindowsKeyboardLightingAdapter();
        var at = TimeSpan.Zero;

        AssertKey(0x4B, (5, 12), "Left");
        AssertKey(0x48, (5, 13), "Up");
        AssertKey(0x4D, (5, 14), "Right");
        AssertKey(0x50, (6, 13), "Down");

        void AssertKey(uint scanCode, (int Row, int Column) expected, string name)
        {
            Assert.True(adapter.TryTranslate(
                scanCode,
                WindowsKeyboardLightingAdapter.ExtendedFlag,
                at += TimeSpan.FromMilliseconds(25),
                out var keyEvent), name);
            Assert.Equal(expected.Row, keyEvent.Row);
            Assert.Equal(expected.Column, keyEvent.Column);
        }
    }

    [Fact]
    public async Task FilterDriverInputsReachRippleAndReactiveSpecialKeyPositions()
    {
        await using var adapter = new WindowsKeyboardLightingAdapter();

        AssertInput(new(BladeMappingInputKind.RazerKey, 0x0A, true), (1, 15), "M1");
        AssertInput(new(BladeMappingInputKind.RazerKey, 0x0B, true), (2, 15), "M2");
        AssertInput(new(BladeMappingInputKind.RazerKey, 0x03, true), (3, 15), "M3");
        AssertInput(new(BladeMappingInputKind.RazerKey, 0xD3, true), (4, 15), "M4");
        AssertInput(new(BladeMappingInputKind.RazerKey, 0xD4, true), (5, 15), "M5");
        AssertInput(new(BladeMappingInputKind.Keyboard, 0x4B, true, true), (5, 12), "Left");
        AssertInput(new(BladeMappingInputKind.Keyboard, 0x48, true, true), (5, 13), "Up");
        AssertInput(new(BladeMappingInputKind.Keyboard, 0x4D, true, true), (5, 14), "Right");
        AssertInput(new(BladeMappingInputKind.Keyboard, 0x50, true, true), (6, 13), "Down");

        void AssertInput(BladeMappingInputEvent input, (int Row, int Column) expected, string name)
        {
            adapter.ObserveMappingInput(input);
            var events = new List<QuickLightingKeyEvent>();
            adapter.DrainTo(events);
            var keyEvent = Assert.Single(events);
            Assert.Equal(expected.Row, keyEvent.Row);
            Assert.Equal(expected.Column, keyEvent.Column);
        }
    }

    [Fact]
    public async Task PowerKeyUsesTheTopRightLogicalPosition()
    {
        await using var adapter = new WindowsKeyboardLightingAdapter();

        Assert.True(adapter.TryTranslate(
            0x5E,
            WindowsKeyboardLightingAdapter.ExtendedFlag,
            TimeSpan.Zero,
            out var keyEvent));
        Assert.Equal((0, 15), (keyEvent.Row, keyEvent.Column));
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
