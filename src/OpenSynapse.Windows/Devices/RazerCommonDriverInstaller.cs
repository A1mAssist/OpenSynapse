using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

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

    internal static bool IsInstalled =>
        Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\RzCommon") is not null;

    internal static async Task<RazerDriverInstallResult> InstallAsync(
        IProgress<RazerDriverInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
        {
            return new RazerDriverInstallResult(true);
        }

        var folder = Path.Combine(RazerDriverInstaller.CacheDirectory, "RzCommon");
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

            RazerDriverInstaller.VerifyPackage(
                sysPath,
                new RazerDriverPackage(
                    "RZCOMMON",
                    "Razer Common Driver",
                    "10.0.15063.0",
                    0,
                    CabUrl));
        }
        catch (Exception exception) when (
            exception is IOException or HttpRequestException or InvalidOperationException or
                CryptographicException)
        {
            return new RazerDriverInstallResult(
                false,
                exception is InvalidDataException or CryptographicException
                    ? RazerDriverInstallFailure.InvalidPackage
                    : RazerDriverInstallFailure.DownloadFailed);
        }

        progress?.Report(new RazerDriverInstallProgress(
            RazerDriverInstallStage.InstallingCommon,
            "RzCommon"));
        return RunPnPUtil(infPath);
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
