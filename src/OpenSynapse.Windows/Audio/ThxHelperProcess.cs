using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

/// <summary>
/// Owns one OpenSynapse.ThxHelper child process and its JSON-line pipe.
/// Callers do the admission check; this class only serializes requests.
/// </summary>
internal sealed class ThxHelperProcess : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;

    internal async Task<ThxAudioStatus> SendAsync(
        ThxAudioHelperRequest request,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var error = await EnsureHelperAsync(cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                return new ThxAudioStatus(ThxAudioAvailability.ComponentsUnavailable, error, null);
            }

            try
            {
                await _input!.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                var line = await ReadLineWithTimeoutAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    await StopHelperAsync().ConfigureAwait(false);
                    return new ThxAudioStatus(
                        ThxAudioAvailability.ComponentsUnavailable,
                        "ThxHelperExited",
                        null);
                }

                var response = JsonSerializer.Deserialize<ThxAudioHelperResponse>(line);
                if (response?.Status is { } status)
                {
                    if (!response.Ok)
                    {
                        await StopHelperAsync().ConfigureAwait(false);
                    }
                    return status;
                }

                await StopHelperAsync().ConfigureAwait(false);
                return new ThxAudioStatus(
                    ThxAudioAvailability.ComponentsUnavailable,
                    response?.Error ?? "ThxHelperInvalidResponse",
                    null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or JsonException or TimeoutException)
            {
                await StopHelperAsync().ConfigureAwait(false);
                return new ThxAudioStatus(
                    ThxAudioAvailability.ComponentsUnavailable,
                    $"ThxHelperFailed:{exception.Message}",
                    null);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopHelperAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<string?> EnsureHelperAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && _input is not null && _output is not null)
        {
            return null;
        }

        await StopHelperAsync().ConfigureAwait(false);
        var path = Path.Combine(AppContext.BaseDirectory, ThxAudioHelperProtocol.ExecutableName);
        if (!File.Exists(path))
        {
            return "ThxHelperMissing";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true),
            WorkingDirectory = AppContext.BaseDirectory,
        };
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            return "ThxHelperStartFailed";
        }

        _process = process;
        _input = process.StandardInput;
        _output = process.StandardOutput;
        _ = DrainErrorsAsync(process.StandardError);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }

    private async Task<string?> ReadLineWithTimeoutAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            return await _output!.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await StopHelperAsync().ConfigureAwait(false);
            throw new TimeoutException("THX helper did not respond in time.");
        }
    }

    private static async Task DrainErrorsAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is not null)
            {
            }
        }
        catch (Exception)
        {
            // The request pipe remains authoritative.
        }
    }

    private async Task StopHelperAsync()
    {
        var process = _process;
        _process = null;
        _input = null;
        _output = null;
        if (process is null)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception)
        {
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
