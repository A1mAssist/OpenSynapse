using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Audio;

internal sealed record ThxInstallerPackage(
    string Uri,
    long Length,
    string Sha256,
    string Arguments);

internal sealed record ThxComponentInstallResult(string? Error, bool RebootRequired = false);

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
        string? startService = null)
    {
        if (package.Length <= 0 || package.Sha256.Length != 64)
        {
            return new("ThxInstallerVerificationFailed");
        }

        var directory = Path.Combine(AppDataPaths.Combine("Temp"), "Thx");
        var temporary = Path.Combine(directory, $"thx-{Guid.NewGuid():N}.exe");
        try
        {
            Directory.CreateDirectory(directory);
            using var response = await Client.GetAsync(
                package.Uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } declaredLength &&
                declaredLength != package.Length)
            {
                return new("ThxInstallerVerificationFailed");
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

            if (new FileInfo(temporary).Length != package.Length)
            {
                return new("ThxInstallerVerificationFailed");
            }

            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!string.Equals(
                        Convert.ToHexString(SHA256.HashData(stream)),
                        package.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new("ThxInstallerVerificationFailed");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunAsync(
                temporary,
                repair ? $"/repair {package.Arguments}" : package.Arguments,
                startService).ConfigureAwait(false);
            if (repair && result.Error is not null)
            {
                // A leftover service with no registered bundle cannot be repaired:
                // retry the cached installer as a plain install.
                result = await RunAsync(temporary, package.Arguments, startService)
                    .ConfigureAwait(false);
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
            try
            {
                File.Delete(temporary);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<ThxComponentInstallResult> RunAsync(
        string path,
        string arguments,
        string? startService)
    {
        string? commandFile = null;
        try
        {
            var fileName = path;
            var commandArguments = arguments;
            if (startService is not null)
            {
                // Run the installer and sc start under the same elevated token, so
                // a stopped service does not force a second UAC prompt or a reboot.
                commandFile = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    $"thx-{Guid.NewGuid():N}.cmd");
                await File.WriteAllTextAsync(
                    commandFile,
                    $"@echo off\r\n\"{path}\" {arguments}\r\nset RC=%ERRORLEVEL%\r\nsc start {startService} >nul 2>&1\r\nexit /b %RC%\r\n",
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
            return process.ExitCode switch
            {
                0 => new(null),
                1641 or 3010 => new(null, RebootRequired: true),
                _ => new("ThxInstallerFailed"),
            };
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
}
