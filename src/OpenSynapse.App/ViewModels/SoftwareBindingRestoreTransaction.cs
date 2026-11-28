namespace OpenSynapse.App.ViewModels;

public sealed class SoftwareBindingRestoreTransaction(Func<Task> rollback) : IAsyncDisposable
{
    private bool _committed;
    private bool _disposed;

    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_committed) await rollback().ConfigureAwait(true);
    }
}
