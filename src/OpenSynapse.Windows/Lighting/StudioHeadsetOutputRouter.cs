using System.Collections.Concurrent;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Devices;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

/// <summary>
/// Renders the studio layer stack on the Mako X 1 x 10 LED region. The audio
/// endpoint owns the lighting channel, so this is separate from the HID router.
/// </summary>
public sealed class StudioHeadsetOutputRouter : IAsyncDisposable
{
    private readonly Func<RazerHeadsetAudioConnection?> _connection;
    private readonly Func<int, int, StudioEffectInput>? _inputProvider;
    private readonly Func<Task>? _beforeTakeOver;
    private readonly Func<RazerHeadsetAudioConnection, Task>? _beforeConnectionTakeOver;
    private readonly Action<string>? _diagnostic;
    private readonly RazerHidSpecialLightingService? _krakenLighting;
    private readonly Func<RazerHeadsetAudioConnection, RazerHidSpecialLightingConnection?>? _krakenConnection;
    private StudioWorldRenderer? _world;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _updateGate = new();
    private Func<IReadOnlyList<StudioLayerRender>>? _layersForDevice;
    private StudioDeviceFrameSource? _source;
    private RazerMakoXChromaService? _service;
    private RazerHidSpecialLightingConnection? _kraken;
    private RazerRgb[]? _lastFrame;
    private string? _path;
    private string? _displayName;
    private long _layersVersion;
    private bool _prepared;
    private bool _clockStarted;
    private HashSet<string>? _targetIds;
    private readonly ConcurrentDictionary<string, RazerHeadsetAudioConnection> _owned = new(StringComparer.OrdinalIgnoreCase);
    private RazerHeadsetAudioConnection? _boundConnection;
    private readonly Func<RazerHeadsetAudioConnection, CancellationToken, Task>? _prepareOverride;
    private readonly Func<RazerHeadsetAudioConnection, IReadOnlyList<RazerRgb>, CancellationToken, Task>? _writeOverride;
    private readonly Func<RazerHeadsetAudioConnection, CancellationToken, Task>? _restoreOverride;
    private int _stopped;
    private int _disposed;

    public StudioHeadsetOutputRouter(
        Func<RazerHeadsetAudioConnection?> connection,
        Func<int, int, StudioEffectInput>? inputProvider = null,
        Func<Task>? beforeTakeOver = null,
        Action<string>? diagnostic = null,
        RazerHidSpecialLightingService? krakenLighting = null,
        Func<RazerHeadsetAudioConnection, RazerHidSpecialLightingConnection?>? krakenConnection = null,
        Func<RazerHeadsetAudioConnection, Task>? beforeConnectionTakeOver = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _inputProvider = inputProvider;
        _beforeTakeOver = beforeTakeOver;
        _beforeConnectionTakeOver = beforeConnectionTakeOver;
        _diagnostic = diagnostic;
        _krakenLighting = krakenLighting;
        _krakenConnection = krakenConnection;
    }

    internal StudioHeadsetOutputRouter(
        Func<RazerHeadsetAudioConnection?> connection,
        Func<RazerHeadsetAudioConnection, CancellationToken, Task> prepare,
        Func<RazerHeadsetAudioConnection, IReadOnlyList<RazerRgb>, CancellationToken, Task> write,
        Func<RazerHeadsetAudioConnection, CancellationToken, Task> restore)
        : this(connection)
    {
        _prepareOverride = prepare;
        _writeOverride = write;
        _restoreOverride = restore;
    }

    /// <summary>Raised off the UI thread when the headset rejects a frame.</summary>
    public event Action<string, Exception>? WriteFailed;

    public event Action<string, Exception>? RestoreFailed;

    public IReadOnlyCollection<string> OwnedInstanceIds => _owned.Keys.ToArray();

    /// <summary>
    /// Optional card restore under the gate. Resolves owned IDs against current connections
    /// independently of editing focus; must throw if the target is offline or restoration fails.
    /// </summary>
    public Func<IReadOnlyCollection<string>, Task>? RestoreTargetsAsync { get; set; }

