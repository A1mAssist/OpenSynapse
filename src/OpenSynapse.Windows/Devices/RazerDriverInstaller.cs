using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSynapse.Core.Storage;
using OpenSynapse.Windows.Lifecycle;

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
    string PackageName,
    long BytesReceived = 0,
    long TotalBytes = 0,
    double BytesPerSecond = 0)
{
    public TimeSpan? Eta => TotalBytes > 0 && BytesPerSecond > 0
        ? TimeSpan.FromSeconds(Math.Max(0d, (TotalBytes - BytesReceived) / BytesPerSecond))
        : null;
}

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
    int ExitCode = 0,
    bool RequiresRestart = false,
    string? Error = null);

/// <summary>
/// Downloads an official Razer per-device driver package, verifies its
/// Authenticode signature and runs the NSIS installer through the machine
/// service or the elevated app fallback. The package is the same one Synapse
/// uses; no third-party driver is accepted.
/// </summary>
internal static partial class RazerDriverInstaller
{
    private const uint WtdUiNone = 2;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 0;
    private const uint WtdStateActionClose = 2;

    internal static string CacheDirectory => AppDataPaths.Combine("DriverCache");

    internal static string ServiceCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OpenSynapse",
        "Service",
        "DriverCache");

    internal static async Task<RazerDriverInstallResult> InstallAsync(
        ushort productId,
        RazerDriverPackage package,
        IProgress<RazerDriverInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        string path;
        try
        {
            path = await EnsurePackageAsync(package, CacheDirectory, progress, cancellationToken)
                .ConfigureAwait(false);
            VerifyPackage(path);
        }
        catch (InvalidDataException exception)
        {
            return new RazerDriverInstallResult(
                false, RazerDriverInstallFailure.InvalidPackage, Error: exception.Message);
        }
        catch (HttpRequestException)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.DownloadFailed);
        }
        catch (IOException)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.DownloadFailed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RazerDriverInstallResult(false, RazerDriverInstallFailure.Timeout);
        }

        return await InstallVerifiedAsync(
            productId, path, package.Name, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Installs a package the caller already downloaded. The app downloads with
    /// progress and hands the verified path to the elevated service or installer.
    /// </summary>
    internal static async Task<RazerDriverInstallResult> InstallAsync(
        ushort productId,
        string packagePath,
        IProgress<RazerDriverInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(packagePath);
        try
        {
            VerifyPackage(path);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new RazerDriverInstallResult(
                false, RazerDriverInstallFailure.InvalidPackage, Error: exception.Message);
        }

        return await InstallVerifiedAsync(
            productId, path, Path.GetFileName(path), progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<RazerDriverInstallResult> InstallVerifiedAsync(
        ushort productId,
        string path,
        string packageName,
        IProgress<RazerDriverInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new RazerDriverInstallProgress(
            RazerDriverInstallStage.Installing,
            packageName));
        var result = await RunInstallerAsync(path, elevated: true, cancellationToken)
            .ConfigureAwait(false);
        return result.Success
            ? await CompleteInstallAsync(
                    productId, path, result, elevated: true, cancellationToken)
                .ConfigureAwait(false)
            : result;
    }

    /// <summary>
    /// Runs inside the machine-wide service, which is already elevated, so the
    /// vendor package is executed without a UAC prompt.
    /// </summary>
    internal static async Task<RazerDriverInstallResult> InstallAsServiceAsync(
        ushort productId,
        string? packagePath = null,
        CancellationToken cancellationToken = default)
    {
        string path;
        try
        {
            if (!string.IsNullOrWhiteSpace(packagePath))
            {
                path = Path.GetFullPath(packagePath);
                VerifyPackage(path);
            }
            else
            {
                if (await RazerDriverPackageCatalog
                        .ResolveAsync(productId, cancellationToken)
                        .ConfigureAwait(false) is not { } package)
                {
                    return new RazerDriverInstallResult(
                        false,
                        RazerDriverInstallFailure.InvalidPackage,
                        Error: $"No Razer driver package is catalogued for PID {productId:X4}.");
                }

                EnsureServiceCacheDirectory();
                path = await EnsurePackageAsync(
                        package,
                        ServiceCacheDirectory,
                        progress: null,
                        cancellationToken)
                    .ConfigureAwait(false);
                VerifyPackage(path);
            }
        }
        catch (InvalidDataException exception)
        {
            return new RazerDriverInstallResult(
                false, RazerDriverInstallFailure.InvalidPackage, Error: exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return new RazerDriverInstallResult(
                false, RazerDriverInstallFailure.DownloadFailed, Error: exception.Message);
        }
        catch (IOException exception)
        {
            return new RazerDriverInstallResult(
                false, RazerDriverInstallFailure.DownloadFailed, Error: exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new RazerDriverInstallResult(
                false, RazerDriverInstallFailure.StartFailed, Error: exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RazerDriverInstallResult(
                false,
                RazerDriverInstallFailure.Timeout,
                Error: "The Razer driver package download timed out.");
        }

        var result = await RunInstallerAsync(path, elevated: false, cancellationToken)
            .ConfigureAwait(false);
        return result.Success
            ? await CompleteInstallAsync(
                    productId, path, result, elevated: false, cancellationToken)
                .ConfigureAwait(false)
            : result;
    }

    internal static async Task<string> EnsurePackageAsync(
        RazerDriverPackage package,
        IProgress<RazerDriverInstallProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        await EnsurePackageAsync(package, CacheDirectory, progress, cancellationToken)
            .ConfigureAwait(false);

    private static async Task<string> EnsurePackageAsync(
        RazerDriverPackage package,
        string cacheDirectory,
        IProgress<RazerDriverInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDirectory);
        if (StringComparer.OrdinalIgnoreCase.Equals(cacheDirectory, CacheDirectory))
        {
            DeleteLegacyCacheDirectory();
        }

        var fileName = Path.GetFileName(new Uri(package.Url).AbsolutePath);
        var path = Path.Combine(cacheDirectory, fileName);
        if (File.Exists(path) && TryVerifyPackage(path, requireRazerPublisher: true, out _))
        {
            return path;
        }

        var temporary = path + ".download";
        progress?.Report(new RazerDriverInstallProgress(
            RazerDriverInstallStage.Downloading,
            package.Name));
        await RazerDriverDownloader.DownloadAsync(
            package.Url,
            temporary,
            progress is null ? null : new DownloadProgressAdapter(progress, package.Name),
            cancellationToken).ConfigureAwait(false);

        File.Move(temporary, path, overwrite: true);
        return path;
    }

    private sealed class DownloadProgressAdapter(
        IProgress<RazerDriverInstallProgress> target,
        string packageName) : IProgress<RazerDriverDownloadProgress>
    {
        public void Report(RazerDriverDownloadProgress value) =>
            target.Report(new RazerDriverInstallProgress(
                RazerDriverInstallStage.Downloading,
                packageName,
                value.BytesReceived,
                value.TotalBytes,
                value.BytesPerSecond));
    }

    /// <summary>
    /// Razer's silent installer can return 0 without staging its filter INFs.
    /// When that happens, unpack the verified package and bind the INFs from
    /// the package itself instead of reporting an installed driver that does
    /// not exist.
    /// </summary>
    private static async Task<RazerDriverInstallResult> CompleteInstallAsync(
        ushort productId,
        string packagePath,
        RazerDriverInstallResult result,
        bool elevated,
        CancellationToken cancellationToken)
    {
        if (RazerDeviceRegistry.BuiltIn.Find(0x1532, productId)?.Category ==
            OpenSynapse.Core.Devices.DeviceCategory.Laptop)
        {
            return await CompleteLowerFilterAsync(
                    productId, packagePath, result, elevated, cancellationToken)
                .ConfigureAwait(false);
        }

        if (RazerFilterInfBinder.FindStagedInfs(productId).Count > 0)
        {
            return result;
        }

        var extractRoot = Path.Combine(
            Path.GetDirectoryName(packagePath)!,
            "extracted",
            Path.GetFileNameWithoutExtension(packagePath));
        var extracted = await ExtractDriverPackageAsync(
                packagePath, extractRoot, cancellationToken)
            .ConfigureAwait(false);
        if (extracted is null)
        {
            return result with
            {
                Success = false,
                Error = "The Razer filter INF could not be unpacked from the driver package.",
            };
        }

        var bind = elevated
            ? await RazerFilterInfBinder.ApplyAsync(
                    productId, cancellationToken, extracted)
                .ConfigureAwait(false)
            : await RazerFilterInfBinder.ApplyDirectAsync(
                    productId, cancellationToken, extracted)
                .ConfigureAwait(false);
        if (!bind.Applied)
        {
            return result with
            {
                Success = false,
                Error = bind.Error ?? "The Razer filter INF could not be bound.",
            };
        }

        return result with { RequiresRestart = true };
    }

    /// <summary>
    /// The vendor installer stages only the HID filters, so a machine can end up
    /// with a working RZCONTROL endpoint and a dead Fn row. The lower filter is
    /// completed here from the same verified package.
    /// </summary>
    private static async Task<RazerDriverInstallResult> CompleteLowerFilterAsync(
        ushort productId,
        string packagePath,
        RazerDriverInstallResult result,
        bool elevated,
        CancellationToken cancellationToken)
    {
        // The DKM lower filter only carries Blade Fn input. A mouse never gets a
        // LowerFilters entry, so running this step for one would always fail the
        // whole install.
        if (RazerDeviceRegistry.BuiltIn.Find(0x1532, productId)?.Category !=
                OpenSynapse.Core.Devices.DeviceCategory.Laptop ||
            RazerFilterInfBinder.IsDkmAttached(productId))
        {
            return result;
        }

        var extractRoot = Path.Combine(
            Path.GetDirectoryName(packagePath)!,
            "extracted",
            productId.ToString("X4"));
        var extracted = await ExtractLowerFiltersAsync(packagePath, extractRoot, cancellationToken)
            .ConfigureAwait(false);
        if (extracted is null)
        {
            return result with
            {
                Success = false,
                Error = "The DKM lower filter could not be unpacked from the driver package.",
            };
        }

        var bind = elevated
            ? await RazerFilterInfBinder.ApplyBladeDkmElevatedAsync(
                    productId, extracted, cancellationToken)
                .ConfigureAwait(false)
            : await RazerFilterInfBinder.ApplyBladeDkmAsync(
                    productId, extracted, cancellationToken)
                .ConfigureAwait(false);

        return bind.Applied
            ? result with { RequiresRestart = true }
            : result with
            {
                Success = false,
                Error = bind.Error ?? result.Error,
            };
    }

    internal static bool IsSuccessfulExitCode(int exitCode) => exitCode is 0 or 3010 or 1641;

    /// <summary>
    /// DPInst answers 259 (ERROR_NO_MORE_ITEMS) when every matching device already
    /// has the driver. Nothing was installed, but the device stack is the state the
    /// caller wants, so it must verify the attachment instead of reporting a failure.
    /// </summary>
    internal static bool IsAlreadyInstalledExitCode(int exitCode) => exitCode == 259;

    internal static void EnsureServiceCacheDirectory() =>
        ProgramDataSecurity.ProtectDirectory(ServiceCacheDirectory, allowUsersRead: false);

    /// <summary>
    /// The user cache used to live under the Velopack per-user install root,
    /// which an upgrade or uninstall may delete. Remove that leftover once;
    /// only the cache subfolder is touched, never the surrounding install.
    /// </summary>
    private static void DeleteLegacyCacheDirectory()
    {
        try
        {
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenSynapse",
                "DriverCache");
            if (Directory.Exists(legacy))
            {
                Directory.Delete(legacy, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.Security.SecurityException)
        {
        }
    }

    private static async Task<RazerDriverInstallResult> RunInstallerAsync(
        string path,
        bool elevated,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            Arguments = "/S",
            WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = elevated,
            Verb = elevated ? "runas" : string.Empty,
            CreateNoWindow = !elevated,
            WindowStyle = ProcessWindowStyle.Hidden,
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
        catch (Win32Exception exception)
        {
            return new RazerDriverInstallResult(
                false, RazerDriverInstallFailure.StartFailed, Error: exception.Message);
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

            return IsSuccessfulExitCode(process.ExitCode)
                ? new RazerDriverInstallResult(
                    true,
                    RazerDriverInstallFailure.None,
                    process.ExitCode,
                    RequiresRestart: process.ExitCode is 3010 or 1641)
                : new RazerDriverInstallResult(
                    false,
                    RazerDriverInstallFailure.ExitCode,
                    process.ExitCode);
        }
    }

    internal static void VerifyPackage(string path, bool requireRazerPublisher = true)
    {
        if (!TryVerifyPackage(path, requireRazerPublisher, out var error))
        {
            throw new InvalidDataException(error);
        }
    }

    private static bool TryVerifyPackage(
        string path, bool requireRazerPublisher, out string error)
    {
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

            if (requireRazerPublisher)
            {
                string subject;
                try
                {
                    // The certificate is embedded in an executable, not an X.509 certificate file.
#pragma warning disable SYSLIB0057
                    using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
                    subject = signer.SubjectName.Format(multiLine: true);
                }
                catch (CryptographicException exception)
                {
                    // WinVerifyTrust only vouches for the signature it found; the embedded
                    // signer can still be unreadable on a republished package.
                    error = $"Razer driver package has no readable signer certificate: {exception.Message}";
                    return false;
                }

                if (!IsRazerPublisher(subject))
                {
                    error = "Razer driver package has an unexpected publisher.";
                    return false;
                }
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

    internal static bool IsRazerPublisher(string formattedSubject) =>
        formattedSubject.Split('\n').Any(line =>
            string.Equals(line.TrimEnd('\r'), "CN=Razer USA Ltd.", StringComparison.Ordinal));

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
