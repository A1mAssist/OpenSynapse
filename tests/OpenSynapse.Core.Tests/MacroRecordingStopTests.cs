using System.Reflection;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class MacroRecordingStopTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopShortcutNotifiesOnceAndNeverEntersRecording(bool stopInCallback)
    {
        using var runtime = new MacroRuntime(_ => false, new NeverInjectSink(), installInputHooks: false);
        var stop = new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 0x78 };
        var key = new MacroInput { Kind = MacroInputKind.Keyboard, VirtualKey = 65 };
        var notifications = 0;
        IReadOnlyList<MacroStep>? captured = null;
        runtime.RecordingStopRequested += () =>
        {
            notifications++;
            // A different thread can take the lock: callbacks must run outside it.
            Assert.True(Task.Run(() => runtime.IsRecording).Wait(TimeSpan.FromSeconds(2)));
            if (stopInCallback) captured = runtime.StopRecording();
        };
        runtime.StartRecording(new() { StopShortcut = stop, RecordDelays = false });
        Assert.False(Handle(runtime, new(key, true, 1)));
        Assert.False(Handle(runtime, new(key, false, 2)));
        Assert.True(Handle(runtime, new(stop, true, 3)));
        Assert.True(Handle(runtime, new(stop, true, 4)));
        Assert.True(Handle(runtime, new(stop, false, 5)));
        captured ??= runtime.StopRecording();
        Assert.Equal(1, notifications);
        Assert.NotEmpty(captured);
        Assert.DoesNotContain(captured, step => step.Input?.Matches(stop) == true);
        Assert.False(runtime.IsRecording);
    }

    private static bool Handle(MacroRuntime runtime, MacroInputEvent value) =>
        (bool)(typeof(MacroRuntime).GetMethod("HandleInput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(runtime, [value])!);

    private sealed class NeverInjectSink : IMacroInputSink
    {
        public Task SendAsync(MacroStep step, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Recording must not inject");
        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Recording must not inject");
        public Task SendMouseMoveAsync(MacroMouseMoveMode mode, int x, int y, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Recording must not inject");
    }
}