    /// <summary>Null means All; empty selects nothing. IDs are audio DevicePaths.</summary>
    public async Task SetTargetsAsync(
        IReadOnlyCollection<string>? targetIds, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var selected = targetIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _targetIds, selected);
            await RestoreOwnedAsync(false, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsSelected(string id) => Volatile.Read(ref _targetIds)?.Contains(id) ?? true;

    public string? InstanceId => _path;

    public string? DisplayName => _displayName;

    public ushort ProductId => _kraken?.ProductId ?? 0x05AD;

    public int LedCount => _kraken is null ? RazerMakoXChromaService.LedCount : 1;

    /// <summary>Attaches the shared world canvas; null keeps the per-device renderer.</summary>
    public void AttachWorld(StudioWorldRenderer world) =>
        _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>Discovers Mako X and enters its custom-frame effect once per endpoint.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        await RestoreOwnedAsync(false, true, cancellationToken).ConfigureAwait(false);
        var connection = _connection();
        if (connection is { Transport: RazerHeadsetAudioTransport.KrakenBase37 })
        {
            if (_krakenLighting is null ||
                _krakenConnection?.Invoke(connection) is not { IsReady: true } kraken)
            {
                await ReleaseServiceAsync().ConfigureAwait(false);
                return false;
            }

            if (!string.Equals(_path, connection.DevicePath, StringComparison.OrdinalIgnoreCase) ||
                _kraken?.InstanceId != kraken.InstanceId)
            {
                await ReleaseServiceAsync().ConfigureAwait(false);
                _path = connection.DevicePath;
                _displayName = connection.Name;
                _source = null;
                _sourceLayersVersion = -1;
            }

            _kraken = kraken;
            _boundConnection = connection;

            return IsSelected(connection.DevicePath);
        }

        if (connection is not { Transport: RazerHeadsetAudioTransport.Jolie64 })
        {
            await ReleaseServiceAsync().ConfigureAwait(false);
            return false;
        }

        if (_kraken is not null)
        {
            await ReleaseServiceAsync().ConfigureAwait(false);
        }

        if (_boundConnection != connection)
        {
            await ReleaseServiceAsync().ConfigureAwait(false);
            _path = connection.DevicePath;
            _boundConnection = connection;
            _displayName = connection.Name;
            _sourceLayersVersion = -1;
            _clockStarted = false;
            _service = new RazerMakoXChromaService(
                connection.DevicePath,
                _diagnostic,
                keepOpen: true);
        }

        if (_service is not null && IsSelected(connection.DevicePath) && !_prepared)
        {
            try
            {
                if (_beforeConnectionTakeOver is not null)
                {
                    await _beforeConnectionTakeOver(connection).ConfigureAwait(false);
                }
                if (_beforeTakeOver is not null)
                {
                    await _beforeTakeOver().ConfigureAwait(false);
                }
                if (!_prepared)
                {
                    _owned[connection.DevicePath] = connection;
                    if (_prepareOverride is { } prepare)
                        await prepare(connection, cancellationToken).ConfigureAwait(false);
                    else
                        await _service.SetCustomFrameEffectAsync(cancellationToken).ConfigureAwait(false);
                    _prepared = true;
                }
            }
            catch (Exception exception) when (IsWriteFailure(exception))
            {
                WriteFailed?.Invoke(_displayName ?? "Razer Mako X", exception);
                await ReleaseServiceAsync().ConfigureAwait(false);
                return false;
            }
        }

        return _service is not null && IsSelected(connection.DevicePath);
    }

    /// <summary>Replaces the layer builder and re-arms a stopped router.</summary>
    public void Update(Func<IReadOnlyList<StudioLayerRender>> layersForDevice)
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

