using System.Reflection;
using OpenSynapse.App.Runtime;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroRuntimeTests
{
    [Fact]
    public async Task HoldTriggerReleasedBeforePlaybackStartsDoesNotRun()
    {
        using var runtime = new MacroRuntime(_ => true);
        var trigger = MiddleButton();
        runtime.UpdateMacros([HoldRepeatMacro(trigger, delayMilliseconds: 1000)]);

        var handleInput = HandleInputMethod();

        Assert.True((bool)handleInput.Invoke(
            runtime,
            [new MacroInputEvent(trigger, true, 1)])!);
        Assert.True((bool)handleInput.Invoke(
            runtime,
            [new MacroInputEvent(trigger, false, 2)])!);

        await Task.Delay(200);
        Assert.False(runtime.IsPlaying);
    }

    [Fact]
    public async Task SecondMouseDownRecoversFromLostRelease()
    {
        using var runtime = new MacroRuntime(_ => true);
        var trigger = MiddleButton();
        runtime.UpdateMacros([HoldRepeatMacro(trigger, delayMilliseconds: 5000)]);
        var handleInput = HandleInputMethod();

        Assert.True((bool)handleInput.Invoke(
            runtime,
            [new MacroInputEvent(trigger, true, 1)])!);
        await WaitUntilAsync(() => runtime.IsPlaying, TimeSpan.FromSeconds(2));

        // The first up never arrives. A second down must not stay swallowed.
        Assert.True((bool)handleInput.Invoke(
            runtime,
            [new MacroInputEvent(trigger, true, 2)])!);
        await WaitUntilAsync(() => !runtime.IsPlaying, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task HoldPlaybackStopsWhenPhysicalTriggerDisappears()
    {
        var checks = 0;
        using var runtime = new MacroRuntime(_ => ++checks <= 2);
        var trigger = MiddleButton();
        runtime.UpdateMacros([HoldRepeatMacro(trigger, delayMilliseconds: 5000)]);

        Assert.True((bool)HandleInputMethod().Invoke(
            runtime,
            [new MacroInputEvent(trigger, true, 1)])!);

        await WaitUntilAsync(() => runtime.IsPlaying, TimeSpan.FromSeconds(2));
        await WaitUntilAsync(() => !runtime.IsPlaying, TimeSpan.FromSeconds(2));
    }

    private static MacroInput MiddleButton() => new()
    {
        Kind = MacroInputKind.MouseButton,
        MouseButton = 3,
    };

    private static MacroDefinition HoldRepeatMacro(MacroInput trigger, int delayMilliseconds) => new()
    {
        Id = "hold-repeat",
        Name = "Hold repeat",
        Trigger = new MacroTrigger
        {
            Input = trigger,
            Mode = MacroTriggerMode.HoldRepeat,
        },
        PlaybackMode = MacroPlaybackMode.RepeatUntilStopped,
        Steps =
        [
            new MacroStep
            {
                Kind = MacroStepKind.Delay,
                DelayMilliseconds = delayMilliseconds,
            },
        ],
    };

    private static MethodInfo HandleInputMethod() =>
        typeof(MacroRuntime).GetMethod(
            "HandleInput",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("HandleInput was not found.");

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException();
            }
            await Task.Delay(20);
        }
    }
}
