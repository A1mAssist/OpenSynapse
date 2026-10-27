using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace OpenSynapse.Windows.Input;

/// <summary>
/// Installs the scheduled task that starts the elevated macro agent.
///
/// The task runs with the interactive user's token at the highest available
/// integrity level, so the agent lives in the user session and its SendInput
/// calls reach elevated windows. Creating a highest-run-level task needs
/// administrator rights, so this runs once from the install hook, never from
/// the app's normal startup.
/// </summary>
public static class ElevatedMacroTaskInstaller
{
    private const int TaskDoesNotExist = 1;

    /// <summary>
    /// Creates or replaces the agent task for the given user. Returns false
    /// when the task could not be created, in which case macros keep running
    /// through the app's own SendInput path.
    /// </summary>
    public static bool EnsureInstalled(string executablePath, string? userSid = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var sid = userSid ?? CurrentUserSid();
        if (string.IsNullOrWhiteSpace(sid))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        var xml = BuildTaskXml(fullPath, sid);
        var xmlPath = Path.Combine(
            Path.GetTempPath(),
            $"OpenSynapseMacroAgent-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
            var result = RunSchTasks(
                "/create",
                "/tn",
                ElevatedMacroProtocol.TaskName,
                "/xml",
                xmlPath,
                "/f");
            return result.ExitCode == 0;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static bool Remove()
    {
        var result = RunSchTasks(
            "/delete",
            "/tn",
            ElevatedMacroProtocol.TaskName,
            "/f");
        return result.ExitCode == 0 || result.ExitCode == TaskDoesNotExist;
    }

    /// <summary>
    /// Starts the agent now. A task at the highest run level can still be
    /// triggered by an ordinary process, so the app does not need elevation
    /// to reach the agent.
    /// </summary>
    public static bool TryStart()
    {
        var result = RunSchTasks("/run", "/tn", ElevatedMacroProtocol.TaskName);
        return result.ExitCode == 0;
    }

    public static bool IsInstalled()
    {
        var result = RunSchTasks("/query", "/tn", ElevatedMacroProtocol.TaskName);
        return result.ExitCode == 0;
    }

    private static string BuildTaskXml(string executablePath, string sid)
    {
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>OpenSynapse macro agent. Sends macro input from the signed-in user's session.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{sid}</UserId>
                  <Delay>PT15S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{sid}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{executablePath}</Command>
                  <Arguments>{ElevatedMacroProtocol.AgentArgument}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string? CurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static SchTasksResult RunSchTasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
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

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("schtasks.exe could not be started.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new SchTasksResult(process.ExitCode, $"{output}{error}".Trim());
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new SchTasksResult(-1, exception.Message);
        }
    }

    private readonly record struct SchTasksResult(int ExitCode, string Output);
}
