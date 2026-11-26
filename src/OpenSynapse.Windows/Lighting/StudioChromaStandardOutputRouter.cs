using System.Collections.Concurrent;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Renders the studio stack once per class-0x10 Chroma device and writes the
/// representative colour through its Static effect. These devices address no
/// individual LEDs, so one colour covers the whole device.
/// </summary>
public sealed class StudioChromaStandardOutputRouter : IAsyncDisposable
{
    // SetEffectAsync reads, writes, then reads back; the 25 fps pump would flood a
    // feature endpoint Razer's own UI drives at human speed.
    private const int MinWriteIntervalMilliseconds = 200;

    private readonly Func<IReadOnlyList<RazerChromaDeviceConnection>> _connections;
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly Func<RazerChromaDeviceConnection, RazerChromaStandardService> _createService;
    private readonly ConcurrentDictionary<string, DeviceOutput> _outputs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly object _updateGate = new();
    private Func<RazerChromaDeviceConnection, IReadOnlyList<StudioLayerRender>>? _layersForDevice;
    private StudioWorldRenderer? _world;
    private long _layersVersion;
    private HashSet<string>? _targetIds;
    private int _stopped;
    private int _disposed;

    public StudioChromaStandardOutputRouter(
        Func<IReadOnlyList<RazerChromaDeviceConnection>> connections,
        Func<int, int, StudioEffectInput>? inputProvider = null)
        : this(connections, target => new RazerChromaStandardService(
            new RazerFeatureTransport(), target.DevicePath, TimeSpan.FromMilliseconds(20),
            channel: target.Channel, productId: target.ProductId), inputProvider)
    {
    }

