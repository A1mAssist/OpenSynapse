using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Audio;

internal sealed record ThxInstallerPackage(
    string Uri,
    long Length,
    string Sha256,
    string Arguments,
    Version? Version = null);

internal sealed record ThxComponentInstallResult(
    string? Error,
    bool RebootRequired = false,
    bool AlreadyInstalled = false);

/// <summary>
/// Downloads the official per-device THX package and runs its silent installer.
/// The package contains THX APO/SCU/service components only; it does not carry Synapse.
/// </summary>
internal static class ThxComponentInstaller
{
    private static readonly HttpClient Client = new()
    {
        // The THX package is about 40 MB on Razer's CDN. The linked token below
        // is the single budget for download + install; a shorter client timeout
        // turns a slow but healthy download into an unhandled TaskCanceledException.
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static readonly TimeSpan InstallBudget = TimeSpan.FromMinutes(15);

    internal static async Task<ThxComponentInstallResult> InstallAsync(
        ThxInstallerPackage package,
        CancellationToken cancellationToken,
        bool repair = false,
        string? startService = null,
        ThxInstallerPackage? extra = null,
        IProgress<double>? progress = null,
        Action<string>? diagnostic = null)
    {
        if (package.Length <= 0 || package.Sha256.Length != 64)
        {
            return new("ThxInstallerVerificationFailed");
        }
        if (extra is not null && (extra.Length <= 0 || extra.Sha256.Length != 64))
        {
            return new("ThxInstallerVerificationFailed");
        }

        var directory = Path.Combine(AppDataPaths.Combine("Temp"), "Thx");
        var temporary = Path.Combine(directory, $"thx-{Guid.NewGuid():N}.exe");
        var extraTemporary = extra is null
            ? null
            : Path.Combine(directory, $"thx-{Guid.NewGuid():N}.exe");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(InstallBudget);
        var token = budget.Token;
        try
        {
            Directory.CreateDirectory(directory);
            var totalLength = package.Length + (extra?.Length ?? 0);
            diagnostic?.Invoke(
                $"thx installer download start main={package.Length} extra={extra?.Length ?? 0}");
            var downloadError = await DownloadAsync(
                package,
                temporary,
                token,
                new Progress<double>(fraction =>
                    progress?.Report(fraction * package.Length / totalLength)))
                .ConfigureAwait(false);
            if (downloadError is not null)
            {
                return new(downloadError);
            }
            if (extra is not null)
            {
                downloadError = await DownloadAsync(
                    extra,
                    extraTemporary!,
                    token,
                    new Progress<double>(fraction => progress?.Report(
                        (package.Length + fraction * extra.Length) / totalLength)))
                    .ConfigureAwait(false);
                if (downloadError is not null)
                {
                    return new(downloadError);
                }
            }
            progress?.Report(1d);
            diagnostic?.Invoke("thx installer download done");

            token.ThrowIfCancellationRequested();
            var arguments = repair ? $"/repair {package.Arguments}" : package.Arguments;
            diagnostic?.Invoke($"thx installer start args={arguments} repair={repair}");
            var result = await RunAsync(
                temporary,
                arguments,
                startService,
                extraTemporary,
                extra?.Arguments,
                token).ConfigureAwait(false);
            diagnostic?.Invoke(
                $"thx installer exit error={result.Error ?? "-"} " +
                $"reboot={result.RebootRequired} already={result.AlreadyInstalled}");
            if (repair && result.Error is not null)
            {
                // A leftover service with no registered bundle cannot be repaired:
                // retry the cached installer as a plain install.
                diagnostic?.Invoke("thx installer repair failed; retrying plain install");
                result = await RunAsync(
                    temporary,
                    package.Arguments,
                    startService,
                    extraTemporary,
                    extra?.Arguments,
                    token).ConfigureAwait(false);
                diagnostic?.Invoke(
                    $"thx installer exit error={result.Error ?? "-"} " +
                    $"reboot={result.RebootRequired} already={result.AlreadyInstalled}");
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            diagnostic?.Invoke("thx installer timed out");
            return new("ThxInstallerTimeout");
        }
        catch (HttpRequestException)
        {
            return new("ThxInstallerDownloadFailed");
        }
        catch (IOException)
        {
            return new("ThxInstallerDownloadFailed");
        }
        catch (UnauthorizedAccessException)
        {
            return new("ThxInstallerWriteFailed");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new("ThxInstallerCancelled");
        }
        catch (Win32Exception)
        {
            return new("ThxInstallerStartFailed");
        }
        finally
        {
            TryDelete(temporary);
            if (extraTemporary is not null)
            {
                TryDelete(extraTemporary);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task<string?> DownloadAsync(
        ThxInstallerPackage package,
        string path,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        using var response = await Client.GetAsync(
            package.Uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } declaredLength &&
            declaredLength != package.Length)
        {
            return "ThxInstallerVerificationFailed";
        }

        var buffer = new byte[81920];
        long received = 0;
        var reportedPercent = -1;
        await using (var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            int read;
            while ((read = await source
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false)) > 0)
            {
                await target
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                received += read;
                var percent = (int)Math.Min(100L, received * 100L / package.Length);
                if (percent != reportedPercent)
                {
                    reportedPercent = percent;
                    progress?.Report(percent / 100d);
                }
            }
        }
        progress?.Report(1d);

        if (new FileInfo(path).Length != package.Length)
        {
            return "ThxInstallerVerificationFailed";
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return string.Equals(
            Convert.ToHexString(SHA256.HashData(stream)),
            package.Sha256,
            StringComparison.OrdinalIgnoreCase)
                ? null
                : "ThxInstallerVerificationFailed";
    }

    private static async Task<ThxComponentInstallResult> RunAsync(
        string path,
        string arguments,
        string? startService,
        string? extraPath = null,
        string? extraArguments = null,
        CancellationToken cancellationToken = default)
    {
        string? commandFile = null;
        Process? process = null;
        try
        {
            var fileName = path;
            var commandArguments = arguments;
            if (startService is not null || extraPath is not null)
            {
                // Run the installer and the service restart under the same elevated
                // token, so a stopped or stale service does not force a second UAC
                // prompt or a reboot.
                commandFile = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    $"thx-{Guid.NewGuid():N}.cmd");
                var script = new StringBuilder()
                    .AppendLine("@echo off")
                    .AppendLine($"\"{path}\" {arguments}")
                    .AppendLine("set RC=%ERRORLEVEL%")
                    .AppendLine("set REBOOT=0");
                if (extraPath is not null)
                {
                    // The preset package is supplementary. Keep
                    // only its reboot signal so a preset failure cannot mask the
                    // APO installer's exit code.
                    script
                        .AppendLine($"\"{extraPath}\" {extraArguments}")
                        .AppendLine("if %ERRORLEVEL%==1641 set REBOOT=1")
                        .AppendLine("if %ERRORLEVEL%==3010 set REBOOT=1");
                }
                script
                    .AppendLine("if %REBOOT%==1 if %RC%==0 set RC=3010")
                    .AppendLine("if %REBOOT%==1 if %RC%==1638 set RC=3010");
                if (startService is not null)
                {
                    // The THX APO is bound per audio endpoint. A device that
                    // re-enumerated keeps the old endpoint state in VSSrv, so a
                    // plain start is not enough: restart it after the installer.
                    script
                        .AppendLine($"net stop {startService} /y >nul 2>&1")
                        .AppendLine($"net start {startService} >nul 2>&1");
                }
                script.AppendLine("exit /b %RC%");
                await File.WriteAllTextAsync(
                    commandFile,
                    script.ToString(),
                    CancellationToken.None).ConfigureAwait(false);
                fileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                commandArguments = $"/d /c call \"{commandFile}\"";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = commandArguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new("ThxInstallerStartFailed");
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return ClassifyExitCode(process.ExitCode);
        }
        finally
        {
            // On timeout the elevated cmd.exe is still using the batch file.
            // Leave it alone; deleting it would abort the remaining install steps.
            if (commandFile is not null && (process is null || process.HasExited))
            {
                try
                {
                    File.Delete(commandFile);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    internal static ThxComponentInstallResult ClassifyExitCode(int exitCode) => exitCode switch
    {
        0 => new(null),
        1641 or 3010 => new(null, RebootRequired: true),
        // The same pinned version is already installed. The service restart
        // above still re-binds the APO to the current endpoint.
        1638 => new(null, AlreadyInstalled: true),
        // The per-model package returns 3 when a matching service is already
        // present (for example after Razer's own THX installer). The native
        // client is still staged by the caller.
        3 => new(null, AlreadyInstalled: true),
        1618 => new("ThxInstallerBusy"),
        _ => new($"ThxInstallerExit{exitCode}"),
    };
}
