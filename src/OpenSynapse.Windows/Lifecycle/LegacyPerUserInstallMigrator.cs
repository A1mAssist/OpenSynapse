using System.ComponentModel;
using System.Diagnostics;
using OpenSynapse.Core.Storage;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Removes a legacy per-user Velopack install after the machine-wide MSI lands.
/// User data is copied to its new, Velopack-independent location before the old
/// uninstaller runs because that uninstaller deletes %LOCALAPPDATA%\OpenSynapse.
/// </summary>
public static class LegacyPerUserInstallMigrator
{
    private const string LegacyFolderName = "OpenSynapse";
    private const string LegacyUninstallerName = "Update.exe";
    private const string LegacyCurrentDirectoryName = "current";
    private const string LegacyExecutableName = "OpenSynapse.App.exe";
    private const int UninstallTimeoutMilliseconds = 30_000;
    private const int ProcessStopTimeoutMilliseconds = 5_000;

    private static readonly HashSet<string> LegacyRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        LegacyUninstallerName,
        LegacyExecutableName,
        "OpenSynapse.exe",
        ".msi-installed",
        ".portable",
        "sq.version",
        "RELEASES",
        "assets.win.json",
        "releases.win.json",
    };

    private static readonly HashSet<string> LegacyRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        LegacyCurrentDirectoryName,
        "packages",
    };

    /// <summary>
    /// Called after a machine-wide install. Returns true only when a legacy
    /// per-user install was found and removed.
    /// </summary>
    public static bool TryMigrate(string? machineExecutablePath)
    {
        if (!IsMachineInstall(machineExecutablePath, out var fullPath))
        {
            return false;
        }

        _ = CopyTransitionalUserData();
        var legacyRoot = GetLegacyRoot();
        var uninstaller = Path.Combine(legacyRoot, LegacyUninstallerName);
        var legacyExecutable = Path.Combine(
            legacyRoot,
            LegacyCurrentDirectoryName,
            LegacyExecutableName);
        if (!File.Exists(uninstaller) || !File.Exists(legacyExecutable))
        {
            _ = CleanupOrphanedInstallerArtifacts(legacyRoot);
            return false;
        }

        return Migrate(
            legacyRoot,
            fullPath,
            AppDataPaths.Root,
            stopLegacyProcesses: StopLegacyProcesses,
            uninstallLegacy: UninstallLegacy,
            repairStartup: WindowsStartupManager.TryRepairStaleRegistration);
    }

    /// <summary>
    /// Safety net for a machine-wide app launch after an interrupted migration.
    /// Copies missing user data without invoking the legacy uninstaller, then
    /// drops the installer leftovers of a legacy install that is already gone.
    /// </summary>
    public static bool CopyLegacyUserData(string? machineExecutablePath)
    {
        if (!IsMachineInstall(machineExecutablePath, out _))
        {
            return false;
        }

        var copied = CopyTransitionalUserData();
        var legacyRoot = GetLegacyRoot();
        if (Directory.Exists(legacyRoot))
        {
            copied |= CopyUserData(legacyRoot, AppDataPaths.Root);
            copied |= CleanupOrphanedInstallerArtifacts(legacyRoot);
        }

        return copied;
    }

    /// <summary>
    /// Removes Update.exe and packages\ once the legacy application itself is
    /// gone. While the legacy app still runs, Update.exe stays because it is
    /// the only uninstaller the user has for that install.
    /// </summary>
    internal static bool CleanupOrphanedInstallerArtifacts(string legacyRoot)
    {
        if (File.Exists(Path.Combine(
                legacyRoot,
                LegacyCurrentDirectoryName,
                LegacyExecutableName)))
        {
            return false;
        }

        return CleanupStaleArtifacts(legacyRoot);
    }

    /// <summary>
    /// Removes the transitional data root once every file in it also exists in
    /// the new data root. A file that is still missing is kept so the next
    /// launch can copy it instead of losing it with the directory.
    /// </summary>
    public static bool CleanupTransitionalRootIfMigrated() =>
        CleanupTransitionalRoot(AppDataPaths.TransitionalRoot, AppDataPaths.Root);

    internal static bool CleanupTransitionalRoot(string transitionalRoot, string destinationRoot)
    {
        if (!Directory.Exists(transitionalRoot))
        {
            return false;
        }

        var removed = false;
        foreach (var file in Directory.EnumerateFiles(
                     transitionalRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            var destination = Path.Combine(
                destinationRoot,
                Path.GetRelativePath(transitionalRoot, file));
            if (!File.Exists(destination))
            {
                continue;
            }

            removed |= TryDeleteFile(file);
        }

        if (!Directory.Exists(transitionalRoot))
        {
            return removed;
        }

        // Only drop the directory when it holds no data that still needs to move.
        foreach (var file in Directory.EnumerateFiles(
                     transitionalRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (File.Exists(file))
            {
                return removed;
            }
        }

        TryDeleteDirectory(transitionalRoot);
        return removed || !Directory.Exists(transitionalRoot);
    }

    internal static bool Migrate(
        string legacyRoot,
        string machineExecutablePath,
        string destinationDataRoot,
        Action<string> stopLegacyProcesses,
        Action<string> uninstallLegacy,
        Func<string, bool> repairStartup)
    {
        try
        {
            stopLegacyProcesses(legacyRoot);
            _ = CopyUserData(legacyRoot, destinationDataRoot);
            uninstallLegacy(legacyRoot);
            if (File.Exists(Path.Combine(
                    legacyRoot,
                    LegacyCurrentDirectoryName,
                    LegacyExecutableName)))
            {
                return false;
            }

            _ = repairStartup(machineExecutablePath);
            CleanupStaleArtifacts(legacyRoot);
            return true;
        }
        catch (Exception exception) when (IsExpectedMigrationException(exception))
        {
            return false;
        }
    }

    private static bool IsMachineInstall(string? executablePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !DedicatedGpuModeServiceInstaller.IsMachineInstallPath(executablePath))
        {
            return false;
        }

        fullPath = Path.GetFullPath(executablePath);
        return true;
    }

    internal static string GetLegacyRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyFolderName);

    /// <summary>
    /// True when a path points into the legacy per-user install. The check
    /// respects the directory boundary so that a sibling folder with a
    /// similar name is not treated as legacy.
    /// </summary>
    internal static bool IsUnderLegacyRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var prefix = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(GetLegacyRoot())) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool CopyTransitionalUserData()
    {
        if (!Directory.Exists(AppDataPaths.TransitionalRoot))
        {
            return false;
        }

        return CopyUserData(AppDataPaths.TransitionalRoot, AppDataPaths.Root);
    }

    private static void StopLegacyProcesses(string legacyRoot)
    {
        var processes = new List<Process>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (IsUnderDirectory(path, legacyRoot))
                    {
                        processes.Add(process);
                        continue;
                    }
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or
                        NotSupportedException)
                {
                }

                process.Dispose();
            }

            foreach (var process in processes)
            {
                try
                {
                    _ = process.CloseMainWindow();
                }
                catch (InvalidOperationException)
                {
                }
            }

            WaitForExit(processes, ProcessStopTimeoutMilliseconds);
            foreach (var process in processes)
            {
                if (HasExited(process))
                {
                    continue;
                }

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception or
                        NotSupportedException)
                {
                }
            }

            WaitForExit(processes, ProcessStopTimeoutMilliseconds);
            if (processes.Any(process => !HasExited(process)))
            {
                throw new InvalidOperationException(
                    "The legacy OpenSynapse process could not be stopped.");
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void UninstallLegacy(string legacyRoot)
    {
        var uninstaller = Path.Combine(legacyRoot, LegacyUninstallerName);
        var legacyExecutable = Path.Combine(
            legacyRoot,
            LegacyCurrentDirectoryName,
            LegacyExecutableName);
        var startInfo = new ProcessStartInfo(uninstaller)
        {
            WorkingDirectory = legacyRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--uninstall");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The legacy uninstaller did not start.");
        if (!process.WaitForExit(UninstallTimeoutMilliseconds))
        {
            var removed = !File.Exists(legacyExecutable);
            TryKill(process);
            if (!removed)
            {
                throw new InvalidOperationException(
                    "The legacy uninstaller did not finish.");
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (File.Exists(legacyExecutable) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
        }

        if (File.Exists(legacyExecutable))
        {
            throw new InvalidOperationException(
                "The legacy OpenSynapse executable is still installed.");
        }
    }

    private static bool CopyUserData(string legacyRoot, string destinationDataRoot)
    {
        Directory.CreateDirectory(destinationDataRoot);
        foreach (var file in Directory.EnumerateFiles(legacyRoot))
        {
            if (ShouldSkipRootFile(file))
            {
                continue;
            }

            var destination = Path.Combine(destinationDataRoot, Path.GetFileName(file));
            if (!File.Exists(destination))
            {
                File.Copy(file, destination);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(legacyRoot))
        {
            var info = new DirectoryInfo(directory);
            if (LegacyRootDirectories.Contains(info.Name) ||
                info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destinationDataRoot, info.Name));
        }

        return true;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (IsTransientUserFile(file))
            {
                continue;
            }

            var destinationFile = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(destinationFile))
            {
                File.Copy(file, destinationFile);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var info = new DirectoryInfo(directory);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, info.Name));
        }
    }

    private static bool ShouldSkipRootFile(string file)
    {
        var name = Path.GetFileName(file);
        if (LegacyRootFiles.Contains(name))
        {
            return true;
        }

        var extension = Path.GetExtension(name);
        return IsTransientUserFile(file) ||
            extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientUserFile(string file) =>
        Path.GetExtension(file).Equals(".lock", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the installer leftovers of a legacy per-user install. Only
    /// installer artifacts are touched; user data such as profiles.json,
    /// behavior.json, language.txt and logs\ stays where it is.
    /// </summary>
    internal static bool CleanupStaleArtifacts(string legacyRoot)
    {
        if (!Directory.Exists(legacyRoot))
        {
            return false;
        }

        var removed = false;
        foreach (var directory in LegacyRootDirectories)
        {
            removed |= TryDeleteDirectory(Path.Combine(legacyRoot, directory));
        }

        foreach (var file in Directory.EnumerateFiles(legacyRoot))
        {
            var name = Path.GetFileName(file);
            var extension = Path.GetExtension(name);
            if (LegacyRootFiles.Contains(name) ||
                extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                removed |= TryDeleteFile(file);
            }
        }

        return removed;
    }

    private static bool IsUnderDirectory(string? path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) +
                Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void WaitForExit(IEnumerable<Process> processes, int timeoutMilliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline &&
               processes.Any(process => !HasExited(process)))
        {
            Thread.Sleep(100);
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _ = process.WaitForExit(ProcessStopTimeoutMilliseconds);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or
                NotSupportedException)
        {
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception exception) when (IsExpectedMigrationException(exception))
        {
            return false;
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (IsExpectedMigrationException(exception))
        {
            return false;
        }
    }

    private static bool IsExpectedMigrationException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or Win32Exception or
            InvalidOperationException or System.Security.SecurityException;
}