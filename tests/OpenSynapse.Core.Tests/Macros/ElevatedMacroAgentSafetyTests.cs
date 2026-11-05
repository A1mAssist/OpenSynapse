using System.ComponentModel;
using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Input;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class ElevatedMacroAgentSafetyTests
{
    [Theory]
    [InlineData(0, 65536, 0, 0)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 65, -1, 0)]
    [InlineData(0, 65, 65536, 0)]
    [InlineData(1, 0, 0, 6)]
    [InlineData(2, 0, 0, 0)]
    [InlineData(3, 0, 0, 0)]
    [InlineData(999, 65, 0, 0)]
    public async Task InvalidInputsNeverReachSinkAndReleaseExistingKeys(int kind, int virtualKey, int scanCode, int mouseButton)
    {
        var sink = new FakeSink();
        var pressed = new List<MacroInput>();
        await PressAsync(sink, pressed, 65);
        var result = await ElevatedMacroAgent.ExecuteRequestAsync(new()
        {
            Kind = ElevatedMacroCommandKind.Input, IsDown = true,
            Input = new() { Kind = (MacroInputKind)kind, VirtualKey = virtualKey, ScanCode = scanCode, MouseButton = mouseButton },
        }, sink, pressed, 0x3000);
        Assert.False(result.Ok);
        Assert.Empty(pressed);
        Assert.Equal(new[] { (65, true), (65, false) }, sink.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Win32AndOverflowFailuresBecomeResponsesAndReleaseKeys(bool overflow)
    {
        var sink = new FakeSink();
        var pressed = new List<MacroInput>();
        await PressAsync(sink, pressed, 65);
        sink.Failure = step => step.IsDown && step.Input!.VirtualKey == 66
            ? overflow ? new OverflowException("range") : new Win32Exception(5, "denied") : null;
        var result = await PressAsync(sink, pressed, 66);
        Assert.False(result.Ok);
        Assert.Equal(overflow ? 0 : 5, result.ErrorCode);
        Assert.Empty(pressed);
        Assert.Contains((65, false), sink.Events);
    }

    [Fact]
    public async Task FinalCleanupRunsWhenTransportOrCancellationEscapes()
    {
        foreach (var failure in new Exception[] { new IOException("reader/writer disconnected"), new OperationCanceledException() })
        {
            var sink = new FakeSink();
            await Assert.ThrowsAsync(failure.GetType(), () => ElevatedMacroAgent.RunWithCleanupAsync(sink, async pressed =>
            {
                Assert.True((await PressAsync(sink, pressed, 65)).Ok);
                throw failure;
            }));
            Assert.Equal(new[] { (65, true), (65, false) }, sink.Events);
            Assert.All(sink.ReleaseTokens, token => Assert.False(token.CanBeCanceled));
        }
    }

    [Fact]
    public async Task CleanupContinuesAfterOneReleaseFailsAndRetainsItForRetry()
    {
        var sink = new FakeSink();
        var pressed = new List<MacroInput>();
        await PressAsync(sink, pressed, 65);
        await PressAsync(sink, pressed, 66);
        sink.Failure = step => !step.IsDown && step.Input!.VirtualKey == 66 ? new Win32Exception(5) : null;
        Assert.IsType<Win32Exception>(await ElevatedMacroAgent.ReleasePressedAsync(sink, pressed));
        Assert.Equal(66, Assert.Single(pressed).VirtualKey);
        Assert.Equal(new[] { (66, false), (65, false) }, sink.Events.Skip(2));
        sink.Failure = null;
        Assert.Null(await ElevatedMacroAgent.ReleasePressedAsync(sink, pressed));
        Assert.Empty(pressed);
    }

    [Fact]
    public async Task SuccessfulRequestsRetainKeysUntilMatchingUpOrReleaseAll()
    {
        var sink = new FakeSink();
        var pressed = new List<MacroInput>();
        await PressAsync(sink, pressed, 65);
        await PressAsync(sink, pressed, 66);
        Assert.Equal(2, pressed.Count);
        Assert.DoesNotContain(sink.Events, item => !item.Down);
        var ping = await ElevatedMacroAgent.ExecuteRequestAsync(new(), sink, pressed, 0x3000);
        Assert.True(ping.Ok);
        Assert.Equal(0x3000, ping.IntegrityLevel);
        Assert.Equal(2, pressed.Count);
        Assert.True((await ElevatedMacroAgent.ExecuteRequestAsync(new() { Kind = ElevatedMacroCommandKind.ReleaseAll }, sink, pressed, 0x3000)).Ok);
        Assert.Empty(pressed);
    }

    [Fact]
    public async Task UnsupportedMoveOrCommandReleasesHeldKeysWithoutCallingMove()
    {
        foreach (var request in new ElevatedMacroRequest[]
        {
            new() { Kind = ElevatedMacroCommandKind.MouseMove, MouseMoveMode = MacroMouseMoveMode.RelativeToStart },
            new() { Kind = (ElevatedMacroCommandKind)99 },
            new() { Version = 99 },
        })
        {
            var sink = new FakeSink();
            var pressed = new List<MacroInput>();
            await PressAsync(sink, pressed, 65);
            Assert.False((await ElevatedMacroAgent.ExecuteRequestAsync(request, sink, pressed, 0x3000)).Ok);
            Assert.Empty(pressed);
            Assert.Equal(0, sink.MoveCount);
        }
    }

    [Fact]
    public async Task SupportedInputLimitsRemainAcceptedWithoutTrackingWheelAsHeld()
    {
        var sink = new FakeSink();
        var pressed = new List<MacroInput>();
        var result = await ElevatedMacroAgent.ExecuteRequestAsync(new()
        {
            Kind = ElevatedMacroCommandKind.Input, IsDown = true,
            Input = new() { VirtualKey = 255, ScanCode = 65535, Extended = true },
        }, sink, pressed, 0x3000);
        Assert.True(result.Ok);
        Assert.Single(pressed);
        await ElevatedMacroAgent.ReleasePressedAsync(sink, pressed);
        foreach (var delta in new[] { -1200, -120, 120, 1200 })
        {
            Assert.True((await ElevatedMacroAgent.ExecuteRequestAsync(new()
            {
                Kind = ElevatedMacroCommandKind.Input,
                Input = new() { Kind = MacroInputKind.MouseWheel, WheelDelta = delta },
            }, sink, pressed, 0x3000)).Ok);
            Assert.Empty(pressed);
        }
    }

    [Fact]
    public async Task RequestReaderBoundsLengthAndRejectsUnterminatedInput()
    {
        var valid = new string('a', ElevatedMacroProtocol.MaximumMessageLength);
        Assert.Equal(valid, await ElevatedMacroAgent.ReadRequestLineAsync(new StringReader(valid + "\r\n"), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => ElevatedMacroAgent.ReadRequestLineAsync(new StringReader(valid + "xx"), default));
        await Assert.ThrowsAsync<InvalidDataException>(() => ElevatedMacroAgent.ReadRequestLineAsync(new StringReader("{partial"), default));
    }

    private static Task<ElevatedMacroResponse> PressAsync(FakeSink sink, List<MacroInput> pressed, int key) =>
        ElevatedMacroAgent.ExecuteRequestAsync(new()
        {
            Kind = ElevatedMacroCommandKind.Input, IsDown = true,
            Input = new() { Kind = MacroInputKind.Keyboard, VirtualKey = key },
        }, sink, pressed, 0x3000);

    private sealed class FakeSink : IMacroInputSink
    {
        internal List<(int Key, bool Down)> Events { get; } = [];
        internal List<CancellationToken> ReleaseTokens { get; } = [];
        internal Func<MacroStep, Exception?>? Failure { get; set; }
        internal int MoveCount { get; private set; }
        public Task SendAsync(MacroStep step, CancellationToken cancellationToken = default)
        {
            Events.Add((step.Input!.VirtualKey, step.IsDown));
            if (!step.IsDown) ReleaseTokens.Add(cancellationToken);
            if (Failure?.Invoke(step) is { } failure) throw failure;
            return Task.CompletedTask;
        }
        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendMouseMoveAsync(MacroMouseMoveMode mode, int x, int y, CancellationToken cancellationToken = default)
        {
            MoveCount++;
            return Task.CompletedTask;
        }
    }
}
