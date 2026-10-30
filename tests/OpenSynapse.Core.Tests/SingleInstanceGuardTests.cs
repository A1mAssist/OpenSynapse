using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public async Task AnElevatedTakeoverAsksTheExistingInstanceToExitAndThenAcquires()
    {
        var name = $@"Local\OpenSynapse.Test.{Guid.NewGuid():N}";
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));
        Assert.NotNull(first);

        var sawShutdown = new TaskCompletionSource<SingleInstanceSignal>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = Task.Run(() =>
        {
            var signal = first!.WaitForSignal(CancellationToken.None);
            sawShutdown.TrySetResult(signal);
            if (signal == SingleInstanceSignal.Shutdown)
            {
                first!.Dispose();
            }
        });

        try
        {
            SingleInstanceGuard? second = null;
            var acquired = await Task.Run(() => SingleInstanceGuard.TryAcquire(
                name,
                out second,
                activateExisting: false,
                requestExistingShutdown: true,
                shutdownTimeout: TimeSpan.FromSeconds(5)));

            Assert.Equal(SingleInstanceSignal.Shutdown, await sawShutdown.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(acquired);
            Assert.NotNull(second);
            second!.Dispose();
        }
        finally
        {
            first!.Dispose();
            await observer.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ASecondLaunchWithoutTakeoverSignalsActivationOnly()
    {
        var name = $@"Local\OpenSynapse.Test.{Guid.NewGuid():N}";
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));
        Assert.NotNull(first);

        var signal = new TaskCompletionSource<SingleInstanceSignal>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = Task.Run(() => signal.TrySetResult(
            first!.WaitForSignal(CancellationToken.None)));

        try
        {
            Assert.False(SingleInstanceGuard.TryAcquire(name, out _, activateExisting: true));
            Assert.Equal(SingleInstanceSignal.Activate, await signal.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            first!.Dispose();
            await observer.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
