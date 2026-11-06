using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenSynapse.Windows.Devices;

internal enum RazerDriverInstallStage
{
    Downloading,
    Installing,
    DownloadingCommon,
    InstallingCommon,
}

internal sealed record RazerDriverInstallProgress(
    RazerDriverInstallStage Stage,
    string PackageName);

internal enum RazerDriverInstallFailure
{
    None,
    Cancelled,
    Timeout,
    ExitCode,
    InvalidPackage,
    DownloadFailed,
    StartFailed,
}

internal sealed record RazerDriverInstallResult(
    bool Success,
    RazerDriverInstallFailure Failure = RazerDriverInstallFailure.None,
    int ExitCode = 0);

/// <summary>
/// Downloads an official Razer per-device driver package, verifies its
/// Authenticode signature and runs the NSIS installer elevated. The package is
/// the same one Synapse uses; no third-party driver is accepted.
/// </summary>
internal static class RazerDriverInstaller
{
    private const uint WtdUiNone = 2;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 0;
    private const uint WtdStateActionClose = 2;

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    internal static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OpenSynapse",
        "DriverCache");

    internal static async Task<RazerDriverInstallResult> InstallAsync(
        RazerDriverPackage package,
        IProgress<RazerDriverInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        string path;
        try
        {
            path = await EnsurePackageAsync(package, progress, cancellationToken)
                .ConfigureAwait(false);
            VerifyPackage(path, package);
        }
        catch (InvalidDataException)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.InvalidPackage);
        }
        catch (HttpRequestException)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.DownloadFailed);
        }
        catch (IOException)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.DownloadFailed);
        }

        progress?.Report(new RazerDriverInstallProgress(
            RazerDriverInstallStage.Installing,
            package.Name));
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = "/S",
            WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = true,
            Verb = "runas",
        };

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Razer driver installer did not start.");
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new RazerDriverInstallResult(false, RazerDriverInstallFailure.Timeout);
            }

            return process.ExitCode == 0
                ? new RazerDriverInstallResult(true)
                : new RazerDriverInstallResult(
                    false,
                    RazerDriverInstallFailure.ExitCode,
                    process.ExitCode);
        }
    }

    internal static async Task<string> EnsurePackageAsync(
        RazerDriverPackage package,
        IProgress<RazerDriverInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(CacheDirectory);
        var fileName = Path.GetFileName(new Uri(package.Url).AbsolutePath);
        var path = Path.Combine(CacheDirectory, fileName);
        if (File.Exists(path) &&
            (package.Size <= 0 || new FileInfo(path).Length == package.Size) &&
            TryVerifyPackage(path, package, out _))
        {
            return path;
        }

        var temporary = path + ".download";
        progress?.Report(new RazerDriverInstallProgress(
            RazerDriverInstallStage.Downloading,
            package.Name));
        using (var response = await Client.GetAsync(
                   package.Url,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            if (package.Size > 0 && response.Content.Headers.ContentLength is long length &&
                length != package.Size)
            {
                throw new InvalidDataException("Razer driver package size does not match the catalog.");
            }

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

        if (package.Size > 0 && new FileInfo(temporary).Length != package.Size)
        {
            File.Delete(temporary);
            throw new InvalidDataException("Razer driver package download is incomplete.");
        }

        File.Move(temporary, path, overwrite: true);
        return path;
    }

    internal static void VerifyPackage(string path, RazerDriverPackage package)
    {
        if (!TryVerifyPackage(path, package, out var error))
        {
            throw new InvalidDataException(error);
        }
    }

    private static bool TryVerifyPackage(
        string path,
        RazerDriverPackage package,
        out string error)
    {
        if (package.Size > 0 && new FileInfo(path).Length != package.Size)
        {
            error = "Razer driver package size does not match the catalog.";
            return false;
        }

        var company = FileVersionInfo.GetVersionInfo(path).CompanyName;
        if (company?.Contains("Razer", StringComparison.OrdinalIgnoreCase) != true)
        {
            error = "Razer driver package publisher is not Razer.";
            return false;
        }

        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var file = new WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
            FilePath = path,
        };
        var data = new WinTrustData
        {
            Size = (uint)Marshal.SizeOf<WinTrustData>(),
            UiChoice = WtdUiNone,
            UnionChoice = WtdChoiceFile,
            StateAction = WtdStateActionVerify,
            File = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>()),
        };

        try
        {
            Marshal.StructureToPtr(file, data.File, false);
            var status = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            if (status != 0)
            {
                error = $"Razer driver package signature verification failed (0x{status:X8}).";
                return false;
            }

            error = string.Empty;
            return true;
        }
        finally
        {
            data.StateAction = WtdStateActionClose;
            WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            Marshal.FreeHGlobal(data.File);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        internal uint Size;
        internal string FilePath;
        internal IntPtr FileHandle;
        internal IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        internal uint Size;
        internal IntPtr PolicyCallbackData;
        internal IntPtr SipClientData;
        internal uint UiChoice;
        internal uint RevocationChecks;
        internal uint UnionChoice;
        internal IntPtr File;
        internal uint StateAction;
        internal IntPtr StateData;
        internal IntPtr UrlReference;
        internal uint ProvFlags;
        internal uint UiContext;
        internal IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr window,
        ref Guid actionId,
        ref WinTrustData data);
}
