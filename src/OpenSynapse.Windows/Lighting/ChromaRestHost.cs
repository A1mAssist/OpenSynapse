using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Core.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.Windows.Lighting;

internal sealed partial class ChromaRestHost : IAsyncDisposable
{
    private const int Port = 54235;
    private const int Success = 0;
    private const int InvalidParameter = 87;
    private const int NotSupported = 50;
    private const int DeviceNotConnected = 1167;
    private const int ClientLimit = 1152;
    private const int AccessDenied = 5;
    // Chroma clients heartbeat every second; two seconds bounds recovery without
    // treating a normal short scheduling pause as a game exit.
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Func<IReadOnlyList<DeviceDescriptor>> _devices;
    private readonly IBladeLightingController _lighting;
    private readonly Func<Task> _restoreLighting;
    private readonly Func<bool> _bladeChromaOverrideEnabled;
    private readonly Func<ChromaDeviceKind, IReadOnlyList<RazerRgb>, int, int, CancellationToken, Task<bool>>? _applyEffect;
    private readonly Func<bool> _restorePersistentEffect;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blockedSources;
    private readonly HashSet<string> _knownSources;
    private readonly int _port;
    private readonly object _gate = new();
    private readonly Dictionary<TcpClient, Task> _clients = [];
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _effectGate = new(1, 1);
    private readonly SemaphoreSlim _sessionAvailable = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly ChromaExternalFrameSource _frameSource = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private Task? _sweepLoop;
    private int _started;
    private int _disposed;
    private int _nextSessionId;
    private long _framesAccepted;
    private long _framesSkipped;
    private long _sessionRecoveryFailures;

