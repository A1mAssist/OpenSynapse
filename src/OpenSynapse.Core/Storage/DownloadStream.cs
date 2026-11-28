namespace OpenSynapse.Core.Storage;

public static class DownloadStream
{
    public static async ValueTask<int> ReadAsync(Stream source, Memory<byte> buffer,
        CancellationToken cancellationToken, TimeSpan? idleTimeout = null)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(idleTimeout ?? TimeSpan.FromSeconds(30));
        try { return await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("The download stopped receiving data. Retry the download.", exception);
        }
    }
}
