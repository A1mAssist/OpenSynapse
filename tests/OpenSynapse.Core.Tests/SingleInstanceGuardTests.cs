using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.Core.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void OnlyOneGuardCanOwnTheSameName()
    {
        var name = $@"Local\OpenSynapse.Tests.{Guid.NewGuid():N}";

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));
        Assert.False(SingleInstanceGuard.TryAcquire(name, out var second));
        Assert.Null(second);

        first!.Dispose();
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var replacement));
        replacement!.Dispose();
    }

    [Fact]
    public async Task SecondLaunchSignalsTheFirstGuard()
    {
        var name = $@"Local\OpenSynapse.Tests.{Guid.NewGuid():N}";
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var activation = Task.Run(() => first!.WaitForActivation(cancellation.Token));

        Assert.False(SingleInstanceGuard.TryAcquire(name, out var second));
        Assert.Null(second);
        Assert.True(await activation.WaitAsync(TimeSpan.FromSeconds(1)));

        first!.Dispose();
    }
}