    internal StudioChromaStandardOutputRouter(
        Func<IReadOnlyList<RazerChromaDeviceConnection>> connections,
        Func<RazerChromaDeviceConnection, RazerChromaStandardService> createService,
        Func<int, int, StudioEffectInput>? inputProvider = null)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _createService = createService ?? throw new ArgumentNullException(nameof(createService));
        _inputProvider = inputProvider;
    }

    /// <summary>Raised off the UI thread when one device rejects a colour.</summary>
    public event Action<string, Exception>? WriteFailed;

    /// <summary>Raised off the UI thread when a device cannot be returned to its own effect.</summary>
    public event Action<string, Exception>? RestoreFailed;

    public IReadOnlyCollection<string> OwnedInstanceIds =>
        _outputs.Where(pair => pair.Value.Touched).Select(pair => pair.Key).ToArray();

    /// <summary>Optional card restore, called under the gate for live owned targets only.</summary>
    public Func<IReadOnlyCollection<string>, Task>? RestoreTargetsAsync { get; set; }

    /// <summary>Devices the next apply would write to.</summary>
    public IReadOnlyList<RazerChromaDeviceConnection> GetTargets() =>
        _connections().Where(target => IsSelected(target.DevicePath)).ToArray();

    /// <summary>Null means All; empty selects nothing. IDs are DevicePaths.</summary>
    public async Task SetTargetsAsync(
        IReadOnlyCollection<string>? targetIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var selected = targetIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _targetIds, selected);
            await RestoreOutputsAsync(false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private bool IsSelected(string id) => Volatile.Read(ref _targetIds)?.Contains(id) ?? true;

    /// <summary>Attaches the shared world canvas; null keeps the per-device renderer.</summary>
    public void AttachWorld(StudioWorldRenderer world) =>
        _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>
    /// Replaces the per-device layer builder and re-arms a stopped router. The
    /// builder runs once per device, so region masks use that device's dimensions.
    /// </summary>
    public void Update(Func<RazerChromaDeviceConnection, IReadOnlyList<StudioLayerRender>> layersForDevice)
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
    /// Renders and writes one colour to every connected device. Returns false when
    /// no device is attached or the router has been stopped.
    /// </summary>
    public async Task<bool> ApplyAsync(TimeSpan elapsed, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopped) != 0) return false;
            await RestoreOutputsAsync(false, cancellationToken).ConfigureAwait(false);
            var targets = GetTargets();
            if (targets.Count == 0) return false;

            Func<RazerChromaDeviceConnection, IReadOnlyList<StudioLayerRender>>? layersForDevice;
            long layersVersion;
            lock (_updateGate)
            {
                layersForDevice = _layersForDevice;
                layersVersion = _layersVersion;
            }

            var now = Environment.TickCount64;
            var worldGeneration = _world?.Generation ?? 0;
            foreach (var target in targets)
            {
                _outputs.TryGetValue(target.DevicePath, out var output);
                if (output is not null && output.Source is not StudioWorldDeviceFrameSource &&
                    _world?.Canvas?.HasDevice(target.DevicePath) != true)
                    output.WorldGeneration = worldGeneration;
                if (output is null ||
                    output.Connection != target ||
                    output.WorldGeneration != worldGeneration)
                {
                    var previous = output;
                    var source = _world?.CreateSource(target.DevicePath, 1, 1, 1, 1, null)
                        ?? new StudioDeviceFrameSource(1, 1, _inputProvider);
                    output = new DeviceOutput(source)
                    {
                        Connection = target,
                        Service = _createService(target),
                        WorldGeneration = worldGeneration,
                    };
                    if (previous is not null)
                    {
                        output.Baseline = previous.Baseline;
                        output.BaselineAttempted = previous.BaselineAttempted;
                        output.Touched = previous.Touched;
                    }
                    _outputs[target.DevicePath] = output;
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
                var color = colors.Count > 0 ? colors[0] : default;
                if (output.Color == color) continue;
                if (output.Attempted && now - output.LastAttemptTimestamp < MinWriteIntervalMilliseconds)
                {
                    continue;
                }

                output.Attempted = true;
                output.LastAttemptTimestamp = now;

                var service = output.Service!;

                try
                {
                    if (!output.BaselineAttempted)
                    {
                        output.Baseline = await CaptureBaselineAsync(service, cancellationToken).ConfigureAwait(false);
                        output.BaselineAttempted = true;
                    }

                    output.Touched = true;
                    await service.SetEffectAsync(
                        RazerChromaEffect.Static,
                        [color.Red, color.Green, color.Blue],
                        cancellationToken).ConfigureAwait(false);
                    output.Color = color;
                }
                catch (Exception exception) when (IsWriteFailure(exception))
                {
                    // One device dropping out must not skip the rest, and a failed write
                    // may have landed partially, so force the full colour again next tick.
                    output.Color = null;
                    WriteFailed?.Invoke(target.Name, exception);
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
    /// Stops accepting colours and returns each device to the effect it had before
    /// the studio took it over. A failed restore keeps the entry so the next stop retries.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _stopped, 1);
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RestoreOutputsAsync(true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async Task RestoreOutputsAsync(bool all, CancellationToken cancellationToken)
    {
        foreach (var (key, output) in _outputs.ToArray())
        {
            if (!all && IsSelected(key)) continue;
            output.Color = null;
            output.Attempted = false;
            if (!output.Touched)
            {
                _outputs.TryRemove(key, out _);
                continue;
            }

            var current = _connections().FirstOrDefault(target =>
                string.Equals(target.DevicePath, key, StringComparison.OrdinalIgnoreCase));
            if (current is null) continue;
            try
            {
                if (RestoreTargetsAsync is { } restore)
                    await restore([key]).ConfigureAwait(false);
                else if (output.Baseline is { } baseline)
                    await _createService(current).SetEffectAsync(baseline.Effect, baseline.Colors, cancellationToken)
                        .ConfigureAwait(false);
                else
                    throw new NotSupportedException("The original Chroma effect could not be read.");
                _outputs.TryRemove(key, out _);
            }
            catch (Exception exception) when (IsWriteFailure(exception))
            {
                RestoreFailed?.Invoke(key, exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            // Disposal has no caller to report to; RestoreFailed already recorded it.
        }
        finally
        {
            _applyGate.Dispose();
        }
    }

    private static async Task<Baseline?> CaptureBaselineAsync(
        RazerChromaStandardService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await service.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            return state.Effect is { } effect
                ? new Baseline(effect, state.Colors.ToArray())
                : null;
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            // Without a readable original effect the last studio colour stays.
            return null;
        }
    }

    private static bool IsWriteFailure(Exception exception) =>
        exception switch
        {
            ObjectDisposedException or OperationCanceledException => false,
            IOException or UnauthorizedAccessException or TimeoutException or
                InvalidOperationException or System.ComponentModel.Win32Exception or
                System.Runtime.InteropServices.COMException or
                InvalidDataException or NotSupportedException => true,
            _ => false,
        };

    private readonly record struct Baseline(RazerChromaEffect Effect, byte[] Colors);

    private sealed class DeviceOutput
    {
        internal DeviceOutput(StudioDeviceFrameSource source)
        {
            Source = source;
        }

        internal StudioDeviceFrameSource Source { get; }
        internal RazerChromaDeviceConnection? Connection { get; set; }
        internal RazerChromaStandardService? Service { get; set; }
        internal RazerRgb? Color { get; set; }
        internal long LastAttemptTimestamp { get; set; }
        internal bool Attempted { get; set; }
        internal long LayersVersion { get; set; } = -1;
        internal long WorldGeneration { get; set; } = -1;
        internal Baseline? Baseline { get; set; }
        internal bool BaselineAttempted { get; set; }
        internal bool Touched { get; set; }
        internal bool ClockStarted { get; set; }
    }
}
