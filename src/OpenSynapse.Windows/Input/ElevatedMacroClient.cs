using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSynapse.Core.Macros;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Sends injection requests to the elevated macro agent.
///
/// Every call fails soft: when the agent is missing or refuses a request the
/// caller keeps using its own SendInput path, so the app still works on
/// machines where the task was never installed.
/// </summary>
public sealed class ElevatedMacroClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// True when the agent answered a ping, so the caller knows the elevated
    /// path is usable before switching playback over to it.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            new ElevatedMacroRequest { Kind = ElevatedMacroCommandKind.Ping },
            cancellationToken).ConfigureAwait(false);
        return response?.Ok == true;
    }

    /// <summary>
    /// True when the agent answers and runs above the app's integrity level.
    ///
    /// A task that was registered without elevation, or that ran under a
    /// different account, would fail the same way the app's own SendInput
    /// call does, so the app checks the reported level before it routes
    /// macro output there.
    /// </summary>
    public async Task<bool> IsElevatedAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            new ElevatedMacroRequest { Kind = ElevatedMacroCommandKind.Ping },
            cancellationToken).ConfigureAwait(false);
        return response?.Ok == true &&
            ProcessIntegrityLevel.IsElevated(response.IntegrityLevel);
    }

    public Task<ElevatedMacroResponse?> SendInputAsync(
        MacroInput input,
        bool isDown,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return SendAsync(
            new ElevatedMacroRequest
            {
                Kind = ElevatedMacroCommandKind.Input,
                Input = input,
                IsDown = isDown,
            },
            cancellationToken);
    }

    public Task<ElevatedMacroResponse?> SendMouseMoveAsync(
        MacroMouseMoveMode mode,
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            new ElevatedMacroRequest
            {
                Kind = ElevatedMacroCommandKind.MouseMove,
                MouseMoveMode = mode,
                X = x,
                Y = y,
            },
            cancellationToken);
    }

    public Task<ElevatedMacroResponse?> SendTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SendAsync(
            new ElevatedMacroRequest
            {
                Kind = ElevatedMacroCommandKind.Text,
                Text = text,
            },
            cancellationToken);
    }

    public Task<ElevatedMacroResponse?> ReleaseAllAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync(
            new ElevatedMacroRequest { Kind = ElevatedMacroCommandKind.ReleaseAll },
            cancellationToken);

    private async Task<ElevatedMacroResponse?> SendAsync(
        ElevatedMacroRequest request,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);

            await using var pipe = new NamedPipeClientStream(
                ".",
                ElevatedMacroProtocol.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(false),
                4096,
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: false,
                4096,
                leaveOpen: true);

            await writer.WriteLineAsync(
                JsonSerializer.Serialize(request, SerializerOptions).AsMemory(),
                timeout.Token).ConfigureAwait(false);

            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) ||
                line.Length > ElevatedMacroProtocol.MaximumMessageLength)
            {
                return null;
            }

            return JsonSerializer.Deserialize<ElevatedMacroResponse>(line, SerializerOptions);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or UnauthorizedAccessException or
                InvalidOperationException or JsonException or ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }
}
