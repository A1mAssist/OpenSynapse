using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenSynapse.Core.Audio;

namespace OpenSynapse.Windows.Audio;

public sealed class WindowsThxAudioController : IThxAudioController, IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient ComponentClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public static bool IsSupportedModel() => ThxAudioAdmission.IsSupportedHostModel();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;

    public Task<ThxAudioStatus> ReadAsync(
        ThxAudioEndpoint endpoint,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "read",
            EndpointName(endpoint)), cancellationToken);

    public Task<ThxAudioStatus> SetSpatialAsync(
        ThxAudioEndpoint endpoint,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "setSpatial",
            EndpointName(endpoint),
            Enabled: enabled), cancellationToken);

    public Task<ThxAudioStatus> SetEqualizerAsync(
        ThxAudioEndpoint endpoint,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "setEqualizer",
            EndpointName(endpoint),
            Enabled: enabled), cancellationToken);

    public Task<ThxAudioStatus> SelectPresetAsync(
        ThxAudioEndpoint endpoint,
        string presetId,
        IReadOnlyList<double> gainsDb,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "selectPreset",
            EndpointName(endpoint),
            PresetId: presetId,
            GainsDb: gainsDb.ToArray()), cancellationToken);

    public Task<ThxAudioStatus> SetEnhancementAsync(
        ThxAudioEndpoint endpoint,
        ThxAudioEnhancementKind kind,
        bool enabled,
        int? levelPercent,
        CancellationToken cancellationToken = default) =>
        SendAsync(new ThxAudioHelperRequest(
            ThxAudioHelperProtocol.Version,
            "setEnhancement",
            EndpointName(endpoint),
            Enabled: enabled,
            Enhancement: kind == ThxAudioEnhancementKind.VoiceClarity
                ? "voiceClarity"
                : "soundNormalization",
            LevelPercent: levelPercent), cancellationToken);

    public async Task<ThxAudioStatus> InstallNativeComponentAsync(
        ThxAudioEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var admission = ThxAudioAdmission.Check();
        if (admission.Status.IsAvailable)
        {
            return await ReadAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }
        if (admission.Status.Error != "ThxNativeMissing")
        {
            return admission.Status;
        }

        var destination = ThxAudioAdmission.NativeDllPath;
        var directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("The THX component path has no directory.");
        var temporary = Path.Combine(
            directory,
            $"{ThxAudioAdmission.NativeDllName}.{Guid.NewGuid():N}.download");
        try
        {
            Directory.CreateDirectory(directory);
            using var response = await ComponentClient.GetAsync(
                ThxAudioAdmission.NativeDllUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } declaredLength &&
                declaredLength != ThxAudioAdmission.NativeDllLength)
            {
                return DownloadFailure("ThxDownloadVerificationFailed");
            }

            await using (var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporary).Length != ThxAudioAdmission.NativeDllLength)
            {
                return DownloadFailure("ThxDownloadVerificationFailed");
            }

            using (var stream = new FileStream(
                temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!string.Equals(
                        Convert.ToHexString(SHA256.HashData(stream)),
                        ThxAudioAdmission.NativeDllSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return DownloadFailure("ThxDownloadVerificationFailed");
                }
            }

            File.Move(temporary, destination, overwrite: true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DownloadFailure("ThxDownloadFailed");
        }
        catch (HttpRequestException)
        {
            return DownloadFailure("ThxDownloadFailed");
        }
        catch (UnauthorizedAccessException)
        {
            return DownloadFailure("ThxDownloadWriteFailed");
        }
        catch (IOException)
        {
            return DownloadFailure("ThxDownloadFailed");
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return await ReadAsync(endpoint, cancellationToken).ConfigureAwait(false);
    }

    private static ThxAudioStatus DownloadFailure(string error) =>
        new(ThxAudioAvailability.ComponentsUnavailable, error, null);

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

    private async Task<ThxAudioStatus> SendAsync(
        ThxAudioHelperRequest request,
        CancellationToken cancellationToken)
    {
        var admission = ThxAudioAdmission.Check();
        if (admission.Profile is null || !admission.Status.IsAvailable)
        {
            return admission.Status;
        }

        await _gate.WaitAsync(cancellationToken);
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

    private static string EndpointName(ThxAudioEndpoint endpoint) =>
        endpoint == ThxAudioEndpoint.Speakers ? "speakers" : "headphones";
}
