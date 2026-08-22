using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class RazerFilterInputHostTests
{
    [Fact]
    public async Task StartsFailClosedAndStopsInReverseSafetyOrder()
    {
        var driver = new FakeDriverChannel();
        await using var host = new RazerFilterInputHost(driver, static _ => { });

        await host.StartAsync();

        Assert.Equal([0, 1], driver.PostedSlots.Take(2));
        Assert.Equal(
            [
                RazerFilterInputHost.EnableInputHooks,
                RazerFilterInputHost.EnableInputNotify,
                .. Enumerable.Repeat(RazerFilterInputProtocol.SetInputHook, 23),
            ],
            driver.Controls.Select(static item => item.Code));

        await host.DisposeAsync();

        var cleanup = driver.Controls.Skip(25).ToArray();
        Assert.Equal(
            RazerFilterInputHost.OfficialProduct710Hooks.Reverse().Select(static hook => hook.ScanCode),
            cleanup.Take(23).Select(static item => BitConverter.ToUInt16(item.Payload, 10)));
        Assert.Equal(RazerFilterInputHost.EnableInputNotify, cleanup[23].Code);
        Assert.Equal(RazerFilterInputHost.EnableInputHooks, cleanup[24].Code);
        Assert.Equal(1, driver.CancelCount);
        Assert.True(driver.Disposed);
    }

    [Fact]
    public async Task ReaderFailureCancelsTheOtherReadAndRestoresDriverState()
    {
        var driver = new FakeDriverChannel();
        var host = new RazerFilterInputHost(driver, static _ => { });
        await host.StartAsync();

        driver.FailRead(0, new IOException("read failed"));

        var failure = await Assert.ThrowsAsync<IOException>(() => host.Completion);
        Assert.Equal("read failed", failure.Message);
        Assert.Equal(1, driver.CancelCount);
        Assert.DoesNotContain(driver.Controls, static item =>
            item.Code == RazerFilterInputProtocol.EnableInputRedirect);
        await Assert.ThrowsAsync<IOException>(async () => await host.DisposeAsync());
        Assert.True(driver.Disposed);
    }

    [Fact]
    public async Task HandlerFailureRestoresDriverState()
    {
        var driver = new FakeDriverChannel();
        var host = new RazerFilterInputHost(
            driver,
            static _ => throw new InvalidOperationException("handler failed"));
        await host.StartAsync();

        driver.CompleteRead(0, InputFrame(2, 1, 0x3B, 0));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Completion);
        Assert.Equal("handler failed", failure.Message);
        Assert.Equal(1, driver.CancelCount);
        Assert.DoesNotContain(driver.Controls, static item =>
            item.Code == RazerFilterInputProtocol.EnableInputRedirect);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());
    }

    [Fact]
    public async Task PartialHookFailureClearsOnlyInstalledHooks()
    {
        var driver = new FakeDriverChannel { FailControlAt = 4 };
        var host = new RazerFilterInputHost(driver, static _ => { });

        var failure = await Assert.ThrowsAsync<IOException>(() => host.StartAsync());

        Assert.Equal("control failed", failure.Message);
        Assert.DoesNotContain(driver.Controls, static item =>
            item.Code == RazerFilterInputProtocol.EnableInputRedirect);
        var clear = Assert.Single(driver.Controls, static item =>
            item.Code == RazerFilterInputProtocol.ClearInputHook);
        Assert.Equal(0x13, BitConverter.ToUInt16(clear.Payload, 10));
        Assert.Equal(1, driver.CancelCount);
        Assert.True(driver.Disposed);
    }

    [Fact]
    public async Task PostReadFailureWaitsForPreviouslyPostedReadBeforeDisposingDriver()
    {
        var driver = new FakeDriverChannel
        {
            FailPostAt = 2,
            AutoCompleteCancellation = false,
        };
        var host = new RazerFilterInputHost(driver, static _ => { });

        var start = host.StartAsync();

        Assert.False(start.IsCompleted);
        Assert.Equal([0, 1], driver.PostedSlots);
        Assert.Equal(1, driver.CancelCount);
        Assert.False(driver.Disposed);

        driver.CompleteCancellation();

        var failure = await Assert.ThrowsAsync<IOException>(() => start);
        Assert.Equal("post failed", failure.Message);
        Assert.True(driver.Disposed);
    }

    [Fact]
    public async Task ConcurrentDisposeRunsNativeCleanupOnce()
    {
        var driver = new FakeDriverChannel();
        var host = new RazerFilterInputHost(driver, static _ => { });
        await host.StartAsync();

        await Task.WhenAll(
            host.DisposeAsync().AsTask(),
            host.DisposeAsync().AsTask());

        Assert.Equal(1, driver.CancelCount);
        Assert.True(driver.Disposed);
    }

    [Fact]
    public async Task ConsumerUsageUsesOfficialInputIoctlAndIsReleasedOnStop()
    {
        var driver = new FakeDriverChannel();
        var host = new RazerFilterInputHost(driver, static _ => { });
        await host.StartAsync();

        host.SendConsumerUsage(0x6F);
        var press = driver.Controls.Last();
        Assert.Equal(RazerFilterInputProtocol.SubmitInput, press.Code);
        Assert.Equal(0x6F, BitConverter.ToUInt16(press.Payload, 8));

        await host.DisposeAsync();

        var release = Assert.Single(driver.Controls, static item =>
            item.Code == RazerFilterInputProtocol.SubmitInput &&
            BitConverter.ToUInt16(item.Payload, 8) == 0);
        Assert.Equal(RazerFilterInputProtocol.ConsumerInputLength, release.Payload.Length);
    }

    [Fact]
    public async Task FailedConsumerPressIsStillReleasedOnStop()
    {
        var driver = new FakeDriverChannel { FailControlAt = 26 };
        var host = new RazerFilterInputHost(driver, static _ => { });
        await host.StartAsync();

        await Assert.ThrowsAsync<IOException>(() =>
            Task.Run(() => host.SendConsumerUsage(0x70)));
        await host.DisposeAsync();

        Assert.Contains(driver.Controls, static item =>
            item.Code == RazerFilterInputProtocol.SubmitInput &&
            BitConverter.ToUInt16(item.Payload, 8) == 0);
    }

    private static byte[] InputFrame(uint eventType, uint kind, ushort code, ushort flag)
    {
        var frame = new byte[RazerFilterInputProtocol.InputFrameLength];
        BitConverter.GetBytes(eventType).CopyTo(frame, 8);
        BitConverter.GetBytes(kind).CopyTo(frame, 16);
        BitConverter.GetBytes(code).CopyTo(frame, 22);
        BitConverter.GetBytes(flag).CopyTo(frame, 24);
        return frame;
    }

    private sealed class FakeDriverChannel : IRazerFilterDriverChannel
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource<ReadOnlyMemory<byte>>[] _reads =
            [CreateRead(), CreateRead()];

        internal List<int> PostedSlots { get; } = [];
        internal List<(uint Code, byte[] Payload)> Controls { get; } = [];
        internal int CancelCount { get; private set; }
        internal bool Disposed { get; private set; }
        internal int? FailControlAt { get; init; }
        internal int? FailPostAt { get; init; }
        internal bool AutoCompleteCancellation { get; init; } = true;
        private int _controlAttempts;
        private int _postAttempts;

        public void PostRead(int slot)
        {
            lock (_gate)
            {
                PostedSlots.Add(slot);
                _postAttempts++;
                if (_postAttempts == FailPostAt)
                {
                    throw new IOException("post failed");
                }
                if (_reads[slot].Task.IsCompleted)
                {
                    _reads[slot] = CreateRead();
                }
            }
        }

        public Task<ReadOnlyMemory<byte>> CompleteReadAsync(int slot)
        {
            lock (_gate)
            {
                return _reads[slot].Task;
            }
        }

        public void WriteControl(uint controlCode, byte[] payload)
        {
            lock (_gate)
            {
                _controlAttempts++;
                if (_controlAttempts == FailControlAt)
                {
                    throw new IOException("control failed");
                }
                Controls.Add((controlCode, [.. payload]));
            }
        }

        public void CancelPendingReads()
        {
            lock (_gate)
            {
                CancelCount++;
                if (AutoCompleteCancellation)
                {
                    CompleteCancellationCore();
                }
            }
        }

        public void Dispose() => Disposed = true;

        internal void FailRead(int slot, Exception exception)
        {
            lock (_gate)
            {
                _reads[slot].TrySetException(exception);
            }
        }

        internal void CompleteRead(int slot, byte[] frame)
        {
            lock (_gate)
            {
                _reads[slot].TrySetResult(frame);
            }
        }

        internal void CompleteCancellation()
        {
            lock (_gate)
            {
                CompleteCancellationCore();
            }
        }

        private void CompleteCancellationCore()
        {
            foreach (var read in _reads)
            {
                read.TrySetCanceled();
            }
        }

        private static TaskCompletionSource<ReadOnlyMemory<byte>> CreateRead() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
