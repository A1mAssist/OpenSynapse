using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

internal sealed class RazerLegacyKeypadInputHost(
    IRazerFilterDriverChannel driver,
    Action enableCapture,
    Action restoreCapture,
    Func<BladeMappingInputEvent, Task> onInput) : IAsyncDisposable
{
    internal const int ReadSlots = 10;
    private readonly object _gate = new();
    private Task? _reader;
    private readonly Task<ReadOnlyMemory<byte>>?[] _pendingReads = new Task<ReadOnlyMemory<byte>>?[ReadSlots];
    private Task? _dispose;
    private bool _captureAttempted;
    private volatile bool _stopping;

    internal Task Completion => _reader ?? Task.CompletedTask;

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var slot = 0; slot < _pendingReads.Length; slot++)
            {
                driver.PostRead(slot);
                _pendingReads[slot] = driver.CompleteReadAsync(slot);
            }
            _reader = ReadAsync();
            if (_reader.IsCompleted) await _reader.ConfigureAwait(false);
            _captureAttempted = true;
            enableCapture();
        }
        catch (Exception startupError)
        {
            try { await DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanupError) { throw new AggregateException(startupError, cleanupError); }
            throw;
        }
    }

    private async Task ReadAsync()
    {
        for (var slot = 0; ; slot = (slot + 1) % _pendingReads.Length)
        {
            ReadOnlyMemory<byte> frame;
            try { frame = await _pendingReads[slot]!.ConfigureAwait(false); }
            catch (OperationCanceledException) when (_stopping) { return; }
            lock (_gate)
            {
                if (_stopping) return;
                // Keep a read queued while the ordered consumer processes this copied record.
                driver.PostRead(slot);
                _pendingReads[slot] = driver.CompleteReadAsync(slot);
            }
            foreach (var input in RazerLegacyKeypadInputProtocol.ParseInput(frame.Span))
                await onInput(input).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_dispose ??= StopAsync());
    }

    private async Task StopAsync()
    {
        _stopping = true;
        var errors = new List<Exception>();
        // rzudd has no file-close cleanup; restore capture before canceling the read.
        try { if (_captureAttempted) restoreCapture(); } catch (Exception error) { errors.Add(error); }
        try { driver.CancelPendingReads(); } catch (Exception error) { errors.Add(error); }
        if (_reader is not null)
        {
            try { await _reader.ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        }
        foreach (var pendingRead in _pendingReads)
        {
            if (pendingRead is null) continue;
            try { await pendingRead.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!errors.Contains(error)) errors.Add(error); }
        }
        driver.Dispose();
        if (errors.Count != 0) throw new AggregateException("Legacy keypad input cleanup failed.", errors);
    }
}