    internal ChromaRestHost(
        Func<IReadOnlyList<DeviceDescriptor>> devices,
        IBladeLightingController lighting,
        Func<Task> restoreLighting,
        Func<bool>? restorePersistentEffect = null,
        Func<ChromaDeviceKind, IReadOnlyList<RazerRgb>, int, int, CancellationToken, Task<bool>>? applyEffect = null,
        Func<bool>? bladeChromaOverrideEnabled = null,
        IEnumerable<string>? blockedSources = null,
        int port = Port, TimeSpan? requestTimeout = null)
    {
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
        _restoreLighting = restoreLighting ?? throw new ArgumentNullException(nameof(restoreLighting));
        _bladeChromaOverrideEnabled = bladeChromaOverrideEnabled ?? (() => true);
        _applyEffect = applyEffect;
        _restorePersistentEffect = restorePersistentEffect ?? (() => true);
        _blockedSources = new HashSet<string>(blockedSources ?? [], StringComparer.OrdinalIgnoreCase);
        _knownSources = new HashSet<string>(_blockedSources, StringComparer.OrdinalIgnoreCase);
        _port = port;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        if (_requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    internal bool IsRunning => Volatile.Read(ref _started) != 0;
    internal int ListeningPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? _port;
    internal long FramesAccepted => Interlocked.Read(ref _framesAccepted);
    internal long FramesSkipped => Interlocked.Read(ref _framesSkipped);
    internal ChromaRestSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new(IsRunning, _sessions.Values.FirstOrDefault(session => session.Active)?.Title,
                    FramesAccepted, FramesSkipped, _knownSources.Order(StringComparer.OrdinalIgnoreCase)
                        .Select(title => new ChromaSourceSnapshot(title, !_blockedSources.Contains(title),
                            _sessions.Values.Any(session => StringComparer.OrdinalIgnoreCase.Equals(session.Title, title)),
                            _sessions.Values.Any(session => session.Active && StringComparer.OrdinalIgnoreCase.Equals(session.Title, title))))
                        .ToArray());
            }
        }
    }

    internal async Task SetSourceAllowedAsync(string title, bool allowed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        title = title.Trim();
        if (title.Length > 64) throw new ArgumentOutOfRangeException(nameof(title));
        await _effectGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var released = false;
            lock (_gate)
            {
                _knownSources.Add(title);
                if (allowed) _blockedSources.Remove(title);
                else _blockedSources.Add(title);
                foreach (var session in _sessions.Values.Where(session =>
                    !allowed && StringComparer.OrdinalIgnoreCase.Equals(session.Title, title)))
                {
                    released |= session.Active;
                    session.Active = false;
                    session.ActiveEffectId = null;
                }
            }
            if (released)
            {
                await StopLightingAsync().ConfigureAwait(false);
                await _restoreLighting().ConfigureAwait(false);
            }
        }
        finally
        {
            _effectGate.Release();
        }
    }
    internal long SessionRecoveryFailures => Interlocked.Read(ref _sessionRecoveryFailures);
    internal int ActiveClientCount { get { lock (_gate) return _clients.Count; } }

    internal string? ActiveSessionTitle
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Values.FirstOrDefault(session => session.Active)?.Title;
            }
        }
    }

    internal Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _acceptLoop = AcceptLoopAsync();
            _sweepLoop = SweepLoopAsync();
            return Task.CompletedTask;
        }
        catch
        {
            Interlocked.Exchange(ref _started, 0);
            _listener = null;
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
        _listener?.Stop();
        var loops = new[] { _acceptLoop, _sweepLoop }.Where(task => task is not null).Cast<Task>().ToArray();
        try
        {
            try
            {
                try { await Task.WhenAll(loops).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            }
            finally
            {
                Task[] clients;
                lock (_gate) clients = _clients.Values.ToArray();
                try { await Task.WhenAll(clients).ConfigureAwait(false); }
                finally
                {
                    lock (_gate) _clients.Clear();
                    await StopAllSessionsAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _started, 0);
            _effectGate.Dispose();
            _sessionAvailable.Dispose();
            _stop.Dispose();
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                Task handler;
                lock (_gate)
                {
                    if (_stop.IsCancellationRequested) { client.Dispose(); break; }
                    handler = HandleClientAsync(client);
                    _clients.Add(client, handler);
                }
                _ = handler.ContinueWith(_ => { lock (_gate) _clients.Remove(client); }, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
        {
        }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            deadline.CancelAfter(_requestTimeout);
            using var abort = deadline.Token.Register(client.Dispose);
            try
            {
                using var stream = new BufferedStream(client.GetStream(), 4096);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
                try
                {
                    var request = await ChromaRestRequest.ReadAsync(stream, deadline.Token).ConfigureAwait(false);
                    if (request is not null)
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        await DispatchAsync(writer, request.Method, request.Path, request.Body).ConfigureAwait(false);
                    }
                }
                catch (ChromaRestRequest.InvalidRequest exception)
                {
                    await WriteResponseAsync(writer, exception.Status, new { result = InvalidParameter }).ConfigureAwait(false);
                }
                catch (Exception) when (!deadline.IsCancellationRequested)
                {
                    await WriteResponseAsync(writer, 500, new { result = InvalidParameter }).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (deadline.IsCancellationRequested ||
                exception is IOException or SocketException or ObjectDisposedException) { }
        }
    }

    private async Task DispatchAsync(StreamWriter writer, string method, string rawPath, string body)
    {
        var path = rawPath.Split('?', 2)[0].TrimEnd('/');
        if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(writer, 200, new { result = Success }).ConfigureAwait(false);
            return;
        }
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
            path.Equals("/razer/chromasdk", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(writer, 200, new { version = "4.0.0" }).ConfigureAwait(false);
            return;
        }
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
            path.Equals("/razer/chromasdk", StringComparison.OrdinalIgnoreCase))
        {
            await InitializeAsync(writer, body).ConfigureAwait(false);
            return;
        }

        var session = FindSession(path, out var suffix);
        if (session is null)
        {
            await WriteResponseAsync(writer, 404, new { result = DeviceNotConnected }).ConfigureAwait(false);
            return;
        }
        if (suffix.Equals("heartbeat", StringComparison.OrdinalIgnoreCase) &&
            (method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
             method.Equals("POST", StringComparison.OrdinalIgnoreCase)))
        {
            int tick;
            var alive = false;
            lock (_gate)
            {
                alive = _sessions.TryGetValue(session.Id, out var current) &&
                    ReferenceEquals(current, session);
                if (alive)
                {
                    session.LastHeartbeat = DateTimeOffset.UtcNow;
                    tick = ++session.Tick;
                }
                else
                {
                    tick = 0;
                }
            }
            await WriteResponseAsync(writer, 200, new
            {
                tick,
                result = alive ? Success : DeviceNotConnected,
            }).ConfigureAwait(false);
            return;
        }
        var endpoint = FindEffectEndpoint(suffix);
        if (endpoint is { } target &&
            (method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
             method.Equals("POST", StringComparison.OrdinalIgnoreCase)))
        {
            var createEffect = method.Equals("POST", StringComparison.OrdinalIgnoreCase);
            var result = await ApplyEffectAsync(session, body, createEffect, target.Kind).ConfigureAwait(false);
            await WriteResponseAsync(writer, 200, createEffect
                ? new { result = result.Code, id = result.Id }
                : new { result = result.Code }).ConfigureAwait(false);
            return;
        }
        // Chroma SDK clients use the generic effect endpoint to activate an
        // effect created by POST /keyboard. Keep the keyboard endpoint above
        // as a compatibility path, but accept the standard {"id":"..."}
        // payload here as well.
        if (suffix.Equals("effect", StringComparison.OrdinalIgnoreCase) &&
            (method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
             method.Equals("POST", StringComparison.OrdinalIgnoreCase)))
        {
            var result = await ApplyEffectAsync(
                session, body, createEffect: false, ChromaDeviceKind.Keyboard).ConfigureAwait(false);
            await WriteResponseAsync(writer, 200, new { result = result.Code }).ConfigureAwait(false);
            return;
        }
        if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) && endpoint is { } deleteTarget)
        {
            var effectId = suffix.Length > deleteTarget.Name.Length + 1
                ? suffix[(deleteTarget.Name.Length + 1)..]
                : null;
            var result = await ClearEffectAsync(session, effectId, deleteTarget.Kind).ConfigureAwait(false);
            await WriteResponseAsync(writer, 200, new { result }).ConfigureAwait(false);
            return;
        }
        if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) &&
            (suffix.Equals("effect", StringComparison.OrdinalIgnoreCase) ||
             suffix.StartsWith("effect/", StringComparison.OrdinalIgnoreCase)))
        {
            var effectId = suffix.Length > "effect/".Length
                ? suffix["effect/".Length..]
                : TryReadEffectId(body, out var validEffectId) ? validEffectId : null;
            if (suffix.Equals("effect", StringComparison.OrdinalIgnoreCase) && effectId is null)
            {
                await WriteResponseAsync(writer, 200, new { result = InvalidParameter }).ConfigureAwait(false);
                return;
            }
            var result = await ClearEffectAsync(session, effectId).ConfigureAwait(false);
            await WriteResponseAsync(writer, 200, new { result }).ConfigureAwait(false);
            return;
        }
        if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(suffix))
        {
            await RemoveSessionAsync(session).ConfigureAwait(false);
            await WriteResponseAsync(writer, 200, new { result = Success }).ConfigureAwait(false);
            return;
        }
        await WriteResponseAsync(writer, 404, new { result = InvalidParameter }).ConfigureAwait(false);
    }

    private async Task InitializeAsync(StreamWriter writer, string body)
    {
        try
        {
            var info = JsonSerializer.Deserialize<ChromaAppInfo>(body, JsonOptions)
                ?? throw new InvalidOperationException();
            if (string.IsNullOrWhiteSpace(info.Title) || info.Title.Length > 64 ||
                string.IsNullOrWhiteSpace(info.Description) || info.Description.Length > 256 ||
                info.Author is null || string.IsNullOrWhiteSpace(info.Author.Name) || info.Author.Name.Length > 64 ||
                info.Author.Contact is null || info.Author.Contact.Length > 64 ||
                info.DeviceSupported is null || info.DeviceSupported.Count == 0 ||
                !info.DeviceSupported.Any(device => ChromaDeviceNames.Contains(device)) ||
                !(info.Category?.Equals("application", StringComparison.OrdinalIgnoreCase) == true ||
                  info.Category?.Equals("game", StringComparison.OrdinalIgnoreCase) == true))
            {
                await WriteResponseAsync(writer, 200, new { result = InvalidParameter }).ConfigureAwait(false);
                return;
            }

            var id = Interlocked.Increment(ref _nextSessionId);
            var session = new Session(id.ToString(), info.Title.Trim());
            var stopping = false;
            var sourceLimit = false;
            var signalSweep = false;
            lock (_gate)
            {
                stopping = _stop.IsCancellationRequested;
                sourceLimit = !_knownSources.Contains(session.Title) && _knownSources.Count >= 128;
                if (!stopping && !sourceLimit)
                {
                    _knownSources.Add(session.Title);
                    signalSweep = _sessions.Count == 0;
                    _sessions[session.Id] = session;
                }
            }
            if (signalSweep)
            {
                SignalSessionSweep();
            }
            if (stopping)
            {
                await WriteResponseAsync(writer, 503, new { result = DeviceNotConnected }).ConfigureAwait(false);
                return;
            }
            if (sourceLimit)
            {
                await WriteResponseAsync(writer, 200, new { result = ClientLimit }).ConfigureAwait(false);
                return;
            }
            await WriteResponseAsync(writer, 200, new
            {
                sessionid = id,
                uri = $"http://127.0.0.1:{ListeningPort}/razer/chromasdk/{session.Id}"
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            await WriteResponseAsync(writer, 400, new { result = InvalidParameter }).ConfigureAwait(false);
        }
    }

    private async Task<(int Code, string? Id)> ApplyEffectAsync(
        Session session,
        string body,
        bool createEffect,
        ChromaDeviceKind kind)
    {
        await _effectGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            return await ApplyEffectCoreAsync(session, body, createEffect, kind).ConfigureAwait(false);
        }
        finally
        {
            _effectGate.Release();
        }
    }

    private async Task<(int Code, string? Id)> ApplyEffectCoreAsync(
        Session session,
        string body,
        bool createEffect,
        ChromaDeviceKind kind)
    {
        var claimedSession = false;
        string? effectId = null;
        try
        {
            lock (_gate)
            {
                if (!_sessions.TryGetValue(session.Id, out var current) || !ReferenceEquals(current, session))
                {
                    return (DeviceNotConnected, null);
                }
                if (_blockedSources.Contains(session.Title))
                {
                    return (AccessDenied, null);
                }
                if (_sessions.Values.Any(item => item.Active && !ReferenceEquals(item, session)))
                {
                    return (ClientLimit, null);
                }
            }
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (InvalidParameter, null);
            }

            ParsedFrame frame;
            if (!createEffect && root.TryGetProperty("id", out var idElement))
            {
                if (idElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(effectId = idElement.GetString()) ||
                    !session.Effects.TryGetValue(effectId, out frame!))
                {
                    return (InvalidParameter, null);
                }
            }
            else
            {
                if (!root.TryGetProperty("effect", out var effectElement) ||
                    effectElement.ValueKind != JsonValueKind.String)
                {
                    return (InvalidParameter, null);
                }
                var parsed = ParseFrame(kind, root, effectElement.GetString());
                if (parsed.Code != Success)
                {
                    return (parsed.Code, null);
                }
                frame = parsed.Frame!;
                if (createEffect)
                {
                    if (session.Effects.Count >= 256)
                    {
                        return (ClientLimit, null);
                    }
                    effectId = Guid.NewGuid().ToString();
                }
            }

            lock (_gate)
            {
                if (!_sessions.TryGetValue(session.Id, out var current) || !ReferenceEquals(current, session))
                {
                    return (DeviceNotConnected, null);
                }
                if (_sessions.Values.Any(item => item.Active && !ReferenceEquals(item, session)))
                {
                    return (ClientLimit, null);
                }

                session.Active = true;
                session.LastHeartbeat = DateTimeOffset.UtcNow;
                session.ApplyInProgress = true;
                claimedSession = true;
            }
            if (createEffect)
            {
                session.Effects[effectId!] = frame;
            }

            var changed = frame.BladeFrame is null || _frameSource.Publish(frame.BladeFrame);
            if (!changed)
            {
                Interlocked.Increment(ref _framesSkipped);
            }
            // Re-assert ownership on every frame. If the user changed the
            // normal effect while a game is alive, the next game frame takes
            // the device back without restarting an already active runtime.
            var applied = false;
            var devices = _devices();
            if (frame.BladeFrame is not null &&
                _bladeChromaOverrideEnabled() && devices.Any(device =>
                    device.ProtocolFamily is "blade-710" or "blade-710-chroma" &&
                    device.Access == DeviceAccessState.Available))
            {
                try
                {
                    await _lighting.ApplyExternalAsync(
                        devices,
                        _frameSource,
                        restorePersistentEffect: false,
                        _stop.Token).ConfigureAwait(false);
                    applied = true;
                }
                catch (InvalidOperationException) when (_applyEffect is not null)
                {
                    // RazerHid-only systems do not have a Blade target.
                }
            }
            if (_applyEffect is not null)
            {
                applied |= await _applyEffect(
                    frame.Kind, frame.ChromaFrame, frame.Rows, frame.Columns, _stop.Token)
                    .ConfigureAwait(false);
            }
            if (!applied)
            {
                if (createEffect && effectId is not null)
                {
                    session.Effects.Remove(effectId);
                }
                lock (_gate)
                {
                    if (_sessions.TryGetValue(session.Id, out var current) && ReferenceEquals(current, session))
                    {
                        session.Active = false;
                        session.ActiveEffectId = null;
                    }
                }
                return (DeviceNotConnected, null);
            }
            if (changed)
            {
                Interlocked.Increment(ref _framesAccepted);
            }
            session.ActiveEffectId = effectId;
            return (Success, effectId);
        }
        catch (InvalidOperationException)
        {
            return (InvalidParameter, null);
        }
        catch (JsonException)
        {
            return (InvalidParameter, null);
        }
        catch (FormatException)
        {
            return (InvalidParameter, null);
        }
        catch (OverflowException)
        {
            return (InvalidParameter, null);
        }
        catch (Exception) when (!_stop.IsCancellationRequested)
        {
            if (createEffect && effectId is not null)
            {
                session.Effects.Remove(effectId);
            }
            if (claimedSession)
            {
                lock (_gate)
                {
                    if (_sessions.TryGetValue(session.Id, out var current) && ReferenceEquals(current, session))
                    {
                        session.Active = false;
                        if (string.Equals(session.ActiveEffectId, effectId, StringComparison.OrdinalIgnoreCase))
                        {
                            session.ActiveEffectId = null;
                        }
                    }
                }
            }
            return (DeviceNotConnected, null);
        }
        finally
        {
            if (claimedSession)
            {
                lock (_gate)
                {
                    if (_sessions.TryGetValue(session.Id, out var current) && ReferenceEquals(current, session))
                    {
                        session.ApplyInProgress = false;
                        session.LastHeartbeat = DateTimeOffset.UtcNow;
                    }
                }
                SignalSessionSweep();
            }
        }
    }


    private static bool TryReadEffectId(string body, out string? effectId)
    {
        effectId = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            effectId = id.GetString();
            return !string.IsNullOrWhiteSpace(effectId);
        }
        catch (JsonException)
        {
            return false;
        }
    }


    private static readonly (string Name, ChromaDeviceKind Kind)[] EffectEndpoints =
    [
        ("keyboard", ChromaDeviceKind.Keyboard),
        ("mouse", ChromaDeviceKind.Mouse),
        ("headset", ChromaDeviceKind.Headset),
        ("mousepad", ChromaDeviceKind.Mousepad),
        ("keypad", ChromaDeviceKind.Keypad),
        ("chromalink", ChromaDeviceKind.ChromaLink),
    ];

    private static readonly IReadOnlySet<string> ChromaDeviceNames = new HashSet<string>(
        ["keyboard", "mouse", "headset", "mousepad", "mousemat", "keypad", "chromalink"],
        StringComparer.OrdinalIgnoreCase);

    private static (string Name, ChromaDeviceKind Kind)? FindEffectEndpoint(string suffix)
    {
        foreach (var endpoint in EffectEndpoints)
        {
            if (suffix.Equals(endpoint.Name, StringComparison.OrdinalIgnoreCase) ||
                suffix.StartsWith(endpoint.Name + "/", StringComparison.OrdinalIgnoreCase))
            {
                return endpoint;
            }
        }
        return null;
    }

    private Session? FindSession(string path, out string suffix)
    {
        suffix = string.Empty;
        const string prefix = "/razer/chromasdk/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = path[prefix.Length..].Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(parts[0], out var session)) return null;
            suffix = parts.Length == 1 ? string.Empty : parts[1];
            return session;
        }
    }

    private async Task SweepLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TimeSpan wait;
                lock (_gate)
                {
                    var nextExpiry = _sessions.Values
                        .Where(item => !item.ApplyInProgress)
                        .Select(item => item.LastHeartbeat + SessionTimeout)
                        .DefaultIfEmpty(DateTimeOffset.MaxValue)
                        .Min();
                    wait = nextExpiry == DateTimeOffset.MaxValue
                        ? Timeout.InfiniteTimeSpan
                        : TimeSpan.FromTicks(Math.Max(0, (nextExpiry - DateTimeOffset.UtcNow).Ticks));
                }

                if (await _sessionAvailable.WaitAsync(wait, _stop.Token).ConfigureAwait(false))
                {
                    continue;
                }

                Session[] expired;
                lock (_gate)
                {
                    expired = _sessions.Values
                        .Where(item => !item.ApplyInProgress &&
                            DateTimeOffset.UtcNow - item.LastHeartbeat >= SessionTimeout)
                        .ToArray();
                }
                foreach (var session in expired)
                {
                    try
                    {
                        await RemoveSessionAsync(session, requireExpired: true).ConfigureAwait(false);
                    }
                    catch when (!_stop.IsCancellationRequested)
                    {
                        Interlocked.Increment(ref _sessionRecoveryFailures);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void SignalSessionSweep()
    {
        try
        {
            _sessionAvailable.Release();
        }
        catch (SemaphoreFullException)
        {
        }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task RemoveSessionAsync(Session session, bool requireExpired = false)
    {
        await _effectGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            bool removed;
            bool wasActive;
            lock (_gate)
            {
                removed = (!requireExpired ||
                           !session.ApplyInProgress &&
                           DateTimeOffset.UtcNow - session.LastHeartbeat >= SessionTimeout) &&
                    _sessions.Remove(session.Id);
                wasActive = session.Active;
                session.Active = false;
            }
            if (!removed)
            {
                return;
            }
            // Keep cleanup under the same gate as activation. Otherwise a new
            // session can claim the device between removal and StopAsync and
            // then be stopped by this stale cleanup.
            if (wasActive && !HasActiveSession())
            {
                await StopLightingAsync().ConfigureAwait(false);
                await _restoreLighting().ConfigureAwait(false);
            }
        }
        finally
        {
            _effectGate.Release();
        }
    }

    private async Task<int> ClearEffectAsync(
        Session session,
        string? effectId,
        ChromaDeviceKind? endpointKind = null)
    {
        await _effectGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            bool removed;
            bool wasActive;
            var clearedKind = endpointKind ?? ChromaDeviceKind.Keyboard;
            lock (_gate)
            {
                removed = _sessions.ContainsKey(session.Id);
                if (!removed)
                {
                    return DeviceNotConnected;
                }
                if (effectId is not null && session.Effects.TryGetValue(effectId, out var stored))
                {
                    clearedKind = stored.Kind;
                }
                if (effectId is not null && !session.Effects.Remove(effectId))
                {
                    return InvalidParameter;
                }
                if (effectId is null)
                {
                    session.Effects.Clear();
                }
                if (effectId is not null && !string.Equals(session.ActiveEffectId, effectId, StringComparison.OrdinalIgnoreCase))
                {
                    return Success;
                }
                wasActive = session.Active;
                session.Active = false;
                session.ActiveEffectId = null;
                _frameSource.Clear();
            }
            if (wasActive && clearedKind != ChromaDeviceKind.Keyboard && _applyEffect is not null)
            {
                await _applyEffect(
                    clearedKind,
                    [default],
                    1,
                    1,
                    CancellationToken.None).ConfigureAwait(false);
            }
            if (removed && wasActive && !HasActiveSession())
            {
                await StopLightingAsync().ConfigureAwait(false);
                await _restoreLighting().ConfigureAwait(false);
            }
        }
        finally
        {
            _effectGate.Release();
        }
        return Success;
    }

    private async Task StopAllSessionsAsync()
    {
        await _effectGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            bool hadActiveSession;
            lock (_gate)
            {
                hadActiveSession = _sessions.Values.Any(session => session.Active);
                _sessions.Clear();
            }
            if (hadActiveSession)
            {
                await StopLightingAsync().ConfigureAwait(false);
                await _restoreLighting().ConfigureAwait(false);
            }
        }
        finally
        {
            _effectGate.Release();
        }
    }

    private bool HasActiveSession()
    {
        lock (_gate) return _sessions.Values.Any(session => session.Active);
    }

    private async Task StopLightingAsync()
    {
        try
        {
            await _lighting.StopAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (_applyEffect is not null)
        {
            // An RazerHid-only session has no Blade runtime to stop.
        }
    }

    private static async Task WriteResponseAsync(StreamWriter writer, int status, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await writer.WriteAsync($"HTTP/1.1 {status} {Reason(status)}\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type, Accept\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}").ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        404 => "Not Found",
        413 => "Payload Too Large",
        503 => "Service Unavailable",
        _ => "Internal Server Error",
    };

    private sealed class Session(string id, string title)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public DateTimeOffset LastHeartbeat { get; set; } = DateTimeOffset.UtcNow;
        public int Tick { get; set; }
        public bool Active { get; set; }
        public bool ApplyInProgress { get; set; }
        public string? ActiveEffectId { get; set; }
        public Dictionary<string, ParsedFrame> Effects { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ParsedFrame(
        RazerRgb[] ChromaFrame,
        int Rows,
        int Columns,
        ChromaDeviceKind Kind,
        RazerRgb[]? BladeFrame = null);

    private sealed class ChromaAppInfo
    {
        public string? Title { get; init; }
        public string? Description { get; init; }
        public ChromaAuthor? Author { get; init; }
        [JsonPropertyName("device_supported")]
        public List<string>? DeviceSupported { get; init; }
        public string? Category { get; init; }
    }

    private sealed class ChromaAuthor
    {
        public string? Name { get; init; }
        public string? Contact { get; init; }
    }

}

internal readonly record struct ChromaRestSnapshot(
    bool IsRunning,
    string? ActiveSessionTitle,
    long FramesAccepted,
    long FramesSkipped,
    IReadOnlyList<ChromaSourceSnapshot>? Sources = null);

internal sealed record ChromaSourceSnapshot(string Title, bool Allowed, bool Connected, bool Active);
