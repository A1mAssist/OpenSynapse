using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace OpenSynapse.Windows.Lifecycle;

/// <summary>
/// Installs the optional machine-wide broker. The service binary is the
/// protected OpenSynapse.App.exe already installed under Program Files; no
/// user-writable executable is ever given LocalSystem startup authority.
/// </summary>
public static class DedicatedGpuModeServiceInstaller
{
    private const int ServiceAlreadyExists = 1073;
    private const int ServiceDoesNotExist = 1060;
    internal const string ServiceSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;IU)";

    public static bool IsMachineInstallPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     })
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                var prefix = Path.TrimEndingDirectorySeparator(root) +
                    Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        return false;
    }

    public static bool EnsureInstalled(string? executablePath)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var userSid = identity.User?.Value;
            return EnsureInstalled(executablePath, userSid,
                new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) || identity.IsSystem,
                DedicatedGpuServiceConfiguration.IsProtectedExecutable,
                DedicatedGpuServiceConfiguration.Read, RunSc, Stop);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool EnsureInstalled(string? executablePath, string? userSid, bool mayChange,
        Func<string, bool> isProtected, Func<DedicatedGpuServiceConfiguration?> read,
        Func<string[], ScResult> run, Func<bool> stop)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(userSid) ||
            !isProtected(executablePath) || !DedicatedGpuServiceConfiguration.IsValidSid(userSid)) return false;
        var command = $"\"{Path.GetFullPath(executablePath)}\" {DedicatedGpuModeServiceProtocol.ServiceArgument} " +
            $"{DedicatedGpuModeServiceProtocol.ServiceUserArgument} {userSid}";
        var current = read();
        if (current?.Matches(command, ServiceSddl) == true) return true;
        if (!mayChange) return false;
        if (current is null)
        {
            var created = run(["create", DedicatedGpuModeServiceProtocol.ServiceName, "binPath=", command,
                "type=", "own", "start=", "demand", "obj=", "LocalSystem",
                "DisplayName=", "OpenSynapse GPU Mode", "error=", "normal"]);
            if (created.ExitCode is not (0 or ServiceAlreadyExists)) return false;
            current = read();
            if (current is null) return false;
        }
        if (!current.MatchesSettings(command))
        {
            if (current.IsRunning && (!stop() || read() is not { IsRunning: false })) return false;
            if (run(["config", DedicatedGpuModeServiceProtocol.ServiceName, "binPath=", command,
                "type=", "own", "start=", "demand", "obj=", "LocalSystem", "error=", "normal"]).ExitCode != 0) return false;
        }
        if (!current.MatchesDacl(ServiceSddl) &&
            run(["sdset", DedicatedGpuModeServiceProtocol.ServiceName, ServiceSddl]).ExitCode != 0) return false;
        if (run(["description", DedicatedGpuModeServiceProtocol.ServiceName,
            "Provides fixed GPU/CPU controls, the OpenSynapse macro agent, and the Chroma broker."]).ExitCode != 0) return false;
        return read()?.Matches(command, ServiceSddl) == true;
    }

    public static bool Stop()
    {
        try
        {
            using var controller = new ServiceController(
                DedicatedGpuModeServiceProtocol.ServiceName);
            controller.Refresh();
            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                return true;
            }

            controller.Stop();
            controller.WaitForStatus(
                ServiceControllerStatus.Stopped,
                TimeSpan.FromSeconds(10));
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or
                System.ServiceProcess.TimeoutException or ObjectDisposedException)
        {
            return false;
        }
    }

    public static bool Remove()
    {
        Stop();
        try
        {
            var result = RunSc(
                "delete",
                DedicatedGpuModeServiceProtocol.ServiceName);
            return result.ExitCode == 0 || result.ExitCode == ServiceDoesNotExist;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private static ScResult RunSc(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("sc.exe could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScResult(process.ExitCode, $"{output}{error}".Trim());
    }

    internal readonly record struct ScResult(int ExitCode, string Output);
}
