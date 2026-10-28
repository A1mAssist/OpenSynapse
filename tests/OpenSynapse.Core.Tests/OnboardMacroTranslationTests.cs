using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Macros;
using Xunit;

namespace OpenSynapse.Core.Tests;

/// <summary>
/// Onboard streams hold keys, mouse buttons, one vertical wheel notch and delays. These
/// tests pin the encodings that must not drift and the step numbers the UI reports when a
/// local macro cannot be stored.
/// </summary>
public sealed class OnboardMacroTranslationTests
{
    [Fact]
    public void HorizontalWheelIsRejectedAtItsStepNumber()
    {
        var steps = new[]
        {
            KeyStep(0x1E, isDown: true),
            new MacroStep
            {
                Kind = MacroStepKind.Input,
                Input = new MacroInput
                {
                    Kind = MacroInputKind.MouseWheel,
                    WheelDelta = 120,
                    WheelHorizontal = true,
                },
                IsDown = true,
            },
        };

        var exception = Assert.Throws<OnboardMacroUnsupportedException>(
            () => OnboardMacroTranslation.FromSteps(steps));

        Assert.Equal(2, exception.StepIndex);
    }

    [Theory]
    [InlineData(120, new byte[] { 0x0A, 0x01 })]
    [InlineData(-120, new byte[] { 0x0A, 0xFF })]
    public void VerticalWheelKeepsTheNotchEncoding(int delta, byte[] expected)
    {
        var steps = new[]
        {
            new MacroStep
            {
                Kind = MacroStepKind.Input,
                Input = new MacroInput { Kind = MacroInputKind.MouseWheel, WheelDelta = delta },
                IsDown = true,
            },
        };

        var items = OnboardMacroTranslation.FromSteps(steps);

        Assert.Equal(expected, RazerOnboardMacroCodec.Encode(items));
    }

    [Fact]
    public void KeyAndDelaySequenceKeepsTheEstablishedBytes()
    {
        var steps = new[]
        {
            KeyStep(0x1E, isDown: true),
            new MacroStep { Kind = MacroStepKind.Delay, DelayMilliseconds = 100 },
            KeyStep(0x1E, isDown: false),
        };

        var items = OnboardMacroTranslation.FromSteps(steps);

        Assert.Equal(
            new byte[] { 0x01, 0x04, 0x11, 0x64, 0x02, 0x04 },
            RazerOnboardMacroCodec.Encode(items));
    }

    [Theory]
    [InlineData(MacroInputKind.NagaSideButton)]
    [InlineData(MacroInputKind.RazerKey)]
    public void DeviceSpecificInputsAreRejectedAtTheirStepNumber(MacroInputKind kind)
    {
        var steps = new[]
        {
            new MacroStep
            {
                Kind = MacroStepKind.Input,
                Input = new MacroInput
                {
                    Kind = kind,
                    SideButton = 1,
                    DeviceId = 1,
                    RazerKeyCode = 1,
                },
                IsDown = true,
                AutoRelease = false,
            },
        };

        var exception = Assert.Throws<OnboardMacroUnsupportedException>(
            () => OnboardMacroTranslation.FromSteps(steps));

        Assert.Equal(1, exception.StepIndex);
    }

    [Fact]
    public void TextStepFailsAtStepOne()
    {
        var steps = new[] { new MacroStep { Kind = MacroStepKind.Text, Text = "hi" } };

        var exception = Assert.Throws<OnboardMacroUnsupportedException>(
            () => OnboardMacroTranslation.FromSteps(steps));

        Assert.Equal(1, exception.StepIndex);
    }

    private static MacroStep KeyStep(int scanCode, bool isDown) => new()
    {
        Kind = MacroStepKind.Input,
        Input = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            ScanCode = scanCode,
            VirtualKey = 0x41,
        },
        IsDown = isDown,
        AutoRelease = false,
    };
}
