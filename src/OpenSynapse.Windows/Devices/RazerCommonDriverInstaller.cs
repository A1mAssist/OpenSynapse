using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;
using OpenSynapse.Windows.Lifecycle;

namespace OpenSynapse.Windows.Devices;

/// <summary>
/// The per-device RzDev filter creates a RAZER\DeviceContol child device. That
/// child only gets an RZCONTROL interface when the common RzCommon driver is
/// staged. The package here is the WHQL-signed Razer Common Driver published
/// in the Microsoft Update Catalog; only the common INF is installed.
/// </summary>
internal static class RazerCommonDriverInstaller
{
    private const string CabUrl =
        "https://catalog.s.download.windowsupdate.com/d/msdownload/update/driver/drvs/2026/03/" +
        "a945e0b5-39b7-4277-aa60-674bec5f999a_194e82cd8ea60dfeb1c797a779f8463cc722af74.cab";
    private const string CabSha256 =
        "68ECDD94589C1B05F66FB4D758CE95700599E59DBF769A70424098B93434C3B2";
    private const long CabSize = 83089;

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    internal static bool IsInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\RzCommon");
            if (key?.GetValue("ImagePath") is not string imagePath ||
                string.IsNullOrWhiteSpace(imagePath))
            {
                return false;
            }

            var path = imagePath.Trim();
            if (path.StartsWith('"'))
            {
                var closingQuote = path.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    path = path[1..closingQuote];
                }
            }

            path = Environment.ExpandEnvironmentVariables(path);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            const string systemRootPrefix = @"\SystemRoot\";
            if (path.StartsWith(systemRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(windows, path[systemRootPrefix.Length..]);
            }
            else if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(windows, path);
            }

            return File.Exists(path);
        }
    }

    /// <summary>
    /// Starts the common service after the INF is staged. A demand-start driver
    /// can remain stopped until this is called, which leaves the RZCONTROL
    /// child device absent even though the package is installed.
    /// </summary>
    internal static bool TryStartService(out string? error) =>
        WindowsServiceControl.TryStart(
            OpenSynapseServiceProtocol.RazerCommonServiceName,
            out error);

    /// <summary>
    /// Starts RzCommon through the machine-wide host, which runs elevated, and
    /// falls back to the in-process attempt for portable installs.
    /// </summary>
    internal static async Task<string?> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (await OpenSynapseServiceClient.TryStartDriverServiceAsync(
                OpenSynapseServiceProtocol.RazerCommonServiceName,
                cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return TryStartService(out var error)
            ? null
            : error ?? "The Razer common driver service did not start.";
    }

    internal static async Task<RazerDriverInstallResult> InstallAsync(
        IProgress<RazerDriverInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            return new RazerDriverInstallResult(true);
        }

        var (infPath, failure) = await EnsurePackageAsync(
                RazerDriverInstaller.CacheDirectory,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        progress?.Report(new RazerDriverInstallProgress(
            RazerDriverInstallStage.InstallingCommon,
            "RzCommon"));
        var result = RunPnPUtil(infPath!);
        if (result.Success)
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// Runs inside the machine-wide service, which is already elevated, so the
    /// same common-driver install needs no UAC prompt.
    /// </summary>
    internal static async Task<RazerDriverInstallResult> InstallAsServiceAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            return new RazerDriverInstallResult(true);
        }

        try
        {
            RazerDriverInstaller.EnsureServiceCacheDirectory();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RazerDriverInstallResult(
                false, RazerDriverInstallFailure.StartFailed, Error: exception.Message);
        }

        var (infPath, failure) = await EnsurePackageAsync(
                RazerDriverInstaller.ServiceCacheDirectory,
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        var result = RunPnPUtilDirect(infPath!);
        if (result.Success)
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private static async Task<(string? InfPath, RazerDriverInstallResult? Failure)> EnsurePackageAsync(
        string cacheDirectory,
        IProgress<RazerDriverInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(cacheDirectory, "RzCommon");
        Directory.CreateDirectory(folder);
        var cabPath = Path.Combine(folder, "RzCommon.cab");
        var infPath = Path.Combine(folder, "RzCommonU.inf");
        var sysPath = Path.Combine(folder, "RzCommon.sys");
        var catPath = Path.Combine(folder, "rzcommon.cat");

        try
        {
            if (!File.Exists(cabPath) ||
                new FileInfo(cabPath).Length != CabSize ||
                !string.Equals(ComputeSha256(cabPath), CabSha256, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new RazerDriverInstallProgress(
                    RazerDriverInstallStage.DownloadingCommon,
                    "RzCommon"));
                await DownloadCabAsync(cabPath, cancellationToken).ConfigureAwait(false);
            }

            if (!File.Exists(infPath) || !File.Exists(sysPath) || !File.Exists(catPath))
            {
                ExtractCab(cabPath, folder);
            }

            RazerDriverInstaller.VerifyPackage(sysPath);
            return (infPath, null);
        }
        catch (Exception exception) when (
            exception is IOException or HttpRequestException or InvalidOperationException or
                CryptographicException)
        {
            return (null, new RazerDriverInstallResult(
                false,
                exception is InvalidDataException or CryptographicException
                    ? RazerDriverInstallFailure.InvalidPackage
                    : RazerDriverInstallFailure.DownloadFailed));
        }
    }

    private static async Task DownloadCabAsync(string path, CancellationToken cancellationToken)
    {
        var temporary = path + ".download";
        using (var response = await Client.GetAsync(
                   CabUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = new FileStream(
                temporary,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        var hash = ComputeSha256(temporary);
        if (new FileInfo(temporary).Length != CabSize ||
            !string.Equals(hash, CabSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporary);
            throw new InvalidDataException("Razer Common Driver package hash mismatch.");
        }

        File.Move(temporary, path, overwrite: true);
    }
    private static void ExtractCab(string cabPath, string folder)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "expand.exe"),
            Arguments = $"\"{cabPath}\" -F:* \"{folder}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = folder,
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("expand.exe did not start.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException("Razer Common Driver package could not be extracted.");
        }
    }
    private static RazerDriverInstallResult RunPnPUtilDirect(string infPath)
    {
        const char quote = '"';
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            Arguments = string.Concat("/add-driver ", quote, infPath, quote, " /install"),
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.StartFailed);
        }

        if (process is null)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.StartFailed);
        }

        using (process)
        {
            process.WaitForExit();
            return process.ExitCode is 0 or 3010 or 1641
                ? new RazerDriverInstallResult(true)
                : new RazerDriverInstallResult(
                    false,
                    RazerDriverInstallFailure.ExitCode,
                    process.ExitCode);
        }
    }

    private static RazerDriverInstallResult RunPnPUtil(string infPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "pnputil.exe"),
            Arguments = $"/add-driver \"{infPath}\" /install",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("pnputil did not start.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.Cancelled);
        }
        catch (Win32Exception)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.StartFailed);
        }

        using (process)
        {
            process.WaitForExit();
            return process.ExitCode is 0 or 3010 or 1641
                ? new RazerDriverInstallResult(true)
                : new RazerDriverInstallResult(
                    false,
                    RazerDriverInstallFailure.ExitCode,
                    process.ExitCode);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
