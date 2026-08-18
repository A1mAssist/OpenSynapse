using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Core.Tests;

public sealed class BladeSoftwareModeCoordinatorTests
{
    [Fact]
    public async Task OnlyLastOwnerRestoresNormalMode()
    {
        var coordinator = new BladeSoftwareModeCoordinator();
        var enters = 0;
        var restores = 0;
        var first = await AcquireAsync();
        var second = await AcquireAsync();

        await first.ReleaseAsync(RestoreAsync);
        Assert.Equal(0, restores);
        await second.ReleaseAsync(RestoreAsync);

        Assert.Equal(2, enters);
        Assert.Equal(1, restores);
        return;

        Task<BladeSoftwareModeCoordinator.BladeSoftwareModeLease> AcquireAsync() =>
            coordinator.AcquireAsync(
                "blade",
                _ =>
                {
                    Interlocked.Increment(ref enters);
                    return Task.CompletedTask;
                },
                RestoreAsync,
                CancellationToken.None);

        Task RestoreAsync()
        {
            Interlocked.Increment(ref restores);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FailedAdditionalOwnerDoesNotRestoreExistingOwner()
    {
        var coordinator = new BladeSoftwareModeCoordinator();
        var restores = 0;
        var first = await coordinator.AcquireAsync(
            "blade",
            static _ => Task.CompletedTask,
            RestoreAsync,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.AcquireAsync(
            "blade",
            static _ => throw new InvalidOperationException("takeover failed"),
            RestoreAsync,
            CancellationToken.None));
        Assert.Equal(0, restores);

        await first.ReleaseAsync(RestoreAsync);
        Assert.Equal(1, restores);

        Task RestoreAsync()
        {
            Interlocked.Increment(ref restores);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task NewOwnerWaitsForLastOwnerRestore()
    {
        var coordinator = new BladeSoftwareModeCoordinator();
        var restoreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRestore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextEntered = false;
        var first = await coordinator.AcquireAsync(
            "blade",
            static _ => Task.CompletedTask,
            static () => Task.CompletedTask,
            CancellationToken.None);

        var release = first.ReleaseAsync(async () =>
        {
            restoreStarted.TrySetResult();
            await allowRestore.Task;
        }).AsTask();
        await restoreStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var acquire = coordinator.AcquireAsync(
            "blade",
            _ =>
            {
                nextEntered = true;
                return Task.CompletedTask;
            },
            static () => Task.CompletedTask,
            CancellationToken.None);
        await Task.Delay(20);
        Assert.False(nextEntered);

        allowRestore.TrySetResult();
        await release;
        var second = await acquire;
        Assert.True(nextEntered);
        await second.ReleaseAsync(static () => Task.CompletedTask);
    }
}
