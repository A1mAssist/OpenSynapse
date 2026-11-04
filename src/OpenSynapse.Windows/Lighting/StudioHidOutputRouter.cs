using System.Collections.Concurrent;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Renders the studio stack once per HID target and writes each frame through
/// RazerHid. Matrix devices use frame writes; devices without frame support use
/// one representative Static zone color. Per-device frame sources keep dimensions,
/// masks, and dedup state independent while every device shares the caller's timeline.
/// </summary>
public sealed partial class StudioHidOutputRouter : IAsyncDisposable
{
    private readonly RazerHidDeviceService _service;
    private readonly Func<IReadOnlyList<RazerHidDeviceConnection>> _connections;
    private readonly Func<RazerHidDeviceConnection, bool> _isEnabled;
    private readonly Func<RazerHidDeviceConnection, bool> _isOverrideEnabled;
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly StudioHidInputBridge _inputBridge;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly ConcurrentDictionary<string, DeviceOutput> _outputs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StudioLedLayout> _layouts =
        new(StringComparer.OrdinalIgnoreCase);
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
        _inputBridge = new StudioHidInputBridge(this);
        _inputBridge.Attach();
    }

    /// <summary>Raised off the UI thread when one device rejects a frame.</summary>
    public event Action<string, Exception>? WriteFailed;

    /// <summary>Raised off the UI thread when a stop cannot restore a device's own effect.</summary>
    public event Action<string, Exception>? RestoreFailed;

    /// <summary>Connections the next apply would write to.</summary>
    public IReadOnlyList<RazerHidDeviceConnection> GetTargets() =>
        _connections().Where(IsTarget).ToArray();

    /// <summary>
    /// The zone a single-color studio frame should drive: the default zone when
    /// it accepts Static, otherwise the first zone that does. A device with no
    /// Static-capable zone cannot represent a studio color.
    /// </summary>
    public static RazerHidLedZone? FindStaticZone(RazerHidDeviceConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.Capabilities.Contains(RazerHidBackendCapability.LightingEffectWrite))
        {
            return null;
        }

        if (connection.LightingZones.TryGetValue(connection.Definition.DefaultLedZone, out var defaultZone) &&
            defaultZone.LightingEffects.Contains(RazerHidLightingEffect.Static))
        {
            return connection.Definition.DefaultLedZone;
        }

        foreach (var (zone, capabilities) in connection.LightingZones.OrderBy(pair => pair.Key))
        {
            if (capabilities.LightingEffects.Contains(RazerHidLightingEffect.Static)) return zone;
        }

        return null;
    }

    /// <summary>
    /// Binds the manifest matrix/device mapping to one HID instance. The next
    /// apply rebuilds that device's frame source when the layout changes.
    /// </summary>
    public void SetLayout(string instanceId, StudioLedLayout? layout)
    {
        if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrWhiteSpace(instanceId)) return;
        if (layout is null || layout.Leds.Count == 0) _layouts.TryRemove(instanceId, out _);
        else _layouts[instanceId] = layout;
    }

    /// <summary>
    /// Replaces the layer builder and re-arms a stopped router. The builder runs
    /// once per device, so region masks can use that matrix's dimensions.
    /// </summary>
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
    /// Applies one key transition to the frame source of a single device. The
    /// input uses that device's manifest matrix coordinates. Unknown instances
    /// and devices that have not been applied are ignored.
    /// </summary>
    public int HandleInput(string instanceId, StudioTriggerInput input, double nowSeconds)
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _stopped) != 0 ||
            string.IsNullOrWhiteSpace(instanceId))
        {
            return 0;
        }

        return _outputs.TryGetValue(instanceId, out var output)
            ? output.Source.HandleInput(input, nowSeconds)
            : 0;
    }

    /// <summary>
    /// Renders and writes one frame to every ready target. Returns false when no
    /// compatible device is connected or the router has been stopped.
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
                _outputs.TryRemove(staleId, out _);
            }

            foreach (var target in targets)
            {
                var isMatrix = target.Definition.MatrixDimensions is not null &&
                    target.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite);
                // A device that cannot accept a frame gets one representative cell.
                var dimensions = isMatrix
                    ? target.Definition.MatrixDimensions!
                    : new RazerHidMatrixDimensions(1, 1);
                var layout = isMatrix ? GetLayout(target.InstanceId) : null;
                var matrixRows = layout is { MatrixRows: > 0, MatrixColumns: > 0 }
                    ? layout.MatrixRows
                    : dimensions.Rows;
                var matrixColumns = layout is { MatrixRows: > 0, MatrixColumns: > 0 }
                    ? layout.MatrixColumns
                    : dimensions.Columns;
                _outputs.TryGetValue(target.InstanceId, out var previousOutput);
                var output = previousOutput;

                // A reconnect is a new endpoint, and a manifest reload can replace the
                // layout object; either invalidates this device's frame source and dedup state.
                if (output is null ||
                    !ReferenceEquals(output.Connection, target) ||
                    output.Rows != dimensions.Rows ||
                    output.Columns != dimensions.Columns ||
                    !ReferenceEquals(output.Layout, layout))
                {
                    output = new DeviceOutput(
                        new StudioDeviceFrameSource(
                            matrixRows,
                            matrixColumns,
                            dimensions.Rows,
                            dimensions.Columns,
                            layout,
                            _inputProvider),
                        dimensions.Rows,
                        dimensions.Columns,
                        layout)
                    {
                        Connection = target,
                    };
                    if (previousOutput is not null &&
                        ReferenceEquals(previousOutput.Connection, target))
                    {
                        output.Baseline = previousOutput.Baseline;
                        output.BaselineAttempted = previousOutput.BaselineAttempted;
                        output.Touched = previousOutput.Touched;
                    }
                    _outputs[target.InstanceId] = output;
                }

                if (output.Connection is null)
                {
                    output.Connection = target;
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

                if (!output.BaselineAttempted)
                {
                    output.Baseline = await CaptureBaselineAsync(target, cancellationToken).ConfigureAwait(false);
                    output.BaselineAttempted = true;
                }

                try
                {
                    output.Touched = true;
                    if (isMatrix)
                    {
                        await _service.SetCustomFrameAsync(target, frame, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await SetStaticZoneColorAsync(target, frame[0], cancellationToken)
                            .ConfigureAwait(false);
                    }

                    output.Frame = frame;
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                    // One endpoint dropping out must not skip the remaining devices.
                    // A failed write may have landed partially, so force a full rewrite next tick.
                    output.Frame = null;
                    WriteFailed?.Invoke(target.Definition.DisplayName, exception);
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
            var failures = new List<string>();
            foreach (var (key, output) in _outputs.ToArray())
            {
                if (!output.Touched || output.Connection is null)
                {
                    _outputs.TryRemove(key, out _);
                    continue;
                }

                // Restore the endpoint that is live now, not a stale path from before a
                // reconnect. A device that is gone keeps its last state and is skipped.
                var connection = output.Connection;
                var current = _connections().FirstOrDefault(candidate =>
                    candidate.IsReady &&
                    candidate.Definition.ProductId == connection.Definition.ProductId &&
                    string.Equals(candidate.InstanceId, connection.InstanceId,
                        StringComparison.OrdinalIgnoreCase));
                if (current is null)
                {
                    _outputs.TryRemove(key, out _);
                    continue;
                }

                if (output.Baseline is { } baseline)
                {
                    var restoreFailures = await RestoreAsync(current, baseline, cancellationToken)
                        .ConfigureAwait(false);
                    if (restoreFailures.Count == 0)
                    {
                        _outputs.TryRemove(key, out _);
                        continue;
                    }

                    ReportRestoreFailure(current, restoreFailures, failures);
                    // Keep the entry so a later Stop retries the capture-based restore.
                    continue;
                }

                // No readable original effect. The extracted manifests document Spectrum
                // as quickEffectOnExit, so use the official exit state instead of leaving
                // the last Studio frame on the device.
                bool exitApplied;
                try
                {
                    exitApplied = await ApplyOfficialExitEffectAsync(current, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (IsDeviceFailure(exception))
                {
                    ReportRestoreFailure(current, [exception], failures);
                    // Keep the entry so a later Stop retries the exit effect.
                    continue;
                }

                if (exitApplied)
                {
                    _outputs.TryRemove(key, out _);
                    RestoreFailed?.Invoke(current.Definition.DisplayName, new NotSupportedException(
                        "The device exposes no readable original effect; the documented Studio exit effect (Spectrum) was applied."));
                    continue;
                }

                _outputs.TryRemove(key, out _);
                var unsupported = new NotSupportedException(
                    "The device exposes no readable original effect and does not admit the documented Studio exit effect.");
                RestoreFailed?.Invoke(current.Definition.DisplayName, unsupported);
                failures.Add($"{current.Definition.DisplayName}: {unsupported.Message}");
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Could not restore the original lighting for {string.Join("; ", failures)}.");
            }
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
        catch (Exception exception) when (IsDeviceFailure(exception))
        {
            // Disposal has no caller to report to; RestoreFailed already recorded it.
        }
        finally
        {
            _inputBridge.Dispose();
            _applyGate.Dispose();
        }
    }

    private Task SetStaticZoneColorAsync(
        RazerHidDeviceConnection connection,
        RazerHidColor color,
        CancellationToken cancellationToken)
    {
        var zone = FindStaticZone(connection) ?? throw new NotSupportedException(
            $"RazerHid device 1532:{connection.Definition.ProductId:X4} has no static lighting zone.");
        return _service.SetLightingAsync(connection, new RazerHidLightingSettings(
            RazerHidLightingEffect.Static,
            Primary: color,
            Storage: connection.Definition.DefaultStorage,
            Zone: zone), cancellationToken);
    }

    internal StudioLedLayout? GetLayout(string? instanceId) =>
        instanceId is not null &&
        _layouts.TryGetValue(instanceId, out var layout) &&
        layout.Leds.Count > 0
            ? layout
            : null;

    private bool IsTarget(RazerHidDeviceConnection connection) =>
        connection.IsReady &&
        _isEnabled(connection) &&
        _isOverrideEnabled(connection) &&
        (IsMatrixTarget(connection) || FindStaticZone(connection) is not null);

    private static bool IsMatrixTarget(RazerHidDeviceConnection connection) =>
        connection.Definition.MatrixDimensions is not null &&
        connection.Capabilities.Contains(RazerHidBackendCapability.MatrixFrameWrite);

    private sealed class DeviceOutput
    {
        internal DeviceOutput(
            StudioDeviceFrameSource source,
            int rows,
            int columns,
            StudioLedLayout? layout)
        {
            Source = source;
            Rows = rows;
            Columns = columns;
            Layout = layout;
        }

        internal StudioDeviceFrameSource Source { get; }
        internal int Rows { get; }
        internal int Columns { get; }
        internal StudioLedLayout? Layout { get; }
        internal RazerHidDeviceConnection? Connection { get; set; }
        internal RazerHidColor[]? Frame { get; set; }
        internal long LayersVersion { get; set; } = -1;
        internal DeviceBaseline? Baseline { get; set; }
        internal bool BaselineAttempted { get; set; }
        internal bool Touched { get; set; }
    }

}
