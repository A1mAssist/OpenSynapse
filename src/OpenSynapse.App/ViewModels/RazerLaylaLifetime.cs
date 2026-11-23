namespace OpenSynapse.App.ViewModels;

// Discovery, profile replay and shutdown share this lifetime, including work awaiting enumeration.
internal sealed class RazerLaylaLifetime
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stopGate = new();
    private Task? _stopTask;

    internal void BeginStop() => _stop.Cancel();

    internal async Task<bool> RunAsync(Func<CancellationToken, Task> action, CancellationToken token = default)
    {
        if (_stop.IsCancellationRequested) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        try
        {
            await _gate.WaitAsync(linked.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                await action(linked.Token);
                return !linked.IsCancellationRequested;
            }
            finally { _gate.Release(); }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested && !token.IsCancellationRequested)
        {
            return false;
        }
    }

    internal Task StopAsync(Func<Task> cleanup)
    {
        BeginStop();
        lock (_stopGate) return _stopTask ??= StopCoreAsync(cleanup);
    }

    private async Task StopCoreAsync(Func<Task> cleanup)
    {
        await _gate.WaitAsync();
        try { await cleanup(); }
        finally { _gate.Release(); }
    }
}
