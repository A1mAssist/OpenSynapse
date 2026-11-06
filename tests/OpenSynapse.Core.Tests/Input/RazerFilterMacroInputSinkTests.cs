using OpenSynapse.Core.Macros;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Input;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerFilterMacroInputSinkTests
{
    [Fact]
    public async Task KeyboardStepsUseDriverSubmitInputAndReleaseTheKey()
    {
        var driver = new FakeDriver();
        var fallback = new FakeSink();
        await using var sink = new RazerFilterMacroInputSink(fallback, _ => new(driver));

        await sink.SendAsync(KeyStep(scanCode: 0x13, isDown: true));
        await sink.SendAsync(KeyStep(scanCode: 0x13, isDown: false));

        Assert.Empty(fallback.Steps);
        Assert.Equal(2, driver.Writes.Count);
        var (downCode, down) = driver.Writes[0];
        Assert.Equal(RazerFilterInputProtocol.SubmitInput, downCode);
        Assert.Equal(0x20, down.Length);
        Assert.Equal(1u, BitConverter.ToUInt32(down, 4));
        Assert.Equal(0x13, BitConverter.ToUInt16(down, 10));
        Assert.Equal(0, BitConverter.ToUInt16(down, 12));

        var (_, up) = driver.Writes[1];
        Assert.Equal(1, BitConverter.ToUInt16(up, 12));
    }

    [Fact]
    public async Task VirtualKeyOnlyUsesTheExtendedScanCode()
    {
        var driver = new FakeDriver();
        await using var sink = new RazerFilterMacroInputSink(new FakeSink(), _ => new(driver));

        await sink.SendAsync(KeyStep(virtualKey: 0xA3, scanCode: 0, isDown: true));

        var (_, payload) = Assert.Single(driver.Writes);
        Assert.Equal(0x1D, BitConverter.ToUInt16(payload, 10));
        Assert.Equal(2, BitConverter.ToUInt16(payload, 12));
    }

    [Fact]
    public async Task MissingDriverFallsBackWithoutRetryingDiscovery()
    {
        var fallback = new FakeSink();
        var opens = 0;
        await using var sink = new RazerFilterMacroInputSink(
            fallback,
            _ =>
            {
                opens++;
                return new ValueTask<IRazerFilterDriverChannel?>((IRazerFilterDriverChannel?)null);
            });

        await sink.SendAsync(KeyStep(scanCode: 0x13, isDown: true));
        await sink.SendAsync(KeyStep(scanCode: 0x13, isDown: false));

        Assert.Equal(1, opens);
        Assert.Equal(2, fallback.Steps.Count);
        Assert.All(fallback.Steps, step => Assert.Equal(MacroStepKind.Input, step.Kind));
    }

    [Fact]
    public async Task ReleaseAllSendsKeyUpForEveryHeldKey()
    {
        var driver = new FakeDriver();
        var fallback = new FakeSink();
        await using var sink = new RazerFilterMacroInputSink(fallback, _ => new(driver));

        await sink.SendAsync(KeyStep(scanCode: 0x13, isDown: true));
        await sink.ReleaseAllAsync();

        Assert.Equal(2, driver.Writes.Count);
        Assert.Equal(1, BitConverter.ToUInt16(driver.Writes[1].Payload, 12));
        Assert.Equal(1, fallback.ReleaseCount);
    }

    [Fact]
    public async Task DriverWriteFailureReleasesAndFallsBack()
    {
        var driver = new FakeDriver { FailWriteAt = 1 };
        var fallback = new FakeSink();
        await using var sink = new RazerFilterMacroInputSink(fallback, _ => new(driver));

        await sink.SendAsync(KeyStep(scanCode: 0x13, isDown: true));

        var step = Assert.Single(fallback.Steps);
        Assert.True(step.IsDown);
        Assert.True(driver.Disposed);
    }

    private static MacroStep KeyStep(int scanCode, bool isDown, int virtualKey = 0x52) => new()
    {
        Kind = MacroStepKind.Input,
        Input = new MacroInput
        {
            Kind = MacroInputKind.Keyboard,
            VirtualKey = virtualKey,
            ScanCode = scanCode,
        },
        IsDown = isDown,
    };

    private sealed class FakeDriver : IRazerFilterDriverChannel
    {
        internal List<(uint Code, byte[] Payload)> Writes { get; } = [];
        internal int? FailWriteAt { get; init; }
        internal bool Disposed { get; private set; }

        public void PostRead(int slot)
        {
        }

        public Task<ReadOnlyMemory<byte>> CompleteReadAsync(int slot) =>
            Task.FromResult(ReadOnlyMemory<byte>.Empty);

        public void WriteControl(uint controlCode, byte[] payload)
        {
            if (Writes.Count + 1 == FailWriteAt)
            {
                throw new IOException("driver removed");
            }

            Writes.Add((controlCode, [.. payload]));
        }

        public void CancelPendingReads()
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeSink : IMacroInputSink
    {
        internal List<MacroStep> Steps { get; } = [];
        internal int ReleaseCount { get; private set; }

        public Task SendAsync(MacroStep step, CancellationToken cancellationToken = default)
        {
            Steps.Add(step);
            return Task.CompletedTask;
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendMouseMoveAsync(
            MacroMouseMoveMode mode,
            int x,
            int y,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReleaseAllAsync(CancellationToken cancellationToken = default)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }
}
