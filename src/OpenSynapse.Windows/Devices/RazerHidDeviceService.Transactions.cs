using System.Collections.Concurrent;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

public sealed partial class RazerHidDeviceService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TransactionGates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly AsyncLocal<HashSet<string>?> HeldTransactions = new();

    /// <summary>
    /// Serializes a read/write sequence for one physical device. The periodic refresh,
    /// mapping reads and a user write otherwise interleave between a write and its
    /// read-back. Nested calls from the same async flow reuse the held gate.
    /// </summary>
    public async Task<T> RunExclusiveAsync<T>(
        RazerHidDeviceConnection connection,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        RequireReady(connection);
        var context = SynchronizationContext.Current;
        var key = string.IsNullOrWhiteSpace(connection.DevicePath)
            ? connection.InstanceId
            : connection.DevicePath!;
        var held = HeldTransactions.Value;
        if (held?.Contains(key) == true)
        {
            return await RunOnContextAsync(context, () => action(cancellationToken))
                .ConfigureAwait(false);
        }

        var gate = TransactionGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
        {
            throw new RazerDeviceTimeoutException(
                "The device operation queue timed out.", retryableBusy: false);
        }

        var previous = HeldTransactions.Value;
        var next = previous is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
        next.Add(key);
        HeldTransactions.Value = next;
        try
        {
            return await RunOnContextAsync(context, () => action(cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            HeldTransactions.Value = previous;
            gate.Release();
        }
    }

    public Task RunExclusiveAsync(
        RazerHidDeviceConnection connection,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(connection, async token =>
        {
            await action(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
}
