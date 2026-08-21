using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class BladeMappingActionExecutorTests
{
    [Fact]
    public async Task ExecutesMultiInOrderAndDelegatesOnlyLeafActions()
    {
        var trace = new List<string>();
        await using var executor = CreateExecutor(
            outputs => trace.AddRange(outputs.Select(Format)),
            (action, _) =>
            {
                trace.Add($"leaf:{action.Kind}");
                return ValueTask.CompletedTask;
            },
            (milliseconds, _) =>
            {
                trace.Add($"delay:{milliseconds}");
                return ValueTask.CompletedTask;
            });

        await executor.ExecuteAsync(
            Owner(1),
            new BladeMultiMappingAction(
            [
                new BladeKeyboardMappingAction(0x2A, true, false),
                new BladeDelayMappingAction(10),
                new BladeCommandMappingAction(
                    BladeMappingOutputKind.GameMode,
                    BladeMappingCommand.Toggle),
                new BladeKeyboardMappingAction(0x2A, false, false),
            ]));

        Assert.Equal(
            ["2A:down", "delay:10", "leaf:GameMode", "2A:up"],
            trace);
    }

    [Fact]
    public async Task LeafFailureReleasesEveryKeyOwnedByThatPhysicalInput()
    {
        var sent = new List<BladeMappingOutputEvent>();
        await using var executor = CreateExecutor(
            outputs => sent.AddRange(outputs),
            (_, _) => ValueTask.FromException(new InvalidOperationException("leaf failed")));

        await executor.ExecuteAsync(
            Owner(2),
            new BladeKeyboardMappingAction(0x2A, true, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(
                Owner(2),
                new BladeCommandMappingAction(
                    BladeMappingOutputKind.GameMode,
                    BladeMappingCommand.Toggle)));

        Assert.Equal(
        [
            new BladeMappingOutputEvent(0x2A, true),
            new BladeMappingOutputEvent(0x2A, false),
        ], sent);
    }

    [Fact]
    public async Task RuntimeAndActionOwnersDoNotReleaseSharedOutputEarly()
    {
        var sent = new List<BladeMappingOutputEvent>();
        await using var executor = CreateExecutor(outputs => sent.AddRange(outputs));

        executor.SendRuntimeOutputs([new BladeMappingOutputEvent(0x30, true)]);
        await executor.ExecuteAsync(
            Owner(3),
            new BladeKeyboardMappingAction(0x30, true, false));
        executor.SendRuntimeOutputs([new BladeMappingOutputEvent(0x30, false)]);

        Assert.Equal([new BladeMappingOutputEvent(0x30, true)], sent);

        await executor.ExecuteAsync(
            Owner(3),
            new BladeKeyboardMappingAction(0x30, false, false));
        Assert.Equal(
        [
            new BladeMappingOutputEvent(0x30, true),
            new BladeMappingOutputEvent(0x30, false),
        ], sent);
    }

    [Fact]
    public async Task VolumeTurboRepeatsCatalogSequenceUntilMatchingRelease()
    {
        var sent = new List<BladeMappingOutputEvent>();
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var executor = CreateExecutor(
            outputs => sent.AddRange(outputs),
            delay: async (_, cancellationToken) =>
            {
                delayEntered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            });
        var owner = Owner(4);

        await executor.ExecuteAsync(
            owner,
            new BladeTurboMappingAction(
                BladeProduct710TurboCatalog.VolumeDownId,
                true,
                100,
                null));
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await executor.ExecuteAsync(
            owner,
            new BladeTurboMappingAction(
                BladeProduct710TurboCatalog.VolumeDownId,
                false,
                null,
                null));

        Assert.Equal(
        [
            new BladeMappingOutputEvent(0x2E, true, true),
            new BladeMappingOutputEvent(0x2E, false, true),
        ], sent);
    }

    [Fact]
    public async Task StopDuringProjectionTurboReleasesKeysInReverseOrder()
    {
        var sent = new List<BladeMappingOutputEvent>();
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var executor = CreateExecutor(
            outputs => sent.AddRange(outputs),
            delay: async (_, cancellationToken) =>
            {
                delayEntered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            });

        var execute = executor.ExecuteAsync(
            Owner(5),
            new BladeTurboMappingAction(
                BladeProduct710TurboCatalog.ProjectionSettingsId,
                true,
                null,
                1));
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await executor.StopAsync();
        await execute;

        Assert.Equal(
        [
            new BladeMappingOutputEvent(0x5B, true, true),
            new BladeMappingOutputEvent(0x19, true),
            new BladeMappingOutputEvent(0x19, false),
            new BladeMappingOutputEvent(0x5B, false, true),
        ], sent);
    }

    [Fact]
    public async Task StopReleasesRuntimeOutputsThatWereStillDown()
    {
        var sent = new List<BladeMappingOutputEvent>();
        await using var executor = CreateExecutor(outputs => sent.AddRange(outputs));

        executor.SendRuntimeOutputs([new BladeMappingOutputEvent(0x20, true)]);
        await executor.StopAsync();

        Assert.Equal(
        [
            new BladeMappingOutputEvent(0x20, true),
            new BladeMappingOutputEvent(0x20, false),
        ], sent);
    }

    [Fact]
    public async Task BackgroundTurboFailureIsReportedWithoutWaitingForPhysicalRelease()
    {
        var executor = CreateExecutor(_ => throw new InvalidOperationException("send failed"));

        await executor.ExecuteAsync(
            Owner(6),
            new BladeTurboMappingAction(
                BladeProduct710TurboCatalog.VolumeDownId,
                true,
                100,
                null));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal("send failed", failure.Message);
        await Assert.ThrowsAsync<AggregateException>(
            async () => await executor.DisposeAsync());
    }

    [Fact]
    public async Task PublishesActualDownStateAfterDownUpAndStop()
    {
        var snapshots = new List<BladeMappingOutputEvent[]>();
        await using var executor = CreateExecutor(
            _ => { },
            stateChanged: state => snapshots.Add(state.ToArray()));

        await executor.ExecuteAsync(
            Owner(6),
            new BladeKeyboardMappingAction(0x2A, true, false));
        await executor.ExecuteAsync(
            Owner(6),
            new BladeKeyboardMappingAction(0x2A, false, false));
        executor.SendRuntimeOutputs([new BladeMappingOutputEvent(0x20, true)]);
        await executor.StopAsync();

        Assert.Equal(4, snapshots.Count);
        Assert.Equal([new BladeMappingOutputEvent(0x2A, true)], snapshots[0]);
        Assert.Empty(snapshots[1]);
        Assert.Equal([new BladeMappingOutputEvent(0x20, true)], snapshots[2]);
        Assert.Empty(snapshots[3]);
    }

    [Fact]
    public async Task DoesNotPublishSyntheticStateWhenKeyboardSendFails()
    {
        var callbackCount = 0;
        await using var executor = CreateExecutor(
            _ => throw new InvalidOperationException("send failed"),
            stateChanged: _ => callbackCount++);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(
                Owner(7),
                new BladeKeyboardMappingAction(0x2A, true, false)));

        Assert.Equal(0, callbackCount);
    }

    private static BladeMappingActionExecutor CreateExecutor(
        Action<IReadOnlyList<BladeMappingOutputEvent>> send,
        Func<BladeMappingAction, CancellationToken, ValueTask>? leaf = null,
        Func<int, CancellationToken, ValueTask>? delay = null,
        Action<IReadOnlyList<BladeMappingOutputEvent>>? stateChanged = null) =>
        new(
            send,
            leaf ?? (static (_, _) => ValueTask.CompletedTask),
            delay ?? (static (_, _) => ValueTask.CompletedTask),
            stateChanged);

    private static BladeMappingInputEvent Owner(int code) =>
        new(BladeMappingInputKind.RazerKey, code, true);

    private static string Format(BladeMappingOutputEvent value) =>
        $"{value.ScanCode:X2}:{(value.IsDown ? "down" : "up")}";
}
