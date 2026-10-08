using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Runtime;

/// <summary>
/// A device-independent software-lighting frame. The producer creates one frame
/// per tick; the runtime projects that frame once for every compatible device.
/// </summary>
internal readonly record struct OpenRazerSoftwareLightingFrame
{
    internal int Rows { get; }
    internal int Columns { get; }
    internal IReadOnlyList<RazerRgb> Pixels { get; }

    internal OpenRazerSoftwareLightingFrame(int rows, int columns, IReadOnlyList<RazerRgb> pixels)
        : this()
    {
        if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Count != checked(rows * columns))
            throw new ArgumentException("The lighting frame pixel count does not match its dimensions.", nameof(pixels));

        Rows = rows;
        Columns = columns;
        Pixels = pixels.ToArray();
    }

    internal RazerRgb this[int row, int column] => Pixels[row * Columns + column];
}

/// <summary>
/// Drives software-generated frames to compatible OpenRazer matrix devices.
/// Audio or other expensive sampling belongs in the frame provider and is
/// intentionally invoked once per tick, rather than once per device.
/// </summary>
internal sealed class OpenRazerSoftwareLightingRuntime : IAsyncDisposable
{
    private readonly OpenRazerDeviceService _service;
    private readonly Func<IReadOnlyList<OpenRazerDeviceConnection>> _connections;
    private readonly Func<OpenRazerDeviceConnection, bool> _isEnabled;
    private readonly Func<OpenRazerDeviceConnection, bool> _isOverrideEnabled;
    private readonly Func<OpenRazerDeviceConnection, bool> _targetFilter;
    private readonly string? _instanceId;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly Dictionary<string, AppliedFrame> _lastFrames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private int _disposed;

    internal OpenRazerSoftwareLightingRuntime(
        OpenRazerDeviceService service,
        Func<IReadOnlyList<OpenRazerDeviceConnection>> connections,
        Func<OpenRazerDeviceConnection, bool>? isEnabled = null,
        Func<OpenRazerDeviceConnection, bool>? isOverrideEnabled = null,
        string? instanceId = null,
        Func<OpenRazerDeviceConnection, bool>? targetFilter = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _isEnabled = isEnabled ?? (static _ => true);
        _isOverrideEnabled = isOverrideEnabled ?? (static _ => true);
        _targetFilter = targetFilter ?? (static _ => true);
        _instanceId = instanceId;
    }

    internal bool IsRunning => _runTask is { IsCompleted: false };

