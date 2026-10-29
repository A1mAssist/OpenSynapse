using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Macros;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class OnboardMacroCodecTests
{
    [Fact]
    public void KeyTapRoundTripsThroughHidUsage()
    {
        var bytes = RazerOnboardMacroCodec.Encode(
        [
            new OnboardMacroItem(OnboardMacroItemKind.Key) { Scancode = 0x1E },
            new OnboardMacroItem(OnboardMacroItemKind.Key) { Scancode = 0x1E, Released = true },
        ]);

        Assert.Equal(new byte[] { 0x01, 0x04, 0x02, 0x04 }, bytes);

        var items = RazerOnboardMacroCodec.Decode(bytes);
        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal(OnboardMacroItemKind.Key, item.Kind);
                Assert.Equal(0x1E, item.Scancode);
                Assert.False(item.Released);
            },
            item =>
            {
                Assert.Equal(OnboardMacroItemKind.Key, item.Kind);
                Assert.Equal(0x1E, item.Scancode);
                Assert.True(item.Released);
            });
    }

    [Fact]
    public void ButtonMaskDecodesIntoPressAndRelease()
    {
        var items = RazerOnboardMacroCodec.Decode([0x08, 0x01, 0x08, 0x00]);

        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal(OnboardMacroItemKind.MouseButton, item.Kind);
                Assert.Equal(1, item.MouseButton);
                Assert.True(item.MousePressed);
            },
            item =>
            {
                Assert.Equal(OnboardMacroItemKind.MouseButton, item.Kind);
                Assert.Equal(1, item.MouseButton);
                Assert.False(item.MousePressed);
            });
    }

    [Theory]
    [InlineData(0, new byte[] { 0x11, 0x00 })]
    [InlineData(255, new byte[] { 0x11, 0xFF })]
    [InlineData(256, new byte[] { 0x12, 0x01, 0x00 })]
    [InlineData(65535, new byte[] { 0x12, 0xFF, 0xFF })]
    [InlineData(65536, new byte[] { 0x13, 0x01, 0x00, 0x00 })]
    [InlineData(0xFFFFFF, new byte[] { 0x13, 0xFF, 0xFF, 0xFF })]
    public void DelayPicksTheNarrowestWidth(int milliseconds, byte[] expected)
    {
        var bytes = RazerOnboardMacroCodec.Encode(
            [new OnboardMacroItem(OnboardMacroItemKind.Delay) { DelayMilliseconds = milliseconds }]);

        Assert.Equal(expected, bytes);
        Assert.Equal(
            milliseconds,
            RazerOnboardMacroCodec.Decode(bytes)[0].DelayMilliseconds);
    }

    [Fact]
    public void CursorKeepsBothAxes()
    {
        var items = RazerOnboardMacroCodec.Decode([0x15, 0x01, 0x2C, 0x00, 0x7B]);

        var cursor = Assert.Single(items);
        Assert.Equal(OnboardMacroItemKind.Cursor, cursor.Kind);
        Assert.Equal(300, cursor.X);
        Assert.Equal(123, cursor.Y);
    }

    [Fact]
    public void UnknownOpcodeStopsInsteadOfGuessingALength()
    {
        Assert.Throws<InvalidDataException>(() => RazerOnboardMacroCodec.Decode([0x05, 0x00]));
    }

    [Fact]
    public void TruncatedItemIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => RazerOnboardMacroCodec.Decode([0x15, 0x01]));
    }

    [Fact]
    public void StreamRoundTrips()
    {
        OnboardMacroItem[] items =
        [
            new(OnboardMacroItemKind.Key) { Scancode = 0x1E },
            new(OnboardMacroItemKind.Delay) { DelayMilliseconds = 40 },
            new(OnboardMacroItemKind.Key) { Scancode = 0x1E, Released = true },
            new(OnboardMacroItemKind.MouseButton) { MouseButton = 3, MousePressed = true },
            new(OnboardMacroItemKind.MouseButton) { MouseButton = 3, MousePressed = false },
            new(OnboardMacroItemKind.Wheel) { WheelDelta = 120 },
            new(OnboardMacroItemKind.Cursor) { X = 1920, Y = 1080 },
        ];

        var decoded = RazerOnboardMacroCodec.Decode(RazerOnboardMacroCodec.Encode(items));
        Assert.Equal(items.Length, decoded.Count);
        Assert.Equal(items[^1].X, decoded[^1].X);
        Assert.Equal(items[^1].Y, decoded[^1].Y);
    }
}

public sealed class OnboardMacroProtocolTests
{
    [Theory]
    [InlineData(0, 256)]
    [InlineData(1, 512)]
    [InlineData(255, 512)]
    [InlineData(256, 512)]
    [InlineData(257, 768)]
    [InlineData(720, 1024)]
    public void AllocationRoundsUpToWholeBlocks(int dataSize, int expected)
    {
        Assert.Equal(expected, RazerOnboardMacroProtocol.RequiredAllocation(dataSize));
    }