    public async Task<bool> ApplyAsync(TimeSpan elapsed, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopped) != 0) return false;
            await RestoreOwnedAsync(false, true, cancellationToken).ConfigureAwait(false);
            // Editing focus can change the provider; retain the applied endpoint until refresh or reselection.
            if (_boundConnection is null || !IsSelected(_boundConnection.DevicePath) ||
                (_service is not null && !_prepared))
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            if (_path is null || !IsSelected(_path) || (_service is null && _kraken is null))
            {
                return false;
            }

            Func<IReadOnlyList<StudioLayerRender>>? layersForDevice;
            long layersVersion;
            lock (_updateGate)
            {
                layersForDevice = _layersForDevice;
                layersVersion = _layersVersion;
            }

            var worldGeneration = _world?.Generation ?? 0;
            if (_source is not null && _source is not StudioWorldDeviceFrameSource &&
                _world?.Canvas?.HasDevice(_path) != true)
                _sourceWorldGeneration = worldGeneration;
            if (_source is null || _sourceWorldGeneration != worldGeneration)
            {
                var ledCount = _kraken is null ? RazerMakoXChromaService.LedCount : 1;
                _source = _world?.CreateSource(
                        _path,
                        1,
                        ledCount,
                        1,
                        ledCount,
                        null)
                    ?? new StudioDeviceFrameSource(1, ledCount, _inputProvider);
                _sourceWorldGeneration = worldGeneration;
                _sourceLayersVersion = -1;
                _clockStarted = false;
            }
            if (_sourceLayersVersion != layersVersion)
            {
                _source.Update(layersForDevice?.Invoke() ?? []);
                _sourceLayersVersion = layersVersion;
            }

            if (!_clockStarted)
            {
                if (_source is not StudioWorldDeviceFrameSource && elapsed > TimeSpan.Zero)
                    await _source.RenderAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
                _clockStarted = true;
            }
            var colors = await _source.RenderAsync(elapsed, cancellationToken).ConfigureAwait(false);
            if (_kraken is { } kraken)
            {
                var color = colors.Count == 0 ? default : colors[0];
                if (_lastFrame is { Length: 1 } last &&
                    last[0].Red == color.Red &&
                    last[0].Green == color.Green &&
                    last[0].Blue == color.Blue)
                {
                    return true;
                }

                try
                {
                    _owned[_path] = _boundConnection!;
                    await _krakenLighting!.SetKrystalFrameAsync(
                            kraken,
                            new RazerHidColor(color.Red, color.Green, color.Blue),
                            cancellationToken)
                        .ConfigureAwait(false);
                    _lastFrame = [color];
                    return true;
                }
                catch (Exception exception) when (IsWriteFailure(exception))
                {
                    _lastFrame = null;
                    WriteFailed?.Invoke(_displayName ?? "Razer Kraken V3", exception);
                    return false;
                }
            }

            var service = _service;
            if (service is null)
            {
                return false;
            }

            if (_lastFrame is { } previous && colors.SequenceEqual(previous))
            {
                return true;
            }

            var frame = new byte[colors.Count * 3];
            for (var index = 0; index < colors.Count; index++)
            {
                frame[index * 3] = colors[index].Red;
                frame[(index * 3) + 1] = colors[index].Green;
                frame[(index * 3) + 2] = colors[index].Blue;
            }

            try
            {
                _owned[_path] = _boundConnection!;
                if (_writeOverride is { } write)
                    await write(_boundConnection!, colors, cancellationToken).ConfigureAwait(false);
                else
                    await service.SetFrameAsync(frame, cancellationToken).ConfigureAwait(false);
                _lastFrame = colors.ToArray();
                return true;
            }
            catch (Exception exception) when (IsWriteFailure(exception))
            {
                _lastFrame = null;
                WriteFailed?.Invoke(_displayName ?? "Razer Mako X", exception);
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops the pump and leaves custom-frame mode.</summary>
    public async Task StopAsync(bool applyFallback = true, CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _stopped, 1);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RestoreOwnedAsync(true, applyFallback, cancellationToken).ConfigureAwait(false);
            await ReleaseServiceAsync().ConfigureAwait(false);
            _source = null;
            _sourceLayersVersion = -1;
            _sourceWorldGeneration = -1;
            _lastFrame = null;
            _prepared = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RestoreOwnedAsync(bool all, bool applyFallback, CancellationToken cancellationToken)
    {
        foreach (var (id, owned) in _owned.ToArray())
        {
            if (!all && IsSelected(id)) continue;
            if (string.Equals(_path, id, StringComparison.OrdinalIgnoreCase))
                await ReleaseServiceAsync().ConfigureAwait(false);
            try
            {
                if (RestoreTargetsAsync is { } restore)
                    await restore([id]).ConfigureAwait(false);
                else
                {
                    var current = _connection();
                    if (!applyFallback || current is null || current.ProductId != owned.ProductId ||
                        current.Transport != owned.Transport ||
                        !string.Equals(current.DevicePath, id, StringComparison.OrdinalIgnoreCase)) continue;
                    if (_restoreOverride is { } restoreOverride)
                        await restoreOverride(current, cancellationToken).ConfigureAwait(false);
                    else if (current.Transport == RazerHeadsetAudioTransport.Jolie64)
                    {
                        await using var service = new RazerMakoXChromaService(current.DevicePath, _diagnostic);
                        await service.SetEffectAsync(RazerChromaEffect.Off, [], cancellationToken).ConfigureAwait(false);
                    }
                    else if (_krakenConnection?.Invoke(current) is { IsReady: true } kraken && _krakenLighting is not null)
                        await _krakenLighting.SetKrakenLightingAsync(kraken,
                            new RazerHidLightingSettings(RazerHidLightingEffect.Off), cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                    else
                        continue;
                }
                _owned.TryRemove(id, out _);
            }
            catch (Exception exception) when (IsWriteFailure(exception))
            {
                RestoreFailed?.Invoke(owned.Name, exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            await StopAsync(applyFallback: false).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
        }
        finally
        {
            _gate.Dispose();
        }
    }

    private long _sourceLayersVersion = -1;
    private long _sourceWorldGeneration = -1;

    private async Task ReleaseServiceAsync()
    {
        var service = _service;
        _service = null;
        _boundConnection = null;
        _kraken = null;
        _path = null;
        _displayName = null;
        _prepared = false;
        _lastFrame = null;
        _sourceWorldGeneration = -1;
        _source = null;
        _clockStarted = false;
        if (service is not null) await service.DisposeAsync().ConfigureAwait(false);
    }

    private static bool IsWriteFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or TimeoutException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception or
            NotSupportedException;
}
