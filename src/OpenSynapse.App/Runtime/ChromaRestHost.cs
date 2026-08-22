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
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SessionSweepInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Func<IReadOnlyList<DeviceDescriptor>> _devices;
    private readonly IBladeLightingController _lighting;
    private readonly Func<Task> _restoreLighting;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ChromaExternalFrameSource _frameSource = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private Task? _sweepLoop;
    private int _started;
    private int _disposed;
    private int _nextSessionId;

    internal ChromaRestHost(
        Func<IReadOnlyList<DeviceDescriptor>> devices,
        IBladeLightingController lighting,
        Func<Task> restoreLighting)
    {
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
        _restoreLighting = restoreLighting ?? throw new ArgumentNullException(nameof(restoreLighting));
    }

    internal bool IsRunning => Volatile.Read(ref _started) != 0;

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
        var path = rawPath.Split('?', 2)[0];
        if (method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(writer, 200, new { result = Success }).ConfigureAwait(false);
            return;
        }
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && path == "/razer/chromasdk")
        {
            await WriteResponseAsync(writer, 200, new { version = "4.0.0" }).ConfigureAwait(false);
            return;
        }
        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && path == "/razer/chromasdk")
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
        if (suffix.Equals("heartbeat", StringComparison.OrdinalIgnoreCase) && method.Equals("PUT", StringComparison.OrdinalIgnoreCase))
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
            var result = await ApplyKeyboardAsync(session, body).ConfigureAwait(false);
            await WriteResponseAsync(writer, 200, method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                ? new { result = result.Code, id = result.Id }
                : new { result = result.Code }).ConfigureAwait(false);
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
            lock (_gate) _sessions[session.Id] = session;
            await WriteResponseAsync(writer, 200, new
            {
                sessionid = id,
                uri = $"http://localhost:{Port}/razer/chromasdk/{session.Id}"
            }).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await WriteResponseAsync(writer, 400, new { result = InvalidParameter }).ConfigureAwait(false);
        }
    }

    private async Task<(int Code, string? Id)> ApplyKeyboardAsync(Session session, string body)
    {
        await session.ApplyGate.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            return await ApplyKeyboardCoreAsync(session, body).ConfigureAwait(false);
        }
        finally
        {
            session.ApplyGate.Release();
        }
    }

    private async Task<(int Code, string? Id)> ApplyKeyboardCoreAsync(Session session, string body)
    {
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
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var effect = root.GetProperty("effect").GetString();
            RazerRgb[] frame;
            switch (effect)
            {
                case "CHROMA_NONE":
                    frame = new RazerRgb[QuickLightingEngine.PixelCount];
                    break;
                case "CHROMA_STATIC":
                    frame = ChromaKeyboardFrameMapper.Static(
                        ChromaKeyboardFrameMapper.ToRgb(root.GetProperty("param").GetProperty("color").GetUInt32()));
                    break;
                case "CHROMA_CUSTOM":
                    frame = ChromaKeyboardFrameMapper.Custom(ParseMatrix(
                        root.GetProperty("param")));
                    break;
                case "CHROMA_CUSTOM2":
                    var custom2 = root.GetProperty("param");
                    frame = ChromaKeyboardFrameMapper.Custom2Key(
                        ParseMatrix(custom2.GetProperty("color")),
                        ParseMatrix(custom2.GetProperty("key")));
                    break;
                case "CHROMA_CUSTOM_KEY":
                    var parameter = root.GetProperty("param");
                    frame = ChromaKeyboardFrameMapper.CustomKey(
                        ParseMatrix(parameter.GetProperty("color")),
                        ParseMatrix(parameter.GetProperty("key")));
                    break;
                default:
                    return (NotSupported, null);
            }
            _frameSource.Publish(frame);
            // Re-assert ownership on every frame. If the user changed the
            // normal effect while a game is alive, the next game frame takes
            // the device back without restarting an already active runtime.
            await _lighting.ApplyExternalAsync(_devices(), _frameSource, _stop.Token).ConfigureAwait(false);
            lock (_gate)
            {
                session.LastHeartbeat = DateTimeOffset.UtcNow;
                session.Active = true;
            }
            return (Success, Guid.NewGuid().ToString());
        }
        catch (InvalidOperationException)
        {
            return (InvalidParameter, null);
        }
        catch (Exception) when (!_stop.IsCancellationRequested)
        {
            return (DeviceNotConnected, null);
        }
    }

    private static List<List<uint>> ParseMatrix(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException();
        }

        var matrix = new List<List<uint>>();
        foreach (var row in element.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException();
            }

            var values = new List<uint>();
            foreach (var value in row.EnumerateArray())
            {
                values.Add(value.GetUInt32());
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
                lock (_gate) expired = _sessions.Values.Where(item => DateTimeOffset.UtcNow - item.LastHeartbeat > SessionTimeout).ToArray();
                foreach (var session in expired) await RemoveSessionAsync(session).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RemoveSessionAsync(Session session)
    {
        await session.ApplyGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        bool removed;
        bool wasActive;
        try
        {
            lock (_gate)
            {
                removed = _sessions.Remove(session.Id);
                wasActive = session.Active;
                session.Active = false;
            }
        }
        finally
        {
            session.ApplyGate.Release();
        }
        if (!removed)
        {
            return;
        }
        if (wasActive && !HasActiveSession())
        {
            await _lighting.StopAsync().ConfigureAwait(false);
            await _restoreLighting().ConfigureAwait(false);
        }
    }

    private async Task StopAllSessionsAsync()
    {
        Session[] sessions;
        lock (_gate) sessions = _sessions.Values.ToArray();
        foreach (var session in sessions)
        {
            await session.ApplyGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        bool hadActiveSession;
        lock (_gate)
        {
            hadActiveSession = sessions.Any(session => session.Active);
            _sessions.Clear();
            foreach (var session in sessions) session.Active = false;
        }
        foreach (var session in sessions) session.ApplyGate.Release();
        if (hadActiveSession)
        {
            await _lighting.StopAsync().ConfigureAwait(false);
            await _restoreLighting().ConfigureAwait(false);
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
        await writer.WriteAsync($"HTTP/1.1 {status} {Reason(status)}\r\nContent-Type: application/json\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}").ConfigureAwait(false);
    }

    private static string Reason(int status) => status switch { 200 => "OK", 400 => "Bad Request", 404 => "Not Found", 413 => "Payload Too Large", _ => "Internal Server Error" };

    private sealed class Session(string id, string title)
    {
        public string Id { get; } = id;
        public string Title { get; } = title;
        public DateTimeOffset LastHeartbeat { get; set; } = DateTimeOffset.UtcNow;
        public int Tick { get; set; }
        public bool Active { get; set; }
        public SemaphoreSlim ApplyGate { get; } = new(1, 1);
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
