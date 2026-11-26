using System.Collections.Concurrent;
using System.Diagnostics;
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
    private StudioWorldRenderer? _world;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly ConcurrentDictionary<string, DeviceOutput> _outputs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StudioLedLayout> _layouts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _updateGate = new();
    private readonly object _inputGate = new();
    private readonly Dictionary<string, HashSet<(int Row, int Column)>> _heldKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private Func<RazerHidDeviceConnection, IReadOnlyList<StudioLayerRender>>? _layersForDevice;
    private long _layersVersion;
    private HashSet<string>? _targetIds;
    private double _inputSeconds;
    private long _inputTimestamp;
    private int _stopped;
    private int _disposed;

    /// <summary>
    /// A wireless keyboard drops a static custom frame after its idle timeout.
    /// Rewriting an unchanged frame keeps the custom effect armed.
    /// </summary>
    internal int KeepAliveIntervalMilliseconds { get; set; } = 2000;

    // Keep input on the frame timeline, including time spent waiting for HID writes.
    internal double InputSeconds
    {
        get
        {
            lock (_updateGate)
                return _inputTimestamp == 0 ? 0 :
                    _inputSeconds + Stopwatch.GetElapsedTime(_inputTimestamp).TotalSeconds;
        }
    }

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

    public Func<string, Task>? BeforeTakeOverAsync { get; set; }

    /// <summary>Mapped physical input, also available to the preview before Apply.</summary>
    internal event Action<string, StudioTriggerInput>? InputObserved;

    internal void ObserveInput(string instanceId, StudioTriggerInput input) =>
        InputObserved?.Invoke(instanceId, input);

    /// <summary>Connections the next apply would write to.</summary>
    public IReadOnlyList<RazerHidDeviceConnection> GetTargets() =>
        _connections().Where(IsTarget).ToArray();

    /// <summary>Attaches the shared world canvas; null keeps the per-device renderer.</summary>
    public void AttachWorld(StudioWorldRenderer world) =>
        _world = world ?? throw new ArgumentNullException(nameof(world));

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
        if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrWhiteSpace(instanceId))
        {
            return 0;
        }

        lock (_inputGate)
        {
            if (!_heldKeys.TryGetValue(instanceId, out var held))
                _heldKeys[instanceId] = held = [];
            if (input.IsDown) held.Add((input.Row, input.Column));
            else held.Remove((input.Row, input.Column));
            return Volatile.Read(ref _stopped) == 0 && IsSelected(instanceId) && _outputs.TryGetValue(instanceId, out var output)
                ? output.Source.HandleInput(input, nowSeconds)
                : 0;
        }
    }

    public int TriggerPlayback(string instanceId, StudioTriggerInput input, double nowSeconds) =>
        Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _stopped) == 0 &&
        IsSelected(instanceId) && _outputs.TryGetValue(instanceId, out var output)
            ? output.Source.TriggerPlayback(input, nowSeconds)
            : 0;

    /// <summary>
    /// Renders and writes one frame to every ready target. Returns false when no
    /// compatible device is connected or the router has been stopped.
    /// </summary>
    public async Task<bool> ApplyAsync(TimeSpan elapsed, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopped) != 0) return false;
            await RestoreOutputsAsync(false, true, cancellationToken).ConfigureAwait(false);
            var targets = _connections().Where(IsTarget).ToArray();
            if (targets.Length == 0) return false;
            var writers = CollapseSharedEndpoints(targets);
            var now = Environment.TickCount64;

            Func<RazerHidDeviceConnection, IReadOnlyList<StudioLayerRender>>? layersForDevice;
            long layersVersion;
            var worldGeneration = _world?.Generation ?? 0;
            lock (_updateGate)
            {
                _inputSeconds = Math.Max(0, elapsed.TotalSeconds);
                _inputTimestamp = Stopwatch.GetTimestamp();
                layersForDevice = _layersForDevice;
                layersVersion = _layersVersion;
            }

            foreach (var target in writers)
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

                if (output is not null && output.Source is not StudioWorldDeviceFrameSource &&
                    _world?.Canvas?.HasDevice(target.InstanceId) != true)
                    output.WorldGeneration = worldGeneration;

                // A reconnect is a new endpoint, and a manifest reload can replace the
                // layout object; either invalidates this device's frame source and dedup state.
                if (output is null ||
                    output.Connection is null ||
                    !output.Connection.IsSameEndpoint(target) ||
                    output.Rows != dimensions.Rows ||
                    output.Columns != dimensions.Columns ||
                    !ReferenceEquals(output.Layout, layout) ||
                    output.WorldGeneration != worldGeneration)
                {
                    var source = _world?.CreateSource(
                            target.InstanceId,
                            matrixRows,
                            matrixColumns,
                            dimensions.Rows,
                            dimensions.Columns,
                            layout)
                        ?? new StudioDeviceFrameSource(
                            matrixRows,
                            matrixColumns,
                            dimensions.Rows,
                            dimensions.Columns,
                            layout,
                            _inputProvider);
                    output = new DeviceOutput(
                        source,
                        dimensions.Rows,
                        dimensions.Columns,
                        layout)
                    {
                        Connection = target,
                        WorldGeneration = worldGeneration,
                    };
                    if (previousOutput is not null)
                    {
                        // The physical instance owns the baseline even when the endpoint
                        // object changes after a reconnect.
                        output.Baseline = previousOutput.Baseline;
                        output.BaselineAttempted = previousOutput.BaselineAttempted;
                        output.Touched = previousOutput.Touched;
                        output.TakeOverPrepared = previousOutput.TakeOverPrepared &&
                            previousOutput.Connection?.IsSameEndpoint(target) == true;
                    }
                    lock (_inputGate)
                    {
                        if (_heldKeys.TryGetValue(target.InstanceId, out var held))
                            output.Source.RestoreHeldKeys(held);
                        _outputs[target.InstanceId] = output;
                    }
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

                if (!output.ClockStarted)
                {
                    if (output.Source is not StudioWorldDeviceFrameSource && elapsed > TimeSpan.Zero)
                        await output.Source.RenderAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
                    output.ClockStarted = true;
                }
                var colors = await output.Source.RenderAsync(elapsed, cancellationToken).ConfigureAwait(false);
                var frame = new RazerHidColor[colors.Count];
                for (var index = 0; index < frame.Length; index++)
                {
                    var color = colors[index];
                    frame[index] = new RazerHidColor(color.Red, color.Green, color.Blue);
                }

                var unchanged = output.Frame is { } previous && previous.AsSpan().SequenceEqual(frame);
                if (unchanged && now - output.LastWriteTimestamp < KeepAliveIntervalMilliseconds)
                {
                    continue;
                }

                if (!output.BaselineAttempted)
                {
                    output.Baseline = await RazerHidLightingBaseline
                        .CaptureAsync(_service, target, cancellationToken).ConfigureAwait(false);
                    output.BaselineAttempted = true;
                }

                try
                {
                    output.Touched = true;
                    if (!output.TakeOverPrepared)
                    {
                        if (BeforeTakeOverAsync is { } prepare) await prepare(target.InstanceId).ConfigureAwait(false);
                        output.TakeOverPrepared = true;
                    }
                    if (isMatrix)
                    {
                        // SetCustomFrameAsync selects the custom frame first, so the
                        // keep-alive rewrite also re-arms a device that left the mode.
                        await _service.SetCustomFrameAsync(target, frame, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await SetStaticZoneColorAsync(target, frame[0], cancellationToken)
                            .ConfigureAwait(false);
                    }

                    output.Frame = frame;
                    output.LastWriteTimestamp = now;
                }
                catch (Exception exception) when (RazerHidLightingBaseline.IsDeviceFailure(exception))
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
    /// One HID endpoint holds one custom frame, so a dock and the mouse it links cannot
    /// both be driven: the last write wins and the other device stays dark. Keep the
    /// widest matrix per endpoint and let the rest follow the firmware's own effect.
    /// </summary>
    private static IReadOnlyList<RazerHidDeviceConnection> CollapseSharedEndpoints(
        IReadOnlyList<RazerHidDeviceConnection> targets)
    {
        if (targets.Count < 2) return targets;
        var writers = new List<RazerHidDeviceConnection>(targets.Count);
        foreach (var group in targets.GroupBy(
                     target => target.DevicePath ?? target.InstanceId,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() == 1)
            {
                writers.Add(group.First());
                continue;
            }

            var winner = group
                .OrderByDescending(target => MatrixArea(target.Definition.MatrixDimensions))
                .First();
            System.Diagnostics.Debug.WriteLine(
                $"studio: shared endpoint {group.Key} holds " +
                string.Join(" + ", group.Select(item => $"{item.Definition.ProductId:X4}")) +
                $"; writing {winner.Definition.ProductId:X4} only");
            writers.Add(winner);
        }

        return writers;
    }

    private static int MatrixArea(RazerHidMatrixDimensions? dimensions) =>
        dimensions is { Rows: > 0, Columns: > 0 } ? dimensions.Rows * dimensions.Columns : 0;

    /// <summary>
    /// Stops accepting frames and waits for the in-flight write to finish.
    /// Call <see cref="Update"/> before the next apply to start a new session.
    /// </summary>
    /// <param name="applyFallback">
    /// When false, no Spectrum exit effect is written for a device without a readable
    /// baseline; the caller is responsible for restoring configured lighting.
    /// </param>
    public async Task StopAsync(CancellationToken cancellationToken = default, bool applyFallback = true)
    {
        Volatile.Write(ref _stopped, 1);
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var failures = await RestoreOutputsAsync(true, applyFallback, cancellationToken).ConfigureAwait(false);
            if (failures.Count > 0)
                throw new InvalidOperationException(
                    $"Could not restore the original lighting for {string.Join("; ", failures)}.");
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async Task<List<string>> RestoreOutputsAsync(
        bool all, bool applyFallback, CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var (key, output) in _outputs.ToArray())
        {
            if (!all && IsSelected(key)) continue;
            output.Frame = null;
            output.TakeOverPrepared = false;
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
                // Keep the baseline so a later Stop can restore the reconnected instance.
                continue;
            }

            if (RestoreTargetsAsync is { } restore)
            {
                try
                {
                    await restore([key]).ConfigureAwait(false);
                    _outputs.TryRemove(key, out _);
                }
                catch (Exception exception) when (RazerHidLightingBaseline.IsDeviceFailure(exception))
                {
                    ReportRestoreFailure(current, [exception], failures);
                }
                continue;
            }

            if (output.Baseline is { } baseline)
            {
                var restoreFailures = await baseline.RestoreAsync(_service, current, cancellationToken)
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

            if (!applyFallback)
            {
                // The caller re-applies its configured lighting after the router stops.
                // Do not write the documented Spectrum exit effect over that restore.
                // No restore was performed; keep ownership until the caller can retry.
                continue;
            }

            // No readable original effect. The extracted manifests document Spectrum
            // as quickEffectOnExit, so use the official exit state instead of leaving
            // the last Studio frame on the device.
            bool exitApplied;
            try
            {
                exitApplied = await RazerHidLightingBaseline
                    .ApplyExitEffectAsync(_service, current, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (RazerHidLightingBaseline.IsDeviceFailure(exception))
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

            var unsupported = new NotSupportedException(
                "The device exposes no readable original effect and does not admit the documented Studio exit effect.");
            RestoreFailed?.Invoke(current.Definition.DisplayName, unsupported);
            failures.Add($"{current.Definition.DisplayName}: {unsupported.Message}");
        }

        return failures;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (RazerHidLightingBaseline.IsDeviceFailure(exception))
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
        IsSelected(connection.InstanceId) &&
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
        internal long LastWriteTimestamp { get; set; }
        internal long LayersVersion { get; set; } = -1;
        internal long WorldGeneration { get; set; } = -1;
        internal RazerHidLightingBaseline? Baseline { get; set; }
        internal bool BaselineAttempted { get; set; }
        internal bool Touched { get; set; }
        internal bool TakeOverPrepared { get; set; }
        internal bool ClockStarted { get; set; }
    }

}
