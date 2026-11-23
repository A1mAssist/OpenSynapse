namespace OpenSynapse.App.ViewModels;

// The connection owns its runtime, so changing pages cannot stop a software effect.
internal sealed class HeadsetAudioSession(RazerHeadsetAudioViewModel viewModel)
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _stopGate = new();
    private Task? _stopTask;

    internal RazerHeadsetAudioViewModel ViewModel { get; } = viewModel;

    internal async Task<bool> RunAsync(
        Func<RazerHeadsetAudioViewModel, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await _operations.WaitAsync(linked.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                await action(ViewModel, linked.Token);
                return !linked.IsCancellationRequested;
            }
            finally { _operations.Release(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    internal Task StopAsync()
    {
        lock (_stopGate) return _stopTask ??= StopCoreAsync();
    }

    internal void CancelOperations() => _lifetime.Cancel();

    private async Task StopCoreAsync()
    {
        _lifetime.Cancel();
        await _operations.WaitAsync();
        try { await ViewModel.DisposeAsync(); }
        finally { _operations.Release(); }
    }
}
