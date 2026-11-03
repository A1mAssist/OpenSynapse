using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Renders the studio stack once per matrix device and writes each frame through
/// RazerHid. Per-device frame sources keep dimensions, masks, and dedup state
/// independent while every device shares the caller's timeline.
/// </summary>
public sealed class StudioHidOutputRouter : IAsyncDisposable
{
    private readonly RazerHidDeviceService _service;
    private readonly Func<IReadOnlyList<RazerHidDeviceConnection>> _connections;
    private readonly Func<RazerHidDeviceConnection, bool> _isEnabled;
    private readonly Func<RazerHidDeviceConnection, bool> _isOverrideEnabled;
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly Dictionary<string, DeviceOutput> _outputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _updateGate = new();
    private Func<RazerHidDeviceConnection, IReadOnlyList<StudioLayerRender>>? _layersForDevice;
    private long _layersVersion;
    private int _stopped;
    private int _disposed;

    public StudioHidOutputRouter(
        RazerHidDeviceService service,
        Func<IReadOnlyList<RazerHidDeviceConnection>> connections,
        Func<RazerHidDeviceConnection, bool>? isEnabled = null,
        Func<RazerHidDeviceConnection, bool>? isOverrideEnabled = null,
        Func<int, int, StudioEffectInput>? inputProvider = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _isEnabled = isEnabled ?? (static _ => true);
        _isOverrideEnabled = isOverrideEnabled ?? (static _ => true);
        _inputProvider = inputProvider;
    }

    /// <summary>
    /// Replaces the layer builder and re-arms a stopped router. The builder runs
    /// once per device, so region masks can use that matrix's dimensions.
    /// </summary>
    /// <summary>Connections the next apply would write to.</summary>
    public IReadOnlyList<RazerHidDeviceConnection> GetTargets() =>
        _connections().Where(IsTarget).ToArray();

    public void Update(Func<RazerHidDeviceConnection, IReadOnlyList<StudioLayerRender>> layersForDevice)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(layersForDevice);
        lock (_updateGate)
        {
            _layersForDevice = layersForDevice;
            _layersVersion++;
        }
        Volatile.Write(ref _stopped, 0);
    }

    /// <summary>
    /// Renders and writes one frame to every ready matrix device. Returns false
    /// when no compatible device is connected or the router has been stopped.
    /// </summary>
    public async Task<bool> ApplyAsync(TimeSpan elapsed, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var targets = _connections().Where(IsTarget).ToArray();
        if (targets.Length == 0) return false;

        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopped) != 0) return false;

            Func<RazerHidDeviceConnection, IReadOnlyList<StudioLayerRender>>? layersForDevice;
            long layersVersion;
            lock (_updateGate)
            {
                layersForDevice = _layersForDevice;
                layersVersion = _layersVersion;
            }

            var liveIds = targets.Select(target => target.InstanceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var staleId in _outputs.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            {
                _outputs.Remove(staleId);
            }

            foreach (var target in targets)
            {
                var dimensions = target.Definition.MatrixDimensions!;
                if (!_outputs.TryGetValue(target.InstanceId, out var output) ||
                    output.Rows != dimensions.Rows ||
                    output.Columns != dimensions.Columns)
                {
                    output = new DeviceOutput(
                        new StudioDeviceFrameSource(dimensions.Rows, dimensions.Columns, _inputProvider),
                        dimensions.Rows,
                        dimensions.Columns);
                    _outputs[target.InstanceId] = output;
                }

                // A reconnect is a new endpoint; its dedup state must not suppress the first frame.
                if (!ReferenceEquals(output.Connection, target))
                {
                    output.Connection = target;
                    output.Frame = null;
                    // Masks and effect parameters can be device-specific, so re-bind them
                    // even when the replacement reports the same matrix dimensions.
                    output.LayersVersion = -1;
                }

                if (output.LayersVersion != layersVersion)
                {
                    output.Source.Update(layersForDevice?.Invoke(target) ?? []);
                    output.LayersVersion = layersVersion;
                }

                var colors = await output.Source.RenderAsync(elapsed, cancellationToken).ConfigureAwait(false);
                var frame = new RazerHidColor[colors.Count];
                for (var index = 0; index < frame.Length; index++)
                {
                    var color = colors[index];
                    frame[index] = new RazerHidColor(color.Red, color.Green, color.Blue);
                }

                if (output.Frame is { } previous && previous.AsSpan().SequenceEqual(frame))
                {
                    continue;
                }

                try
                {
                    await _service.SetCustomFrameAsync(target, frame, cancellationToken).ConfigureAwait(false);
                    output.Frame = frame;
                }
                catch (Exception exception) when (IsDeviceWriteFailure(exception))
                {
                    // One endpoint dropping out must not skip the remaining devices.
                    // A failed write may have landed partially, so force a full rewrite next tick.
                    output.Frame = null;
                }
            }

            return true;
        }
        finally
        {
            _applyGate.Release();
        }
    }

    /// <summary>
    /// Stops accepting frames and waits for the in-flight write to finish.
    /// Call <see cref="Update"/> before the next apply to start a new session.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _stopped, 1);
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _outputs.Clear();
        }
        finally
        {
            _applyGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Dispose();
        }
    }

    private static bool IsDeviceWriteFailure(Exception exception) =>
        exception switch
        {
            ObjectDisposedException or OperationCanceledException => false,
            IOException or UnauthorizedAccessException or TimeoutException or
                InvalidOperationException or System.ComponentModel.Win32Exception or
                InvalidDataException or NotSupportedException => true,
            _ => false,
        };

    private bool IsTarget(RazerHidDeviceConnection connection) =>
        connection.IsReady &&
        connection.Definition.MatrixDimensions is not null &&
        connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite) &&
        _isEnabled(connection) &&
        _isOverrideEnabled(connection);

    private sealed class DeviceOutput
    {
        internal DeviceOutput(StudioDeviceFrameSource source, int rows, int columns)
        {
            Source = source;
            Rows = rows;
            Columns = columns;
        }

        internal StudioDeviceFrameSource Source { get; }
        internal int Rows { get; }
        internal int Columns { get; }
        internal RazerHidDeviceConnection? Connection { get; set; }
        internal RazerHidColor[]? Frame { get; set; }
        internal long LayersVersion { get; set; } = -1;
    }
}
