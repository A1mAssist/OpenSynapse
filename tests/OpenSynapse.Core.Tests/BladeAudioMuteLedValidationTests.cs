using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeAudioMuteLedValidationTests
{
    [Fact]
    public void ParsesBoundedMicrophoneValidation()
    {
        var options = BladeAudioMuteLedValidation.Options.Parse(
        [
            "--blade-audio-mute-led",
            "--target", "microphone",
            "--hold-seconds", "15",
            "--output", "result.json",
        ]);

        Assert.Equal(BladeAudioMuteTarget.Microphone, options.Target);
        Assert.Equal(15, options.HoldSeconds);
        Assert.Equal("result.json", options.OutputPath);
        Assert.False(options.KeepNormalMode);
        Assert.False(options.TransientDriverMode);
    }

    [Fact]
    public void ParsesBoundedSpeakerValidation()
    {
        var options = BladeAudioMuteLedValidation.Options.Parse(
        [
            "--blade-audio-mute-led",
            "--target", "speaker",
            "--hold-seconds", "20",
            "--output", "speaker-result.json",
        ]);

        Assert.Equal(BladeAudioMuteTarget.Speaker, options.Target);
        Assert.Equal(20, options.HoldSeconds);
    }

    [Fact]
    public void ParsesNormalModeValidation()
    {
        var options = BladeAudioMuteLedValidation.Options.Parse(
        [
            "--blade-audio-mute-led",
            "--target", "speaker",
            "--normal-mode",
            "--output", "normal-mode.json",
        ]);

        Assert.True(options.KeepNormalMode);
    }

    [Fact]
    public void ParsesTransientDriverModeValidation()
    {
        var options = BladeAudioMuteLedValidation.Options.Parse(
        [
            "--blade-audio-mute-led",
            "--target", "speaker",
            "--transient-driver-mode",
            "--output", "transient-mode.json",
        ]);

        Assert.True(options.TransientDriverMode);
        Assert.Throws<ArgumentException>(() => BladeAudioMuteLedValidation.Options.Parse(
        [
            "--blade-audio-mute-led",
            "--target", "speaker",
            "--normal-mode",
            "--transient-driver-mode",
            "--output", "invalid-mode.json",
        ]));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("both")]
    public void RejectsUnknownTarget(string target) =>
        Assert.Throws<ArgumentException>(() => BladeAudioMuteLedValidation.Options.Parse(
        [
            "--blade-audio-mute-led",
            "--target", target,
            "--output", "result.json",
        ]));
}
