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
    private long _layersVersion;
    private StudioArgbPort[] _ports = [];
    private RazerHidSpecialLightingConnection? _connection;
    private string? _preparedKey;
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

    /// <summary>Ports the next apply would write to.</summary>
    public IReadOnlyList<StudioArgbPort> GetTargets() => Volatile.Read(ref _ports);

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
            // A refresh starts a new session; Update re-arms the frame pump.
            Volatile.Write(ref _stopped, 1);
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

            var key = ConnectionKey(connection);
            if (_prepare is not null && !string.Equals(_preparedKey, key, StringComparison.Ordinal))
            {
                _preparedKey = key;
                try
                {
                    await _prepare(connection, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsWriteFailure(exception))
                {
                    // The old Chroma page treated a refused mode echo as non-fatal;
                    // frames may still be accepted, so report it and keep going.
                    _preparedKey = null;
                    WriteFailed?.Invoke(connection.DisplayName, exception);
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
            if (Volatile.Read(ref _stopped) != 0 || _connection is not { } connection)
            {
                return false;
            }

            Func<StudioArgbPort, IReadOnlyList<StudioLayerRender>>? layersForPort;
            long layersVersion;
            lock (_updateGate)
            {
                layersForPort = _layersForPort;
                layersVersion = _layersVersion;
            }

            foreach (var port in _ports)
            {
                if (!_outputs.TryGetValue(port.Channel, out var output) ||
                    output.LedCount != port.LedCount)
                {
                    output = new PortOutput(
                        port.LedCount,
                        new StudioDeviceFrameSource(1, port.LedCount, _inputProvider));
                    _outputs[port.Channel] = output;
                }

                if (output.LayersVersion != layersVersion)
                {
                    output.Source.Update(layersForPort?.Invoke(port) ?? []);
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
                    output.Touched = true;
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
            if (_connection is { } connection &&
                _outputs.Values.Any(output => output.Touched))
            {
                // A failed restore keeps the outputs and prepared key so the next
                // Stop can retry the same endpoint.
                await _restore(connection, cancellationToken).ConfigureAwait(false);
            }

            _outputs.Clear();
            _preparedKey = null;
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
        internal bool Touched { get; set; }
        internal long LayersVersion { get; set; } = -1;
    }
}
