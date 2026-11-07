using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using OpenSynapse.Windows.Input;

namespace OpenSynapse.Windows.Lighting;

internal enum ChromaSdkShimResult
{
    Success = 0,
    SdkNotFound = 2,
    ShimMissing = 3,
    Failed = 4,
    Cancelled = 5,
    NotInstalled = 6,
}

internal static class ChromaSdkShimInstaller
{
    private const string FileName = "RzChromaSDK64.dll";
    private const string BackupName = FileName + ".opensynapse-backup";
    private const string SdkRegistryKey = @"SOFTWARE\WOW6432Node\Razer Chroma SDK";

    internal static string ShimSourcePath =>
        Path.Combine(AppContext.BaseDirectory, "ChromaSdkShim", FileName);

    internal static string? SdkBinDirectory
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(SdkRegistryKey);
            var install = key?.GetValue("InstallPath64") as string;
            if (string.IsNullOrWhiteSpace(install))
            {
                install = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Razer Chroma SDK");
            }

            var bin = Path.Combine(install, "bin");
            return Directory.Exists(bin) ? bin : null;
        }
    }

    internal static bool IsInstalled
        => IsInstalledAt(SdkBinDirectory);

    internal static bool IsInstalledAt(string? directory)
    {
        if (directory is null) return false;
        var target = Path.Combine(directory, FileName);
        var backup = Path.Combine(directory, BackupName);
        return File.Exists(target) && File.Exists(backup) &&
            new FileInfo(target).Length != new FileInfo(backup).Length;
    }

    internal static bool CanInstall => File.Exists(ShimSourcePath) && SdkBinDirectory is not null;

    internal static int Install() => InstallTo(SdkBinDirectory, ShimSourcePath);

    internal static int InstallTo(string? directory, string source)
    {
        if (!File.Exists(source)) return (int)ChromaSdkShimResult.ShimMissing;
        if (directory is null) return (int)ChromaSdkShimResult.SdkNotFound;
        var target = Path.Combine(directory, FileName);
        var backup = Path.Combine(directory, BackupName);
        if (!File.Exists(target)) return (int)ChromaSdkShimResult.SdkNotFound;

        var temporary = Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(source, temporary, true);
            File.Replace(temporary, target, File.Exists(backup) ? null : backup, true);
            return (int)ChromaSdkShimResult.Success;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (int)ChromaSdkShimResult.Failed;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    internal static int Restore() => RestoreFrom(SdkBinDirectory);

    internal static int RestoreFrom(string? directory)
    {
        if (directory is null) return (int)ChromaSdkShimResult.SdkNotFound;
        var target = Path.Combine(directory, FileName);
        var backup = Path.Combine(directory, BackupName);
        if (!File.Exists(backup)) return (int)ChromaSdkShimResult.NotInstalled;

        var temporary = Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(backup, temporary, true);
            if (File.Exists(target))
            {
                File.Replace(temporary, target, null, true);
            }
            else
            {
                File.Move(temporary, target);
            }

            File.Delete(backup);
            return (int)ChromaSdkShimResult.Success;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (int)ChromaSdkShimResult.Failed;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    internal static Task<int> InstallAsync(CancellationToken cancellationToken = default) =>
        ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.Current())
            ? Task.FromResult(Install())
            : RunElevatedAsync("--install-chroma-shim", cancellationToken);

    internal static Task<int> RestoreAsync(CancellationToken cancellationToken = default) =>
        ProcessIntegrityLevel.IsElevated(ProcessIntegrityLevel.Current())
            ? Task.FromResult(Restore())
            : RunElevatedAsync("--restore-chroma-shim", cancellationToken);

    private static async Task<int> RunElevatedAsync(
        string argument,
        CancellationToken cancellationToken)
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return (int)ChromaSdkShimResult.Failed;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = argument,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return (int)ChromaSdkShimResult.Failed;
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return (int)ChromaSdkShimResult.Cancelled;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return (int)ChromaSdkShimResult.Failed;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
