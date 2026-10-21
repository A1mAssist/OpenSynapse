using OpenSynapse.Core.Macros;
using OpenSynapse.Core.Profiles;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroTests
{
    [Fact]
    public void RecordingSessionPairsPressAndReleaseIntoOneStep()
    {
        var session = new MacroRecordingSession();
        var key = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = 0x41,
            ScanCode = 0x1E,
        };

        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000));
        Assert.True(session.TryAdd(key, isDown: false, timestampMilliseconds: 1_250));

        var step = Assert.Single(session.Snapshot());
        Assert.True(step.IsDown);
        Assert.Equal(250, step.HoldMilliseconds);
    }

    [Fact]
    public void RecordingSessionUsesElapsedTimeAndClampsLargeGaps()
    {
        var session = new MacroRecordingSession();
        var key = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = 0x41,
            ScanCode = 0x1E,
        };

        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000));
        Assert.True(session.TryAdd(key, isDown: false, timestampMilliseconds: 1_250));
        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 200_000));

        var steps = session.Snapshot();
        Assert.Equal(2, steps.Count);
        Assert.Equal(0, steps[0].DelayMilliseconds);
        Assert.Equal(250, steps[0].HoldMilliseconds);
        Assert.Equal(MacroDefinition.MaxDelayMilliseconds, steps[1].DelayMilliseconds);
    }

    [Fact]
    public void MacroDefinitionDropsInvalidTriggerAndNormalizesSteps()
    {
        var macro = new MacroDefinition
        {
            Name = "  Example  ",
            Trigger = new MacroTrigger
            {
                Input = new MacroInput
                {
                    Kind = MacroInputKind.MouseButton,
                    MouseButton = 1,
                },
            },
            Steps =
            [
                new MacroStep
                {
                    Input = new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0 },
                    DelayMilliseconds = 10,
                },
                new MacroStep
                {
                    Input = new MacroInput
                    {
                        Kind = MacroInputKind.MouseButton,
                        MouseButton = 4,
                    },
                    DelayMilliseconds = 70_000,
                },
            ],
        };

        macro.ApplySafeDefaults();

        Assert.Equal("Example", macro.Name);
        Assert.Null(macro.Trigger);
        Assert.Single(macro.Steps);
        Assert.Equal(4, macro.Steps[0].Input!.MouseButton);
        Assert.Equal(MacroDefinition.MaxDelayMilliseconds, macro.Steps[0].DelayMilliseconds);
    }

    [Fact]
    public void MacroProfileCloneKeepsIndependentMacroCopies()
    {
        var document = ProfileDocument.CreateDefault();
        var macro = new MacroDefinition
        {
            Id = "macro-1",
            Name = "Copy",
            Trigger = new MacroTrigger
            {
                Input = new MacroInput
                {
                    Kind = MacroInputKind.MouseButton,
                    MouseButton = 5,
                },
            },
            Steps =
            [
                new MacroStep
                {
                    Input = new MacroInput
                    {
                        Kind = MacroInputKind.Keyboard,
                        VirtualKey = 0x41,
                        ScanCode = 0x1E,
                    },
                    IsDown = true,
                },
            ],
        };
        document.Profiles[ProfileCatalog.DefaultProfileName].Macros.Add(macro);

        var clone = document.Clone();

        var cloned = Assert.Single(clone.Profiles[ProfileCatalog.DefaultProfileName].Macros);
        Assert.NotSame(macro, cloned);
        Assert.NotSame(macro.Trigger, cloned.Trigger);
        Assert.NotSame(macro.Steps[0], cloned.Steps[0]);
        Assert.Equal(macro.Name, cloned.Name);
    }

    [Fact]
    public async Task ProfileStoreRoundTripsMacros()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-macro-{Guid.NewGuid():N}.json");
        try
        {
            var document = ProfileDocument.CreateDefault();
            document.Profiles[ProfileCatalog.DefaultProfileName].Macros.Add(new MacroDefinition
            {
                Id = "macro-roundtrip",
                Name = "Round trip",
                PlaybackMode = MacroPlaybackMode.Repeat,
                RepeatCount = 3,
                Trigger = new MacroTrigger
                {
                    Input = new MacroInput
                    {
                        Kind = MacroInputKind.Keyboard,
                        VirtualKey = 0x41,
                        ScanCode = 0x1E,
                    },
                },
                Steps =
                [
                    new MacroStep
                    {
                        Input = new MacroInput
                        {
                            Kind = MacroInputKind.Keyboard,
                            VirtualKey = 0x41,
                            ScanCode = 0x1E,
                        },
                        IsDown = true,
                    },
                ],
            });

            await ProfileStore.ExportAsync(document, path);
            var loaded = await new ProfileStore(path).LoadAsync();

            var macro = Assert.Single(
                loaded.Profiles[ProfileCatalog.DefaultProfileName].Macros);
            Assert.Equal("macro-roundtrip", macro.Id);
            Assert.Equal("Round trip", macro.Name);
            Assert.Equal(MacroPlaybackMode.Repeat, macro.PlaybackMode);
            Assert.Equal(3, macro.RepeatCount);
            Assert.Equal(MacroInputKind.Keyboard, macro.Trigger!.Input.Kind);
            Assert.Equal(0x41, macro.Trigger.Input.VirtualKey);
            Assert.Single(macro.Steps);
            Assert.True(macro.Steps[0].IsDown);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void KeyboardMatchIncludesExtendedFlag()
    {
        var left = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = 0x25,
            Extended = false,
        };
        var right = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = 0x25,
            Extended = true,
        };

        Assert.True(left.Matches(left.Clone()));
        Assert.False(left.Matches(right));
    }

    [Fact]
    public void RecordingSessionCanIgnoreDelays()
    {
        var session = new MacroRecordingSession(recordDelays: false);
        var key = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = 0x41,
            ScanCode = 0x1E,
        };

        Assert.True(session.TryAdd(key, isDown: true, timestampMilliseconds: 1_000));
        Assert.True(session.TryAdd(key, isDown: false, timestampMilliseconds: 5_000));

        Assert.All(session.Snapshot(), step => Assert.Equal(0, step.DelayMilliseconds));
    }

    [Fact]
    public void DelayStepSurvivesDefaultsAndRepeatCountIsClamped()
    {
        var macro = new MacroDefinition
        {
            PlaybackMode = MacroPlaybackMode.Repeat,
            RepeatCount = 10_000,
            Steps =
            [
                new MacroStep
                {
                    Kind = MacroStepKind.Delay,
                    DelayMilliseconds = 70_000,
                },
                new MacroStep
                {
                    Input = new MacroInput(),
                },
            ],
        };

        macro.ApplySafeDefaults();

        Assert.Equal(MacroPlaybackMode.Repeat, macro.PlaybackMode);
        Assert.Equal(MacroDefinition.MaxRepeatCount, macro.RepeatCount);
        var step = Assert.Single(macro.Steps);
        Assert.Equal(MacroStepKind.Delay, step.Kind);
        Assert.Equal(MacroDefinition.MaxDelayMilliseconds, step.DelayMilliseconds);
        Assert.Null(step.Input);
    }

    [Fact]
    public void MouseWheelIsValidForStepsButNotTriggers()
    {
        var wheel = new MacroInput
        {
            Kind = MacroInputKind.MouseWheel,
            WheelDelta = 120,
        };

        Assert.True(wheel.IsValid(allowPrimaryMouseButton: true));
        Assert.False(wheel.IsValid(allowPrimaryMouseButton: false, allowWheel: false));
    }

    [Fact]
    public void MacroKeyBoardFitsEverySlotInsideItsGrid()
    {
        var board = MacroKeyCatalog.Board;

        Assert.All(board.Slots, slot =>
        {
            Assert.InRange(slot.Column, 0, board.Columns - slot.ColumnSpan);
            Assert.InRange(slot.Row, 0, board.Rows - slot.RowSpan);
        });
    }

    [Fact]
    public void MacroKeyBoardSlotsDoNotOverlap()
    {
        var board = MacroKeyCatalog.Board;

        for (var first = 0; first < board.Slots.Count; first++)
        {
            for (var second = first + 1; second < board.Slots.Count; second++)
            {
                var a = board.Slots[first];
                var b = board.Slots[second];
                var columnsOverlap = a.Column < b.Column + b.ColumnSpan && b.Column < a.Column + a.ColumnSpan;
                var rowsOverlap = a.Row < b.Row + b.RowSpan && b.Row < a.Row + a.RowSpan;
                Assert.False(columnsOverlap && rowsOverlap, $"{a.Key.Label} overlaps {b.Key.Label}.");
            }
        }
    }
}