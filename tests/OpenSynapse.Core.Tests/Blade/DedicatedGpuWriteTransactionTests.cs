using System.ComponentModel;
using System.Text;
using System.Text.Json;
using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class DedicatedGpuWriteTransactionTests
{
    private static DedicatedGpuModeServiceRequest Request(string operation = "set") => new(1, operation, false);
    private static DedicatedGpuModeServiceResponse Response(bool handled = true) =>
        new(1, handled, DedicatedGpuSwitchOutcome.Success, 0, null, null);

    [Fact]
    public async Task CancellationBeforeUacDispatchNeverStartsTheHelper()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var starts = 0;
        var result = await WindowsDedicatedGpuMode.RunElevatedHelperAsync(
            () => { starts++; return null; }, cancel.Token);
        Assert.Equal(DedicatedGpuSwitchOutcome.Cancelled, result.Outcome);
        Assert.Equal(0, starts);
    }

    [Theory]
    [InlineData(1223, DedicatedGpuSwitchOutcome.Cancelled)]
    [InlineData(5, DedicatedGpuSwitchOutcome.Failed)]
    public async Task ARejectedUacLaunchHasNotDispatchedHardware(int code, DedicatedGpuSwitchOutcome expected)
    {
        var result = await WindowsDedicatedGpuMode.RunElevatedHelperAsync(
            () => throw new Win32Exception(code), CancellationToken.None);
        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task NullProcessIsAConfirmedLaunchFailure() =>
        Assert.Equal(DedicatedGpuSwitchOutcome.Failed,
            (await WindowsDedicatedGpuMode.RunElevatedHelperAsync(() => null, CancellationToken.None)).Outcome);

    [Theory]
    [InlineData(DedicatedGpuSwitchOutcome.Success)]
    [InlineData(DedicatedGpuSwitchOutcome.Failed)]
    public async Task AStartedHelperRemainsOwnedUntilItsTerminalResult(DedicatedGpuSwitchOutcome outcome)
    {
        using var cancel = new CancellationTokenSource();
        var terminal = new TaskCompletionSource<DedicatedGpuSwitchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = WindowsDedicatedGpuMode.RunElevatedHelperAsync(() => terminal.Task, cancel.Token);
        cancel.Cancel();
        Assert.False(wait.IsCompleted);
        terminal.SetResult(new(outcome));
        Assert.Equal(outcome, (await wait).Outcome);
    }

    [Fact]
    public async Task LosingTheStartedHelperTerminalStateIsNotReportedAsCancellation() =>
        await Assert.ThrowsAsync<DedicatedGpuOperationUncertainException>(() =>
            WindowsDedicatedGpuMode.RunElevatedHelperAsync(
                () => Task.FromException<DedicatedGpuSwitchResult>(new IOException("lost process")),
                CancellationToken.None));

    [Theory]
    [InlineData("set")]
    [InlineData("restart")]
    public async Task ServiceCancellationAfterDispatchStillWaitsForTheResponse(string operation)
    {
        using var cancel = new CancellationTokenSource();
        var pipe = new FakePipe { OnWrite = cancel.Cancel };
        var exchange = DedicatedGpuModeServiceClient.ExchangeAsync(Request(operation), cancel.Token,
            _ => Task.FromResult<Stream>(pipe));
        await pipe.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cancel.IsCancellationRequested);
        Assert.False(exchange.IsCompleted);
        Assert.False(pipe.ReadToken.IsCancellationRequested);
        pipe.Reply.SetResult(JsonSerializer.Serialize(Response()) + "\n");
        Assert.Equal(DedicatedGpuSwitchOutcome.Success, (await exchange)!.Outcome);
        Assert.Equal(1, pipe.Writes);
    }

    [Fact]
    public async Task CancellationBeforeTheWriteDoesNotDispatchBytes()
    {
        using var cancel = new CancellationTokenSource();
        var pipe = new FakePipe();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DedicatedGpuModeServiceClient.ExchangeAsync(Request(), cancel.Token, _ =>
            {
                cancel.Cancel();
                return Task.FromResult<Stream>(pipe);
            }));
        Assert.Equal(0, pipe.Writes);
    }

    [Fact]
    public async Task AConnectFailureIsSafeForTheExistingFallback()
    {
        var result = await DedicatedGpuModeServiceClient.ExchangeAsync(Request(), CancellationToken.None,
            _ => Task.FromException<Stream>(new IOException("not connected")));
        Assert.Null(result);
    }

    [Fact]
    public async Task AWriteFailureMightHaveSentACompleteRequest()
    {
        var pipe = new FakePipe { OnWrite = () => throw new IOException("lost write acknowledgement") };
        await Assert.ThrowsAsync<DedicatedGpuOperationUncertainException>(() =>
            DedicatedGpuModeServiceClient.ExchangeAsync(Request(), CancellationToken.None,
                _ => Task.FromResult<Stream>(pipe)));
        Assert.Equal(1, pipe.Writes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("garbage\n")]
    [InlineData("null\n")]
    [InlineData("{\"Version\":2,\"Handled\":true,\"Outcome\":0}\n")]
    [InlineData("{\"Version\":1,\"Handled\":true,\"Outcome\":999}\n")]
    public async Task LostOrMalformedWriteResponsesAreUncertain(string line)
    {
        var pipe = new FakePipe();
        pipe.Reply.SetResult(line);
        await Assert.ThrowsAsync<DedicatedGpuOperationUncertainException>(() =>
            DedicatedGpuModeServiceClient.ExchangeAsync(Request(), CancellationToken.None,
                _ => Task.FromResult<Stream>(pipe)));
    }

    [Fact]
    public async Task ResponseTimeoutIsUncertainRatherThanAnAbortedWrite()
    {
        var pipe = new FakePipe();
        await Assert.ThrowsAsync<DedicatedGpuOperationUncertainException>(() =>
            DedicatedGpuModeServiceClient.ExchangeAsync(Request(), CancellationToken.None,
                _ => Task.FromResult<Stream>(pipe), TimeSpan.Zero));
    }

    [Fact]
    public async Task ExplicitProtocolRejectionIsAConfirmedNonExecution()
    {
        var pipe = new FakePipe();
        pipe.Reply.SetResult(JsonSerializer.Serialize(Response(false)) + "\n");
        var result = await DedicatedGpuModeServiceClient.ExchangeAsync(Request(), CancellationToken.None,
            _ => Task.FromResult<Stream>(pipe));
        Assert.False(result!.Handled);
    }

    [Fact]
    public async Task ResponseLossNeverRetriesWritesOrFallsBackToUac()
    {
        var operations = new List<string>();
        await Assert.ThrowsAsync<DedicatedGpuOperationUncertainException>(() =>
            DedicatedGpuModeServiceClient.SendForResponseAsync(Request(), CancellationToken.None,
            _ => Task.FromResult(true), (request, _) =>
            {
                operations.Add(request.Operation);
                throw new DedicatedGpuOperationUncertainException("lost response");
            }, (_, _) => throw new InvalidOperationException("must not retry")));
        Assert.Equal(["set"], operations);
    }

    [Fact]
    public async Task SafeConnectFailuresCanStillUseTheExistingWakeupRetry()
    {
        var calls = 0;
        var delays = 0;
        var response = await DedicatedGpuModeServiceClient.SendForResponseAsync(Request(), CancellationToken.None,
                _ => Task.FromResult(true), (_, _) =>
                {
                    calls++;
                    return Task.FromResult<DedicatedGpuModeServiceResponse?>(null);
                }, (_, _) => { delays++; return Task.CompletedTask; });
        Assert.Null(response);
        Assert.Equal(2, calls);
        Assert.Equal(1, delays);
    }

    private sealed class FakePipe : Stream
    {
        internal readonly TaskCompletionSource<string> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? OnWrite;
        internal int Writes;
        internal CancellationToken ReadToken { get; private set; }
        private byte[]? _response;
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes++;
            OnWrite?.Invoke();
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write([], 0, buffer.Length);
            return ValueTask.CompletedTask;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadToken = cancellationToken;
            ReadEntered.TrySetResult();
            _response ??= Encoding.UTF8.GetBytes(await Reply.Task.WaitAsync(cancellationToken));
            var count = Math.Min(buffer.Length, _response.Length - _position);
            _response.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
