using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// A device-independent software-lighting frame. The producer creates one frame
/// per tick; the runtime projects that frame once for every compatible device.
/// </summary>
internal readonly record struct RazerHidSoftwareLightingFrame
{
    internal int Rows { get; }
    internal int Columns { get; }
    internal IReadOnlyList<RazerRgb> Pixels { get; }

    internal RazerHidSoftwareLightingFrame(int rows, int columns, IReadOnlyList<RazerRgb> pixels)
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
/// Drives software-generated frames to compatible RazerHid matrix devices.
/// Audio or other expensive sampling belongs in the frame provider and is
/// intentionally invoked once per tick, rather than once per device.
/// </summary>
internal sealed class RazerHidSoftwareLightingRuntime : IAsyncDisposable
{
    private readonly RazerHidDeviceService _service;
    private readonly Func<IReadOnlyList<RazerHidDeviceConnection>> _connections;
    private readonly Func<RazerHidDeviceConnection, bool> _isEnabled;
    private readonly Func<RazerHidDeviceConnection, bool> _isOverrideEnabled;
    private readonly Func<RazerHidDeviceConnection, bool> _targetFilter;
    private readonly string? _instanceId;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly Dictionary<string, AppliedFrame> _lastFrames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lifecycleGate = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private int _disposed;

    internal RazerHidSoftwareLightingRuntime(
        RazerHidDeviceService service,
        Func<IReadOnlyList<RazerHidDeviceConnection>> connections,
        Func<RazerHidDeviceConnection, bool>? isEnabled = null,
        Func<RazerHidDeviceConnection, bool>? isOverrideEnabled = null,
        string? instanceId = null,
        Func<RazerHidDeviceConnection, bool>? targetFilter = null)
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
        Func<CancellationToken, ValueTask<RazerHidSoftwareLightingFrame?>> frameProvider,
        TimeSpan frameInterval,
        CancellationToken cancellationToken = default) =>
        StartFrameLoopAsync(frameProvider, frameInterval, cancellationToken);

    private Task StartFrameLoopAsync(
        Func<CancellationToken, ValueTask<RazerHidSoftwareLightingFrame?>> frameProvider,
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
        RazerHidSoftwareLightingFrame source,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var targets = _connections()
            .Where(connection => connection.IsReady &&
                connection.Definition.MatrixDimensions is not null &&
                connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite) &&
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
            // RazerHid writes one row per HID transaction. Keep small matrices
            // responsive, but avoid driving large/slow devices at Blade's cadence.
            using var timer = new PeriodicTimer(GetEffectiveFrameInterval(frameInterval));
            do
            {
                var pixels = await frameProvider(cancellationToken).ConfigureAwait(false);
                await ApplyFrameAsync(
                    new RazerHidSoftwareLightingFrame(sourceRows, sourceColumns, pixels),
                    cancellationToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunFrameAsync(
        Func<CancellationToken, ValueTask<RazerHidSoftwareLightingFrame?>> frameProvider,
        TimeSpan frameInterval,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(GetEffectiveFrameInterval(frameInterval));
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

    private TimeSpan GetEffectiveFrameInterval(TimeSpan requested)
    {
        var targets = _connections()
            .Where(connection => connection.IsReady &&
                connection.Definition.MatrixDimensions is not null &&
                connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite) &&
                _isEnabled(connection) &&
                _isOverrideEnabled(connection) &&
                _targetFilter(connection) &&
                (_instanceId is null || StringComparer.OrdinalIgnoreCase.Equals(connection.InstanceId, _instanceId)))
            .ToArray();
        if (targets.Length == 0)
        {
            return requested;
        }

        var slowestDeviceWait = targets.Max(connection => connection.Definition.DeviceWait);
        var rows = targets.Max(connection => connection.Definition.MatrixDimensions!.Rows);
        var transportFloor = slowestDeviceWait * Math.Max(1, (int)rows) + TimeSpan.FromMilliseconds(1);
        // Keep the requested cadence unless the actual HID transfer time cannot sustain it.
        return requested > transportFloor ? requested : transportFloor;
    }

    private async Task ClearStateAsync(CancellationToken cancellationToken)
    {
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _lastFrames.Clear(); }
        finally { _applyGate.Release(); }
    }

    private static RazerHidColor[] Resize(
        RazerHidSoftwareLightingFrame source,
        int rows,
        int columns)
    {
        var result = new RazerHidColor[checked(rows * columns)];
        for (var row = 0; row < rows; row++)
        {
            var sourceRow = ResolveSourceIndex(row, source.Rows, rows);
            if (sourceRow < 0) continue;
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = ResolveSourceIndex(column, source.Columns, columns);
                if (sourceColumn < 0) continue;
                var color = source[sourceRow, sourceColumn];
                result[row * columns + column] = new RazerHidColor(color.Red, color.Green, color.Blue);
            }
        }
        return result;
    }

    /// <summary>
    /// A sparse device matrix keeps the source rows/columns 1:1 instead of
    /// stretching the picture into media or underglow rows it never had.
    /// </summary>
    private static int ResolveSourceIndex(int index, int sourceCount, int targetCount)
    {
        if (targetCount > sourceCount)
        {
            return index < sourceCount ? index : -1;
        }

        return sourceCount == 1 || targetCount == 1
            ? 0
            : (int)Math.Round(index * (sourceCount - 1d) / (targetCount - 1d));
    }

    private sealed record AppliedFrame(RazerHidDeviceConnection Connection, RazerHidColor[] Frame);
}