    [Fact]
    public void MemoryChunkEchoesOffsetAndSize()
    {
        var response = new byte[RazerFeatureReport.Length];
        var arguments = response.AsSpan(RazerFeatureReport.ArgumentsOffset);
        arguments[0] = 0;
        arguments[1] = 1;
        arguments[2] = 0;
        arguments[3] = 0;
        arguments[4] = 0;
        arguments[5] = 72;
        arguments[6] = 3;
        arguments[7] = 0x01;
        arguments[8] = 0x04;
        arguments[9] = 0x02;

        var chunk = RazerOnboardMacroProtocol.ParseMemoryChunk(response);
        Assert.Equal(72, chunk.Offset);
        Assert.Equal(new byte[] { 0x01, 0x04, 0x02 }, chunk.Data);
    }

    [Fact]
    public void ReadAndWriteArgumentsCarryTheSevenByteHeader()
    {
        Assert.Equal(
            new byte[] { 0, 3, 0, 0, 0, 72, 24 },
            RazerOnboardMacroProtocol.CreateReadMemoryArguments(3, 72, 24));

        Assert.Equal(
            new byte[] { 0, 3, 0, 0, 0, 72, 2, 0x01, 0x04 },
            RazerOnboardMacroProtocol.CreateWriteMemoryArguments(3, 72, [0x01, 0x04]));
    }

    [Fact]
    public void StorageInfoSplitsFourCounters()
    {
        var response = new byte[RazerFeatureReport.Length];
        var arguments = response.AsSpan(RazerFeatureReport.ArgumentsOffset);
        arguments[1] = 5;
        arguments[2] = 0x00;
        arguments[3] = 0x00;
        arguments[4] = 0x10;
        arguments[5] = 0x00;
        arguments[6] = 0;
        arguments[7] = 0;
        arguments[8] = 0x04;
        arguments[9] = 0x00;
        arguments[10] = 0;
        arguments[11] = 0;
        arguments[12] = 0x02;
        arguments[13] = 0x00;

        var info = RazerOnboardMacroProtocol.ParseStorageInfo(response);
        Assert.Equal(5, info.MaxId);
        Assert.Equal(4096, info.MaxMemory);
        Assert.Equal(1024, info.AvailableMemory);
        Assert.Equal(512, info.RecyclableMemory);
    }

    [Fact]
    public void AutoReleaseStepBecomesDownHoldUp()
    {
        var steps = new List<MacroStep>
        {
            new()
            {
                Kind = MacroStepKind.Input,
                Input = new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0x41, ScanCode = 0x1E },
                IsDown = true,
                AutoRelease = true,
                HoldMilliseconds = 40,
            },
        };

        var items = OnboardMacroTranslation.FromSteps(steps);
        Assert.Collection(
            items,
            item => Assert.False(item.Released),
            item => Assert.Equal(40, item.DelayMilliseconds),
            item => Assert.True(item.Released));
    }

    [Fact]
    public void PlainUpStepBecomesOnlyARelease()
    {
        var steps = new List<MacroStep>
        {
            new()
            {
                Kind = MacroStepKind.Input,
                Input = new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0x41, ScanCode = 0x1E },
                IsDown = false,
            },
        };

        var item = Assert.Single(OnboardMacroTranslation.FromSteps(steps));
        Assert.True(item.Released);
    }

    [Fact]
    public void HeldStepKeepsItsOwnRelease()
    {
        var steps = new List<MacroStep>
        {
            new()
            {
                Kind = MacroStepKind.Input,
                Input = new MacroInput { Kind = MacroInputKind.MouseButton, MouseButton = 4 },
                IsDown = true,
                AutoRelease = false,
            },
        };

        var item = Assert.Single(OnboardMacroTranslation.FromSteps(steps));
        Assert.Equal(4, item.MouseButton);
        Assert.True(item.MousePressed);
    }

    [Fact]
    public void StepsTheSlotCannotHoldAreRejected()
    {
        var steps = new List<MacroStep> { new() { Kind = MacroStepKind.Text, Text = "hello" } };
        var exception = Assert.Throws<OnboardMacroUnsupportedException>(
            () => OnboardMacroTranslation.FromSteps(steps));
        Assert.Equal(1, exception.StepIndex);
    }

    [Fact]
    public void MacroIdListReadsTwoByteIdsAfterTheHeader()
    {
        var response = new byte[RazerFeatureReport.Length];
        var arguments = response.AsSpan(RazerFeatureReport.ArgumentsOffset);
        arguments[3] = 3;
        arguments[4] = 0x00;
        arguments[5] = 0x02;
        arguments[6] = 0x00;
        arguments[7] = 0x05;
        arguments[8] = 0x00;
        arguments[9] = 0x09;

        var (total, ids) = RazerOnboardMacroProtocol.ParseMacroIdList(response);
        Assert.Equal(3, total);
        Assert.Equal(new byte[] { 2, 5, 9 }, ids);
    }

    [Fact]
    public void AllocationArgumentsUseTheSameIdAndSizeHeader()
    {
        Assert.Equal(
            new byte[] { 0, 2, 0, 0, 2, 0 },
            RazerOnboardMacroProtocol.CreateAllocateArguments(2, 512));
    }

    [Fact]
    public void DataSizeIsReadFromTheTrailingWord()
    {
        var response = new byte[RazerFeatureReport.Length];
        var arguments = response.AsSpan(RazerFeatureReport.ArgumentsOffset);
        arguments[5] = 0x2C;
        Assert.Equal(44, RazerOnboardMacroProtocol.ParseMacroDataSize(response));
    }
}

