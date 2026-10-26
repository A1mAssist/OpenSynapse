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
    private const string ServiceSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;IU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)";

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
        if (!IsMachineInstallPath(executablePath) ||
            string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            if (ServiceExists())
            {
                return true;
            }

            using var identity = WindowsIdentity.GetCurrent();
            var userSid = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(userSid))
            {
                return false;
            }

            var create = RunSc(
                "create",
                DedicatedGpuModeServiceProtocol.ServiceName,
                "binPath=",
                $"\"{executablePath}\" {DedicatedGpuModeServiceProtocol.ServiceArgument} " +
                    $"{DedicatedGpuModeServiceProtocol.ServiceUserArgument} {userSid}",
                "start=",
                "demand",
                "obj=",
                "LocalSystem",
                "DisplayName=",
                "OpenSynapse GPU Mode",
                "error=",
                "normal");
            if (create.ExitCode != 0 && create.ExitCode != ServiceAlreadyExists)
            {
                return false;
            }

            RunSc(
                "description",
                DedicatedGpuModeServiceProtocol.ServiceName,
                "Controls the NVIDIA display adapter for OpenSynapse.");
            RunSc(
                "sdset",
                DedicatedGpuModeServiceProtocol.ServiceName,
                ServiceSddl);
            return true;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
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

    private static bool ServiceExists()
    {
        try
        {
            using var controller = new ServiceController(
                DedicatedGpuModeServiceProtocol.ServiceName);
            controller.Refresh();
            _ = controller.Status;
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
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

    private readonly record struct ScResult(int ExitCode, string Output);
}
