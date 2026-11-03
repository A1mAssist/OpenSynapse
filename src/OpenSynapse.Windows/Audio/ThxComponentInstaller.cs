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

    internal static async Task<string?> InstallAsync(
        ThxInstallerPackage package,
        CancellationToken cancellationToken)
    {
        if (package.Length <= 0 || package.Sha256.Length != 64)
        {
            return "ThxInstallerVerificationFailed";
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
                return "ThxInstallerVerificationFailed";
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
                return "ThxInstallerVerificationFailed";
            }

            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!string.Equals(
                        Convert.ToHexString(SHA256.HashData(stream)),
                        package.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "ThxInstallerVerificationFailed";
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = new ProcessStartInfo
            {
                FileName = temporary,
                Arguments = package.Arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "ThxInstallerStartFailed";
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            return process.ExitCode is 0 or 1641 or 3010
                ? null
                : "ThxInstallerFailed";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return "ThxInstallerDownloadFailed";
        }
        catch (IOException)
        {
            return "ThxInstallerDownloadFailed";
        }
        catch (UnauthorizedAccessException)
        {
            return "ThxInstallerWriteFailed";
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return "ThxInstallerCancelled";
        }
        catch (Win32Exception)
        {
            return "ThxInstallerStartFailed";
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
}
