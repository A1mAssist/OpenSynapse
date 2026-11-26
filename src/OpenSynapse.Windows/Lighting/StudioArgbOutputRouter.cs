using System.Collections.Concurrent;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// One addressable channel rendered as a 1 x N strip.
/// </summary>
public readonly record struct StudioArgbPort(
    byte Channel,
    int LedCount,
    string InstanceId,
    ushort ProductId,
    string DisplayName);

/// <summary>
/// Renders the studio stack once per addressable channel and writes each frame
/// through the ARGB filter endpoint. It shares the studio compositor with the
/// matrix router but keeps its own port clocks and dedup state.
/// </summary>
public sealed class StudioArgbOutputRouter : IAsyncDisposable
{
    public const int PortCount = RazerHidArgbFrame.MaxChannel + 1;

    private readonly Func<CancellationToken, Task<RazerHidSpecialLightingConnection?>> _discover;
    private readonly Func<RazerHidSpecialLightingConnection, byte, IReadOnlyList<RazerHidColor>, CancellationToken, Task> _writeFrame;
    private readonly Func<RazerHidSpecialLightingConnection, CancellationToken, Task>? _prepare;
    private readonly Func<RazerHidSpecialLightingConnection, CancellationToken, Task> _restore;
    private readonly Func<IReadOnlyList<int>> _ledCounts;
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly Dictionary<byte, PortOutput> _outputs = [];
    private readonly object _updateGate = new();
    private Func<StudioArgbPort, IReadOnlyList<StudioLayerRender>>? _layersForPort;
    private StudioWorldRenderer? _world;
    private long _layersVersion;
    private StudioArgbPort[] _ports = [];
    private RazerHidSpecialLightingConnection? _connection;
    private string? _preparedKey;
    private HashSet<string>? _targetIds;
    private readonly ConcurrentDictionary<string, byte> _ownedIds = new(StringComparer.OrdinalIgnoreCase);
    private int _stopped;
    private int _disposed;

    public StudioArgbOutputRouter(
        RazerHidSpecialLightingService service,
        Func<IReadOnlyList<int>> ledCounts,
        Func<int, int, StudioEffectInput>? inputProvider = null)
        : this(
            CreateDiscover(service),
            service.SetArgbFrameAsync,
            service.SetArgbCustomFrameModeAsync,
            ledCounts,
            inputProvider,
            ApplyOfficialExitEffectAsync)
    {
    }

    internal StudioArgbOutputRouter(
        Func<CancellationToken, Task<RazerHidSpecialLightingConnection?>> discover,
        Func<RazerHidSpecialLightingConnection, byte, IReadOnlyList<RazerHidColor>, CancellationToken, Task> writeFrame,
        Func<RazerHidSpecialLightingConnection, CancellationToken, Task>? prepare,
        Func<IReadOnlyList<int>> ledCounts,
        Func<int, int, StudioEffectInput>? inputProvider = null,
        Func<RazerHidSpecialLightingConnection, CancellationToken, Task>? restore = null)
    {
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        _writeFrame = writeFrame ?? throw new ArgumentNullException(nameof(writeFrame));
        _prepare = prepare;
        _restore = restore ?? (static (_, _) => Task.CompletedTask);
        _ledCounts = ledCounts ?? throw new ArgumentNullException(nameof(ledCounts));
        _inputProvider = inputProvider;
    }

    /// <summary>Raised off the UI thread when one port rejects a frame.</summary>
    public event Action<string, Exception>? WriteFailed;

    public event Action<string, Exception>? RestoreFailed;

    public IReadOnlyCollection<string> OwnedInstanceIds => _ownedIds.Keys.ToArray();

    /// <summary>Optional card restore, called under the gate for live owned targets only.</summary>
    public Func<IReadOnlyCollection<string>, Task>? RestoreTargetsAsync { get; set; }

    /// <summary>Ports the next apply would write to.</summary>
    public IReadOnlyList<StudioArgbPort> GetTargets() =>
        Volatile.Read(ref _ports).Where(port => IsSelected(port.InstanceId)).ToArray();

