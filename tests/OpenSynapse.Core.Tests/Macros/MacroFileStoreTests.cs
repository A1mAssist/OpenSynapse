using OpenSynapse.Core.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroFileStoreTests
{
    [Fact]
    public async Task MacroRoundTripsThroughAStandaloneFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-macro-{Guid.NewGuid():N}.json");
        try
        {
            var macro = new MacroDefinition
            {
                Name = "Combo",
                Trigger = new MacroTrigger
                {
                    Input = new MacroInput
                    {
                        Kind = MacroInputKind.Keyboard,
                        VirtualKey = 0x4D,
                    },
                    Modifiers = MacroModifierKeys.Control | MacroModifierKeys.Shift,
                    Mode = MacroTriggerMode.Toggle,
                },
                PlaybackSpeed = 1.5,
                Steps =
                [
                    new MacroStep
                    {
                        Kind = MacroStepKind.Input,
                        Input = new MacroInput
                        {
                            Kind = MacroInputKind.Keyboard,
                            VirtualKey = 0x41,
                        },
                        IsDown = true,
                        AutoRelease = true,
                    },
                ],
            };

            await MacroFileStore.ExportAsync(macro, path);
            var loaded = await MacroFileStore.ImportAsync(path);

            Assert.Equal(macro.Name, loaded.Name);
            Assert.Equal(MacroModifierKeys.Control | MacroModifierKeys.Shift, loaded.Trigger!.Modifiers);
            Assert.Equal(MacroTriggerMode.Toggle, loaded.Trigger.Mode);
            Assert.Equal(1.5, loaded.PlaybackSpeed);
            Assert.Single(loaded.Steps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReferencedMacroBundleRoundTripsAsOneFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-bundle-{Guid.NewGuid():N}.json");
        try
        {
            var referenced = new MacroDefinition
            {
                Id = "referenced",
                Name = "Referenced",
                Steps =
                [
                    new MacroStep
                    {
                        Kind = MacroStepKind.Input,
                        Input = new MacroInput
                        {
                            Kind = MacroInputKind.Keyboard,
                            VirtualKey = 0x42,
                        },
                        IsDown = true,
                        AutoRelease = true,
                    },
                ],
            };
            var root = new MacroDefinition
            {
                Id = "root",
                Name = "Root",
                Steps =
                [
                    new MacroStep
                    {
                        Kind = MacroStepKind.MacroReference,
                        MacroId = "referenced",
                    },
                ],
            };

            await MacroFileStore.ExportAsync(
                root,
                new Dictionary<string, MacroDefinition>
                {
                    [referenced.Id] = referenced,
                    [root.Id] = root,
                },
                path);
            var bundle = await MacroFileStore.ImportBundleAsync(path);

            Assert.Equal(2, bundle.Count);
            Assert.Equal(root.Id, bundle[0].Id);
            Assert.Equal("referenced", bundle[0].Steps[0].MacroId);
            Assert.Equal(referenced.Id, bundle[1].Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IncompleteStepIsRejectedInsteadOfSilentlyDropped()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-incomplete-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "version": 1,
                  "macro": {
                    "name": "Incomplete",
                    "steps": [
                      { "kind": "Text", "text": "" },
                      { "kind": "Delay", "delayMilliseconds": 10 }
                    ]
                  }
                }
                """);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => MacroFileStore.ImportAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EmptyMacroFileRoundTrips()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-empty-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"version":1,"macro":{"name":"Empty","steps":[]}}""");
            var macro = await MacroFileStore.ImportAsync(path);

            Assert.Equal("Empty", macro.Name);
            Assert.Empty(macro.Steps);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
