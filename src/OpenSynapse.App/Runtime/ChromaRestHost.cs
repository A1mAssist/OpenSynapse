using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Devices;
using OpenSynapse.Windows.Lighting;
using OpenSynapse.Windows.Protocols;

namespace OpenSynapse.App.Runtime;

internal sealed class ChromaRestHost : IAsyncDisposable
{
    private const int Port = 54235;
    private const int Success = 0;
    private const int InvalidParameter = 87;
    private const int NotSupported = 50;
    private const int DeviceNotConnected = 1167;
    private const int ClientLimit = 1152;
    // Chroma clients heartbeat every second; two seconds bounds recovery without
    // treating a normal short scheduling pause as a game exit.
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SessionSweepInterval = TimeSpan.FromMilliseconds(100);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Func<IReadOnlyList<DeviceDescriptor>> _devices;
    private readonly IBladeLightingController _lighting;
    private readonly Func<Task> _restoreLighting;
    private readonly Func<bool> _restorePersistentEffect;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _effectGate = new(1, 1);
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
        Func<bool>? restorePersistentEffect = null)
    {
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
        _restoreLighting = restoreLighting ?? throw new ArgumentNullException(nameof(restoreLighting));
        _restorePersistentEffect = restorePersistentEffect ?? (() => true);
    }

    internal bool IsRunning => Volatile.Read(ref _started) != 0;
    internal long FramesAccepted => Interlocked.Read(ref _framesAccepted);
    internal long FramesSkipped => Interlocked.Read(ref _framesSkipped);
    internal ChromaRestSnapshot Snapshot => new(
        IsRunning,
        ActiveSessionTitle,
        FramesAccepted,
        FramesSkipped);
    internal long SessionRecoveryFailures => Interlocked.Read(ref _sessionRecoveryFailures);

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
            _listener = new TcpListener(IPAddress.Loopback, Port);
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
            await Task.WhenAll(loops).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }

        await StopAllSessionsAsync().ConfigureAwait(false);
        _effectGate.Dispose();
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n", AutoFlush = true })
        {
            try
            {
                var requestLine = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false)))
                {
                    var separator = line.IndexOf(':');
                    if (separator > 0)
                    {
                        headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                    }
                }
                var contentLength = 0;
                if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding) &&
                    !transferEncoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(writer, 400, new { result = InvalidParameter }).ConfigureAwait(false);
                    return;
                }
                if (headers.TryGetValue("Content-Length", out var rawLength) &&
                    (!int.TryParse(rawLength, out contentLength) || contentLength < 0))
                {
                    await WriteResponseAsync(writer, 400, new { result = InvalidParameter }).ConfigureAwait(false);
                    return;
                }
                if (contentLength < 0 || contentLength > 2 * 1024 * 1024)
                {
                    await WriteResponseAsync(writer, 413, new { result = InvalidParameter }).ConfigureAwait(false);
                    return;
                }
                var body = contentLength == 0 ? string.Empty : await ReadBodyAsync(reader, contentLength).ConfigureAwait(false);
                var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3)
                {
                    await WriteResponseAsync(writer, 400, new { result = InvalidParameter }).ConfigureAwait(false);
                    return;
                }
                await DispatchAsync(writer, parts[0], parts[1], body).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch
            {
                try { await WriteResponseAsync(writer, 500, new { result = InvalidParameter }).ConfigureAwait(false); }
                catch { }
            }
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
            lock (_gate)
            {
                session.LastHeartbeat = DateTimeOffset.UtcNow;
                tick = ++session.Tick;
            }
            await WriteResponseAsync(writer, 200, new { tick, result = Success }).ConfigureAwait(false);
            return;
        }
        if (suffix.Equals("keyboard", StringComparison.OrdinalIgnoreCase) && (method.Equals("PUT", StringComparison.OrdinalIgnoreCase) || method.Equals("POST", StringComparison.OrdinalIgnoreCase)))
        {
            var createEffect = method.Equals("POST", StringComparison.OrdinalIgnoreCase);
            var result = await ApplyKeyboardAsync(session, body, createEffect).ConfigureAwait(false);
            await WriteResponseAsync(writer, 200, method.Equals("POST", StringComparison.OrdinalIgnoreCase)
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
            var result = await ApplyKeyboardAsync(session, body, createEffect: false).ConfigureAwait(false);
            await WriteResponseAsync(writer, 200, new { result = result.Code }).ConfigureAwait(false);
            return;
        }
        if (method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) &&
            (suffix.Equals("keyboard", StringComparison.OrdinalIgnoreCase) ||
             suffix.StartsWith("keyboard/", StringComparison.OrdinalIgnoreCase)))
        {
            var effectId = suffix.Length > "keyboard/".Length
                ? suffix["keyboard/".Length..]
                : null;
            var result = await ClearKeyboardEffectAsync(session, effectId).ConfigureAwait(false);
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
            var result = await ClearKeyboardEffectAsync(session, effectId).ConfigureAwait(false);
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
                !info.DeviceSupported.Any(device => device.Equals("keyboard", StringComparison.OrdinalIgnoreCase)) ||
                !(info.Category?.Equals("application", StringComparison.OrdinalIgnoreCase) == true ||
                  info.Category?.Equals("game", StringComparison.OrdinalIgnoreCase) == true))
            {
                await WriteResponseAsync(writer, 200, new { result = InvalidParameter }).ConfigureAwait(false);
                return;
            }

            var id = Interlocked.Increment(ref _nextSessionId);
            var session = new Session(id.ToString(), info.Title);
            var stopping = false;
            lock (_gate)
            {
                stopping = _stop.IsCancellationRequested;
                if (!stopping) _sessions[session.Id] = session;
            }
            if (stopping)
            {
                await WriteResponseAsync(writer, 503, new { result = DeviceNotConnected }).ConfigureAwait(false);
                return;
            }
            await WriteResponseAsync(writer, 200, new
            {
                sessionid = id,
                uri = $"http://localhost:{Port}/razer/chromasdk/{session.Id}"
            }).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            await WriteResponseAsync(writer, 400, new { result = InvalidParameter }).ConfigureAwait(false);
        }
    }

    private async Task<(int Code, string? Id)> ApplyKeyboardAsync(
        Session session,
        string body,
        bool createEffect)
    {
        await _effectGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            return await ApplyKeyboardCoreAsync(session, body, createEffect).ConfigureAwait(false);
        }
        finally
        {
            _effectGate.Release();
        }
    }

    private async Task<(int Code, string? Id)> ApplyKeyboardCoreAsync(
        Session session,
        string body,
        bool createEffect)
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

            RazerRgb[] frame;
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
                var parsed = ParseFrame(root, effectElement.GetString());
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

            var changed = _frameSource.Publish(frame);
            if (!changed && !_lighting.RuntimeCompletion.IsCompleted)
            {
                Interlocked.Increment(ref _framesSkipped);
                session.ActiveEffectId = effectId;
                return (Success, effectId);
            }

            // Re-assert ownership on every frame. If the user changed the
            // normal effect while a game is alive, the next game frame takes
            // the device back without restarting an already active runtime.
            await _lighting.ApplyExternalAsync(
                _devices(),
                _frameSource,
                restorePersistentEffect: false,
                _stop.Token).ConfigureAwait(false);
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
            }
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException();
        }
        return element;
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

    private static (int Code, RazerRgb[]? Frame) ParseFrame(JsonElement root, string? effect)
    {
        switch (effect?.ToUpperInvariant())
        {
            case "CHROMA_NONE":
                return (Success, new RazerRgb[QuickLightingEngine.PixelCount]);
            case "CHROMA_STATIC":
                var staticParam = RequireObject(root, "param");
                return (Success, ChromaKeyboardFrameMapper.Static(
                    ChromaKeyboardFrameMapper.ToRgb(ParseColor(staticParam, "color"))));
            case "CHROMA_CUSTOM":
                if (!root.TryGetProperty("param", out var custom))
                {
                    throw new InvalidOperationException();
                }
                return (Success, ChromaKeyboardFrameMapper.Custom(ParseMatrix(custom, 6, 22)));
            case "CHROMA_CUSTOM2":
                var custom2 = RequireObject(root, "param");
                return (Success, ChromaKeyboardFrameMapper.Custom2Key(
                    ParseMatrix(custom2, "color", 8, 24),
                    ParseMatrix(custom2, "key", 6, 22)));
            case "CHROMA_CUSTOM_KEY":
                var parameter = RequireObject(root, "param");
                return (Success, ChromaKeyboardFrameMapper.CustomKey(
                    ParseMatrix(parameter, "color", 6, 22),
                    ParseMatrix(parameter, "key", 6, 22)));
            default:
                return (NotSupported, null);
        }
    }

    private static uint ParseColor(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetUInt32(out var value) ||
            value > 0x00FF_FFFFu)
        {
            throw new InvalidOperationException();
        }
        return value;
    }

    private static List<List<uint>> ParseMatrix(JsonElement parent, string propertyName, int rows, int columns)
    {
        if (!parent.TryGetProperty(propertyName, out var element))
        {
            throw new InvalidOperationException();
        }
        return ParseMatrix(element, rows, columns);
    }

    private static List<List<uint>> ParseMatrix(JsonElement element, int rows, int columns)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != rows)
        {
            throw new InvalidOperationException();
        }

        var matrix = new List<List<uint>>();
        foreach (var row in element.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != columns)
            {
                throw new InvalidOperationException();
            }

            var values = new List<uint>();
            foreach (var value in row.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out var color))
                {
                    throw new InvalidOperationException();
                }
                values.Add(color);
            }
            matrix.Add(values);
        }
        return matrix;
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
        using var timer = new PeriodicTimer(SessionSweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                Session[] expired;
                lock (_gate) expired = _sessions.Values
                    .Where(item => !item.ApplyInProgress && DateTimeOffset.UtcNow - item.LastHeartbeat > SessionTimeout)
                    .ToArray();
                foreach (var session in expired)
                {
                    try
                    {
                        await RemoveSessionAsync(session).ConfigureAwait(false);
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

    private async Task RemoveSessionAsync(Session session)
    {
        await _effectGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            bool removed;
            bool wasActive;
            lock (_gate)
            {
                removed = _sessions.Remove(session.Id);
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
                await _lighting.StopAsync().ConfigureAwait(false);
                await _restoreLighting().ConfigureAwait(false);
            }
        }
        finally
        {
            _effectGate.Release();
        }
    }

    private async Task<int> ClearKeyboardEffectAsync(Session session, string? effectId)
    {
        await _effectGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            bool removed;
            bool wasActive;
            lock (_gate)
            {
                removed = _sessions.ContainsKey(session.Id);
                if (!removed)
                {
                    return DeviceNotConnected;
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
            if (removed && wasActive && !HasActiveSession())
            {
                await _lighting.StopAsync().ConfigureAwait(false);
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
                await _lighting.StopAsync().ConfigureAwait(false);
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

    private static async Task<string> ReadBodyAsync(StreamReader reader, int length)
    {
        var builder = new StringBuilder(Math.Min(length, 16 * 1024));
        var byteCount = 0;
        var buffer = new char[Math.Min(Math.Max(length, 1), 4096)];
        while (byteCount < length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            byteCount += Encoding.UTF8.GetByteCount(buffer, 0, read);
            if (byteCount > length) throw new InvalidDataException("Content-Length does not match UTF-8 body.");
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
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
        public Dictionary<string, RazerRgb[]> Effects { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

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
    long FramesSkipped);