    /// <summary>Null means All; empty selects nothing. IDs identify controllers, not channels.</summary>
    public async Task SetTargetsAsync(
        IReadOnlyCollection<string>? targetIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var selected = targetIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _targetIds, selected);
            await RestoreControllersAsync(false, false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private bool IsSelected(string id) => Volatile.Read(ref _targetIds)?.Contains(id) ?? true;

    /// <summary>Attaches the shared world canvas; null keeps the per-port renderer.</summary>
    public void AttachWorld(StudioWorldRenderer world) =>
        _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>
    /// Discovers the controller and enters its custom-frame mode once per
    /// endpoint. Call before <see cref="Update"/> when the user applies.
    /// </summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RazerHidSpecialLightingConnection? connection;
            try
            {
                connection = await _discover(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsWriteFailure(exception))
            {
                _connection = null;
                _outputs.Clear();
                _preparedKey = null;
                Volatile.Write(ref _ports, []);
                WriteFailed?.Invoke(
                    "Razer Chroma Addressable RGB Controller", exception);
                return false;
            }

            var counts = Normalize(_ledCounts());
            var sameConnection = SameConnection(_connection, connection);
            _connection = connection;

            if (connection is null)
            {
                _outputs.Clear();
                _preparedKey = null;
                Volatile.Write(ref _ports, []);
                return false;
            }

            if (connection.ControlDevicePath is null)
            {
                _connection = null;
                _outputs.Clear();
                _preparedKey = null;
                Volatile.Write(ref _ports, []);
                WriteFailed?.Invoke(
                    connection.DisplayName,
                    new InvalidOperationException(connection.Error ?? "The ARGB control endpoint is not available."));
                return false;
            }

            Volatile.Write(ref _ports, CreatePorts(connection, counts));

            if (!sameConnection)
            {
                _outputs.Clear();
                _preparedKey = null;
            }

            await RestoreControllersAsync(false, false, cancellationToken).ConfigureAwait(false);
            if (IsSelected(connection.InstanceId))
                await PrepareAsync(connection, cancellationToken).ConfigureAwait(false);

            return true;
        }
        finally
        {
            _applyGate.Release();
        }
    }