    /// <summary>
    /// Starts a single frame loop. The provider is called once per interval;
    /// an existing loop is cancelled and replaced.
    /// </summary>
    internal async Task StartOrReplaceAsync(
        Func<CancellationToken, ValueTask<IReadOnlyList<RazerRgb>>> frameProvider,
        int sourceRows,
        int sourceColumns,
        TimeSpan frameInterval,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(frameProvider);
        if (sourceRows < 1) throw new ArgumentOutOfRangeException(nameof(sourceRows));
        if (sourceColumns < 1) throw new ArgumentOutOfRangeException(nameof(sourceColumns));
        if (frameInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(frameInterval));

        Task? previous;
        lock (_lifecycleGate)
        {
            previous = _runTask;
            _runCancellation?.Cancel();
        }

        if (previous is not null)
        {
            try { await previous.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        lock (_lifecycleGate)
        {
            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunAsync(frameProvider, sourceRows, sourceColumns, frameInterval, _runCancellation.Token);
        }
    }

    internal Task StartAsync(
        Func<CancellationToken, ValueTask<OpenRazerSoftwareLightingFrame?>> frameProvider,
        TimeSpan frameInterval,
        CancellationToken cancellationToken = default) =>
        StartFrameLoopAsync(frameProvider, frameInterval, cancellationToken);

    private Task StartFrameLoopAsync(
        Func<CancellationToken, ValueTask<OpenRazerSoftwareLightingFrame?>> frameProvider,
        TimeSpan frameInterval,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(frameProvider);
        if (frameInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(frameInterval));

        lock (_lifecycleGate)
        {
            if (IsRunning)
                return Task.CompletedTask;
            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunFrameAsync(frameProvider, frameInterval, _runCancellation.Token);
            return Task.CompletedTask;
        }
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        lock (_lifecycleGate)
        {
            _runCancellation?.Cancel();
            runTask = _runTask;
        }

        if (runTask is not null)
        {
            try { await runTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_runCancellation?.IsCancellationRequested == true) { }
        }

        lock (_lifecycleGate)
        {
            if (ReferenceEquals(_runTask, runTask))
            {
                _runTask = null;
                _runCancellation?.Dispose();
                _runCancellation = null;
            }
        }

        await ClearStateAsync(cancellationToken).ConfigureAwait(false);
    }

    internal Task StopAllAsync(CancellationToken cancellationToken = default) => StopAsync(cancellationToken);

    internal Task StopAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return StopAsync(cancellationToken);
    }

    /// <summary>
    /// Applies one frame immediately. Returns false when no compatible device
    /// is currently available; disabled or non-Chroma devices are ignored.
    /// </summary>
    internal async Task<bool> ApplyFrameAsync(
        OpenRazerSoftwareLightingFrame source,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var targets = _connections()
            .Where(connection => connection.IsReady &&
                connection.Definition.MatrixDimensions is not null &&
                connection.Capabilities.Contains(OpenRazerBackendCapability.MatrixFrameWrite) &&
                _isEnabled(connection) &&
                _isOverrideEnabled(connection) &&
                _targetFilter(connection) &&
                (_instanceId is null || StringComparer.OrdinalIgnoreCase.Equals(connection.InstanceId, _instanceId)))
            .ToArray();

        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var targetIds = targets.Select(target => target.InstanceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleId in _lastFrames.Keys.Where(id => !targetIds.Contains(id)).ToArray())
                _lastFrames.Remove(staleId);

            if (targets.Length == 0)
                return false;

            var wrote = false;
            foreach (var target in targets)
            {
                var dimensions = target.Definition.MatrixDimensions!;
                var frame = Resize(source, dimensions.Rows, dimensions.Columns);
                if (_lastFrames.TryGetValue(target.InstanceId, out var previous) &&
                    ReferenceEquals(previous.Connection, target) &&
                    previous.Frame.AsSpan().SequenceEqual(frame))
                    continue;

                await _service.SetCustomFrameAsync(target, frame, cancellationToken).ConfigureAwait(false);
                _lastFrames[target.InstanceId] = new AppliedFrame(target, frame);
                wrote = true;
            }

            return wrote || targets.Length > 0;
        }
        finally
        {
            _applyGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try { await StopAsync().ConfigureAwait(false); }
        finally { _applyGate.Dispose(); }
    }

    private async Task RunAsync(
        Func<CancellationToken, ValueTask<IReadOnlyList<RazerRgb>>> frameProvider,
        int sourceRows,
        int sourceColumns,
        TimeSpan frameInterval,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(frameInterval);
            do
            {
                var pixels = await frameProvider(cancellationToken).ConfigureAwait(false);
                await ApplyFrameAsync(
                    new OpenRazerSoftwareLightingFrame(sourceRows, sourceColumns, pixels),
                    cancellationToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunFrameAsync(
        Func<CancellationToken, ValueTask<OpenRazerSoftwareLightingFrame?>> frameProvider,
        TimeSpan frameInterval,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(frameInterval);
            do
            {
                if (await frameProvider(cancellationToken).ConfigureAwait(false) is { } frame)
                    await ApplyFrameAsync(frame, cancellationToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ClearStateAsync(CancellationToken cancellationToken)
    {
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _lastFrames.Clear(); }
        finally { _applyGate.Release(); }
    }

    private static OpenRazerColor[] Resize(
        OpenRazerSoftwareLightingFrame source,
        int rows,
        int columns)
    {
        var result = new OpenRazerColor[checked(rows * columns)];
        for (var row = 0; row < rows; row++)
        {
            var sourceRow = source.Rows == 1 || rows == 1
                ? 0
                : (int)Math.Round(row * (source.Rows - 1d) / (rows - 1d));
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = source.Columns == 1 || columns == 1
                    ? 0
                    : (int)Math.Round(column * (source.Columns - 1d) / (columns - 1d));
                var color = source[sourceRow, sourceColumn];
                result[row * columns + column] = new OpenRazerColor(color.Red, color.Green, color.Blue);
            }
        }
        return result;
    }

    private sealed record AppliedFrame(OpenRazerDeviceConnection Connection, OpenRazerColor[] Frame);
}
