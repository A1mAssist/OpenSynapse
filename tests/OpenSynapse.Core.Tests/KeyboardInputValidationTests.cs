namespace OpenSynapse.Core.Tests;

public sealed class KeyboardInputValidationTests
{
    [Fact]
    public void ParsesBoundedSoftwareModeCapture()
    {
        var options = KeyboardInputValidation.Options.Parse([
            "--keyboard-input-log",
            "--software-mode",
            "--hold-seconds", "30",
            "--output", "keyboard-input-software-mode-test.json",
        ]);

        Assert.Equal(30, options.HoldSeconds);
        Assert.True(options.UseSoftwareMode);
    }
}
