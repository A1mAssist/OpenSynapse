using System.Diagnostics;
using System.Globalization;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Starts Velopack's updater through UAC. A machine-wide MSI lives under
/// Program Files, so the updater must run elevated before it can replace the
/// current application directory.
/// </summary>
internal static class MachineWideUpdateLauncher
{
    internal static Process Start(
        string updateExePath,
        string packagePath,
        string rootDirectory,
        string packagesDirectory,
        int waitPid)
    {
        var startInfo = CreateStartInfo(
            updateExePath,
            packagePath,
            rootDirectory,
            packagesDirectory,
            waitPid);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The elevated OpenSynapse updater did not start.");
    }

    internal static ProcessStartInfo CreateStartInfo(
        string updateExePath,
        string packagePath,
        string rootDirectory,
        string packagesDirectory,
        int waitPid)
    {
        if (waitPid <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(waitPid));
        }

        var updateExe = FullPath(updateExePath, nameof(updateExePath));
        var package = FullPath(packagePath, nameof(packagePath));
        var root = FullPath(rootDirectory, nameof(rootDirectory));
        var packages = FullPath(packagesDirectory, nameof(packagesDirectory));
        if (!File.Exists(updateExe))
        {
            throw new FileNotFoundException("The installed Velopack updater was not found.", updateExe);
        }

        if (!File.Exists(package))
        {
            throw new FileNotFoundException("The downloaded update package was not found.", package);
        }

        var relativePackage = Path.GetRelativePath(packages, package);
        if (Path.IsPathRooted(relativePackage) ||
            relativePackage == ".." ||
            relativePackage.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The update package is outside the Velopack package directory.");
        }

        return new ProcessStartInfo(updateExe)
        {
            Arguments = BuildArguments(package, root, packages, waitPid),
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = root,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
    }

    internal static string BuildArguments(
        string packagePath,
        string rootDirectory,
        string packagesDirectory,
        int waitPid) =>
        string.Join(' ',
            "apply",
            "--package", Quote(packagePath),
            "--waitPid", waitPid.ToString(CultureInfo.InvariantCulture),
            "--rootDir", Quote(rootDirectory),
            "--packageDir", Quote(packagesDirectory));

    private static string FullPath(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('"'))
        {
            throw new ArgumentException("A valid path is required.", parameterName);
        }

        return Path.GetFullPath(value);
    }

    private static string Quote(string value) => '"' + value + '"';
}
