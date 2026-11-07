using System.IO.Pipes;
using OpenSynapse.ChromaSdk.Protocol;

namespace OpenSynapse.Windows.Lighting;

internal sealed class ChromaSdkBridgeHost : IAsyncDisposable
{
    private const int MaxClients = 4;

    private readonly Func<ChromaSdkEffect, CancellationToken, Task<bool>> _applyEffect;
    private readonly Func<Task> _restoreLighting;
    private readonly Func<bool> _restorePersistentEffect;
    private readonly Func<uint> _supportedDevices;
    private readonly Action<string>? _log;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _gate = new();
    private readonly HashSet<ClientSession> _clients = [];
    private readonly HashSet<Task> _clientTasks = [];
    private NamedPipeServerStream? _listener;
    private Task? _acceptLoop;
    private ClientSession? _activeSession;
    private int _started;
    private int _disposed;
    private long _framesAccepted;
    private long _framesSkipped;

    internal ChromaSdkBridgeHost(
        Func<ChromaSdkEffect, CancellationToken, Task<bool>> applyEffect,
        Func<Task> restoreLighting,
        Func<bool>? restorePersistentEffect = null,
        Func<uint>? supportedDevices = null,
        string pipeName = ChromaSdkPipe.Name,
        Action<string>? log = null)
    {
        _applyEffect = applyEffect ?? throw new ArgumentNullException(nameof(applyEffect));
        _restoreLighting = restoreLighting ?? throw new ArgumentNullException(nameof(restoreLighting));
        _restorePersistentEffect = restorePersistentEffect ?? (() => true);
        _supportedDevices = supportedDevices ?? (() => 0);
        _log = log;
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    internal bool IsRunning => Volatile.Read(ref _started) != 0;
    internal int ActiveClientCount { get { lock (_gate) return _clients.Count; } }
    internal string? ActiveClientTitle { get { lock (_gate) return _activeSession?.Title; } }
    internal long FramesAccepted => Interlocked.Read(ref _framesAccepted);
    internal long FramesSkipped => Interlocked.Read(ref _framesSkipped);

    internal Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        try
        {
            var listener = CreateListener();
            lock (_gate)
            {
                _listener = listener;
            }
            _acceptLoop = AcceptLoopAsync(listener);
            return Task.CompletedTask;
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            lock (_gate)
            {
                _listener?.Dispose();
                _listener = null;
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Cancel();
        NamedPipeServerStream? listener;
        lock (_gate)
        {
            listener = _listener;
            _listener = null;
        }
        listener?.Dispose();

        try
        {
            try
            {
                var acceptLoop = _acceptLoop;
                if (acceptLoop is not null)
                {
                    try { await acceptLoop.ConfigureAwait(false); }
                    catch (Exception) when (_stop.IsCancellationRequested) { }
                }
            }
            finally
            {
                ClientSession[] clients;
                lock (_gate) clients = [.. _clients];
                foreach (var client in clients) client.Dispose();

                Task[] tasks;
                lock (_gate) tasks = [.. _clientTasks];
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (Exception) when (_stop.IsCancellationRequested) { }

                ClientSession? active;
                lock (_gate) active = _activeSession;
                await EndActiveSessionAsync(active).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _started, 0);
            lock (_gate)
            {
                _activeSession = null;
                _clients.Clear();
                _clientTasks.Clear();
            }
            _sessionGate.Dispose();
            _stop.Dispose();
        }
    }

    private async Task AcceptLoopAsync(NamedPipeServerStream listener)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await listener.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                var session = new ClientSession(listener);
                Task handler;
                lock (_gate)
                {
                    if (_stop.IsCancellationRequested)
                    {
                        session.Dispose();
                        return;
                    }

                    _listener = null;
                    _clients.Add(session);
                    handler = Task.Run(() => HandleClientAsync(session));
                    _clientTasks.Add(handler);
                }

                _ = handler.ContinueWith(_ =>
                {
                    lock (_gate)
                    {
                        _clients.Remove(session);
                        _clientTasks.Remove(handler);
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                listener = CreateListener();
                lock (_gate)
                {
                    if (_stop.IsCancellationRequested)
                    {
                        listener.Dispose();
                        return;
                    }
                    _listener = listener;
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (IOException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleClientAsync(ClientSession session)
    {
        try
        {
            var hello = ChromaSdkPipe.Read(session.Pipe, _stop.Token);
            if (hello is not ChromaSdkHelloMessage value ||
                value.Value.ProtocolVersion != ChromaSdkPipe.ProtocolVersion ||
                value.Value.Title.Length > 64)
            {
                return;
            }

            session.Title = value.Value.Title;
            _log?.Invoke($"client connected: {value.Value.Title} (pid {value.Value.ProcessId})");
            ChromaSdkPipe.Write(session.Pipe, new ChromaSdkHelloAckMessage(
                new ChromaSdkHelloAck(0, _supportedDevices())));

            while (true)
            {
                var message = ChromaSdkPipe.Read(session.Pipe, _stop.Token);
                switch (message)
                {
                    case ChromaSdkSetEffectMessage setEffect:
                        await ApplyAsync(session, setEffect.Value).ConfigureAwait(false);
                        break;
                    case ChromaSdkClearMessage clear:
                        await ApplyAsync(session, new ChromaSdkEffect(
                            clear.DeviceType,
                            ChromaSdkEffectKind.None,
                            1,
                            1,
                            new uint[] { 0 })).ConfigureAwait(false);
                        break;
                    case ChromaSdkQueryMessage:
                        ChromaSdkPipe.Write(session.Pipe, new ChromaSdkQueryAckMessage(
                            new ChromaSdkQueryAck(_supportedDevices())));
                        break;
                    case ChromaSdkByeMessage:
                        return;
                    default:
                        throw new InvalidDataException("Unexpected Chroma SDK message.");
                }
            }
        }
        catch (EndOfStreamException) { }
        catch (IOException) { }
        catch (InvalidDataException) { }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
        catch (Exception) { }
        finally
        {
            try
            {
                await EndActiveSessionAsync(session).ConfigureAwait(false);
            }
            finally
            {
                _log?.Invoke($"client disconnected: {session.Title}");
                session.Dispose();
            }
        }
    }

    private async Task ApplyAsync(ClientSession session, ChromaSdkEffect effect)
    {
        lock (_gate)
        {
            if (_activeSession is not null && !ReferenceEquals(_activeSession, session))
            {
                Interlocked.Increment(ref _framesSkipped);
                return;
            }
        }

        var restore = false;
        // Keep apply and restore under the same gate. Otherwise a disconnect can
        // restore the user's effect while this session is still writing a frame.
        await _sessionGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_activeSession is not null && !ReferenceEquals(_activeSession, session))
                {
                    Interlocked.Increment(ref _framesSkipped);
                    return;
                }
                _activeSession ??= session;
            }

            if (await _applyEffect(effect, _stop.Token).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activeSession, session)) session.Applied = true;
                }
                Interlocked.Increment(ref _framesAccepted);
            }
            else
            {
                Interlocked.Increment(ref _framesSkipped);
                restore = true;
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        if (restore)
        {
            await EndActiveSessionAsync(session).ConfigureAwait(false);
        }
    }

    private async Task EndActiveSessionAsync(ClientSession? session)
    {
        if (session is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_activeSession, session))
            {
                return;
            }
        }

        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            bool restore;
            lock (_gate)
            {
                if (!ReferenceEquals(_activeSession, session))
                {
                    return;
                }

                _activeSession = null;
                restore = session.Applied;
                session.Applied = false;
            }

            if (restore && _restorePersistentEffect())
            {
                try
                {
                    await _restoreLighting().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The device may disappear while restoring; keep the bridge alive.
                }
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private NamedPipeServerStream CreateListener() =>
        new(_pipeName, PipeDirection.InOut, MaxClients, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private sealed class ClientSession(NamedPipeServerStream pipe) : IDisposable
    {
        public NamedPipeServerStream Pipe { get; } = pipe;
        public string Title { get; set; } = string.Empty;
        public bool Applied { get; set; }
        public void Dispose() => Pipe.Dispose();
    }
}
