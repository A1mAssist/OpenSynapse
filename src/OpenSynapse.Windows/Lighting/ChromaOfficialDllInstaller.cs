using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Lighting;

internal enum ChromaOfficialDllInstallOutcome
{
    Installed,
    Cancelled,
    DownloadFailed,
    VerificationFailed,
    WriteFailed,
}

/// <summary>
/// Downloads the Razer-signed RzChromaSDK64.dll we host and copies it into the
/// system directory the broker loads it from. The download stays in the app;
/// only the final file copy needs the administrator token.
/// </summary>
internal static class ChromaOfficialDllInstaller
{
    private const string DownloadUrl =
        "https://github.com/A1mAssist/OpenSynapse/releases/download/chroma-sdk-4.1.0.10151444/RzChromaSDK64.dll";
    private const long PackageLength = 273096;
    private const string PackageSha256 =
        "7AB56EA40C67450F81772CD35C3DAE8E36FC2CA43110A2D8294DCAC4B2E364E7";

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    internal static Task<ChromaOfficialDllInstallOutcome> InstallAsync(
        CancellationToken cancellationToken) =>
        InstallAsync(AppDataPaths.Combine("Temp"), CopyElevated, cancellationToken);

    /// <summary>
    /// Runs inside the machine-wide service, which already holds the rights, so
    /// the same install needs no UAC prompt.
    /// </summary>
    internal static Task<ChromaOfficialDllInstallOutcome> InstallAsServiceAsync(
        CancellationToken cancellationToken) =>
        InstallAsync(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenSynapse",
                "Chroma"),
            CopyDirect,
            cancellationToken);

    private static async Task<ChromaOfficialDllInstallOutcome> InstallAsync(
        string directory,
        Func<string, ChromaOfficialDllInstallOutcome> install,
        CancellationToken cancellationToken)
    {
        var download = Path.Combine(directory, "RzChromaSDK64.download");
        try
        {
            Directory.CreateDirectory(directory);
            using (var response = await Client.GetAsync(
                       DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length &&
                    length != PackageLength)
                {
                    return ChromaOfficialDllInstallOutcome.VerificationFailed;
                }

                await using var source = await response.Content
                    .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var target = new FileStream(
                    download,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(download).Length != PackageLength)
            {
                return ChromaOfficialDllInstallOutcome.VerificationFailed;
            }

            using (var stream = new FileStream(
                       download, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!string.Equals(
                        Convert.ToHexString(SHA256.HashData(stream)),
                        PackageSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return ChromaOfficialDllInstallOutcome.VerificationFailed;
                }
            }

            return install(download);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return ChromaOfficialDllInstallOutcome.DownloadFailed;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return ChromaOfficialDllInstallOutcome.WriteFailed;
        }
        finally
        {
            try
            {
                File.Delete(download);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static ChromaOfficialDllInstallOutcome CopyDirect(string source)
    {
        try
        {
            File.Copy(source, ChromaBrokerLauncher.OfficialDllPath, overwrite: true);
            return ChromaOfficialDllInstallOutcome.Installed;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return ChromaOfficialDllInstallOutcome.WriteFailed;
        }
    }

    private static ChromaOfficialDllInstallOutcome CopyElevated(string source)
    {
        const char quote = '"';
        var target = ChromaBrokerLauncher.OfficialDllPath;
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = "/d /c copy /y " + quote + source + quote + " " + quote + target + quote,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return ChromaOfficialDllInstallOutcome.Cancelled;
        }
        catch (Win32Exception)
        {
            return ChromaOfficialDllInstallOutcome.WriteFailed;
        }

        if (process is null)
        {
            return ChromaOfficialDllInstallOutcome.WriteFailed;
        }

        using (process)
        {
            process.WaitForExit();
        }

        return File.Exists(target) && new FileInfo(target).Length == PackageLength
            ? ChromaOfficialDllInstallOutcome.Installed
            : ChromaOfficialDllInstallOutcome.WriteFailed;
    }
}