    /// <summary>
    /// Replaces the per-port layer builder and re-arms a stopped router. The
    /// builder runs before the frame pump, not on the UI object itself.
    /// </summary>
    public void Update(Func<StudioArgbPort, IReadOnlyList<StudioLayerRender>> layersForPort)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(layersForPort);
        lock (_updateGate)
        {
            _layersForPort = layersForPort;
            _layersVersion++;
        }
        Volatile.Write(ref _stopped, 0);
    }

    /// <summary>
    /// Renders and writes one frame to every configured port. Returns false
    /// when no ready controller is connected or the router has been stopped.
    /// </summary>
    public async Task<bool> ApplyAsync(TimeSpan elapsed, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopped) != 0) return false;
            await RestoreControllersAsync(false, false, cancellationToken).ConfigureAwait(false);
            if (_connection is not { } connection || !IsSelected(connection.InstanceId))
            {
                return false;
            }

            await PrepareAsync(connection, cancellationToken).ConfigureAwait(false);

            Func<StudioArgbPort, IReadOnlyList<StudioLayerRender>>? layersForPort;
            long layersVersion;
            var worldGeneration = _world?.Generation ?? 0;
            lock (_updateGate)
            {
                layersForPort = _layersForPort;
                layersVersion = _layersVersion;
            }

            foreach (var port in _ports)
            {
                _outputs.TryGetValue(port.Channel, out var output);
                if (output is not null && output.Source is not StudioWorldDeviceFrameSource &&
                    _world?.Canvas?.HasDevice(port.InstanceId) != true)
                    output.WorldGeneration = worldGeneration;
                if (output is null ||
                    output.LedCount != port.LedCount ||
                    output.WorldGeneration != worldGeneration)
                {
                    var oldOutput = output;
                    var source = _world?.CreateSource(
                            port.InstanceId,
                            1,
                            port.LedCount,
                            1,
                            port.LedCount,
                            null,
                            port.Channel)
                        ?? new StudioDeviceFrameSource(1, port.LedCount, _inputProvider);
                    output = new PortOutput(
                        port.LedCount,
                        source)
                    {
                        WorldGeneration = worldGeneration,
                    };
                    if (oldOutput is not null)
                    {
                        output.Frame = oldOutput.Frame;
                    }
                    _outputs[port.Channel] = output;
                }

                if (output.LayersVersion != layersVersion)
                {
                    output.Source.Update(layersForPort?.Invoke(port) ?? []);
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

                if (output.Frame is { } previous && previous.AsSpan().SequenceEqual(frame))
                {
                    continue;
                }

                try
                {
                    _ownedIds[connection.InstanceId] = 0;
                    await _writeFrame(connection, port.Channel, frame, cancellationToken).ConfigureAwait(false);
                    output.Frame = frame;
                }
                catch (Exception exception) when (IsWriteFailure(exception))
                {
                    // One strip failing must not skip the other ports.
                    output.Frame = null;
                    WriteFailed?.Invoke(PortName(connection, port.Channel), exception);
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
    /// Stops accepting frames, leaves custom-frame mode through the documented
    /// exit effect, and waits for the in-flight write.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _stopped, 1);
        await _applyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RestoreControllersAsync(true, true, cancellationToken).ConfigureAwait(false);
            _outputs.Clear();
            _preparedKey = null;
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async Task PrepareAsync(RazerHidSpecialLightingConnection connection, CancellationToken cancellationToken)
    {
        var key = ConnectionKey(connection);
        if (_prepare is null || string.Equals(_preparedKey, key, StringComparison.Ordinal)) return;
        _ownedIds[connection.InstanceId] = 0;
        try
        {
            await _prepare(connection, cancellationToken).ConfigureAwait(false);
            _preparedKey = key;
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            _preparedKey = null;
            WriteFailed?.Invoke(connection.DisplayName, exception);
        }
    }

    private async Task RestoreControllersAsync(bool all, bool throwOnFailure, CancellationToken cancellationToken)
    {
        var departing = _ownedIds.Keys.Where(id => all || !IsSelected(id)).ToArray();
        if (departing.Length == 0) return;
        if (_connection is { } old && departing.Contains(old.InstanceId, StringComparer.OrdinalIgnoreCase))
        {
            _outputs.Clear();
            _preparedKey = null;
        }
        try
        {
            // Discovery supplies the live endpoint; offline controllers retain ownership.
            var current = await _discover(cancellationToken).ConfigureAwait(false);
            if (current is null || !departing.Contains(current.InstanceId, StringComparer.OrdinalIgnoreCase)) return;
            if (RestoreTargetsAsync is { } restore)
                await restore([current.InstanceId]).ConfigureAwait(false);
            else
                await _restore(current, cancellationToken).ConfigureAwait(false);
            _ownedIds.TryRemove(current.InstanceId, out _);
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            RestoreFailed?.Invoke("Razer Chroma Addressable RGB Controller", exception);
            if (throwOnFailure) throw;
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
            // Disposal has no caller to report to; the explicit Stop still throws.
        }
        finally
        {
            _applyGate.Dispose();
        }
    }

    private static Func<CancellationToken, Task<RazerHidSpecialLightingConnection?>> CreateDiscover(
        RazerHidSpecialLightingService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return async cancellationToken => (await service.DiscoverAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(connection => connection.Kind == RazerHidSpecialLightingKind.Argb320);
    }

    private static StudioArgbPort[] CreatePorts(
        RazerHidSpecialLightingConnection connection,
        IReadOnlyList<int> counts)
    {
        var ports = new StudioArgbPort[PortCount];
        for (var channel = 0; channel < ports.Length; channel++)
        {
            ports[channel] = new StudioArgbPort(
                (byte)channel,
                counts[channel],
                connection.InstanceId,
                connection.ProductId,
                connection.DisplayName);
        }

        return ports;
    }

    private static int[] Normalize(IReadOnlyList<int> counts)
    {
        var result = new int[PortCount];
        for (var channel = 0; channel < result.Length; channel++)
        {
            var value = channel < counts.Count
                ? counts[channel]
                : RazerHidArgbFrame.MaxLeds;
            result[channel] = Math.Clamp(value, 1, RazerHidArgbFrame.MaxLeds);
        }

        return result;
    }

    private static bool SameConnection(
        RazerHidSpecialLightingConnection? left,
        RazerHidSpecialLightingConnection? right) =>
        left is not null && right is not null &&
        string.Equals(ConnectionKey(left), ConnectionKey(right), StringComparison.Ordinal);

    private static string ConnectionKey(RazerHidSpecialLightingConnection connection) =>
        $"{connection.InstanceId}|{connection.ControlDevicePath}|{connection.FeatureDevicePath}";

    private static string PortName(RazerHidSpecialLightingConnection connection, byte channel) =>
        $"{connection.DisplayName} #{channel + 1}";

    /// <summary>
    /// The extracted 0F1F manifest documents quickEffectOnExit as hardware
    /// effect 3 (Spectrum) and exposes no readable original effect. Step through
    /// Off so a class 0x10 readback that still holds Spectrum cannot skip the
    /// write and leave the controller in custom-frame mode.
    /// </summary>
    private static async Task ApplyOfficialExitEffectAsync(
        RazerHidSpecialLightingConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.FeatureDevicePath is not { } path)
        {
            throw new InvalidOperationException(
                "The ARGB controller did not expose its 91-byte Chroma feature interface.");
        }

        var service = new RazerChromaStandardService(
            new RazerFeatureTransport(), path, TimeSpan.FromMilliseconds(20));
        await service.SetEffectAsync(RazerChromaEffect.Off, [], cancellationToken).ConfigureAwait(false);
        await service.SetEffectAsync(RazerChromaEffect.Spectrum, [], cancellationToken).ConfigureAwait(false);
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

    private sealed class PortOutput
    {
        internal PortOutput(int ledCount, StudioDeviceFrameSource source)
        {
            LedCount = ledCount;
            Source = source;
        }

        internal int LedCount { get; }
        internal StudioDeviceFrameSource Source { get; }
        internal RazerHidColor[]? Frame { get; set; }
        internal bool ClockStarted { get; set; }
        internal long LayersVersion { get; set; } = -1;
        internal long WorldGeneration { get; set; } = -1;
    }
}
