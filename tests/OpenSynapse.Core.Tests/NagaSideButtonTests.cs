using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class NagaSideButtonTests
{
    [Theory]
    [InlineData(0x31, 0x02, 1)]
    [InlineData(0x32, 0x03, 2)]
    [InlineData(0x39, 0x0A, 9)]
    [InlineData(0x30, 0x0B, 10)]
    [InlineData(0x7B, 0x58, 11)]
    [InlineData(0x2C, 0x37, 12)]
    [InlineData(0, 0x02, 1)]
    [InlineData(0xFF, 0x58, 11)]
    [InlineData(0x41, 0x1E, 0)]
    [InlineData(0x6A, 0x37, 0)]
    public void MapsTheNagaSidePlateKeyUsages(int virtualKey, int scanCode, int expected)
    {
        Assert.Equal(expected, RazerNagaSideButtons.FromKeyboard(virtualKey, scanCode));
    }

    [Fact]
    public void DetectsNagaProductsByPidOrProductName()
    {
        Assert.True(RazerNagaSideButtons.IsNaga(0x1532, 0x0092, null));
        Assert.True(RazerNagaSideButtons.IsNaga(0x1532, 0xFFFF, "Razer Naga Pro"));
        Assert.False(RazerNagaSideButtons.IsNaga(0x1532, 0x026C, "Razer Huntsman V2"));
        Assert.False(RazerNagaSideButtons.IsNaga(0x046D, 0x0092, "Razer Naga"));
    }

    [Fact]
    public void NagaSideButtonIsAValidTriggerButNotAPlaybackStep()
    {
        var input = new MacroInput
        {
            Kind = MacroInputKind.NagaSideButton,
            SideButton = 7,
        };

        Assert.False(input.IsValid(allowPrimaryMouseButton: false, allowWheel: false));
        Assert.True(input.IsValid(
            allowPrimaryMouseButton: false,
            allowWheel: false,
            allowNagaSideButton: true));

        var trigger = new MacroTrigger { Input = input };
        Assert.True(trigger.Matches(
            new MacroInput { Kind = MacroInputKind.NagaSideButton, SideButton = 7 },
            MacroModifierKeys.None));
        Assert.False(trigger.Matches(
            new MacroInput { Kind = MacroInputKind.NagaSideButton, SideButton = 8 },
            MacroModifierKeys.None));
        Assert.False(trigger.Matches(
            new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0x37 },
            MacroModifierKeys.None));

        var macro = new MacroDefinition { Trigger = trigger };
        macro.ApplySafeDefaults();
        Assert.NotNull(macro.Trigger);
    }

    [Fact]
    public async Task NagaTriggerRoundTripsThroughMacroFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"opensynapse-naga-trigger-{Guid.NewGuid():N}.json");
        try
        {
            var macro = new MacroDefinition
            {
                Name = "Naga",
                Trigger = new MacroTrigger
                {
                    Input = new MacroInput
                    {
                        Kind = MacroInputKind.NagaSideButton,
                        SideButton = 12,
                    },
                },
                Steps =
                [
                    new MacroStep
                    {
                        Kind = MacroStepKind.Text,
                        Text = "ok",
                    },
                ],
            };

            await MacroFileStore.ExportAsync(macro, path);
            var loaded = await MacroFileStore.ImportAsync(path);

            Assert.Equal(MacroInputKind.NagaSideButton, loaded.Trigger!.Input.Kind);
            Assert.Equal(12, loaded.Trigger.Input.SideButton);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
