namespace OpenSynapse.Windows.Lighting;

internal enum ChromaLightingSource { None, Sdk, Rest }

/// <summary>One external lighting owner across the SDK and REST hosts.</summary>
internal sealed class ChromaLightingSession(
    Func<Task> suspendLighting,
    Func<Task> restoreLighting,
    Func<bool> enabled,
    Func<bool>? needsSuspension = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _owner;
    private bool _outputAvailable = true;
    private volatile bool _restorePending;
    private bool _needsTakeover;
    private readonly Dictionary<int, Func<CancellationToken, Task<bool>>> _latestFrames = [];

    internal bool IsActive => Volatile.Read(ref _owner) != 0;
    internal bool HasPendingRestore => _restorePending;

    internal async Task<bool> ApplyAsync(
        ChromaLightingSource source,
        Func<CancellationToken, Task<bool>> apply,
        CancellationToken cancellationToken,
        int channel = 0)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!enabled()) return true;
            if (IsActive && _owner != (int)source) return false;
            if (!_outputAvailable)
            {
                if (!IsActive) _needsTakeover = true;
                Volatile.Write(ref _owner, (int)source);
                _latestFrames[channel] = apply;
                return true;
            }
            if (!IsActive || _needsTakeover || needsSuspension?.Invoke() == true)
            {
                // Mark ownership before suspension; partial takeover still needs cleanup.
                Volatile.Write(ref _owner, (int)source);
                try { await suspendLighting().ConfigureAwait(false); }
                catch
                {
                    Volatile.Write(ref _owner, 0);
                    _latestFrames.Clear();
                    await restoreLighting().ConfigureAwait(false);
                    throw;
                }
                _needsTakeover = false;
            }
            _latestFrames[channel] = apply;
            return await apply(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal async Task ReleaseAsync(ChromaLightingSource source)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_owner != (int)source) return;
            Volatile.Write(ref _owner, 0);
            _latestFrames.Clear();
            _needsTakeover = false;
            if (_outputAvailable) await restoreLighting().ConfigureAwait(false);
            else _restorePending = true;
        }
        finally { _gate.Release(); }
    }

    internal async Task SetOutputAvailableAsync(bool available, Func<Task> stopOutput)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_outputAvailable == available) return;
            if (available && _restorePending)
            {
                _restorePending = false;
                try { await restoreLighting().ConfigureAwait(false); }
                catch { _restorePending = true; throw; }
            }
            _outputAvailable = available;
            if (!available && IsActive) await stopOutput().ConfigureAwait(false);
            else if (available && IsActive && enabled())
            {
                if (_needsTakeover || needsSuspension?.Invoke() == true)
                {
                    await suspendLighting().ConfigureAwait(false);
                    _needsTakeover = false;
                }
                // Static clients send only once; waking must repaint without another game frame.
                foreach (var frame in _latestFrames.Values)
                    await frame(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }
}
