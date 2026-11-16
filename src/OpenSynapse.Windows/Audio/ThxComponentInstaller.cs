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
    string Arguments);

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
        Timeout = TimeSpan.FromMinutes(5),
    };

    internal static async Task<ThxComponentInstallResult> InstallAsync(
        ThxInstallerPackage package,
        CancellationToken cancellationToken,
        bool repair = false,
        string? startService = null,
        ThxInstallerPackage? extra = null)
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
        try
        {
            Directory.CreateDirectory(directory);
            var downloadError = await DownloadAsync(package, temporary, cancellationToken)
                .ConfigureAwait(false);
            if (downloadError is not null)
            {
                return new(downloadError);
            }
            if (extra is not null)
            {
                downloadError = await DownloadAsync(extra, extraTemporary!, cancellationToken)
                    .ConfigureAwait(false);
                if (downloadError is not null)
                {
                    return new(downloadError);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunAsync(
                temporary,
                repair ? $"/repair {package.Arguments}" : package.Arguments,
                startService,
                extraTemporary,
                extra?.Arguments).ConfigureAwait(false);
            if (repair && result.Error is not null)
            {
                // A leftover service with no registered bundle cannot be repaired:
                // retry the cached installer as a plain install.
                result = await RunAsync(
                    temporary,
                    package.Arguments,
                    startService,
                    extraTemporary,
                    extra?.Arguments).ConfigureAwait(false);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
        CancellationToken cancellationToken)
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
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

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
        string? extraArguments = null)
    {
        string? commandFile = null;
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
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new("ThxInstallerStartFailed");
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return ClassifyExitCode(process.ExitCode);
        }
        finally
        {
            if (commandFile is not null)
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
