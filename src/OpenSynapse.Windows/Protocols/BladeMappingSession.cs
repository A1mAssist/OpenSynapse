namespace OpenSynapse.Windows.Protocols;

/// <summary>
/// Product 710 mapping session. It owns the native MappingEngine lifetime and
/// translates verified internal-key reports into synthetic output events.
/// The caller remains responsible for the final Windows input sink.
/// </summary>
public sealed class BladeMappingSession : IAsyncDisposable
{
    private readonly BladeMappingEngineNativeRuntime _nativeRuntime;
    private readonly BladeMappingInputRuntime _inputRuntime;
    private readonly BladeRazerKeyReportDecoder _decoder = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private bool _started;
    private bool _disposed;

    public BladeMappingSession(
        BladeMappingEngineNativeRuntime nativeRuntime,
        BladeMappingInputRuntime inputRuntime)
    {
        _nativeRuntime = nativeRuntime ?? throw new ArgumentNullException(nameof(nativeRuntime));
        _inputRuntime = inputRuntime ?? throw new ArgumentNullException(nameof(inputRuntime));
    }

    public async Task StartAsync(
        string deviceInfoJson,
        string storageKey,
        string storageValueJson,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
            if (_started)
            {
                throw new InvalidOperationException("Blade Mapping session 已经启动。");
            }

            await _nativeRuntime.StartAsync(
                deviceInfoJson,
                storageKey,
                storageValueJson,
                cancellationToken).ConfigureAwait(false);
            _started = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<BladeMappingOutputEvent> ProcessReport(ReadOnlySpan<byte> report)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        if (!_started)
        {
            throw new InvalidOperationException("Blade Mapping session 尚未启动。");
        }

        return ProcessInputs(_decoder.Process(report));
    }

    /// <summary>
    /// Processes events from an external Raw Input decoder that owns the
    /// device-report state. This keeps the session usable with WM_INPUT while
    /// retaining the report-based API for validation tools.
    /// </summary>
    public IReadOnlyList<BladeMappingOutputEvent> ProcessInputs(
        IEnumerable<BladeMappingInputEvent> inputs)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        if (!_started)
        {
            throw new InvalidOperationException("Blade Mapping session 尚未启动。");
        }

        var output = new List<BladeMappingOutputEvent>();
        foreach (var input in inputs ?? throw new ArgumentNullException(nameof(inputs)))
        {
            output.AddRange(_inputRuntime.Process(input));
        }

        return output;
    }

    public async Task<IReadOnlyList<BladeMappingOutputEvent>> StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                return [];
            }

            _started = false;
            var output = new List<BladeMappingOutputEvent>();
            foreach (var input in _decoder.Reset())
            {
                output.AddRange(_inputRuntime.Process(input));
            }
            output.AddRange(_inputRuntime.Stop());

            await _nativeRuntime.StopAsync().ConfigureAwait(false);
            return output;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        Volatile.Write(ref _disposed, true);
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _inputRuntime.Dispose();
            await _nativeRuntime.DisposeAsync().ConfigureAwait(false);
            _gate.Dispose();
        }
    }
}
