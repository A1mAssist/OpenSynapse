using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// One-shot, explicit-opt-in bridge from Blade WM_INPUT to MappingEngine
/// output injection. It never starts from App startup by itself.
/// </summary>
public sealed class BladeMappingWindowHost : IAsyncDisposable
{
    private readonly BladeMappingSession _session;
    private readonly WindowsKeyboardInputSink _sink;
    private readonly WindowsRawInputHost _rawInput;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private int _started;
    private int _stopped;
    private int _disposed;
    private int _faulted;
    private string? _lastError;

    public BladeMappingWindowHost(
        nint windowHandle,
        string devicePathFragment,
        BladeMappingSession session,
        WindowsKeyboardInputSink? sink = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _sink = sink ?? new WindowsKeyboardInputSink();
        _rawInput = new WindowsRawInputHost(
            windowHandle,
            new BladeRawInputEventDecoder(devicePathFragment),
            HandleInput);
    }

    public string? LastError => Volatile.Read(ref _lastError) ?? _rawInput.LastError;

    public async Task StartAsync(
        string deviceInfoJson,
        string storageKey,
        string storageValueJson,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _stopped) != 0)
            {
                throw new InvalidOperationException("Blade Mapping 窗口宿主已经停止，不能重新启动。");
            }
            if (Interlocked.Exchange(ref _started, 1) != 0)
            {
                throw new InvalidOperationException("Blade Mapping 窗口宿主已经启动。");
            }

            try
            {
                await _session.StartAsync(
                    deviceInfoJson,
                    storageKey,
                    storageValueJson,
                    cancellationToken).ConfigureAwait(false);
                _rawInput.Start();
            }
            catch
            {
                try
                {
                    await StopCoreAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupException)
                {
                    Volatile.Write(ref _lastError, cleanupException.Message);
                }
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await _session.DisposeAsync().ConfigureAwait(false);
            _lifecycle.Dispose();
        }
    }

    private async Task StopCoreAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _rawInput.Dispose();
        if (Volatile.Read(ref _started) == 0)
        {
            return;
        }

        try
        {
            var output = await _session.StopAsync().ConfigureAwait(false);
            _sink.Send(output);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception.Message);
            throw;
        }
        finally
        {
            Volatile.Write(ref _started, 0);
        }
    }

    private void HandleInput(IReadOnlyList<BladeMappingInputEvent> inputs)
    {
        if (Volatile.Read(ref _started) == 0 ||
            Volatile.Read(ref _stopped) != 0 ||
            Volatile.Read(ref _faulted) != 0)
        {
            return;
        }

        try
        {
            _sink.Send(_session.ProcessInputs(inputs));
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _faulted, 1);
            Volatile.Write(ref _lastError, exception.Message);
        }
    }
}
