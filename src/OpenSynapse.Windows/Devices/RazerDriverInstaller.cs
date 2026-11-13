using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

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
    int ExitCode = 0,
    bool RequiresRestart = false,
    string? Error = null);

/// <summary>
/// Downloads an official Razer per-device driver package, verifies its
/// Authenticode signature and runs the NSIS installer through the machine
/// service or the elevated app fallback. The package is the same one Synapse
/// uses; no third-party driver is accepted.
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
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSynapse",
        "DriverCache");

    internal static string ServiceCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OpenSynapse",
        "Service",
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

        progress?.Report(new RazerDriverInstallProgress(
            RazerDriverInstallStage.Installing,
            package.Name));
        var result = await RunInstallerAsync(path, elevated: true, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success ||
            !ushort.TryParse(
                package.ProductId,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out var productId))
        {
            return result;
        }

        return await CompleteLowerFilterAsync(
                productId, path, result, elevated: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs inside the machine-wide service, which is already elevated, so the
    /// vendor package is executed without a UAC prompt.
    /// </summary>
    internal static async Task<RazerDriverInstallResult> InstallAsServiceAsync(
        ushort productId,
        CancellationToken cancellationToken = default)
    {
        if (!RazerDriverPackageCatalog.TryResolve(productId, out var package))
        {
            return new RazerDriverInstallResult(
                false,
                RazerDriverInstallFailure.InvalidPackage,
                Error: $"No Razer driver package is catalogued for PID {productId:X4}.");
        }

        string path;
        try
        {
            EnsureServiceCacheDirectory();
            path = await EnsurePackageAsync(
                    package,
                    ServiceCacheDirectory,
                    progress: null,
                    cancellationToken)
                .ConfigureAwait(false);
            VerifyPackage(path);
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
            ? await CompleteLowerFilterAsync(
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
        using (var response = await Client.GetAsync(
                   package.Url,
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

        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// The vendor package is an NSIS self-extractor: it ignores /D and deletes
    /// its staging directory, so the lower-filter INFs it never installs can
    /// only be read by unpacking the executable. 7za exits 1 for its benign
    /// "data after the end of archive" warning.
    /// </summary>
    internal static async Task<string?> ExtractLowerFiltersAsync(
        string packagePath,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var extractor = Path.Combine(AppContext.BaseDirectory, "7za.exe");
        if (!File.Exists(extractor))
        {
            return null;
        }

        Directory.CreateDirectory(outputDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = extractor,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("x");
        startInfo.ArgumentList.Add("-tNsis");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add($"-o{outputDirectory}");
        startInfo.ArgumentList.Add(@"Drivers\x64\Lower\*");
        startInfo.ArgumentList.Add(@"Drivers\x64\VCon\*");
        startInfo.ArgumentList.Add("-y");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode is 0 or 1 ? outputDirectory : null;
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
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
        if (RazerFilterInfBinder.IsDkmAttached(productId))
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

    internal static void EnsureServiceCacheDirectory()
    {
        Directory.CreateDirectory(ServiceCacheDirectory);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inheritance =
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(ServiceCacheDirectory).SetAccessControl(security);
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
