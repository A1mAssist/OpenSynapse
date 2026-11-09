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
public static class OpenSynapseServiceInstaller
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
                OpenSynapseServiceConfiguration.IsProtectedExecutable,
                OpenSynapseServiceConfiguration.Read, RunSc, Stop);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or
                System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool EnsureInstalled(string? executablePath, string? userSid, bool mayChange,
        Func<string, bool> isProtected, Func<OpenSynapseServiceConfiguration?> read,
        Func<string[], ScResult> run, Func<bool> stop)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(userSid) ||
            !isProtected(executablePath) || !OpenSynapseServiceConfiguration.IsValidSid(userSid)) return false;
        var command = $"\"{Path.GetFullPath(executablePath)}\" {OpenSynapseServiceProtocol.ServiceArgument} " +
            $"{OpenSynapseServiceProtocol.ServiceUserArgument} {userSid}";
        var current = read();
        if (current?.Matches(command, ServiceSddl) == true)
        {
            if (mayChange) RemoveLegacy(run);
            return true;
        }
        if (!mayChange) return false;
        if (current is null)
        {
            var created = run(["create", OpenSynapseServiceProtocol.ServiceName, "binPath=", command,
                "type=", "own", "start=", "demand", "obj=", "LocalSystem",
                "DisplayName=", "OpenSynapse Service", "error=", "normal"]);
            if (created.ExitCode is not (0 or ServiceAlreadyExists)) return false;
            current = read();
            if (current is null) return false;
        }
        if (!current.MatchesSettings(command))
        {
            if (current.IsRunning && (!stop() || read() is not { IsRunning: false })) return false;
            if (run(["config", OpenSynapseServiceProtocol.ServiceName, "binPath=", command,
                "type=", "own", "start=", "demand", "obj=", "LocalSystem", "error=", "normal"]).ExitCode != 0) return false;
        }
        if (!current.MatchesDacl(ServiceSddl) &&
            run(["sdset", OpenSynapseServiceProtocol.ServiceName, ServiceSddl]).ExitCode != 0) return false;
        if (run(["description", OpenSynapseServiceProtocol.ServiceName,
            "Provides OpenSynapse privileged features: GPU and CPU controls, the macro agent, the Chroma broker, and update execution."]).ExitCode != 0) return false;
        if (read()?.Matches(command, ServiceSddl) != true) return false;
        RemoveLegacy(run);
        return true;
    }

    public static bool Stop() =>
        StopService(OpenSynapseServiceProtocol.ServiceName) &
        StopService(OpenSynapseServiceProtocol.LegacyServiceName);

    private static bool StopService(string name)
    {
        ServiceController controller;
        try
        {
            controller = new ServiceController(name);
            controller.Refresh();
        }
        catch (InvalidOperationException)
        {
            // The service is not installed; this name has nothing to stop.
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or ObjectDisposedException)
        {
            return false;
        }

        using (controller)
        {
            try
            {
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
    }

    public static bool Remove()
    {
        Stop();
        try
        {
            var result = RunSc(
                "delete",
                OpenSynapseServiceProtocol.ServiceName);
            var legacy = RunSc(
                "delete",
                OpenSynapseServiceProtocol.LegacyServiceName);
            return IsRemoved(result) && IsRemoved(legacy);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private static void RemoveLegacy(Func<string[], ScResult> run)
    {
        // Delete the pre-rename service only after the new one is in place. Both
        // exit codes are ignored: 1060/1062 mean it was never installed or already stopped.
        _ = run(["stop", OpenSynapseServiceProtocol.LegacyServiceName]);
        _ = run(["delete", OpenSynapseServiceProtocol.LegacyServiceName]);
    }

    private static bool IsRemoved(ScResult result) =>
        result.ExitCode is 0 or ServiceDoesNotExist;

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
