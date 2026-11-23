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

    /// <summary>Raised off the UI thread when the headset rejects a frame.</summary>
    public event Action<string, Exception>? WriteFailed;

    public string? InstanceId => _path;

    public string? DisplayName => _displayName;

    public ushort ProductId => _kraken?.ProductId ?? 0x05AD;

    public int LedCount => _kraken is null ? RazerMakoXChromaService.LedCount : 1;

    /// <summary>Discovers Mako X and enters its custom-frame effect once per endpoint.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Volatile.Write(ref _stopped, 1);
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
                    _kraken = kraken;
                    _path = connection.DevicePath;
                    _displayName = connection.Name;
                    _source = null;
                    _sourceLayersVersion = -1;
                }

                return true;
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

            if (!string.Equals(_path, connection.DevicePath, StringComparison.OrdinalIgnoreCase))
            {
                await ReleaseServiceAsync().ConfigureAwait(false);
                _path = connection.DevicePath;
                _displayName = connection.Name;
                _sourceLayersVersion = -1;
                _service = new RazerMakoXChromaService(
                    connection.DevicePath,
                    _diagnostic,
                    keepOpen: true);
            }

            if (_service is not null)
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

            return _service is not null;
        }
        finally
        {
            _gate.Release();
        }
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
            if (Volatile.Read(ref _stopped) != 0 || (_service is null && _kraken is null))
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

            _source ??= _kraken is null
                ? new StudioDeviceFrameSource(1, RazerMakoXChromaService.LedCount, _inputProvider)
                : new StudioDeviceFrameSource(1, 1, _inputProvider);
            if (_sourceLayersVersion != layersVersion)
            {
                _source.Update(layersForDevice?.Invoke() ?? []);
                _sourceLayersVersion = layersVersion;
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
            if (applyFallback && _prepared && _service is { } service)
            {
                try
                {
                    await service.SetEffectAsync(
                        RazerChromaEffect.Off, [], cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (IsWriteFailure(exception))
                {
                    WriteFailed?.Invoke(_displayName ?? "Razer Mako X", exception);
                }
            }
            else if (applyFallback &&
                _kraken is { } kraken &&
                _krakenLighting is not null)
            {
                try
                {
                    await _krakenLighting.SetKrakenLightingAsync(
                            kraken,
                            new RazerHidLightingSettings(RazerHidLightingEffect.Off),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (IsWriteFailure(exception))
                {
                    WriteFailed?.Invoke(_displayName ?? "Razer Kraken V3", exception);
                }
            }

            await ReleaseServiceAsync().ConfigureAwait(false);
            _source = null;
            _sourceLayersVersion = -1;
            _lastFrame = null;
            _prepared = false;
        }
        finally
        {
            _gate.Release();
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

    private async Task ReleaseServiceAsync()
    {
        var service = _service;
        _service = null;
        _kraken = null;
        _path = null;
        _displayName = null;
        _prepared = false;
        _lastFrame = null;
        if (service is not null) await service.DisposeAsync().ConfigureAwait(false);
    }

    private static bool IsWriteFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or TimeoutException or
            UnauthorizedAccessException or System.ComponentModel.Win32Exception or
            NotSupportedException;
}
